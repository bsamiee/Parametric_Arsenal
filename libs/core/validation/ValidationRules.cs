using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Core.Validation;

/// <summary>Polymorphic validation system using compiled expression trees and cached validators.</summary>
public static class ValidationRules {
    /// <summary>Cache key structure for validator lookups with type, config, and member discrimination.</summary>
    private readonly record struct CacheKey(Type Type, ValidationConfig Config = default, string? Member = null, byte Kind = 0);

    private static readonly ConcurrentDictionary<CacheKey, Func<object, IGeometryContext, SystemError[]>> _validatorCache = new();
    private static readonly ConcurrentDictionary<CacheKey, MemberInfo> _memberCache = new();

    private static readonly MethodInfo _enumerableWhere = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableSelect = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Select), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableToArray = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!;

    private static readonly FrozenDictionary<ValidationConfig, (string[] Properties, string[] Methods, SystemError Error)> _validationRules =
        new Dictionary<ValidationConfig, (string[], string[], SystemError)> {
            [ValidationConfig.Standard] = (["IsValid"], [], ErrorRegistry.Geometry.Invalid),
            [ValidationConfig.AreaCentroid] = (["IsClosed"], ["IsPlanar"], ErrorRegistry.Geometry.CurveNotClosedOrPlanar),
            [ValidationConfig.BoundingBox] = ([], ["GetBoundingBox"], ErrorRegistry.Geometry.BoundingBoxInvalid),
            [ValidationConfig.MassProperties] = (["IsSolid", "IsClosed"], [], ErrorRegistry.Geometry.PropertiesComputationFailed),
            [ValidationConfig.Topology] = (["IsManifold", "IsClosed", "IsSolid", "IsSurface"], ["IsManifold", "IsPointInside"], ErrorRegistry.Geometry.InvalidTopology),
            [ValidationConfig.Degeneracy] = (["IsPeriodic", "IsPolyline"], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular"], ErrorRegistry.Geometry.DegenerateGeometry),
            [ValidationConfig.Tolerance] = ([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse"], ErrorRegistry.Validation.ToleranceExceeded),
            [ValidationConfig.SelfIntersection] = ([], ["SelfIntersections"], ErrorRegistry.Geometry.SelfIntersecting),
            [ValidationConfig.MeshSpecific] = (["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh"], ["IsValidWithLog"], ErrorRegistry.Geometry.MeshNonManifoldEdges),
            [ValidationConfig.SurfaceContinuity] = (["IsPeriodic"], ["IsContinuous"], ErrorRegistry.Geometry.PositionalDiscontinuity),
        }.ToFrozenDictionary();

    /// <summary>Gets or compiles cached validator function for runtime type and validation config.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<object, IGeometryContext, SystemError[]> GetOrCompileValidator(Type runtimeType, ValidationConfig config) =>
        _validatorCache.GetOrAdd(new CacheKey(runtimeType, config), static k => CompileValidator(k.Type, k.Config));

    /// <summary>Compiles expression tree validator for runtime type and validation config using reflection-based rule application.</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, ValidationConfig config) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));

        (MemberInfo Member, SystemError Error)[] memberValidations =
            [.. _validationRules.Keys
                .Where(flag => flag != ValidationConfig.None && flag != ValidationConfig.All && config.HasFlag(flag))
                .SelectMany(flag => {
                    (string[] properties, string[] methods, SystemError error) = _validationRules[flag];
                    return (IEnumerable<(MemberInfo Member, SystemError Error)>)[
                        .. properties.Select(prop => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, Member: prop, Kind: 1),
                                static (key, type) => (type.GetProperty(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                            error)),
                        .. methods.Select(method => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, Member: method, Kind: 2),
                                static (key, type) => (type.GetMethod(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                            error)),
                    ];
                }),
            ];

        Expression[] validationExpressions = [.. memberValidations
            .Where(validation => validation.Member is not null and not Type { Name: "Void" })
            .Select<(MemberInfo Member, SystemError Error), Expression>(validation => validation.Member switch {
                PropertyInfo { PropertyType: Type pt } prop when pt == typeof(bool) =>
                    Expression.Condition(Expression.Not(Expression.Property(Expression.Convert(geometry, runtimeType), prop)),
                        Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                        Expression.Constant(null, typeof(SystemError?))),
                MethodInfo method => Expression.Condition(
                    (method.GetParameters(), method.ReturnType, method.Name) switch {
                        ([], Type rt, _) when rt == typeof(bool) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method)),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(double) =>
                            Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(bool) =>
                            Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method, Expression.Constant(true))),
                        (_, _, string name) when string.Equals(name, "SelfIntersections", StringComparison.Ordinal) =>
                            Expression.NotEqual(Expression.Property(Expression.Call(Expression.Convert(geometry, runtimeType), method), "Count"), Expression.Constant(0)),
                        (_, _, string name) when string.Equals(name, "GetBoundingBox", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Property(Expression.Call(Expression.Convert(geometry, runtimeType), method, Expression.Constant(true)), "IsValid")),
                        (_, _, string name) when string.Equals(name, "IsPointInside", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method, Expression.Constant(new Point3d(0, 0, 0)),
                                Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)), Expression.Constant(false))),
                        _ => Expression.Constant(false),
                    },
                    Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                    Expression.Constant(null, typeof(SystemError?))),
                _ => Expression.Constant(null, typeof(SystemError?)),
            }),
        ];

        return Expression.Lambda<Func<object, IGeometryContext, SystemError[]>>(
            Expression.Call(_enumerableToArray.MakeGenericMethod(typeof(SystemError)),
                Expression.Call(_enumerableSelect.MakeGenericMethod(typeof(SystemError?), typeof(SystemError)),
                    Expression.Call(_enumerableWhere.MakeGenericMethod(typeof(SystemError?)),
                        Expression.NewArrayInit(typeof(SystemError?), validationExpressions),
                        Expression.Lambda<Func<SystemError?, bool>>(Expression.NotEqual(error, Expression.Constant(null, typeof(SystemError?))), error)),
                    Expression.Lambda<Func<SystemError?, SystemError>>(Expression.Convert(error, typeof(SystemError)), error))),
            geometry, context).Compile();
    }
}

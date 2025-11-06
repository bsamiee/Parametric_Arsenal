using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Core.Validation;

/// <summary>Polymorphic validation system using compiled expression trees and cached validators.</summary>
public static class ValidationRules {
    /// <summary>Cache key structure for validator lookups with type, mode, and member discrimination.</summary>
    private readonly struct CacheKey(Type type, ValidationMode mode = ValidationMode.None, string? member = null, byte kind = 0) : IEquatable<CacheKey> {
        public readonly Type Type = type;
        public readonly ValidationMode Mode = mode;
        public readonly string? Member = member;
        public readonly byte Kind = kind;

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is CacheKey other && this.Equals(other);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(this.Type, this.Mode, this.Member, this.Kind);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CacheKey other) => (this.Type, this.Mode, this.Member, this.Kind).Equals((other.Type, other.Mode, other.Member, other.Kind));
    }

    private static readonly ConcurrentDictionary<CacheKey, Func<object, IGeometryContext, SystemError[]>> _validatorCache = new();
    private static readonly ConcurrentDictionary<CacheKey, MemberInfo> _memberCache = new();

    private static readonly MethodInfo _enumerableWhere = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableSelect = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Select), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableToArray = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!;

    private static readonly FrozenDictionary<ValidationMode, (string[] Properties, string[] Methods, SystemError Error)> _validationRules =
        new Dictionary<ValidationMode, (string[], string[], SystemError)> {
            [ValidationMode.Standard] = (["IsValid"], [], CoreErrors.Geometry.Invalid),
            [ValidationMode.AreaCentroid] = (["IsClosed"], ["IsPlanar"], CoreErrors.Geometry.Curve.NotClosedOrPlanar),
            [ValidationMode.BoundingBox] = ([], ["GetBoundingBox"], CoreErrors.Geometry.BoundingBox.Invalid),
            [ValidationMode.MassProperties] = (["IsSolid", "IsClosed"], [], CoreErrors.Geometry.Properties.ComputationFailed),
            [ValidationMode.Topology] = (["IsManifold", "IsClosed", "IsSolid", "IsSurface"], ["IsManifold", "IsPointInside"], CoreErrors.Geometry.Topology.InvalidTopology),
            [ValidationMode.Degeneracy] = (["IsPeriodic", "IsPolyline"], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular"], CoreErrors.Geometry.Degeneracy.DegenerateGeometry),
            [ValidationMode.Tolerance] = ([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse"], CoreErrors.Context.Tolerance.ToleranceExceeded),
            [ValidationMode.SelfIntersection] = ([], ["SelfIntersections"], CoreErrors.Geometry.SelfIntersection.SelfIntersecting),
            [ValidationMode.MeshSpecific] = (["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh"], ["IsValidWithLog"], CoreErrors.Geometry.MeshTopology.NonManifoldEdges),
            [ValidationMode.SurfaceContinuity] = (["IsPeriodic"], ["IsContinuous"], CoreErrors.Geometry.Continuity.PositionalDiscontinuity),
        }.ToFrozenDictionary();

    /// <summary>Gets or compiles cached validator function for runtime type and validation mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<object, IGeometryContext, SystemError[]> GetOrCompileValidator(Type runtimeType, ValidationMode mode) =>
        _validatorCache.GetOrAdd(new CacheKey(runtimeType, mode), static k => CompileValidator(k.Type, k.Mode));

    /// <summary>Generates validation errors for tolerance values using polymorphic parameter detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] For<T>(T input, params object[] args) where T : notnull =>
        (typeof(T), input, args) switch {
            (Type t, double abs, [double rel, double ang]) when t == typeof(double) => [
                .. !(RhinoMath.IsValidDouble(abs) && abs > RhinoMath.ZeroTolerance) ? (SystemError[])[CoreErrors.Context.Tolerance.InvalidAbsolute] : [],
                .. !(RhinoMath.IsValidDouble(rel) && rel is >= 0d and < 1d) ? (SystemError[])[CoreErrors.Context.Tolerance.InvalidRelative] : [],
                .. !(RhinoMath.IsValidDouble(ang) && ang is > RhinoMath.Epsilon and <= RhinoMath.TwoPI) ? (SystemError[])[CoreErrors.Context.Tolerance.InvalidAngle] : [],
            ],
            _ => throw new ArgumentException(CoreErrors.Results.InvalidValidateParameters.Message, nameof(args)),
        };

    /// <summary>Compiles expression tree validator for runtime type and validation mode using reflection-based rule application.</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, ValidationMode mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));

        (MemberInfo Member, SystemError Error)[] memberValidations =
            [.. Enum.GetValues<ValidationMode>()
                .Where(flag => flag is not (ValidationMode.None or ValidationMode.All) && mode.HasFlag(flag) && _validationRules.ContainsKey(flag))
                .SelectMany(flag => {
                    (string[] properties, string[] methods, SystemError error) = _validationRules[flag];
                    return (IEnumerable<(MemberInfo Member, SystemError Error)>)[
                        .. properties.Select(prop => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, ValidationMode.None, prop, 1),
                                static (key, type) => (type.GetProperty(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                            error)),
                        .. methods.Select(method => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, ValidationMode.None, method, 2),
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

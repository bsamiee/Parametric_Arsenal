using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Core.Validation;

/// <summary>Validation system using compiled expression trees and cached validators.</summary>
public static class ValidationRules {
    /// <summary>Cache key structure for validator lookups.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct CacheKey(Type type, V mode = default, string? member = null, byte kind = 0) : IEquatable<CacheKey> {
        public readonly Type Type = type;
        public readonly V Mode = mode;
        public readonly string? Member = member;
        public readonly byte Kind = kind;

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(this.Type, this.Mode, this.Member, this.Kind);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is CacheKey other && this.Equals(other);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CacheKey other) => (this.Type, this.Mode, this.Member, this.Kind).Equals((other.Type, other.Mode, other.Member, other.Kind));
    }

    private static readonly FrozenDictionary<V, (string[] Properties, string[] Methods, SystemError Error)> _validationRules =
        new Dictionary<V, (string[], string[], SystemError)> {
            [V.Standard] = (["IsValid",], [], E.Validation.GeometryInvalid),
            [V.AreaCentroid] = (["IsClosed",], ["IsPlanar",], E.Validation.CurveNotClosedOrPlanar),
            [V.BoundingBox] = ([], ["GetBoundingBox",], E.Validation.BoundingBoxInvalid),
            [V.MassProperties] = (["IsSolid", "IsClosed",], [], E.Validation.MassPropertiesComputationFailed),
            [V.Topology] = (["IsManifold", "IsClosed", "IsSolid", "IsSurface", "HasNakedEdges",], ["IsManifold", "IsPointInside",], E.Validation.InvalidTopology),
            [V.Degeneracy] = (["IsPeriodic", "IsPolyline",], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular", "GetLength",], E.Validation.DegenerateGeometry),
            [V.Tolerance] = ([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse",], E.Validation.ToleranceExceeded),
            [V.MeshSpecific] = (["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh",], [], E.Validation.MeshInvalid),
            [V.SurfaceContinuity] = (["IsPeriodic",], ["IsContinuous",], E.Validation.PositionalDiscontinuity),
            [V.PolycurveStructure] = (["IsValid", "HasGap", "IsNested",], [], E.Validation.PolycurveGaps),
            [V.NurbsGeometry] = (["IsValid", "IsPeriodic", "IsRational", "Degree",], [], E.Validation.NurbsControlPointCount),
            [V.ExtrusionGeometry] = (["IsValid", "IsSolid", "IsClosed", "IsCappedAtTop", "IsCappedAtBottom", "CapCount",], [], E.Validation.ExtrusionProfileInvalid),
            [V.UVDomain] = (["IsValid", "HasNurbsForm",], [], E.Validation.UVDomainSingularity),
            [V.SelfIntersection] = ([], ["HasSelfIntersections",], E.Validation.SelfIntersecting),
            [V.BrepGranular] = ([], ["IsValidTopology", "IsValidGeometry", "IsValidTolerancesAndFlags",], E.Validation.BrepTopologyInvalid),  // Method-specific errors resolved via _memberErrorOverrides
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<(V Mode, string Member), SystemError> _memberErrorOverrides =
        new Dictionary<(V, string), SystemError> {
            [(V.BrepGranular, "IsValidTopology")] = E.Validation.BrepTopologyInvalid,
            [(V.BrepGranular, "IsValidGeometry")] = E.Validation.BrepGeometryInvalid,
            [(V.BrepGranular, "IsValidTolerancesAndFlags")] = E.Validation.BrepTolerancesAndFlagsInvalid,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, MethodInfo> _extensionMethods =
        new Dictionary<string, MethodInfo> {
            ["HasSelfIntersections"] = typeof(ValidationRules).GetMethod(nameof(HasSelfIntersections), BindingFlags.NonPublic | BindingFlags.Static)!,
        }.ToFrozenDictionary();

    private static readonly ConcurrentDictionary<CacheKey, Func<object, IGeometryContext, SystemError[]>> _validatorCache = new();
    private static readonly ConcurrentDictionary<CacheKey, MemberInfo> _memberCache = new();

    private static readonly ConstantExpression _nullSystemError = Expression.Constant(null, typeof(SystemError?));
    private static readonly ConstantExpression _constantTrue = Expression.Constant(true);
    private static readonly ConstantExpression _constantFalse = Expression.Constant(false);
    private static readonly ConstantExpression _originPoint = Expression.Constant(new Point3d(0, 0, 0));
    private static readonly ConstantExpression _continuityC1 = Expression.Constant(Continuity.C1_continuous);

    private static readonly MethodInfo _enumerableWhere = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableSelect = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Select), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableToArray = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;

    /// <summary>Gets or compiles cached validator for runtime type and mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<object, IGeometryContext, SystemError[]> GetOrCompileValidator(Type runtimeType, V mode) =>
        _validatorCache.GetOrAdd(new CacheKey(runtimeType, mode), static k => CompileValidator(k.Type, k.Mode));

    /// <summary>Generates validation errors for tolerance values (used by GeometryContext).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] For<T>(T input, params object[] args) where T : notnull =>
        (typeof(T), input, args) switch {
            (Type t, double absoluteTolerance, [double relativeTolerance, double angleToleranceRadians]) when t == typeof(double) =>
                [.. (!(RhinoMath.IsValidDouble(absoluteTolerance) && absoluteTolerance > RhinoMath.ZeroTolerance) ?
                    [E.Validation.ToleranceAbsoluteInvalid] : Array.Empty<SystemError>()),
                    .. (!(RhinoMath.IsValidDouble(relativeTolerance) && relativeTolerance is >= 0d and < 1d) ?
                    [E.Validation.ToleranceRelativeInvalid] : Array.Empty<SystemError>()),
                    .. (!(RhinoMath.IsValidDouble(angleToleranceRadians) && angleToleranceRadians is > RhinoMath.Epsilon and <= RhinoMath.TwoPI) ?
                    [E.Validation.ToleranceAngleInvalid] : Array.Empty<SystemError>()),
                ],
            _ => throw new ArgumentException(E.Results.InvalidValidate.Message, nameof(args)),
        };

    [Pure]
    private static bool HasSelfIntersections(Curve curve, IGeometryContext context) {
        CurveIntersections? intersections = Intersection.CurveSelf(curve, context.AbsoluteTolerance);
        return intersections is { Count: > 0 };
    }

    /// <summary>Compiles expression tree validator for runtime type (zero-allocation).</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, V mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));
        Expression convertedGeometry = Expression.Convert(geometry, runtimeType);

        (MemberInfo Member, SystemError Error)[] memberValidations =
            [.. V.AllFlags
                .Where(flag => mode.Has(flag) && _validationRules.ContainsKey(flag))
                .SelectMany(flag => {
                    (string[] properties, string[] methods, SystemError error) = _validationRules[flag];
                    return (IEnumerable<(MemberInfo Member, SystemError Error)>)[
                        .. properties.Select(prop => (
                            Member: _memberCache.GetOrAdd(new CacheKey(type: runtimeType, mode: default, member: prop, kind: 1),
                                static (key, type) => (type.GetProperty(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                            Error: _memberErrorOverrides.TryGetValue((flag, prop), out SystemError overrideError) ? overrideError : error)),
                        .. methods.Select(method => (
                            Member: _memberCache.GetOrAdd(new CacheKey(type: runtimeType, mode: default, member: method, kind: 2),
                                static (key, state) => {
                                    (Type Type, FrozenDictionary<string, MethodInfo> Extensions) parameters = state;
                                    MethodInfo? instanceMethod = parameters.Type.GetMethod(key.Member!);
                                    return instanceMethod is not null
                                        ? instanceMethod
                                        : parameters.Extensions.TryGetValue(key.Member!, out MethodInfo extension)
                                            ? extension
                                            : (MemberInfo)typeof(void);
                                }, (runtimeType, _extensionMethods)),
                            Error: _memberErrorOverrides.TryGetValue((flag, method), out SystemError overrideError) ? overrideError : error)),
                    ];
                }),
            ];

        Expression[] validationExpressions = [.. memberValidations
            .Where(validation => validation.Member is not null and not Type { Name: "Void" })
                .Select<(MemberInfo Member, SystemError Error), Expression>(validation => validation.Member switch {
                PropertyInfo { PropertyType: Type pt, Name: string propName } prop when pt == typeof(bool) =>
                    Expression.Condition(Expression.Not(Expression.Property(convertedGeometry, prop)),
                        Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                        _nullSystemError),
                PropertyInfo { PropertyType: Type pt, Name: string propName } prop when pt == typeof(int) && string.Equals(propName, "Degree", StringComparison.Ordinal) =>
                    Expression.Condition(Expression.LessThan(Expression.Property(convertedGeometry, prop), Expression.Constant(1)),  // RhinoCommon NURBS require degree >= 1
                        Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                        _nullSystemError),
                PropertyInfo { PropertyType: Type pt, Name: string propName } prop when pt == typeof(int) && string.Equals(propName, "CapCount", StringComparison.Ordinal) =>
                    Expression.Condition(Expression.NotEqual(Expression.Property(convertedGeometry, prop), Expression.Constant(2)),
                        Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                        _nullSystemError),
                MethodInfo method => Expression.Condition(
                    (method.GetParameters(), method.ReturnType, method.Name) switch {
                        ([], Type rt, _) when rt == typeof(bool) => Expression.Not(method.IsStatic
                            ? Expression.Call(method)
                            : Expression.Call(convertedGeometry, method)),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(double) =>
                            Expression.Not(method.IsStatic
                                ? Expression.Call(method, convertedGeometry, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))
                                : Expression.Call(convertedGeometry, method, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(bool) =>
                            Expression.Not(method.IsStatic
                                ? Expression.Call(method, convertedGeometry, _constantTrue)
                                : Expression.Call(convertedGeometry, method, _constantTrue)),
                        ([{ ParameterType: Type pt }], Type rt, string name) when rt == typeof(bool) && pt == typeof(Continuity) && string.Equals(name, "IsContinuous", StringComparison.Ordinal) =>
                            Expression.Not(method.IsStatic
                                ? Expression.Call(method, convertedGeometry, _continuityC1)
                                : Expression.Call(convertedGeometry, method, _continuityC1)),
                        ([{ ParameterType: Type first }, { ParameterType: Type second }], Type rt, _) when rt == typeof(bool) && method.IsStatic && first.IsAssignableFrom(runtimeType) && second == typeof(IGeometryContext) =>
                            Expression.Not(Expression.Call(method, first == runtimeType ? convertedGeometry : Expression.Convert(convertedGeometry, first), context)),
                        (_, _, string name) when string.Equals(name, "GetBoundingBox", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Property(Expression.Call(convertedGeometry, method, _constantTrue), "IsValid")),
                        (_, _, string name) when string.Equals(name, "IsPointInside", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Call(convertedGeometry, method, _originPoint,
                                Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)), _constantFalse)),
                        (_, Type rt, string name) when string.Equals(name, "GetLength", StringComparison.Ordinal) && rt == typeof(double) =>
                            Expression.LessThanOrEqual(Expression.Call(convertedGeometry, method), Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance))),
                        _ => _constantFalse,
                    },
                    Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                    _nullSystemError),
                _ => _nullSystemError,
            }),
        ];

        return Expression.Lambda<Func<object, IGeometryContext, SystemError[]>>(
            Expression.Call(_enumerableToArray.MakeGenericMethod(typeof(SystemError)),
                Expression.Call(_enumerableSelect.MakeGenericMethod(typeof(SystemError?), typeof(SystemError)),
                    Expression.Call(_enumerableWhere.MakeGenericMethod(typeof(SystemError?)),
                        Expression.NewArrayInit(typeof(SystemError?), validationExpressions),
                        Expression.Lambda<Func<SystemError?, bool>>(Expression.NotEqual(error, _nullSystemError), error)),
                    Expression.Lambda<Func<SystemError?, SystemError>>(Expression.Convert(error, typeof(SystemError)), error))),
            geometry, context).Compile();
    }
}

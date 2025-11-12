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
using Rhino.Geometry.Intersect;

namespace Arsenal.Core.Validation;

/// <summary>Validation via compiled expression trees with caching.</summary>
public static class ValidationRules {
    /// <summary>Cache key for validator lookups.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct CacheKey(Type Type, V Mode = default, string? Member = null, byte Kind = 0);

    private static readonly ConcurrentDictionary<CacheKey, Func<object, IGeometryContext, SystemError[]>> _validatorCache = new();
    private static readonly ConcurrentDictionary<CacheKey, MemberInfo> _memberCache = new();
    private static readonly ConstantExpression _nullSystemError = Expression.Constant(null, typeof(SystemError?));
    private static readonly ConstantExpression _constantTrue = Expression.Constant(true);
    private static readonly ConstantExpression _constantFalse = Expression.Constant(false);
    private static readonly ConstantExpression _originPoint = Expression.Constant(new Point3d(0, 0, 0));
    private static readonly ConstantExpression _continuityC1 = Expression.Constant(Continuity.C1_continuous);
    private static readonly ConstantExpression _nullCurveIntersections = Expression.Constant(null, typeof(CurveIntersections));
    private static readonly MethodInfo _enumerableWhere = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableSelect = typeof(Enumerable).GetMethods()
        .First(static m => string.Equals(m.Name, nameof(Enumerable.Select), StringComparison.Ordinal) && m.GetParameters().Length == 2);
    private static readonly MethodInfo _enumerableToArray = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
    private static readonly MethodInfo _curveSelf = typeof(Intersection).GetMethod(nameof(Intersection.CurveSelf), [typeof(Curve), typeof(double),])!;
    private static readonly MethodInfo _dispose = typeof(IDisposable).GetMethod(name: nameof(IDisposable.Dispose), bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, binder: null, types: Type.EmptyTypes, modifiers: null)!;
    private static readonly MethodInfo _surfaceDomain = typeof(Surface).GetMethod(nameof(Surface.Domain), [typeof(int),])!;
    private static readonly PropertyInfo _intervalLength = typeof(Interval).GetProperty(nameof(Interval.Length), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
    private static readonly PropertyInfo _meshFaces = typeof(Mesh).GetProperty(nameof(Mesh.Faces), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
    private static readonly PropertyInfo _meshFaceCount = typeof(Rhino.Geometry.Collections.MeshFaceList).GetProperty(nameof(Rhino.Geometry.Collections.MeshFaceList.Count), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

    private static readonly FrozenDictionary<V, ((string Member, SystemError Error)[] Properties, (string Member, SystemError Error)[] Methods)> _validationRules =
        new Dictionary<V, ((string Member, SystemError Error)[], (string Member, SystemError Error)[])> {
            [V.Standard] = ([(Member: "IsValid", Error: E.Validation.GeometryInvalid),], []),
            [V.AreaCentroid] = ([(Member: "IsClosed", Error: E.Validation.CurveNotClosedOrPlanar),], [(Member: "IsPlanar", Error: E.Validation.CurveNotClosedOrPlanar),]),
            [V.BoundingBox] = ([], [(Member: "GetBoundingBox", Error: E.Validation.BoundingBoxInvalid),]),
            [V.MassProperties] = ([(Member: "IsSolid", Error: E.Validation.MassPropertiesComputationFailed), (Member: "IsClosed", Error: E.Validation.MassPropertiesComputationFailed),], []),
            [V.Topology] = ([(Member: "IsManifold", Error: E.Validation.InvalidTopology), (Member: "IsClosed", Error: E.Validation.InvalidTopology), (Member: "IsSolid", Error: E.Validation.InvalidTopology), (Member: "IsSurface", Error: E.Validation.InvalidTopology), (Member: "HasNakedEdges", Error: E.Validation.InvalidTopology),], [(Member: "IsManifold", Error: E.Validation.InvalidTopology), (Member: "IsPointInside", Error: E.Validation.InvalidTopology),]),
            [V.Degeneracy] = ([(Member: "IsPeriodic", Error: E.Validation.DegenerateGeometry), (Member: "IsPolyline", Error: E.Validation.DegenerateGeometry),], [(Member: "IsShort", Error: E.Validation.DegenerateGeometry), (Member: "IsSingular", Error: E.Validation.DegenerateGeometry), (Member: "IsDegenerate", Error: E.Validation.DegenerateGeometry), (Member: "IsRectangular", Error: E.Validation.DegenerateGeometry), (Member: "GetLength", Error: E.Validation.DegenerateGeometry),]),
            [V.Tolerance] = ([], [(Member: "IsPlanar", Error: E.Validation.ToleranceExceeded), (Member: "IsLinear", Error: E.Validation.ToleranceExceeded), (Member: "IsArc", Error: E.Validation.ToleranceExceeded), (Member: "IsCircle", Error: E.Validation.ToleranceExceeded), (Member: "IsEllipse", Error: E.Validation.ToleranceExceeded),]),
            [V.MeshSpecific] = ([(Member: "IsManifold", Error: E.Validation.MeshInvalid), (Member: "IsClosed", Error: E.Validation.MeshInvalid), (Member: "HasNgons", Error: E.Validation.MeshInvalid), (Member: "HasVertexColors", Error: E.Validation.MeshInvalid), (Member: "HasVertexNormals", Error: E.Validation.MeshInvalid), (Member: "IsTriangleMesh", Error: E.Validation.MeshInvalid), (Member: "IsQuadMesh", Error: E.Validation.MeshInvalid), (Member: "FacesCount", Error: E.Validation.MeshInvalid),], []),
            [V.SurfaceContinuity] = ([(Member: "IsPeriodic", Error: E.Validation.PositionalDiscontinuity),], [(Member: "IsContinuous", Error: E.Validation.PositionalDiscontinuity),]),
            [V.PolycurveStructure] = ([(Member: "IsValid", Error: E.Validation.PolycurveGaps), (Member: "HasGap", Error: E.Validation.PolycurveGaps), (Member: "IsNested", Error: E.Validation.PolycurveGaps),], []),
            [V.NurbsGeometry] = ([(Member: "IsValid", Error: E.Validation.NurbsControlPointCount), (Member: "IsPeriodic", Error: E.Validation.NurbsControlPointCount), (Member: "IsRational", Error: E.Validation.NurbsControlPointCount), (Member: "Degree", Error: E.Validation.NurbsControlPointCount),], []),
            [V.ExtrusionGeometry] = ([(Member: "IsValid", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsSolid", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsClosed", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsCappedAtTop", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsCappedAtBottom", Error: E.Validation.ExtrusionProfileInvalid), (Member: "CapCount", Error: E.Validation.ExtrusionProfileInvalid),], []),
            [V.UVDomain] = ([(Member: "IsValid", Error: E.Validation.UVDomainSingularity), (Member: "HasNurbsForm", Error: E.Validation.UVDomainSingularity), (Member: "DomainLength", Error: E.Validation.UVDomainSingularity),], []),
            [V.SelfIntersection] = ([], [(Member: "CurveSelf", Error: E.Validation.SelfIntersecting),]),
            [V.BrepGranular] = ([], [(Member: "IsValidTopology", Error: E.Validation.BrepTopologyInvalid), (Member: "IsValidGeometry", Error: E.Validation.BrepGeometryInvalid), (Member: "IsValidTolerancesAndFlags", Error: E.Validation.BrepTolerancesAndFlagsInvalid),]),
        }.ToFrozenDictionary();

    /// <summary>Gets or compiles cached validator for type and mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<object, IGeometryContext, SystemError[]> GetOrCompileValidator(Type runtimeType, V mode) =>
        _validatorCache.GetOrAdd(new CacheKey(runtimeType, mode), static k => CompileValidator(k.Type, k.Mode));

    /// <summary>Validation errors for tolerance values.</summary>
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
            _ => [E.Results.InvalidValidate.WithContext(nameof(args)),],
        };

    /// <summary>Compiles zero-allocation validator via expression trees.</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, V mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));
        (MemberInfo Member, SystemError Error, string Name, byte Kind)[] memberValidations = [.. V.AllFlags.Where(flag => mode.Has(flag) && _validationRules.ContainsKey(flag)).SelectMany(flag => {
            ((string Member, SystemError Error)[] properties, (string Member, SystemError Error)[] methods) = _validationRules[flag];
            return (IEnumerable<(MemberInfo Member, SystemError Error, string Name, byte Kind)>)[.. properties.Select(p => (_memberCache.GetOrAdd(new CacheKey(Type: runtimeType, Mode: default, Member: p.Member, Kind: 1), static (key, type) => (type.GetProperty(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType), p.Error, p.Member, (byte)1)), .. methods.Select(m => (_memberCache.GetOrAdd(new CacheKey(Type: runtimeType, Mode: default, Member: m.Member, Kind: 2), static (key, type) => (type.GetMethod(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType), m.Error, m.Member, (byte)2)),];
        }),
        ];

        UnaryExpression errorConst = Expression.Convert(Expression.Constant(default(SystemError)), typeof(SystemError?));
        Expression[] validationExpressions = [.. memberValidations.Where(v => v.Member is not null and not Type { Name: "Void" } || v.Name is "DomainLength" or "FacesCount" or "CurveSelf").Select(v => v.Member switch {
            PropertyInfo { PropertyType: Type pt, Name: string pn } p when pt == typeof(bool) => Expression.Condition(Expression.Not(Expression.Property(Expression.Convert(geometry, runtimeType), p)), Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError),
            PropertyInfo { PropertyType: Type pt, Name: string pn } p when pt == typeof(int) && string.Equals(pn, "Degree", StringComparison.Ordinal) => Expression.Condition(Expression.LessThan(Expression.Property(Expression.Convert(geometry, runtimeType), p), Expression.Constant(1)), Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError),
            PropertyInfo { PropertyType: Type pt, Name: string pn } p when pt == typeof(int) && string.Equals(pn, "CapCount", StringComparison.Ordinal) => Expression.Condition(Expression.NotEqual(Expression.Property(Expression.Convert(geometry, runtimeType), p), Expression.Constant(2)), Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError),
            PropertyInfo { Name: string pn } when string.Equals(pn, "FacesCount", StringComparison.Ordinal) => Expression.Condition(
                Expression.LessThanOrEqual(
                    Expression.Property(Expression.Property(Expression.Convert(geometry, runtimeType), _meshFaces), _meshFaceCount),
                    Expression.Constant(0)
                ),
                Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)),
                _nullSystemError
            ),
            MethodInfo m => Expression.Condition((m.GetParameters(), m.ReturnType, m.Name) switch {
                ([], Type rt, _) when rt == typeof(bool) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), m)),
                ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(double) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), m, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))),
                ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(bool) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), m, _constantTrue)),
                ([{ ParameterType: Type pt }], Type rt, string n) when rt == typeof(bool) && pt == typeof(Continuity) && string.Equals(n, "IsContinuous", StringComparison.Ordinal) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), m, _continuityC1)),
                (_, _, string n) when string.Equals(n, "GetBoundingBox", StringComparison.Ordinal) => Expression.Not(Expression.Property(Expression.Call(Expression.Convert(geometry, runtimeType), m, _constantTrue), "IsValid")),
                (_, _, string n) when string.Equals(n, "IsPointInside", StringComparison.Ordinal) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), m, _originPoint, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)), _constantFalse)),
                (_, Type rt, string n) when string.Equals(n, "GetLength", StringComparison.Ordinal) && rt == typeof(double) => Expression.LessThanOrEqual(Expression.Call(Expression.Convert(geometry, runtimeType), m), Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance))),
                _ => _constantFalse,
            }, Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError),
            Type when string.Equals(v.Name, "CurveSelf", StringComparison.Ordinal) => typeof(Curve).IsAssignableFrom(runtimeType) ? ((Func<Expression>)(() => { ParameterExpression i = Expression.Variable(typeof(CurveIntersections), "si"); ParameterExpression r = Expression.Variable(typeof(SystemError?), "sr"); return Expression.Block(typeof(SystemError?), [i, r,], Expression.Assign(i, Expression.Call(_curveSelf, Expression.Convert(geometry, runtimeType), Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))), Expression.Assign(r, Expression.Condition(Expression.AndAlso(Expression.NotEqual(i, _nullCurveIntersections), Expression.GreaterThan(Expression.Property(i, nameof(CurveIntersections.Count)), Expression.Constant(0))), Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError)), Expression.IfThen(Expression.NotEqual(i, _nullCurveIntersections), Expression.Call(Expression.Convert(i, typeof(IDisposable)), _dispose)), r); }))() : _nullSystemError,
            Type when string.Equals(v.Name, "DomainLength", StringComparison.Ordinal) && typeof(Surface).IsAssignableFrom(runtimeType) => ((Func<Expression>)(() => {
                Expression converted = Expression.Convert(geometry, runtimeType);
                Expression tolerance = Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance));
                Expression uDomain = Expression.Call(converted, _surfaceDomain, Expression.Constant(0));
                Expression vDomain = Expression.Call(converted, _surfaceDomain, Expression.Constant(1));
                Expression uLength = Expression.Property(uDomain, _intervalLength);
                Expression vLength = Expression.Property(vDomain, _intervalLength);
                Expression failure = Expression.OrElse(
                    Expression.LessThanOrEqual(uLength, tolerance),
                    Expression.LessThanOrEqual(vLength, tolerance)
                );
                return Expression.Condition(failure, Expression.Convert(Expression.Constant(v.Error), typeof(SystemError?)), _nullSystemError);
            }))(),
            _ => _nullSystemError,
        }),
        ];

        return Expression.Lambda<Func<object, IGeometryContext, SystemError[]>>(Expression.Call(_enumerableToArray.MakeGenericMethod(typeof(SystemError)), Expression.Call(_enumerableSelect.MakeGenericMethod(typeof(SystemError?), typeof(SystemError)), Expression.Call(_enumerableWhere.MakeGenericMethod(typeof(SystemError?)), Expression.NewArrayInit(typeof(SystemError?), validationExpressions), Expression.Lambda<Func<SystemError?, bool>>(Expression.NotEqual(error, _nullSystemError), error)), Expression.Lambda<Func<SystemError?, SystemError>>(Expression.Convert(error, typeof(SystemError)), error))), geometry, context).Compile();
    }
}

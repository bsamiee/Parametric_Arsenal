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

/// <summary>Polymorphic validation with compiled expression trees and type-based dispatch.</summary>
public static class ValidationEngine {
    private readonly struct CacheKey(Type type, ulong mode, string? member = null, byte kind = 0) : IEquatable<CacheKey> {
        public readonly Type Type = type;
        public readonly ulong Mode = mode;
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

    private static readonly FrozenDictionary<ulong, (string[] Properties, string[] Methods, int ErrorCode)> _rules =
        new Dictionary<ulong, (string[], string[], int)> {
            [1UL] = (["IsValid",], [], 3000),
            [2UL] = (["IsClosed",], ["IsPlanar",], 3100),
            [4UL] = ([], ["GetBoundingBox",], 3200),
            [8UL] = (["IsSolid", "IsClosed",], [], 3300),
            [16UL] = (["IsManifold", "IsClosed", "IsSolid", "IsSurface",], ["IsManifold", "IsPointInside",], 3400),
            [32UL] = (["IsPeriodic", "IsPolyline",], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular",], 3500),
            [64UL] = ([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse",], 3903),
            [128UL] = ([], ["SelfIntersections",], 3600),
            [256UL] = (["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh",], ["IsValidWithLog",], 3700),
            [512UL] = (["IsPeriodic",], ["IsContinuous",], 3800),
        }.ToFrozenDictionary();

    /// <summary>Validates tolerance values using polymorphic parameter detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] For<T>(T input, params object[] args) where T : notnull =>
        (typeof(T), input, args) switch {
            (Type t, double absoluteTolerance, [double relativeTolerance, double angleToleranceRadians,]) when t == typeof(double) =>
                [.. GetToleranceErrors(absoluteTolerance, relativeTolerance, angleToleranceRadians),],
            _ => throw new ArgumentException("Invalid validation parameters", nameof(args)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IEnumerable<SystemError> GetToleranceErrors(double abs, double rel, double angle) {
        if (!(RhinoMath.IsValidDouble(abs) && abs > RhinoMath.ZeroTolerance)) {
            yield return ErrorFactory.Create(code: 3900);
        }
        if (!(RhinoMath.IsValidDouble(rel) && rel is >= 0d and < 1d)) {
            yield return ErrorFactory.Create(code: 3901);
        }
        if (!(RhinoMath.IsValidDouble(angle) && angle is > RhinoMath.Epsilon and <= RhinoMath.TwoPI)) {
            yield return ErrorFactory.Create(code: 3902);
        }
    }

    /// <summary>Validates geometry using type-based mode dispatch with compiled validators.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] Validate<T>(T? geometry, IGeometryContext context, ulong mode = 1UL) =>
        (geometry, typeof(T).FullName?.StartsWith("Rhino.Geometry.", StringComparison.Ordinal), mode) switch {
            (null, _, _) => [],
            (not null, true, > 0UL) => _validatorCache.GetOrAdd(new CacheKey(geometry.GetType(), mode), static k => CompileValidator(k.Type, k.Mode))(geometry, context),
            (_, true, 0UL) => [],
            _ => [],
        };

    /// <summary>Compiles expression tree validator for runtime type using reflection-based rule application.</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, ulong mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));

        (MemberInfo Member, int ErrorCode)[] memberValidations =
            [.. _rules.Where(kvp => (mode & kvp.Key) == kvp.Key)
                .SelectMany(kvp => {
                    (string[] properties, string[] methods, int errorCode) = kvp.Value;
                    return (IEnumerable<(MemberInfo Member, int ErrorCode)>)[
                        .. properties.Select(prop => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, mode: 0UL, member: prop, kind: 1),
                                static (key, type) => type.GetProperty(key.Member!)!, runtimeType)!,
                            errorCode)),
                        .. methods.Select(method => (
                            Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, mode: 0UL, member: method, kind: 2),
                                static (key, type) => type.GetMethod(key.Member!)!, runtimeType)!,
                            errorCode)),
                    ];
                }),
            ];

        Expression[] validationExpressions = [.. memberValidations
            .Where(validation => validation.Member is not null)
            .Select<(MemberInfo Member, int ErrorCode), Expression>(validation => validation.Member switch {
                PropertyInfo { PropertyType: Type pt } prop when pt == typeof(bool) =>
                    Expression.Condition(Expression.Not(Expression.Property(Expression.Convert(geometry, runtimeType), prop)),
                        Expression.Convert(Expression.Constant(ErrorFactory.Create(code: validation.ErrorCode)), typeof(SystemError?)),
                        Expression.Constant(null, typeof(SystemError?))),
                MethodInfo method => Expression.Condition(
                    (method.GetParameters(), method.ReturnType, method.Name) switch {
                        ([], Type rt, _) when rt == typeof(bool) => Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method)),
                        ([{ ParameterType: Type pt },], Type rt, _) when rt == typeof(bool) && pt == typeof(double) =>
                            Expression.Not(Expression.Call(Expression.Convert(geometry, runtimeType), method, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))),
                        ([{ ParameterType: Type pt },], Type rt, _) when rt == typeof(bool) && pt == typeof(bool) =>
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
                    Expression.Convert(Expression.Constant(ErrorFactory.Create(code: validation.ErrorCode)), typeof(SystemError?)),
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

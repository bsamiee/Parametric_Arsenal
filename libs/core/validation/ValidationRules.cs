using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Rhino.Geometry;

namespace Arsenal.Core.Validation;

/// <summary>Polymorphic validation system using compiled expression trees and cached validators with registry-based dispatch.</summary>
public static class ValidationRules {
    /// <summary>Cache key structure for validator lookups with type, mode, and member discrimination.</summary>
    private readonly struct CacheKey(Type type, ValidationMode mode, string? member = null, byte kind = 0) : IEquatable<CacheKey> {
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

    /// <summary>Gets or compiles cached validator function for runtime type and validation mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<object, IGeometryContext, SystemError[]> GetOrCompileValidator(Type runtimeType, ValidationMode mode) =>
        _validatorCache.GetOrAdd(new CacheKey(runtimeType, mode), static k => CompileValidator(k.Type, k.Mode));

    /// <summary>Compiles expression tree validator for runtime type and validation mode using reflection-based rule application.</summary>
    [Pure]
    private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type runtimeType, ValidationMode mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(object), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));

        ValidationRegistry.RuleConfig[] rules = ValidationRegistry.GetRules(mode);

        (MemberInfo Member, SystemError Error)[] memberValidations =
            [.. rules.SelectMany(rule => {
                SystemError err = ErrorRegistry.Get(rule.ErrorCode);
                return (IEnumerable<(MemberInfo Member, SystemError Error)>)[
                    .. rule.Properties.Select(prop => (
                        Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, ValidationMode.None, prop, 1),
                            static (key, type) => (type.GetProperty(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                        err)),
                    .. rule.Methods.Select(method => (
                        Member: _memberCache.GetOrAdd(new CacheKey(runtimeType, ValidationMode.None, method, 2),
                            static (key, type) => (type.GetMethod(key.Member!) ?? (MemberInfo)typeof(void)), runtimeType),
                        err)),
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

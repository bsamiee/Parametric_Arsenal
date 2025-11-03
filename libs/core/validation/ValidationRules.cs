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

/// <summary>Reflection-driven polymorphic validation system using compiled expression trees and zero-allocation caching.</summary>
public static class ValidationRules {
    /// <summary>Unified cache key structure for zero-allocation lookups with type, mode, and member discrimination.</summary>
    private readonly struct CacheKey(Type type, ValidationMode mode = ValidationMode.None, string? member = null, byte kind = 0) : IEquatable<CacheKey> {
        public readonly Type Type = type;
        public readonly ValidationMode Mode = mode;
        public readonly string? Member = member;
        public readonly byte Kind = kind;

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is CacheKey other && this.Equals(other);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(this.Type, this.Mode, this.Member, this.Kind);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CacheKey other) =>
            this.Type == other.Type && this.Mode == other.Mode &&
            string.Equals(this.Member, other.Member, StringComparison.Ordinal) && this.Kind == other.Kind;
    }
    private static readonly ConcurrentDictionary<CacheKey, object> _cache = new();

    private static readonly FrozenDictionary<ValidationMode, (string[] Properties, string[] Methods, SystemError Error)> _validationRules =
        new Dictionary<ValidationMode, (string[], string[], SystemError)> {
            [ValidationMode.Standard] = (["IsValid"], [], ValidationErrors.Geometry.Invalid),
            [ValidationMode.AreaCentroid] = (["IsClosed"], ["IsPlanar"], ValidationErrors.Geometry.Curve.NotClosedOrPlanar),
            [ValidationMode.BoundingBox] = ([], ["GetBoundingBox"], ValidationErrors.Geometry.BoundingBox.Invalid),
            [ValidationMode.MassProperties] = (["IsSolid", "IsClosed"], [], ValidationErrors.Geometry.Properties.ComputationFailed),
            [ValidationMode.Topology] = (["IsManifold", "IsClosed", "IsSolid", "IsSurface"], ["IsManifold", "IsPointInside"], ValidationErrors.Geometry.Topology.InvalidTopology),
            [ValidationMode.Degeneracy] = (["IsPeriodic", "IsPolyline"], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular"], ValidationErrors.Geometry.Degeneracy.DegenerateGeometry),
            [ValidationMode.Tolerance] = ([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse"], ValidationErrors.Context.Tolerance.ToleranceExceeded),
            [ValidationMode.SelfIntersection] = ([], ["SelfIntersections"], ValidationErrors.Geometry.SelfIntersection.SelfIntersecting),
            [ValidationMode.MeshSpecific] = (["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh"], ["IsValidWithLog"], ValidationErrors.Geometry.MeshTopology.NonManifoldEdges),
            [ValidationMode.SurfaceContinuity] = (["IsPeriodic"], ["IsContinuous"], ValidationErrors.Geometry.Continuity.PositionalDiscontinuity),
        }.ToFrozenDictionary();

    /// <summary>Gets or compiles cached validator function for specified type and validation mode combination.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<T, IGeometryContext, SystemError[]> GetOrCompileValidator<T>(ValidationMode mode) {
        CacheKey key = new(typeof(T), mode);
        object validator = _cache.GetOrAdd(key, k => CompileValidator<T>(k.Mode));
        return (Func<T, IGeometryContext, SystemError[]>)validator;
    }

    /// <summary>Validates geometry using monadic composition with comprehensive error propagation and context awareness.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ValidateGeometry<T>(this Result<T> result, IGeometryContext context, ValidationMode mode = ValidationMode.Standard) where T : GeometryBase =>
        result.Validate(
            predicate: geometry => For(geometry, context, mode).Length == 0,
            error: ValidationErrors.Geometry.Invalid);

    /// <summary>Generates validation errors using polymorphic parameter detection for geometry objects or tolerance values.</summary>
    [Pure]
    public static SystemError[] For<T>(T input, params object[] args) where T : notnull =>
        (typeof(T), input, args) switch {
            // Tolerance validation - when T is double, treat as tolerance validation
            (Type t, double absoluteTolerance, [double relativeTolerance, double angleToleranceRadians]) when t == typeof(double) =>
                [
                    ..(!(RhinoMath.IsValidDouble(absoluteTolerance) && absoluteTolerance > RhinoMath.ZeroTolerance) ? [ValidationErrors.Context.Tolerance.InvalidAbsolute] : Array.Empty<SystemError>()),
                    ..(!(RhinoMath.IsValidDouble(relativeTolerance) && relativeTolerance is >= 0d and < 1d) ? [ValidationErrors.Context.Tolerance.InvalidRelative] : Array.Empty<SystemError>()),
                    ..(!(RhinoMath.IsValidDouble(angleToleranceRadians) && angleToleranceRadians is > RhinoMath.Epsilon and <= RhinoMath.TwoPI) ? [ValidationErrors.Context.Tolerance.InvalidAngle] : Array.Empty<SystemError>()),
                ],

            // Geometry validation - standard path with context and mode
            (Type t, var geometry, [IGeometryContext context, ValidationMode mode]) when t != typeof(double) =>
                geometry switch {
                    null => [ValidationErrors.Geometry.Null],
                    _ => GetOrCompileValidator<T>(mode)(geometry, context),
                },

            // Geometry validation - with context only (default mode)
            (Type t, var geometry, [IGeometryContext context]) when t != typeof(double) =>
                geometry switch {
                    null => [ValidationErrors.Geometry.Null],
                    _ => GetOrCompileValidator<T>(ValidationMode.Standard)(geometry, context),
                },

            _ => throw new ArgumentException(ResultErrors.Factory.InvalidValidateParameters.Message, nameof(args)),
        };

    /// <summary>Compiles expression tree validator for specified type and validation mode with comprehensive reflection-based rule application.</summary>
    [Pure]
    private static Func<T, IGeometryContext, SystemError[]> CompileValidator<T>(ValidationMode mode) {
        (ParameterExpression geometry, ParameterExpression context, ParameterExpression error) = (Expression.Parameter(typeof(T), "g"), Expression.Parameter(typeof(IGeometryContext), "c"), Expression.Parameter(typeof(SystemError?), "e"));

        (object Member, SystemError Error)[] memberValidations =
            [.. Enum.GetValues<ValidationMode>()
                .Where(flag => flag is not (ValidationMode.None or ValidationMode.All) && mode.HasFlag(flag) && _validationRules.ContainsKey(flag))
                .SelectMany(flag => {
                    (string[] properties, string[] methods, SystemError error) = _validationRules[flag];
                    return (IEnumerable<(object Member, SystemError Error)>)[
                        ..properties.Select(prop => (
                            Member: _cache.GetOrAdd(new CacheKey(typeof(T), ValidationMode.None, prop, 1),
                                static (key, type) => type.GetProperty(key.Member!) ?? (object)typeof(void), typeof(T)),
                            error)),
                        ..methods.Select(method => (
                            Member: _cache.GetOrAdd(new CacheKey(typeof(T), ValidationMode.None, method, 2),
                                static (key, type) => type.GetMethod(key.Member!) ?? (object)typeof(void), typeof(T)),
                            error)),
                    ];
                }),];

        Expression[] validationExpressions = [.. memberValidations
            .Where(validation => validation.Member is not null and not Type { Name: "Void" })
            .Select<(object Member, SystemError Error), Expression>(validation => validation.Member switch {
                PropertyInfo { PropertyType: Type pt } prop when pt == typeof(bool) =>
                    Expression.Condition(Expression.Not(Expression.Property(geometry, prop)),
                        Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                        Expression.Constant(null, typeof(SystemError?))),
                MethodInfo method => Expression.Condition(
                    (method.GetParameters(), method.ReturnType, method.Name) switch {
                        ([], Type rt, _) when rt == typeof(bool) => Expression.Not(Expression.Call(geometry, method)),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(double) =>
                            Expression.Not(Expression.Call(geometry, method, Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)))),
                        ([{ ParameterType: Type pt }], Type rt, _) when rt == typeof(bool) && pt == typeof(bool) =>
                            Expression.Not(Expression.Call(geometry, method, Expression.Constant(true))),
                        (_, _, string name) when string.Equals(name, "SelfIntersections", StringComparison.Ordinal) =>
                            Expression.NotEqual(Expression.Property(Expression.Call(geometry, method), "Count"), Expression.Constant(0)),
                        (_, _, string name) when string.Equals(name, "GetBoundingBox", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Property(Expression.Call(geometry, method, Expression.Constant(true)), "IsValid")),
                        (_, _, string name) when string.Equals(name, "IsPointInside", StringComparison.Ordinal) =>
                            Expression.Not(Expression.Call(geometry, method, Expression.Constant(new Point3d(0, 0, 0)),
                                Expression.Property(context, nameof(IGeometryContext.AbsoluteTolerance)), Expression.Constant(false))),
                        _ => Expression.Constant(false),
                    },
                    Expression.Convert(Expression.Constant(validation.Error), typeof(SystemError?)),
                    Expression.Constant(null, typeof(SystemError?))),
                _ => Expression.Constant(null, typeof(SystemError?)),
            }),];

        return Expression.Lambda<Func<T, IGeometryContext, SystemError[]>>(
            Expression.Call(typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!.MakeGenericMethod(typeof(SystemError)),
                Expression.Call(typeof(Enumerable).GetMethods().First(static m => string.Equals(m.Name, nameof(Enumerable.Select), StringComparison.Ordinal) && m.GetParameters().Length == 2).MakeGenericMethod(typeof(SystemError?), typeof(SystemError)),
                    Expression.Call(typeof(Enumerable).GetMethods().First(static m => string.Equals(m.Name, nameof(Enumerable.Where), StringComparison.Ordinal) && m.GetParameters().Length == 2).MakeGenericMethod(typeof(SystemError?)),
                        Expression.NewArrayInit(typeof(SystemError?), validationExpressions),
                        Expression.Lambda<Func<SystemError?, bool>>(Expression.NotEqual(error, Expression.Constant(null, typeof(SystemError?))), error)),
                    Expression.Lambda<Func<SystemError?, SystemError>>(Expression.Convert(error, typeof(SystemError)), error))),
            geometry, context).Compile();
    }
}

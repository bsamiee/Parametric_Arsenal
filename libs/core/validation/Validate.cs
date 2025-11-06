using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Core.Validation;

/// <summary>Singular validation API with polymorphic dispatch for seamless Result integration.</summary>
public static class Validate {
    /// <summary>Validates value with automatic mode inference for geometry types.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Check<T>(T value, IGeometryContext context, ValidationMode mode = default) where T : notnull =>
        (value, mode == ValidationMode.None ? InferMode(value) : mode) switch {
            (GeometryBase g, ValidationMode m) when m != ValidationMode.None =>
                ValidationRules.GetOrCompileValidator(g.GetType(), m)(g, context) switch {
                    SystemError[] { Length: 0 } => ResultFactory.Create(value: value),
                    SystemError[] errors => ResultFactory.Create<T>(errors: errors),
                },
            _ => ResultFactory.Create(value: value),
        };

    /// <summary>Validates geometry with explicit mode specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> For<T>(T value, ValidationMode mode, IGeometryContext context) where T : notnull =>
        value switch {
            GeometryBase g => ValidationRules.GetOrCompileValidator(g.GetType(), mode)(g, context) switch {
                SystemError[] { Length: 0 } => ResultFactory.Create(value: value),
                SystemError[] errors => ResultFactory.Create<T>(errors: errors),
            },
            _ => ResultFactory.Create(value: value),
        };

    /// <summary>Validates Result with polymorphic mode and predicate dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Ensure<T>(
        this Result<T> result,
        IGeometryContext? context = null,
        ValidationMode? mode = null,
        Func<T, bool>? predicate = null,
        int? errorCode = null) =>
        (context, mode, predicate, errorCode) switch {
            ({ } ctx, { } m, null, null) => result.Bind(v => Check(v, ctx, m)),
            (null, null, { } pred, { } code) => result.Ensure(pred, ErrorRegistry.Get(code)),
            (null, null, { } pred, null) => result.Ensure(pred, ErrorRegistry.Get(3000)),
            _ => result,
        };

    /// <summary>Validates tolerance parameters with accumulation of errors.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] Tolerance(double absolute, double relative, double angleRadians) =>
        ValidationRules.For(absolute, relative, angleRadians);

    /// <summary>Validates predicate with conditional error creation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When(bool condition, int errorCode, string? context = null) =>
        ErrorFactory.When(condition, errorCode, context);

    /// <summary>Validates value with predicate and error code.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When<T>(T value, Func<T, bool> predicate, int errorCode, string? context = null) =>
        ErrorFactory.When(value, predicate, errorCode, context);

    /// <summary>Infers validation mode from runtime type for automatic validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValidationMode InferMode(object value) =>
        value switch {
            Curve => ValidationMode.Standard | ValidationMode.BoundingBox,
            Mesh => ValidationMode.Standard | ValidationMode.MeshSpecific,
            Brep => ValidationMode.Standard | ValidationMode.Topology,
            Surface => ValidationMode.Standard | ValidationMode.BoundingBox,
            GeometryBase => ValidationMode.Standard,
            _ => ValidationMode.None,
        };
}

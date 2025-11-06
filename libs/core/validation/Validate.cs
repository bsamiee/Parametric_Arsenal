using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Core.Validation;

/// <summary>Singular validation API with polymorphic dispatch for seamless Result integration.</summary>
public static class Validate {
    /// <summary>Validates geometry with automatic mode inference or explicit specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Check<T>(T value, IGeometryContext context, ValidationMode? mode = null) where T : notnull =>
        (value, mode ?? InferMode(value)) switch {
            (GeometryBase g, ValidationMode m) when m != ValidationMode.None =>
                ValidationRules.GetOrCompileValidator(g.GetType(), m)(g, context) switch {
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
        [.. (!(RhinoMath.IsValidDouble(absolute) && absolute > RhinoMath.ZeroTolerance) ?
            [ErrorRegistry.Get(3900),] : Array.Empty<SystemError>()),
            .. (!(RhinoMath.IsValidDouble(relative) && relative is >= 0d and < 1d) ?
            [ErrorRegistry.Get(3901),] : Array.Empty<SystemError>()),
            .. (!(RhinoMath.IsValidDouble(angleRadians) && angleRadians is > RhinoMath.Epsilon and <= RhinoMath.TwoPI) ?
            [ErrorRegistry.Get(3902),] : Array.Empty<SystemError>()),
        ];

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

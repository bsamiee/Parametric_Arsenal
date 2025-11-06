using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Core.Context;

/// <summary>Immutable tolerance context with validation and unit conversion.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record GeometryContext(
    double AbsoluteTolerance,
    double RelativeTolerance,
    double AngleToleranceRadians,
    UnitSystem Units) : IGeometryContext {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Abs={this.AbsoluteTolerance:g6}, Rel={this.RelativeTolerance:g6}, AngRad={this.AngleToleranceRadians:g6}, Units={this.Units}");

    /// <summary>Squared absolute distance tolerance for fast radius/knn checks without sqrt.</summary>
    [Pure]
    public double AbsoluteToleranceSquared => this.AbsoluteTolerance * this.AbsoluteTolerance;

    /// <summary>Angular tolerance in degrees for UI/display.</summary>
    [Pure]
    public double AngleToleranceDegrees => RhinoMath.ToDegrees(this.AngleToleranceRadians);

    /// <summary>Create context with robust defaults for a unit system.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> CreateWithDefaults(UnitSystem units) =>
        Create(absoluteTolerance: 0.01, relativeTolerance: 0.0, angleToleranceRadians: RhinoMath.ToRadians(1.0), units: units);

    /// <summary>Create context from RhinoDoc model settings.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> FromDocument(RhinoDoc doc) {
        ArgumentNullException.ThrowIfNull(doc);
        return Create(doc.ModelAbsoluteTolerance, doc.ModelRelativeTolerance, doc.ModelAngleToleranceRadians, doc.ModelUnitSystem);
    }

    /// <summary>Converts a length value to target units with full validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<double> ConvertLength(double value, UnitSystem targetUnits) =>
        RhinoMath.IsValidDouble(value) switch {
            false => ResultFactory.Create<double>(errors: [ErrorRegistry.Get(3920),]),
            true => this.GetLengthScale(targetUnits).Bind(scale =>
                RhinoMath.IsValidDouble(value * scale) switch {
                    true => ResultFactory.Create(value: value * scale),
                    false => ResultFactory.Create<double>(errors: [ErrorRegistry.Get(3920),]),
                }),
        };

    /// <summary>Returns scale factor from Units to target units.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<double> GetLengthScale(UnitSystem targetUnits) =>
        (targetUnits == this.Units, RhinoMath.UnitScale(this.Units, targetUnits)) switch {
            (true, _) => ResultFactory.Create(value: 1.0),
            (_, double scale) when RhinoMath.IsValidDouble(scale) && scale > RhinoMath.ZeroTolerance => ResultFactory.Create(value: scale),
            _ => ResultFactory.Create<double>(errors: [ErrorRegistry.Get(3920),]),
        };

    /// <summary>Validates if value differences are within absolute tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinAbsoluteTolerance(double a, double b) => RhinoMath.EpsilonEquals(a, b, this.AbsoluteTolerance);

    /// <summary>Validates if angle differences are within angular tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinAngleTolerance(double angleRadians1, double angleRadians2) =>
        RhinoMath.EpsilonEquals(angleRadians1, angleRadians2, this.AngleToleranceRadians);

    /// <summary>Validates if squared distance is within squared absolute tolerance (faster for distance checks).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinSquaredTolerance(double squaredDistance) => squaredDistance <= this.AbsoluteToleranceSquared;

    /// <summary>Create validated context with normalization for robustness.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> Create(double absoluteTolerance, double relativeTolerance, double angleToleranceRadians, UnitSystem units) =>
        (absoluteTolerance <= 0d ? 0.01 : absoluteTolerance, angleToleranceRadians <= 0d ? RhinoMath.ToRadians(1.0) : angleToleranceRadians) switch {
            (double normAbs, double normAngle) when !RhinoMath.IsValidDouble(normAbs) || !RhinoMath.IsValidDouble(relativeTolerance) || !RhinoMath.IsValidDouble(normAngle) =>
                ResultFactory.Create<GeometryContext>(errors: [ErrorRegistry.Get(3900),]),
            (double normAbs, double normAngle) => Validate.Tolerance(normAbs, relativeTolerance, normAngle) switch {
                SystemError[] { Length: > 0 } errors => ResultFactory.Create<GeometryContext>(errors: errors),
                _ => ResultFactory.Create(value: new GeometryContext(normAbs, relativeTolerance, normAngle, units)),
            },
        };
}

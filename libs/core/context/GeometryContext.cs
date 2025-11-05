using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.InteropServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Core.Context;

/// <summary>Immutable tolerance context with comprehensive validation and unit conversion capabilities.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record GeometryContext(
    double AbsoluteTolerance,
    double RelativeTolerance,
    double AngleToleranceRadians,
    UnitSystem Units) : IGeometryContext {
    [Pure]
    private string DebuggerDisplay =>
        string.Create(CultureInfo.InvariantCulture,
            $"Abs={this.AbsoluteTolerance:g3}, Rel={this.RelativeTolerance:g3}, Ang={this.AngleToleranceRadians:g3}, Units={this.Units}");

    /// <summary>Creates context with default tolerance values for specified unit system.</summary>
    [Pure]
    public static Result<GeometryContext> CreateWithDefaults(UnitSystem units) =>
        Create(0.01, 0d, RhinoMath.ToRadians(1.0), units);

    /// <summary>Creates context from RhinoDoc tolerance settings with validation.</summary>
    [Pure]
    public static Result<GeometryContext> FromDocument(RhinoDoc doc) {
        ArgumentNullException.ThrowIfNull(doc);
        return Create(
            doc.ModelAbsoluteTolerance,
            doc.ModelRelativeTolerance,
            doc.ModelAngleToleranceRadians,
            doc.ModelUnitSystem);
    }

    /// <summary>Converts length value between unit systems with validation.</summary>
    [Pure]
    public Result<double> ConvertLength(double value, UnitSystem targetUnits) =>
        targetUnits switch {
            UnitSystem units when units == this.Units => ResultFactory.Create(value: value),
            UnitSystem units => RhinoMath.UnitScale(this.Units, units) switch {
                double scale when !RhinoMath.IsValidDouble(scale) || scale <= RhinoMath.ZeroTolerance =>
                    ResultFactory.Create<double>(errors: [ValidationErrors.Context.InvalidUnitConversion]),
                double scale => ResultFactory.Create(value: value * scale),
            },
        };

    /// <summary>Creates validated geometry context with normalized tolerance values.</summary>
    [Pure]
    public static Result<GeometryContext> Create(
        double absoluteTolerance,
        double relativeTolerance,
        double angleToleranceRadians,
        UnitSystem units) =>
        (absoluteTolerance <= 0d ? 0.01 : absoluteTolerance,
         angleToleranceRadians <= 0d ? RhinoMath.ToRadians(1.0) : angleToleranceRadians) switch {
            (double normalizedAbsolute, double normalizedAngle) =>
                ValidationRules.For(normalizedAbsolute, relativeTolerance, normalizedAngle) switch {
                    { Length: > 0 } errors => ResultFactory.Create<GeometryContext>(errors: errors),
                    _ => ResultFactory.Create(value: new GeometryContext(normalizedAbsolute, relativeTolerance, normalizedAngle, units)),
                },
        };
}

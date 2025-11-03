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
    private string? _debuggerDisplay;

    [Pure]
    private string DebuggerDisplay => this._debuggerDisplay ??=
        string.Create(CultureInfo.InvariantCulture,
            $"Abs={this.AbsoluteTolerance:g3}, Rel={this.RelativeTolerance:g3}, Ang={this.AngleToleranceRadians:g3}, Units={this.Units}");

    /// <summary>Creates context with standard default tolerance values for specified unit system.</summary>
    [Pure]
    public static Result<GeometryContext> CreateWithDefaults(UnitSystem units) =>
        Create(0.01, 0d, RhinoMath.ToRadians(1.0), units);

    /// <summary>Creates context from existing RhinoDoc tolerance settings with validation.</summary>
    [Pure]
    public static Result<GeometryContext> FromDocument(RhinoDoc doc) {
        ArgumentNullException.ThrowIfNull(doc);
        return Create(
            doc.ModelAbsoluteTolerance,
            doc.ModelRelativeTolerance,
            doc.ModelAngleToleranceRadians,
            doc.ModelUnitSystem);
    }

    /// <summary>Converts length value between unit systems with comprehensive validation and error handling.</summary>
    [Pure]
    public Result<double> ConvertLength(double value, UnitSystem targetUnits) =>
        targetUnits switch {
            var units when units == this.Units => ResultFactory.Create(value: value),
            var units => RhinoMath.UnitScale(this.Units, units) switch {
                var scale when !RhinoMath.IsValidDouble(scale) || scale <= RhinoMath.ZeroTolerance =>
                    ResultFactory.Create<double>(errors: [ValidationErrors.Context.InvalidUnitConversion]),
                var scale => ResultFactory.Create(value: value * scale),
            },
        };

    /// <summary>Creates validated geometry context with normalized tolerance values and comprehensive error checking.</summary>
    [Pure]
    public static Result<GeometryContext> Create(
        double absoluteTolerance,
        double relativeTolerance,
        double angleToleranceRadians,
        UnitSystem units) {
        double normalizedAbsolute = absoluteTolerance <= 0d ? 0.01 : absoluteTolerance;
        double normalizedAngle = angleToleranceRadians <= 0d ? RhinoMath.ToRadians(1.0) : angleToleranceRadians;

        SystemError[] errors = ValidationRules.For(normalizedAbsolute, relativeTolerance, normalizedAngle);
        return errors.Length > 0
            ? ResultFactory.Create<GeometryContext>(errors: errors)
            : ResultFactory.Create(
                value: new GeometryContext(normalizedAbsolute, relativeTolerance, normalizedAngle, units));
    }
}

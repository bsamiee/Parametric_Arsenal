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
    private const double DefaultAbsoluteTolerance = 0.01;
    private const double DefaultRelativeTolerance = 0.0;
    private static readonly double DefaultAngleToleranceRadians = RhinoMath.ToRadians(1.0);

    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Abs={this.AbsoluteTolerance:g6}, Rel={this.RelativeTolerance:g6}, AngRad={this.AngleToleranceRadians:g6}, Units={this.Units}");

    /// <summary>Squared absolute tolerance for fast distance checks.</summary>
    [Pure]
    public double AbsoluteToleranceSquared => this.AbsoluteTolerance * this.AbsoluteTolerance;

    /// <summary>Angular tolerance in degrees.</summary>
    [Pure]
    public double AngleToleranceDegrees => RhinoMath.ToDegrees(this.AngleToleranceRadians);

    /// <summary>True if values differ within absolute tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinAbsoluteTolerance(double a, double b) => RhinoMath.EpsilonEquals(a, b, this.AbsoluteTolerance);

    /// <summary>True if angles differ within angular tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinAngleTolerance(double angleRadians1, double angleRadians2) =>
        RhinoMath.EpsilonEquals(angleRadians1, angleRadians2, this.AngleToleranceRadians);

    /// <summary>True if squared distance within squared tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinSquaredTolerance(double squaredDistance) => squaredDistance <= this.AbsoluteToleranceSquared;

    /// <summary>Creates context with defaults (0.01 abs, 0 rel, 1° angle).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> CreateWithDefaults(UnitSystem units) =>
        Create(absoluteTolerance: DefaultAbsoluteTolerance, relativeTolerance: DefaultRelativeTolerance, angleToleranceRadians: DefaultAngleToleranceRadians, units: units);

    /// <summary>Creates context from RhinoDoc model tolerances.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> FromDocument(RhinoDoc? doc) =>
        doc is null
            ? ResultFactory.Create<GeometryContext>(error: E.Results.NoValueProvided.WithContext("RhinoDoc"))
            : Create(absoluteTolerance: doc.ModelAbsoluteTolerance, relativeTolerance: doc.ModelRelativeTolerance, angleToleranceRadians: doc.ModelAngleToleranceRadians, units: doc.ModelUnitSystem);

    /// <summary>Scale factor from current units to target units.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<double> GetLengthScale(UnitSystem targetUnits) =>
        targetUnits == this.Units ? ResultFactory.Create(value: 1.0) :
        RhinoMath.UnitScale(this.Units, targetUnits) is double scale && RhinoMath.IsValidDouble(scale) && scale > RhinoMath.ZeroTolerance
            ? ResultFactory.Create(value: scale)
            : ResultFactory.Create<double>(errors: [E.Validation.InvalidUnitConversion,]);

    /// <summary>Converts length from current units to target units.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<double> ConvertLength(double value, UnitSystem targetUnits) =>
        !RhinoMath.IsValidDouble(value) ? ResultFactory.Create<double>(errors: [E.Validation.InvalidUnitConversion,]) : this.GetLengthScale(targetUnits).Bind(scale => RhinoMath.IsValidDouble(value * scale) ? ResultFactory.Create(value: value * scale) : ResultFactory.Create<double>(errors: [E.Validation.InvalidUnitConversion,]));

    /// <summary>Creates validated context with normalization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<GeometryContext> Create(double absoluteTolerance, double relativeTolerance, double angleToleranceRadians, UnitSystem units) {
        double normalizedAbsoluteTolerance = absoluteTolerance <= 0d ? DefaultAbsoluteTolerance : absoluteTolerance;
        double normalizedRelativeTolerance = relativeTolerance <= 0d ? DefaultRelativeTolerance : relativeTolerance;
        double normalizedAngleToleranceRadians = angleToleranceRadians <= 0d ? DefaultAngleToleranceRadians : angleToleranceRadians;

        List<SystemError> invalidParameters = new();
        if (!RhinoMath.IsValidDouble(normalizedAbsoluteTolerance)) { invalidParameters.Add(E.Validation.ToleranceAbsoluteInvalid); }
        if (!RhinoMath.IsValidDouble(normalizedRelativeTolerance)) { invalidParameters.Add(E.Validation.ToleranceRelativeInvalid); }
        if (!RhinoMath.IsValidDouble(normalizedAngleToleranceRadians)) { invalidParameters.Add(E.Validation.ToleranceAngleInvalid); }
        SystemError[] invalidParametersArray = [.. invalidParameters];

        return invalidParametersArray is { Length: > 0 } parameterErrors
            ? ResultFactory.Create<GeometryContext>(errors: parameterErrors)
            : ValidationRules.For(normalizedAbsoluteTolerance, normalizedRelativeTolerance, normalizedAngleToleranceRadians) is SystemError[] { Length: > 0 } validationErrors
                ? ResultFactory.Create<GeometryContext>(errors: validationErrors)
                : ResultFactory.Create(value: new GeometryContext(normalizedAbsoluteTolerance, normalizedRelativeTolerance, normalizedAngleToleranceRadians, units));
    }
}

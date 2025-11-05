using System.Diagnostics.Contracts;
using Arsenal.Core.Results;
using Rhino;

namespace Arsenal.Core.Context;

/// <summary>Contract for tolerance-aware geometry evaluation and unit conversion.</summary>
public interface IGeometryContext {
    /// <summary>Absolute distance tolerance in current Units.</summary>
    public double AbsoluteTolerance { get; }

    /// <summary>Squared absolute distance tolerance for fast radius/kNN without sqrt.</summary>
    public double AbsoluteToleranceSquared { get; }

    /// <summary>Relative tolerance for size-dependent checks (rarely used in modern Rhino).</summary>
    public double RelativeTolerance { get; }

    /// <summary>Angular tolerance in radians for computation.</summary>
    public double AngleToleranceRadians { get; }

    /// <summary>Angular tolerance in degrees for UI/display.</summary>
    public double AngleToleranceDegrees { get; }

    /// <summary>Model unit system for this context.</summary>
    public UnitSystem Units { get; }

    /// <summary>Converts a length from Units to target units with validation.</summary>
    [Pure] public Result<double> ConvertLength(double value, UnitSystem targetUnits);

    /// <summary>Returns the scale factor to convert from Units to target units.</summary>
    [Pure] public Result<double> GetLengthScale(UnitSystem targetUnits);

    /// <summary>Validates if value differences are within absolute tolerance.</summary>
    [Pure] public bool IsWithinAbsoluteTolerance(double a, double b);

    /// <summary>Validates if angle differences are within angular tolerance.</summary>
    [Pure] public bool IsWithinAngleTolerance(double angleRadians1, double angleRadians2);

    /// <summary>Validates if squared distance is within squared absolute tolerance.</summary>
    [Pure] public bool IsWithinSquaredTolerance(double squaredDistance);
}

using System.Diagnostics.Contracts;
using Arsenal.Core.Results;
using Rhino;

namespace Arsenal.Core.Context;

/// <summary>Tolerance-aware geometry evaluation and unit conversion.</summary>
public interface IGeometryContext {
    /// <summary>Absolute distance tolerance in current units.</summary>
    public double AbsoluteTolerance { get; }

    /// <summary>Squared absolute tolerance for fast distance checks.</summary>
    public double AbsoluteToleranceSquared { get; }

    /// <summary>Relative tolerance for size-dependent checks.</summary>
    public double RelativeTolerance { get; }

    /// <summary>Angular tolerance in radians.</summary>
    public double AngleToleranceRadians { get; }

    /// <summary>Angular tolerance in degrees.</summary>
    public double AngleToleranceDegrees { get; }

    /// <summary>Model unit system.</summary>
    public UnitSystem Units { get; }

    /// <summary>Converts length from current units to target units.</summary>
    [Pure] public Result<double> ConvertLength(double value, UnitSystem targetUnits);

    /// <summary>Scale factor from current units to target units.</summary>
    [Pure] public Result<double> GetLengthScale(UnitSystem targetUnits);

    /// <summary>True if values differ within absolute tolerance.</summary>
    [Pure] public bool IsWithinAbsoluteTolerance(double a, double b);

    /// <summary>True if angles differ within angular tolerance.</summary>
    [Pure] public bool IsWithinAngleTolerance(double angleRadians1, double angleRadians2);

    /// <summary>True if squared distance within squared tolerance.</summary>
    [Pure] public bool IsWithinSquaredTolerance(double squaredDistance);
}

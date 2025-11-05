using Arsenal.Core.Results;
using Rhino;

namespace Arsenal.Core.Context;

/// <summary>Contract for tolerance-aware geometry evaluation and unit conversion.</summary>
public interface IGeometryContext {
    public double AbsoluteTolerance { get; }
    public double RelativeTolerance { get; }
    public double AngleToleranceRadians { get; }
    public UnitSystem Units { get; }

    /// <summary>Converts length value between unit systems with validation.</summary>
    public Result<double> ConvertLength(double value, UnitSystem targetUnits);
}

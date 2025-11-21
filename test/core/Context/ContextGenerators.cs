using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using CsCheck;
using Rhino;

namespace Arsenal.Core.Tests.Context;

/// <summary>Context generators with zero-allocation static lambdas.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Test generators used for property-based testing")]
public static class ContextGenerators {
    private static readonly UnitSystem[] AllUnitSystems = [
        UnitSystem.None,
        UnitSystem.Angstroms,
        UnitSystem.Nanometers,
        UnitSystem.Microns,
        UnitSystem.Millimeters,
        UnitSystem.Centimeters,
        UnitSystem.Decimeters,
        UnitSystem.Meters,
        UnitSystem.Dekameters,
        UnitSystem.Hectometers,
        UnitSystem.Kilometers,
        UnitSystem.Megameters,
        UnitSystem.Gigameters,
        UnitSystem.Microinches,
        UnitSystem.Mils,
        UnitSystem.Inches,
        UnitSystem.Feet,
        UnitSystem.Yards,
        UnitSystem.Miles,
        UnitSystem.PrinterPoints,
        UnitSystem.PrinterPicas,
        UnitSystem.NauticalMiles,
        UnitSystem.AstronomicalUnits,
        UnitSystem.LightYears,
        UnitSystem.Parsecs,
    ];

    private static readonly UnitSystem[] CommonUnitSystems = [
        UnitSystem.Millimeters,
        UnitSystem.Centimeters,
        UnitSystem.Meters,
        UnitSystem.Inches,
        UnitSystem.Feet,
    ];

    /// <summary>Generates valid absolute tolerance values (positive, non-NaN, non-infinite).</summary>
    [Pure]
    public static Gen<double> ValidToleranceGen => Gen.Double[1e-9, 1e3].Where(static d => RhinoMath.IsValidDouble(d) && d > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid relative tolerance values (0 to 1, exclusive).</summary>
    [Pure]
    public static Gen<double> ValidRelativeToleranceGen => Gen.Double[0.0, 0.999].Where(static d => RhinoMath.IsValidDouble(d) && d >= 0.0 && d < 1.0);

    /// <summary>Generates valid angle tolerance values in radians.</summary>
    [Pure]
    public static Gen<double> ValidAngleToleranceGen => Gen.Double[RhinoMath.Epsilon, RhinoMath.TwoPI].Where(static d => RhinoMath.IsValidDouble(d));

    /// <summary>Generates all Rhino unit systems with uniform distribution.</summary>
    [Pure]
    public static Gen<UnitSystem> UnitSystemGen => Gen.OneOfConst(AllUnitSystems);

    /// <summary>Generates common unit systems (higher probability for typical use cases).</summary>
    [Pure]
    public static Gen<UnitSystem> CommonUnitSystemGen => Gen.OneOfConst(CommonUnitSystems);

    /// <summary>Generates valid GeometryContext via Create() method with property-based tolerances.</summary>
    [Pure]
    public static Gen<GeometryContext> GeometryContextGen =>
        ValidToleranceGen
            .SelectMany(absTol =>
                ValidRelativeToleranceGen.SelectMany(relTol =>
                    ValidAngleToleranceGen.SelectMany(angTol =>
                        UnitSystemGen.Select(units =>
                            GeometryContext.Create(
                                absoluteTolerance: absTol,
                                relativeTolerance: relTol,
                                angleToleranceRadians: angTol,
                                units: units)))))
            .Where(static result => result.IsSuccess)
            .Select(static result => result.Value);

    /// <summary>Generates GeometryContext with common defaults for typical test scenarios.</summary>
    [Pure]
    public static Gen<GeometryContext> DefaultContextGen =>
        CommonUnitSystemGen
            .Select(units => GeometryContext.CreateWithDefaults(units: units))
            .Where(static result => result.IsSuccess)
            .Select(static result => result.Value);

    /// <summary>Generates GeometryContext with high-precision tolerances for numerical testing.</summary>
    [Pure]
    public static Gen<GeometryContext> HighPrecisionContextGen =>
        Gen.Double[1e-12, 1e-6]
            .Where(static d => RhinoMath.IsValidDouble(d))
            .SelectMany(tol =>
                CommonUnitSystemGen.Select(units =>
                    GeometryContext.Create(
                        absoluteTolerance: tol,
                        relativeTolerance: 0.0,
                        angleToleranceRadians: RhinoMath.ToRadians(0.01),
                        units: units)))
            .Where(static result => result.IsSuccess)
            .Select(static result => result.Value);

    /// <summary>Generates GeometryContext with low-precision tolerances for coarse geometry.</summary>
    [Pure]
    public static Gen<GeometryContext> LowPrecisionContextGen =>
        Gen.Double[0.1, 10.0]
            .Where(static d => RhinoMath.IsValidDouble(d))
            .SelectMany(tol =>
                CommonUnitSystemGen.Select(units =>
                    GeometryContext.Create(
                        absoluteTolerance: tol,
                        relativeTolerance: 0.0,
                        angleToleranceRadians: RhinoMath.ToRadians(5.0),
                        units: units)))
            .Where(static result => result.IsSuccess)
            .Select(static result => result.Value);
}

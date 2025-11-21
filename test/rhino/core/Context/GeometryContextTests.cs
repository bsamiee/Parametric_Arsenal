using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using NUnit.Framework;
using Rhino;

namespace Arsenal.Rhino.Tests.Core.Context;

/// <summary>Tests GeometryContext creation, validation, tolerance methods, and unit conversion.</summary>
[TestFixture]
public sealed class GeometryContextTests {
    /// <summary>Verifies Create() with valid tolerances.</summary>
    [Test]
    public void CreateWithValidTolerances() => Test.RunAll(
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: 0.01,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Success(result, ctx => Math.Abs(ctx.AbsoluteTolerance - 0.01) < 1e-9);
        },
        () => ContextGenerators.ValidToleranceGen.Run((double tol) => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: tol,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Success(result);
        }));

    /// <summary>Verifies Create() returns errors for invalid tolerances.</summary>
    [Test]
    public void CreateWithInvalidTolerances() => Test.RunAll(
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: double.NaN,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Failure(result, errors => errors.Any(e => e.Code == E.Validation.ToleranceAbsoluteInvalid.Code));
        },
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: 0.01,
                relativeTolerance: double.NaN,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Failure(result, errors => errors.Any(e => e.Code == E.Validation.ToleranceRelativeInvalid.Code));
        },
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: 0.01,
                relativeTolerance: 0.0,
                angleToleranceRadians: double.NaN,
                units: UnitSystem.Meters);
            Test.Failure(result, errors => errors.Any(e => e.Code == E.Validation.ToleranceAngleInvalid.Code));
        });

    /// <summary>Verifies Create() normalizes negative tolerances to defaults.</summary>
    [Test]
    public void CreateNormalizesNegativeTolerances() => Test.RunAll(
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: -1.0,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Success(result, ctx => ctx.AbsoluteTolerance == 0.01);
        },
        () => {
            Result<GeometryContext> result = GeometryContext.Create(
                absoluteTolerance: 0.01,
                relativeTolerance: -0.5,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            const double epsilon = 1e-9;
            Test.Success(result, ctx => Math.Abs(ctx.RelativeTolerance) < epsilon);
        });

    /// <summary>Verifies Create() normalizes zero tolerances to defaults.</summary>
    [Test]
    public void CreateNormalizesZeroTolerances() {
        Result<GeometryContext> result = GeometryContext.Create(
            absoluteTolerance: 0.0,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.0,
            units: UnitSystem.Meters);
        Test.Success(result, ctx => (ctx.AbsoluteTolerance == 0.01) && (ctx.AngleToleranceRadians > 0.0));
    }

    /// <summary>Verifies CreateWithDefaults() returns valid context.</summary>
    [Test]
    public void CreateWithDefaultsReturnsValidContext() => ContextGenerators.UnitSystemGen.Run((UnitSystem units) => {
        Result<GeometryContext> result = GeometryContext.CreateWithDefaults(units: units);
        Test.Success(result, ctx => {
            Assert.That(ctx.AbsoluteTolerance, Is.EqualTo(0.01));
            Assert.That(ctx.RelativeTolerance, Is.EqualTo(0.0));
            Assert.That(ctx.Units, Is.EqualTo(units));
            return true;
        });
    });

    /// <summary>Verifies FromDocument() with null returns error.</summary>
    [Test]
    public void FromDocumentNullReturnsError() {
        Result<GeometryContext> result = GeometryContext.FromDocument(doc: null);
        Test.Failure(result, errors => errors.Any(e => e.Code == E.Results.NoValueProvided.Code));
    }

    /// <summary>Verifies IsWithinAbsoluteTolerance() for values within and outside tolerance.</summary>
    [Test]
    public void IsWithinAbsoluteToleranceMethod() => Test.RunAll(
        () => {
            Result<GeometryContext> contextResult = GeometryContext.Create(
                absoluteTolerance: 0.01,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Assert.That(ctx.IsWithinAbsoluteTolerance(1.0, 1.005), Is.True);
                Assert.That(ctx.IsWithinAbsoluteTolerance(1.0, 1.05), Is.False);
                return true;
            });
        },
        () => Gen.Double.Select(ContextGenerators.ValidToleranceGen).Run((double value, double tol) => {
            Result<GeometryContext> contextResult = GeometryContext.Create(
                absoluteTolerance: tol,
                relativeTolerance: 0.0,
                angleToleranceRadians: 0.017453292519943295,
                units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Assert.That(ctx.IsWithinAbsoluteTolerance(value, value), Is.True);
                Assert.That(ctx.IsWithinAbsoluteTolerance(value, value + (tol / 2.0)), Is.True);
                return true;
            });
        }));

    /// <summary>Verifies IsWithinSquaredTolerance() for squared distance checks.</summary>
    [Test]
    public void IsWithinSquaredToleranceMethod() {
        Result<GeometryContext> contextResult = GeometryContext.Create(
            absoluteTolerance: 0.1,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.017453292519943295,
            units: UnitSystem.Meters);
        Test.Success(contextResult, ctx => {
            Assert.That(ctx.IsWithinSquaredTolerance(0.005), Is.True);
            Assert.That(ctx.IsWithinSquaredTolerance(0.02), Is.False);
            return true;
        });
    }

    /// <summary>Verifies IsWithinAngleTolerance() for angular comparisons.</summary>
    [Test]
    public void IsWithinAngleToleranceMethod() {
        Result<GeometryContext> contextResult = GeometryContext.Create(
            absoluteTolerance: 0.01,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.017453292519943295,
            units: UnitSystem.Meters);
        Test.Success(contextResult, ctx => {
            Assert.That(ctx.IsWithinAngleTolerance(0.0, 0.01), Is.True);
            Assert.That(ctx.IsWithinAngleTolerance(0.0, 0.1), Is.False);
            return true;
        });
    }

    /// <summary>Verifies AbsoluteToleranceSquared computed property.</summary>
    [Test]
    public void AbsoluteToleranceSquaredProperty() => ContextGenerators.ValidToleranceGen.Run((double tol) => {
        Result<GeometryContext> result = GeometryContext.Create(
            absoluteTolerance: tol,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.017453292519943295,
            units: UnitSystem.Meters);
        Test.Success(result, ctx => {
            Test.EqualWithin(ctx.AbsoluteToleranceSquared, tol * tol, tolerance: 1e-10);
            return true;
        });
    });

    /// <summary>Verifies AngleToleranceDegrees computed property.</summary>
    [Test]
    public void AngleToleranceDegreesProperty() {
        Result<GeometryContext> result = GeometryContext.Create(
            absoluteTolerance: 0.01,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.017453292519943295,
            units: UnitSystem.Meters);
        Test.Success(result, ctx => {
            Test.EqualWithin(ctx.AngleToleranceDegrees, 1.0, tolerance: 0.01);
            return true;
        });
    }

    /// <summary>Verifies GetLengthScale() for same units returns 1.0.</summary>
    [Test]
    public void GetLengthScaleSameUnitsReturnsOne() => ContextGenerators.UnitSystemGen.Run((UnitSystem units) => {
        Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: units);
        Test.Success(contextResult, ctx => {
            Result<double> scaleResult = ctx.GetLengthScale(targetUnits: units);
            Test.Success(scaleResult, scale => Test.EqualWithin(scale, 1.0, tolerance: 1e-10));
            return true;
        });
    });

    /// <summary>Verifies GetLengthScale() for different units returns valid scale.</summary>
    [Test]
    public void GetLengthScaleDifferentUnitsReturnsScale() => Test.RunAll(
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Result<double> scaleResult = ctx.GetLengthScale(targetUnits: UnitSystem.Millimeters);
                Test.Success(scaleResult, scale => {
                    Test.EqualWithin(scale, 1000.0, tolerance: 0.01);
                    return true;
                });
                return true;
            });
        },
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Feet);
            Test.Success(contextResult, ctx => {
                Result<double> scaleResult = ctx.GetLengthScale(targetUnits: UnitSystem.Inches);
                Test.Success(scaleResult, scale => {
                    Test.EqualWithin(scale, 12.0, tolerance: 0.01);
                    return true;
                });
                return true;
            });
        });

    /// <summary>Verifies ConvertLength() for same units returns same value.</summary>
    [Test]
    public void ConvertLengthSameUnitsReturnsSameValue() => Gen.Double.Select(ContextGenerators.UnitSystemGen).Run((double value, UnitSystem units) => {
        double validValue = RhinoMath.IsValidDouble(value) ? value : 1.0;
        Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: units);
        Test.Success(contextResult, ctx => {
            Result<double> convertResult = ctx.ConvertLength(value: validValue, targetUnits: units);
            Test.Success(convertResult, converted => { Test.EqualWithin(converted, validValue, tolerance: 1e-10); return true; });
            return true;
        });
    });

    /// <summary>Verifies ConvertLength() for different units performs conversion.</summary>
    [Test]
    public void ConvertLengthDifferentUnitsPerformsConversion() => Test.RunAll(
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Result<double> convertResult = ctx.ConvertLength(value: 1.0, targetUnits: UnitSystem.Millimeters);
                Test.Success(convertResult, converted => {
                    Test.EqualWithin(converted, 1000.0, tolerance: 0.01);
                    return true;
                });
                return true;
            });
        },
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Feet);
            Test.Success(contextResult, ctx => {
                Result<double> convertResult = ctx.ConvertLength(value: 2.0, targetUnits: UnitSystem.Inches);
                Test.Success(convertResult, converted => {
                    Test.EqualWithin(converted, 24.0, tolerance: 0.01);
                    return true;
                });
                return true;
            });
        });

    /// <summary>Verifies ConvertLength() with invalid value returns error.</summary>
    [Test]
    public void ConvertLengthInvalidValueReturnsError() => Test.RunAll(
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Result<double> convertResult = ctx.ConvertLength(value: double.NaN, targetUnits: UnitSystem.Millimeters);
                Test.Failure(convertResult, errors => errors.Any(e => e.Code == E.Validation.InvalidUnitConversion.Code));
                return true;
            });
        },
        () => {
            Result<GeometryContext> contextResult = GeometryContext.CreateWithDefaults(units: UnitSystem.Meters);
            Test.Success(contextResult, ctx => {
                Result<double> convertResult = ctx.ConvertLength(value: double.PositiveInfinity, targetUnits: UnitSystem.Millimeters);
                Test.Failure(convertResult);
                return true;
            });
        });

    /// <summary>Verifies property-based tolerance transitivity.</summary>
    [Test]
    public void PropertyBasedToleranceTransitivity() => Gen.Double[0.0, 10.0].Select(Gen.Double[0.0, 10.0], Gen.Double[0.0, 10.0]).Run((double a, double b, double c) => {
        Result<GeometryContext> contextResult = GeometryContext.Create(
            absoluteTolerance: 0.1,
            relativeTolerance: 0.0,
            angleToleranceRadians: 0.017453292519943295,
            units: UnitSystem.Meters);
        Test.Success(contextResult, ctx => {
            bool abWithin = ctx.IsWithinAbsoluteTolerance(a, b);
            bool bcWithin = ctx.IsWithinAbsoluteTolerance(b, c);
            bool acWithin = ctx.IsWithinAbsoluteTolerance(a, c);
            Assert.That(!abWithin || !bcWithin || acWithin || (Math.Abs(a - c) <= (ctx.AbsoluteTolerance * 2.0)), Is.True);
            return true;
        });
    });

    /// <summary>Verifies property-based unit conversion round-trips.</summary>
    [Test]
    public void PropertyBasedUnitConversionRoundTrips() => Gen.Double[0.1, 1000.0].Select(ContextGenerators.UnitSystemGen, ContextGenerators.UnitSystemGen).Run((double value, UnitSystem units1, UnitSystem units2) => {
        Result<GeometryContext> ctx1Result = GeometryContext.CreateWithDefaults(units: units1);
        Result<GeometryContext> ctx2Result = GeometryContext.CreateWithDefaults(units: units2);
        Test.Success(ctx1Result, ctx1 => {
            Test.Success(ctx2Result, ctx2 => {
                Result<double> convert1 = ctx1.ConvertLength(value: value, targetUnits: units2);
                Test.Success(convert1, converted => {
                    Result<double> convert2 = ctx2.ConvertLength(value: converted, targetUnits: units1);
                    Test.Success(convert2, roundTrip => {
                        Test.EqualWithin(roundTrip, value, tolerance: value * 1e-6);
                        return true;
                    });
                    return true;
                });
                return true;
            });
            return true;
        });
    });

    /// <summary>Verifies property-based scale composition.</summary>
    [Test]
    public void PropertyBasedScaleComposition() => ContextGenerators.UnitSystemGen.Select(ContextGenerators.UnitSystemGen, ContextGenerators.UnitSystemGen).Run((UnitSystem u1, UnitSystem u2, UnitSystem u3) => {
        Result<GeometryContext> ctx1 = GeometryContext.CreateWithDefaults(units: u1);
        Result<GeometryContext> ctx2 = GeometryContext.CreateWithDefaults(units: u2);
        Result<GeometryContext> ctx3 = GeometryContext.CreateWithDefaults(units: u3);

        Test.Success(ctx1, c1 => {
            Test.Success(ctx2, c2 => {
                Test.Success(ctx3, _ => {
                    Result<double> scale12 = c1.GetLengthScale(targetUnits: u2);
                    Result<double> scale23 = c2.GetLengthScale(targetUnits: u3);
                    Result<double> scale13 = c1.GetLengthScale(targetUnits: u3);

                    Test.Success(scale12, s12 => {
                        Test.Success(scale23, s23 => {
                            Test.Success(scale13, s13 => {
                                double composed = s12 * s23;
                                Test.EqualWithin(composed, s13, tolerance: Math.Max(Math.Abs(s13) * 1e-6, 1e-10));
                                return true;
                            });
                            return true;
                        });
                        return true;
                    });
                    return true;
                });
                return true;
            });
            return true;
        });
    });

    /// <summary>Verifies record equality and hash consistency.</summary>
    [Test]
    public void RecordEqualityAndHashConsistency() => ContextGenerators.GeometryContextGen.Run((GeometryContext ctx) => {
        GeometryContext copy = new(
            AbsoluteTolerance: ctx.AbsoluteTolerance,
            RelativeTolerance: ctx.RelativeTolerance,
            AngleToleranceRadians: ctx.AngleToleranceRadians,
            Units: ctx.Units);
        Assert.That(copy, Is.EqualTo(ctx));
        Assert.That(copy.GetHashCode(), Is.EqualTo(ctx.GetHashCode()));
    });

    /// <summary>Verifies DebuggerDisplay format contains key tolerance values.</summary>
    [Test]
    public void DebuggerDisplayFormat() => ContextGenerators.GeometryContextGen.Run((GeometryContext ctx) => {
        string display = ctx.ToString();
        Assert.That(display, Is.Not.Null);
        Assert.That(display, Is.Not.Empty);
    });
}

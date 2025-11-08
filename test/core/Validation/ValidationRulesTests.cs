using Arsenal.Core.Errors;
using Arsenal.Core.Validation;
using Arsenal.Tests.Common;
using CsCheck;
using Rhino;
using Xunit;

namespace Arsenal.Core.Tests.Validation;

/// <summary>Comprehensive tests for ValidationRules.For tolerance validation covering edge cases, boundary conditions, and property-based validation.</summary>
public sealed class ValidationRulesTests {
    /// <summary>Verifies tolerance validation For double with comprehensive edge cases and algebraic properties.</summary>
    [Fact]
    public void ToleranceValidationComprehensiveSemantics() => TestGen.RunAll(
        () => Assert.Empty(ValidationRules.For(input: 0.01, args: [0.001, 0.1,])),
        () => Assert.Empty(ValidationRules.For(input: 1.0, args: [0.0, 0.5,])),
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.0, args: [0.001, 0.1,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAbsoluteInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: -0.01, args: [0.001, 0.1,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAbsoluteInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [-0.1, 0.1,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceRelativeInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [0.001, 1.0,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceRelativeInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [0.001, 0.0,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAngleInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [0.001, 10.0,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAngleInvalid.Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.0, args: [-0.1, 0.0,]);
            Assert.Equal(3, errors.Length);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: double.NaN, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        });

    /// <summary>Verifies tolerance validation boundary cases with exact threshold values.</summary>
    [Fact]
    public void ToleranceBoundaryValidation() => TestGen.RunAll(
        () => Assert.Empty(ValidationRules.For(input: RhinoMath.ZeroTolerance + 1e-10, args: [0.0, RhinoMath.TwoPI,])),
        () => {
            SystemError[] errors = ValidationRules.For(input: RhinoMath.ZeroTolerance, args: [0.0, 0.1,]);
            Assert.NotEmpty(errors);
        },
        () => Assert.Empty(ValidationRules.For(input: 0.01, args: [0.0, RhinoMath.TwoPI,])),
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [0.0, RhinoMath.TwoPI + 0.01,]);
            Assert.NotEmpty(errors);
        },
        () => Assert.Empty(ValidationRules.For(input: 0.01, args: [0.999, RhinoMath.Epsilon + 0.001,])),
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [1.0, 0.1,]);
            Assert.NotEmpty(errors);
        });

    /// <summary>Verifies For throws ArgumentException for invalid argument patterns.</summary>
    [Fact]
    public void ForThrowsArgumentExceptionForInvalidArgumentPatterns() => TestGen.RunAll(
        () => Assert.Throws<ArgumentException>(() => ValidationRules.For(input: 0.01, args: [0.5,])),
        () => Assert.Throws<ArgumentException>(() => ValidationRules.For(input: 0.01, args: [])),
        () => Assert.Throws<ArgumentException>(() => ValidationRules.For(input: "string", args: [0.5, 0.1,])),
        () => Assert.Throws<ArgumentException>(() => ValidationRules.For(input: 42, args: [0.5, 0.1,])));

    /// <summary>Verifies property-based tolerance validation with random valid inputs.</summary>
    [Fact]
    public void PropertyBasedToleranceValidationRandomValidInputs() =>
        Gen.Double[0.001, 10.0].Select(Gen.Double[0.0, 0.999], Gen.Double[0.001, RhinoMath.TwoPI]).Run(
            (Action<double, double, double>)((abs, rel, angle) => {
                SystemError[] errors = ValidationRules.For(input: abs, args: [rel, angle,]);
                Assert.Empty(errors);
            }), 100);

    /// <summary>Verifies property-based tolerance validation with random invalid absolute tolerance.</summary>
    [Fact]
    public void PropertyBasedToleranceValidationRandomInvalidInputs() =>
        Gen.Double[-10.0, 0.0].Run((Action<double>)(abs => {
            SystemError[] errors = ValidationRules.For(input: abs, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAbsoluteInvalid.Code);
        }), 100);

    /// <summary>Verifies validation errors contain expected domain and code range.</summary>
    [Fact]
    public void ValidationErrorsDomainAndCodeRange() => TestGen.RunAll(
        () => {
            SystemError[] toleranceErrors = ValidationRules.For(input: -0.01, args: [0.5, 0.1,]);
            Assert.All(toleranceErrors, e => {
                Assert.Equal(ErrorDomain.Validation, e.Domain);
                Assert.True(e.Code is >= 3000 and < 4000);
            });
        },
        () => {
            SystemError[] multiErrors = ValidationRules.For(input: 0.0, args: [-0.1, 10.0,]);
            Assert.All(multiErrors, e => {
                Assert.Equal(ErrorDomain.Validation, e.Domain);
                Assert.True(e.Code is >= 3900 and < 3910);
            });
        });

    /// <summary>Verifies tolerance array validation with various combinations.</summary>
    [Fact]
    public void ToleranceArrayValidationCombinations() => TestGen.RunAll(
        () => {
            SystemError[] errors1 = ValidationRules.For(input: 0.001, args: [0.5, 0.1,]);
            SystemError[] errors2 = ValidationRules.For(input: 0.001, args: [0.5, 0.1,]);
            Assert.Equal(errors1.Length, errors2.Length);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.001, args: [0.5, 3.0,]);
            Assert.Single(errors);
            Assert.Equal(E.Validation.ToleranceRelativeInvalid.Code, errors[0].Code);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: double.PositiveInfinity, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        });

    /// <summary>Verifies For determinism: same inputs produce same outputs across multiple calls.</summary>
    [Fact]
    public void ForDeterminismPropertyBased() =>
        Gen.Double[0.001, 10.0].Select(Gen.Double[0.0, 0.999], Gen.Double[0.001, RhinoMath.TwoPI]).Run(
            (Action<double, double, double>)((abs, rel, angle) => {
                SystemError[] errors1 = ValidationRules.For(input: abs, args: [rel, angle,]);
                SystemError[] errors2 = ValidationRules.For(input: abs, args: [rel, angle,]);
                Assert.Equal(errors1.Length, errors2.Length);
            }), 50);

    /// <summary>Verifies error messages are non-empty and contextual.</summary>
    [Fact]
    public void ErrorMessagesNonEmptyAndContextual() => TestGen.RunAll(
        () => {
            SystemError[] errors = ValidationRules.For(input: -0.01, args: [0.5, 0.1,]);
            Assert.All(errors, e => Assert.NotEmpty(e.Message));
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [1.5, 0.1,]);
            Assert.All(errors, e => Assert.NotEmpty(e.Message));
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [0.5, -0.1,]);
            Assert.All(errors, e => Assert.NotEmpty(e.Message));
        });

    /// <summary>Verifies multiple tolerance violations are accumulated correctly.</summary>
    [Fact]
    public void MultipleToleranceViolationsAccumulatedCorrectly() {
        SystemError[] errors = ValidationRules.For(input: -1.0, args: [2.0, -0.5,]);

        TestGen.RunAll(
            () => Assert.True(errors.Length >= 2),
            () => Assert.Contains(errors, e => e.Code == E.Validation.ToleranceAbsoluteInvalid.Code),
            () => Assert.Contains(errors, e => e.Code == E.Validation.ToleranceRelativeInvalid.Code || e.Code == E.Validation.ToleranceAngleInvalid.Code));
    }

    /// <summary>Verifies edge cases with special double values (NaN, Infinity, Epsilon).</summary>
    [Fact]
    public void SpecialDoubleValueValidation() => TestGen.RunAll(
        () => {
            SystemError[] errors = ValidationRules.For(input: double.NaN, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: double.PositiveInfinity, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: double.NegativeInfinity, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        },
        () => {
            SystemError[] errors = ValidationRules.For(input: double.Epsilon, args: [0.5, 0.1,]);
            Assert.NotEmpty(errors);
        },
        () => Assert.Empty(ValidationRules.For(input: RhinoMath.Epsilon, args: [0.0, RhinoMath.Epsilon,])));

    /// <summary>Verifies validation is consistent for boundary values near thresholds.</summary>
    [Fact]
    public void BoundaryValueConsistency() => TestGen.RunAll(
        () => {
            double justAbove = RhinoMath.ZeroTolerance * 1.1;
            SystemError[] errors = ValidationRules.For(input: justAbove, args: [0.0, 0.1,]);
            Assert.Empty(errors);
        },
        () => {
            double justBelow = 0.999999;
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [justBelow, 0.1,]);
            Assert.Empty(errors);
        },
        () => {
            double atBoundary = 1.0;
            SystemError[] errors = ValidationRules.For(input: 0.01, args: [atBoundary, 0.1,]);
            Assert.NotEmpty(errors);
        });
}

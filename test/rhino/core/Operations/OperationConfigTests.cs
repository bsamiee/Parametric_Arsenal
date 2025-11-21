using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Tests.Validation;
using Arsenal.Core.Validation;
using Arsenal.Rhino.Tests.Core.Context;
using Arsenal.Tests.Common;
using CsCheck;
using NUnit.Framework;
using Rhino;

namespace Arsenal.Rhino.Tests.Core.Operations;

/// <summary>Tests OperationConfig creation, defaults, and immutability.</summary>
[TestFixture]
public sealed class OperationConfigTests {
    private static readonly IGeometryContext TestContext = new GeometryContext(
        AbsoluteTolerance: 0.01,
        RelativeTolerance: 0.0,
        AngleToleranceRadians: 0.017453292519943295,
        Units: UnitSystem.Meters);

    /// <summary>Verifies required Context property must be provided.</summary>
    [Test]
    public void RequiredContextProperty() {
        OperationConfig<int, int> config = new() { Context = TestContext };
        Assert.That(config.Context, Is.Not.Null);
        Assert.That(config.Context, Is.EqualTo(TestContext));
    }

    /// <summary>Verifies default values for optional properties.</summary>
    [Test]
    public void DefaultValuesOptionalProperties() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext };
            Assert.That(config.ValidationMode, Is.EqualTo(V.None));
            Assert.That(config.ValidationArgs, Is.Null);
            Assert.That(config.AccumulateErrors, Is.False);
            Assert.That(config.PreTransform, Is.Null);
            Assert.That(config.PostTransform, Is.Null);
            Assert.That(config.InputFilter, Is.Null);
            Assert.That(config.OutputFilter, Is.Null);
            Assert.That(config.EnableParallel, Is.False);
            Assert.That(config.MaxDegreeOfParallelism, Is.EqualTo(-1));
            Assert.That(config.SkipInvalid, Is.False);
            Assert.That(config.ShortCircuit, Is.True);
            Assert.That(config.EnableCache, Is.False);
            Assert.That(config.ErrorPrefix, Is.Null);
            Assert.That(config.OperationName, Is.Null);
            Assert.That(config.EnableDiagnostics, Is.False);
        });

    /// <summary>Verifies record immutability via with expression.</summary>
    [Test]
    public void RecordImmutabilityWithExpression() => Gen.Int.Run((int _) => {
        OperationConfig<int, int> original = new() {
            Context = TestContext,
            ValidationMode = V.Standard,
            AccumulateErrors = true,
            EnableParallel = false,
        };

        OperationConfig<int, int> modified = original with { EnableParallel = true };

        Assert.That(original.ValidationMode, Is.EqualTo(V.Standard));
        Assert.That(original.EnableParallel, Is.False);
        Assert.That(modified.EnableParallel, Is.True);
        Assert.That(modified.ValidationMode, Is.EqualTo(original.ValidationMode));
    });

    /// <summary>Verifies ValidationMode can be set to any V flag.</summary>
    [Test]
    public void ValidationModeCustomValues() => ValidationGenerators.VGen.Run((V mode) => {
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationMode = mode };
        Assert.That(config.ValidationMode, Is.EqualTo(mode));
    });

    /// <summary>Verifies ValidationArgs can hold arbitrary objects.</summary>
    [Test]
    public void ValidationArgsArbitraryObjects() => Gen.Int.Array[1, 5].Run((int[] args) => {
        object[] validationArgs = [.. args.Cast<object>()];
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationArgs = validationArgs };
        Assert.That(config.ValidationArgs!.Length, Is.EqualTo(validationArgs.Length));
    });

    /// <summary>Verifies PreTransform and PostTransform can be set.</summary>
    [Test]
    public void TransformFunctionsCanBeSet() => Test.RunAll(
        () => {
            Result<int> PreTransform(int x) => ResultFactory.Create(value: x * 2);
            OperationConfig<int, int> config = new() { Context = TestContext, PreTransform = PreTransform };
            Assert.That(config.PreTransform, Is.Not.Null);
            Assert.That(config.PreTransform(5).Value, Is.EqualTo(10));
        },
        () => {
            Result<int> PostTransform(int x) => ResultFactory.Create(value: x + 1);
            OperationConfig<int, int> config = new() { Context = TestContext, PostTransform = PostTransform };
            Assert.That(config.PostTransform, Is.Not.Null);
            Assert.That(config.PostTransform(5).Value, Is.EqualTo(6));
        });

    /// <summary>Verifies InputFilter and OutputFilter predicates.</summary>
    [Test]
    public void FilterPredicatesCanBeSet() => Test.RunAll(
        () => {
            bool InputFilter(int x) => x > 0;
            OperationConfig<int, int> config = new() { Context = TestContext, InputFilter = InputFilter };
            Assert.That(config.InputFilter, Is.Not.Null);
            Assert.That(config.InputFilter(5), Is.True);
            Assert.That(config.InputFilter(-5), Is.False);
        },
        () => {
            bool OutputFilter(int x) => x % 2 == 0;
            OperationConfig<int, int> config = new() { Context = TestContext, OutputFilter = OutputFilter };
            Assert.That(config.OutputFilter, Is.Not.Null);
            Assert.That(config.OutputFilter(4), Is.True);
            Assert.That(config.OutputFilter(5), Is.False);
        });

    /// <summary>Verifies parallelism configuration properties.</summary>
    [Test]
    public void ParallelismConfigurationProperties() => Gen.Int[1, 16].Run((int maxDegree) => {
        OperationConfig<int, int> config = new() {
            Context = TestContext,
            EnableParallel = true,
            MaxDegreeOfParallelism = maxDegree,
        };
        Assert.That(config.EnableParallel, Is.True);
        Assert.That(config.MaxDegreeOfParallelism, Is.EqualTo(maxDegree));
    });

    /// <summary>Verifies error handling configuration properties.</summary>
    [Test]
    public void ErrorHandlingConfigurationProperties() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, AccumulateErrors = true };
            Assert.That(config.AccumulateErrors, Is.True);
        },
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, ShortCircuit = false };
            Assert.That(config.ShortCircuit, Is.False);
        },
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, SkipInvalid = true };
            Assert.That(config.SkipInvalid, Is.True);
        });

    /// <summary>Verifies caching configuration property.</summary>
    [Test]
    public void CachingConfigurationProperty() {
        OperationConfig<int, int> config = new() { Context = TestContext, EnableCache = true };
        Assert.That(config.EnableCache, Is.True);
    }

    /// <summary>Verifies ErrorPrefix string can be set.</summary>
    [Test]
    public void ErrorPrefixStringProperty() => Gen.String.Where(static s => s is not null).Run((string prefix) => {
        OperationConfig<int, int> config = new() { Context = TestContext, ErrorPrefix = prefix };
        Assert.That(config.ErrorPrefix, Is.EqualTo(prefix));
    });

    /// <summary>Verifies OperationName for diagnostics.</summary>
    [Test]
    public void OperationNameDiagnostics() => Gen.String.Where(static s => s is not null).Run((string name) => {
        OperationConfig<int, int> config = new() { Context = TestContext, OperationName = name };
        Assert.That(config.OperationName, Is.EqualTo(name));
    });

    /// <summary>Verifies EnableDiagnostics flag.</summary>
    [Test]
    public void EnableDiagnosticsFlag() {
        OperationConfig<int, int> config = new() { Context = TestContext, EnableDiagnostics = true };
        Assert.That(config.EnableDiagnostics, Is.True);
    }

    /// <summary>Verifies DebuggerDisplay format contains key information.</summary>
    [Test]
    public void DebuggerDisplayFormat() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() {
                Context = TestContext,
                OperationName = "TestOp",
                ValidationMode = V.Standard,
            };
            string display = config.ToString();
            Assert.That(display, Is.Not.Null);
        },
        () => {
            OperationConfig<int, int> config = new() {
                Context = TestContext,
                EnableCache = true,
                EnableParallel = true,
                EnableDiagnostics = true,
            };
            string display = config.ToString();
            Assert.That(display, Is.Not.Null);
        });

    /// <summary>Verifies property-based config creation always succeeds with valid Context.</summary>
    [Test]
    public void PropertyBasedConfigCreation() => ContextGenerators.GeometryContextGen.Run((GeometryContext context) => {
        OperationConfig<int, int> config = new() { Context = context };
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Context, Is.EqualTo(context));
    });

    /// <summary>Verifies record equality semantics.</summary>
    [Test]
    public void RecordEqualitySemantics() => Test.RunAll(
        () => {
            OperationConfig<int, int> config1 = new() { Context = TestContext, ValidationMode = V.Standard };
            OperationConfig<int, int> config2 = new() { Context = TestContext, ValidationMode = V.Standard };
            Assert.That(config2, Is.EqualTo(config1));
        },
        () => {
            OperationConfig<int, int> config1 = new() { Context = TestContext, ValidationMode = V.Standard };
            OperationConfig<int, int> config2 = new() { Context = TestContext, ValidationMode = V.Degeneracy };
            Assert.That(config2, Is.Not.EqualTo(config1));
        });

    /// <summary>Verifies generic type parameters work correctly.</summary>
    [Test]
    public void GenericTypeParameters() => Test.RunAll(
        () => {
            OperationConfig<string, int> config = new() { Context = TestContext };
            Assert.That(typeof(OperationConfig<string, int>).GetGenericArguments()[0], Is.EqualTo(typeof(string)));
            Assert.That(typeof(OperationConfig<string, int>).GetGenericArguments()[1], Is.EqualTo(typeof(int)));
        },
        () => {
            OperationConfig<double, string> config = new() { Context = TestContext };
            Assert.That(typeof(OperationConfig<double, string>).GetGenericArguments()[0], Is.EqualTo(typeof(double)));
            Assert.That(typeof(OperationConfig<double, string>).GetGenericArguments()[1], Is.EqualTo(typeof(string)));
        });
}

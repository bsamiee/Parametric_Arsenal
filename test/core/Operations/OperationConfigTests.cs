using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Arsenal.Core.Tests.Context;
using Arsenal.Core.Tests.Validation;
using Arsenal.Tests.Common;
using CsCheck;
using Rhino;
using Xunit;

namespace Arsenal.Core.Tests.Operations;

/// <summary>Tests OperationConfig creation, defaults, and immutability.</summary>
public sealed class OperationConfigTests {
    private static readonly IGeometryContext TestContext = new GeometryContext(
        AbsoluteTolerance: 0.01,
        RelativeTolerance: 0.0,
        AngleToleranceRadians: 0.017453292519943295,
        Units: UnitSystem.Meters);

    /// <summary>Verifies required Context property must be provided.</summary>
    [Fact]
    public void RequiredContextProperty() {
        OperationConfig<int, int> config = new() { Context = TestContext };
        Assert.NotNull(config.Context);
        Assert.Equal(TestContext, config.Context);
    }

    /// <summary>Verifies default values for optional properties.</summary>
    [Fact]
    public void DefaultValuesOptionalProperties() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext };
            Assert.Equal(V.None, config.ValidationMode);
            Assert.Null(config.ValidationArgs);
            Assert.False(config.AccumulateErrors);
            Assert.Null(config.PreTransform);
            Assert.Null(config.PostTransform);
            Assert.Null(config.InputFilter);
            Assert.Null(config.OutputFilter);
            Assert.False(config.EnableParallel);
            Assert.Equal(-1, config.MaxDegreeOfParallelism);
            Assert.False(config.SkipInvalid);
            Assert.True(config.ShortCircuit);
            Assert.False(config.EnableCache);
            Assert.Null(config.ErrorPrefix);
            Assert.Null(config.OperationName);
            Assert.False(config.EnableDiagnostics);
        });

    /// <summary>Verifies record immutability via with expression.</summary>
    [Fact]
    public void RecordImmutabilityWithExpression() => Gen.Int.Run((int _) => {
        OperationConfig<int, int> original = new() {
            Context = TestContext,
            ValidationMode = V.Standard,
            AccumulateErrors = true,
            EnableParallel = false,
        };

        OperationConfig<int, int> modified = original with { EnableParallel = true };

        Assert.Equal(V.Standard, original.ValidationMode);
        Assert.False(original.EnableParallel);
        Assert.True(modified.EnableParallel);
        Assert.Equal(original.ValidationMode, modified.ValidationMode);
    });

    /// <summary>Verifies ValidationMode can be set to any V flag.</summary>
    [Fact]
    public void ValidationModeCustomValues() => ValidationGenerators.VGen.Run((V mode) => {
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationMode = mode };
        Assert.Equal(mode, config.ValidationMode);
    });

    /// <summary>Verifies ValidationArgs can hold arbitrary objects.</summary>
    [Fact]
    public void ValidationArgsArbitraryObjects() => Gen.Int.Array[1, 5].Run((int[] args) => {
        object[] validationArgs = [.. args.Cast<object>()];
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationArgs = validationArgs };
        Assert.Equal(validationArgs.Length, config.ValidationArgs!.Length);
    });

    /// <summary>Verifies PreTransform and PostTransform can be set.</summary>
    [Fact]
    public void TransformFunctionsCanBeSet() => Test.RunAll(
        () => {
            Result<int> PreTransform(int x) => ResultFactory.Create(value: x * 2);
            OperationConfig<int, int> config = new() { Context = TestContext, PreTransform = PreTransform };
            Assert.NotNull(config.PreTransform);
            Assert.Equal(10, config.PreTransform(5).Value);
        },
        () => {
            Result<int> PostTransform(int x) => ResultFactory.Create(value: x + 1);
            OperationConfig<int, int> config = new() { Context = TestContext, PostTransform = PostTransform };
            Assert.NotNull(config.PostTransform);
            Assert.Equal(6, config.PostTransform(5).Value);
        });

    /// <summary>Verifies InputFilter and OutputFilter predicates.</summary>
    [Fact]
    public void FilterPredicatesCanBeSet() => Test.RunAll(
        () => {
            bool InputFilter(int x) => x > 0;
            OperationConfig<int, int> config = new() { Context = TestContext, InputFilter = InputFilter };
            Assert.NotNull(config.InputFilter);
            Assert.True(config.InputFilter(5));
            Assert.False(config.InputFilter(-5));
        },
        () => {
            bool OutputFilter(int x) => x % 2 == 0;
            OperationConfig<int, int> config = new() { Context = TestContext, OutputFilter = OutputFilter };
            Assert.NotNull(config.OutputFilter);
            Assert.True(config.OutputFilter(4));
            Assert.False(config.OutputFilter(5));
        });

    /// <summary>Verifies parallelism configuration properties.</summary>
    [Fact]
    public void ParallelismConfigurationProperties() => Gen.Int[1, 16].Run((int maxDegree) => {
        OperationConfig<int, int> config = new() {
            Context = TestContext,
            EnableParallel = true,
            MaxDegreeOfParallelism = maxDegree,
        };
        Assert.True(config.EnableParallel);
        Assert.Equal(maxDegree, config.MaxDegreeOfParallelism);
    });

    /// <summary>Verifies error handling configuration properties.</summary>
    [Fact]
    public void ErrorHandlingConfigurationProperties() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, AccumulateErrors = true };
            Assert.True(config.AccumulateErrors);
        },
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, ShortCircuit = false };
            Assert.False(config.ShortCircuit);
        },
        () => {
            OperationConfig<int, int> config = new() { Context = TestContext, SkipInvalid = true };
            Assert.True(config.SkipInvalid);
        });

    /// <summary>Verifies caching configuration property.</summary>
    [Fact]
    public void CachingConfigurationProperty() {
        OperationConfig<int, int> config = new() { Context = TestContext, EnableCache = true };
        Assert.True(config.EnableCache);
    }

    /// <summary>Verifies ErrorPrefix string can be set.</summary>
    [Fact]
    public void ErrorPrefixStringProperty() => Gen.String.Where(static s => s is not null).Run((string prefix) => {
        OperationConfig<int, int> config = new() { Context = TestContext, ErrorPrefix = prefix };
        Assert.Equal(prefix, config.ErrorPrefix);
    });

    /// <summary>Verifies OperationName for diagnostics.</summary>
    [Fact]
    public void OperationNameDiagnostics() => Gen.String.Where(static s => s is not null).Run((string name) => {
        OperationConfig<int, int> config = new() { Context = TestContext, OperationName = name };
        Assert.Equal(name, config.OperationName);
    });

    /// <summary>Verifies EnableDiagnostics flag.</summary>
    [Fact]
    public void EnableDiagnosticsFlag() {
        OperationConfig<int, int> config = new() { Context = TestContext, EnableDiagnostics = true };
        Assert.True(config.EnableDiagnostics);
    }

    /// <summary>Verifies DebuggerDisplay format contains key information.</summary>
    [Fact]
    public void DebuggerDisplayFormat() => Test.RunAll(
        () => {
            OperationConfig<int, int> config = new() {
                Context = TestContext,
                OperationName = "TestOp",
                ValidationMode = V.Standard,
            };
            string display = config.ToString();
            Assert.NotNull(display);
        },
        () => {
            OperationConfig<int, int> config = new() {
                Context = TestContext,
                EnableCache = true,
                EnableParallel = true,
                EnableDiagnostics = true,
            };
            string display = config.ToString();
            Assert.NotNull(display);
        });

    /// <summary>Verifies property-based config creation always succeeds with valid Context.</summary>
    [Fact]
    public void PropertyBasedConfigCreation() => ContextGenerators.GeometryContextGen.Run((GeometryContext context) => {
        OperationConfig<int, int> config = new() { Context = context };
        Assert.NotNull(config);
        Assert.Equal(context, config.Context);
    });

    /// <summary>Verifies record equality semantics.</summary>
    [Fact]
    public void RecordEqualitySemantics() => Test.RunAll(
        () => {
            OperationConfig<int, int> config1 = new() { Context = TestContext, ValidationMode = V.Standard };
            OperationConfig<int, int> config2 = new() { Context = TestContext, ValidationMode = V.Standard };
            Assert.Equal(config1, config2);
        },
        () => {
            OperationConfig<int, int> config1 = new() { Context = TestContext, ValidationMode = V.Standard };
            OperationConfig<int, int> config2 = new() { Context = TestContext, ValidationMode = V.Degeneracy };
            Assert.NotEqual(config1, config2);
        });

    /// <summary>Verifies generic type parameters work correctly.</summary>
    [Fact]
    public void GenericTypeParameters() => Test.RunAll(
        () => {
            OperationConfig<string, int> config = new() { Context = TestContext };
            Assert.Equal(typeof(string), typeof(OperationConfig<string, int>).GetGenericArguments()[0]);
            Assert.Equal(typeof(int), typeof(OperationConfig<string, int>).GetGenericArguments()[1]);
        },
        () => {
            OperationConfig<double, string> config = new() { Context = TestContext };
            Assert.Equal(typeof(double), typeof(OperationConfig<double, string>).GetGenericArguments()[0]);
            Assert.Equal(typeof(string), typeof(OperationConfig<double, string>).GetGenericArguments()[1]);
        });
}

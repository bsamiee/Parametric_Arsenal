using System.Collections.Concurrent;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Arsenal.Tests.Common;
using CsCheck;
using Rhino;
using Xunit;

namespace Arsenal.Core.Tests.Operations;

/// <summary>Tests UnifiedOperation polymorphic dispatch, validation, caching, and parallelism.</summary>
public sealed class UnifiedOperationTests {
    private static readonly IGeometryContext TestContext = new GeometryContext(
        AbsoluteTolerance: 0.01,
        RelativeTolerance: 0.0,
        AngleToleranceRadians: 0.017453292519943295,
        Units: UnitSystem.Meters);

    /// <summary>Verifies polymorphic dispatch for Func operation types.</summary>
    [Fact]
    public void PolymorphicDispatchFuncOperations() => Test.RunAll(
        () => Gen.Int.Run((int value) => {
            Func<int, Result<IReadOnlyList<int>>> op = x => ResultFactory.Create(value: (IReadOnlyList<int>)[x * 2]);
            OperationConfig<int, int> config = new() { Context = TestContext };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: op, config: config);
            Test.Success(result, list => list.Count == 1 && list[0] == value * 2);
        }),
        () => Gen.Int.Run((int value) => {
            Func<int, Result<int>> singleOp = x => ResultFactory.Create(value: x + 10);
            OperationConfig<int, int> config = new() { Context = TestContext };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: singleOp, config: config);
            Test.Success(result, list => list.Count == 1 && list[0] == value + 10);
        }));

    /// <summary>Verifies deferred operation with validation mode parameter.</summary>
    [Fact]
    public void DeferredOperationWithValidationMode() => Gen.Int.Run((int value) => {
        Func<int, V, Result<IReadOnlyList<int>>> deferred = (x, mode) =>
            mode == V.None
                ? ResultFactory.Create(value: (IReadOnlyList<int>)[x])
                : ResultFactory.Create(value: (IReadOnlyList<int>)[x, (int)mode]);
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationMode = V.Standard };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: deferred, config: config);
        Test.Success(result, list => list.Count == 2 && list[0] == value);
    });

    /// <summary>Verifies nested Result flattening.</summary>
    [Fact]
    public void NestedResultFlattening() => Gen.Int.Run((int value) => {
        Func<int, Result<Result<IReadOnlyList<int>>>> nested = x =>
            ResultFactory.Create(value: ResultFactory.Create(value: (IReadOnlyList<int>)[x * 3]));
        OperationConfig<int, int> config = new() { Context = TestContext };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: nested, config: config);
        Test.Success(result, list => list.Count == 1 && list[0] == value * 3);
    });

    /// <summary>Verifies list of operations with error accumulation.</summary>
    [Fact]
    public void ListOperationsErrorAccumulation() => Test.RunAll(
        () => Gen.Int.Run((int value) => {
            Result<int> Op1(int x) => ResultFactory.Create(value: x + 1);
            Result<int> Op2(int x) => ResultFactory.Create(value: x + 2);
            Result<int> Op3(int x) => ResultFactory.Create(value: x + 3);
            IReadOnlyList<Func<int, Result<int>>> ops = [Op1, Op2, Op3,];
            OperationConfig<int, int> config = new() { Context = TestContext, AccumulateErrors = true };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: ops, config: config);
            Test.Success(result, list => list.Count == 3);
        }),
        () => Gen.Int.Run((int value) => {
            Result<int> Op1(int x) => ResultFactory.Create(value: x + 1);
            Result<int> Op2(int _) => ResultFactory.Create<int>(error: E.Results.NoValueProvided);
            Result<int> Op3(int x) => ResultFactory.Create(value: x + 3);
            IReadOnlyList<Func<int, Result<int>>> ops = [Op1, Op2, Op3,];
            OperationConfig<int, int> config = new() { Context = TestContext, AccumulateErrors = true };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: ops, config: config);
            Test.Failure(result);
        }));

    /// <summary>Verifies conditional operation based on predicate.</summary>
    [Fact]
    public void ConditionalOperationPredicate() => Test.RunAll(
        () => Gen.Int.Run((int value) => {
            (Func<int, bool> pred, Func<int, Result<IReadOnlyList<int>>> op) conditional = (
                x => x > 0,
                x => ResultFactory.Create(value: (IReadOnlyList<int>)[x * 2]));
            OperationConfig<int, int> config = new() { Context = TestContext };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: conditional, config: config);
            Test.Success(result, list => value > 0 ? list.Count == 1 : list.Count == 0);
        }));

    /// <summary>Verifies validation integration with V.None skipping validation.</summary>
    [Fact]
    public void ValidationIntegrationNoneSkipsValidation() => Gen.Int.Run((int value) => {
        Func<int, Result<IReadOnlyList<int>>> op = x => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        OperationConfig<int, int> config = new() { Context = TestContext, ValidationMode = V.None };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: op, config: config);
        Assert.True(result.IsSuccess);
    });

    /// <summary>Verifies internal caching behavior with repeated calls.</summary>
    [Fact]
    public void InternalCachingBehavior() => Gen.Int.Run((int value) => {
        int executionCount = 0;
        Result<IReadOnlyList<int>> Op(int x) {
            executionCount++;
            return ResultFactory.Create(value: (IReadOnlyList<int>)[x * 2]);
        }
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<int, int> config = new() { Context = TestContext, EnableCache = true };

        Result<IReadOnlyList<int>> result1 = UnifiedOperation.Apply(input: value, operation: operation, config: config);
        Result<IReadOnlyList<int>> result2 = UnifiedOperation.Apply(input: value, operation: operation, config: config);

        Assert.True(result1.IsSuccess && result2.IsSuccess);
        Assert.Equal(result1.Value[0], result2.Value[0]);
        Assert.Equal(1, executionCount);
    });

    /// <summary>Verifies external cache dictionary usage.</summary>
    [Fact]
    public void ExternalCacheDictionary() => Gen.Int.Run((int value) => {
        int executionCount = 0;
        Result<IReadOnlyList<int>> Op(int x) {
            executionCount++;
            return ResultFactory.Create(value: (IReadOnlyList<int>)[x * 3]);
        }
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        ConcurrentDictionary<(object, Type), object> externalCache = new();
        OperationConfig<int, int> config = new() { Context = TestContext };

        Result<IReadOnlyList<int>> result1 = UnifiedOperation.Apply(input: value, operation: operation, config: config, externalCache: externalCache);
        Result<IReadOnlyList<int>> result2 = UnifiedOperation.Apply(input: value, operation: operation, config: config, externalCache: externalCache);

        Assert.True(result1.IsSuccess && result2.IsSuccess);
        Assert.Equal(1, executionCount);
    });

    /// <summary>Verifies parallelism with EnableParallel flag.</summary>
    [Fact]
    public void ParallelismEnabledFlag() => Gen.Int.List[10, 50].Run((List<int> values) => {
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x * 2]);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext, EnableParallel = true };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: operation, config: config);
        Test.Success(result, list => list.Count == values.Count);
    });

    /// <summary>Verifies MaxDegreeOfParallelism parameter.</summary>
    [Fact]
    public void MaxDegreeOfParallelismParameter() => Gen.Int.List[10, 50].Run((List<int> values) => {
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext, EnableParallel = true, MaxDegreeOfParallelism = 2 };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: operation, config: config);
        Test.Success(result, list => list.Count == values.Count);
    });

    /// <summary>Verifies error accumulation vs short-circuit behavior.</summary>
    [Fact]
    public void ErrorAccumulationVsShortCircuit() => Test.RunAll(
        () => {
            IReadOnlyList<int> values = [1, 2, 3, 4, 5];
            Result<IReadOnlyList<int>> Op(int x) =>
                x == 3
                    ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Results.NoValueProvided)
                    : ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
            Func<int, Result<IReadOnlyList<int>>> op = Op;
            OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext, AccumulateErrors = true };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: op, config: config);
            Test.Failure(result);
        },
        () => {
            IReadOnlyList<int> values = [1, 2, 3, 4, 5];
            Result<IReadOnlyList<int>> Op(int x) =>
                x == 3
                    ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Results.NoValueProvided)
                    : ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
            Func<int, Result<IReadOnlyList<int>>> op = Op;
            OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext, ShortCircuit = true };
            Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: op, config: config);
            Test.Failure(result);
        });

    /// <summary>Verifies InputFilter predicate filtering.</summary>
    [Fact]
    public void InputFilterPredicate() => Gen.Int.Run((int value) => {
        Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        bool Filter(int x) => x > 0;
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<int, int> config = new() { Context = TestContext, InputFilter = Filter };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: operation, config: config);
        Assert.Equal(value > 0, result.IsSuccess);
    });

    /// <summary>Verifies OutputFilter predicate filtering.</summary>
    [Fact]
    public void OutputFilterPredicate() => Gen.Int.List[5, 10].Run((List<int> values) => {
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x, x + 1, x + 2]);
        static bool Filter(int x) => x % 2 == 0;
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext, OutputFilter = Filter };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: operation, config: config);
        Test.Success(result, list => list.All(x => x % 2 == 0));
    });

    /// <summary>Verifies PreTransform applied before operation.</summary>
    [Fact]
    public void PreTransformBeforeOperation() => Gen.Int.Run((int value) => {
        Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        Result<int> Transform(int x) => ResultFactory.Create(value: x * 10);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<int, int> config = new() {
            Context = TestContext,
            PreTransform = Transform,
        };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: operation, config: config);
        Test.Success(result, list => list.Count == 1 && list[0] == value * 10);
    });

    /// <summary>Verifies PostTransform applied after operation.</summary>
    [Fact]
    public void PostTransformAfterOperation() => Gen.Int.Run((int value) => {
        Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x, x + 1]);
        Result<int> Transform(int x) => ResultFactory.Create(value: x * 2);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<int, int> config = new() {
            Context = TestContext,
            PostTransform = Transform,
        };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply(input: value, operation: operation, config: config);
        Test.Success(result, list => list.Count == 2 && list[0] == value * 2);
    });

    /// <summary>Verifies SkipInvalid behavior with failed validations.</summary>
    [Fact]
    public void SkipInvalidBehavior() {
        IReadOnlyList<int> values = [1, 2, 3, 4, 5];
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        static bool Filter(IReadOnlyList<int> list) => list.Any(static x => x > 2);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() {
            Context = TestContext,
            InputFilter = Filter,
            SkipInvalid = true,
        };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: operation, config: config);
        Test.Success(result, list => list.Count == 5);
    }

    /// <summary>Verifies empty collection input returns empty result.</summary>
    [Fact]
    public void EmptyCollectionReturnsEmpty() {
        IReadOnlyList<int> empty = [];
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        Func<int, Result<IReadOnlyList<int>>> op = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: empty, operation: op, config: config);
        Test.Success(result, list => list.Count == 0);
    }

    /// <summary>Verifies single-item collection optimization.</summary>
    [Fact]
    public void SingleItemCollectionOptimization() => Gen.Int.Run((int value) => {
        IReadOnlyList<int> single = [value];
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x * 2]);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: single, operation: operation, config: config);
        Test.Success(result, list => list.Count == 1 && list[0] == value * 2);
    });

    /// <summary>Verifies unsupported operation type returns error.</summary>
    [Fact]
    public void UnsupportedOperationTypeError() => Gen.Int.Run((int value) => {
        const string invalidOp = "not a valid operation";
        OperationConfig<int, int> config = new() { Context = TestContext };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<int, int>(input: value, operation: invalidOp, config: config);
        Test.Failure(result, errors => errors.Any(e => e.Code == E.Validation.UnsupportedOperationType.Code));
    });

    /// <summary>Verifies property-based invariant: output count never exceeds input count for single-to-single ops.</summary>
    [Fact]
    public void PropertyBasedOutputCountInvariant() => Gen.Int.List[1, 20].Run((List<int> values) => {
        static Result<IReadOnlyList<int>> Op(int x) => ResultFactory.Create(value: (IReadOnlyList<int>)[x]);
        Func<int, Result<IReadOnlyList<int>>> operation = Op;
        OperationConfig<IReadOnlyList<int>, int> config = new() { Context = TestContext };
        Result<IReadOnlyList<int>> result = UnifiedOperation.Apply<IReadOnlyList<int>, int>(input: values, operation: operation, config: config);
        Test.Success(result, list => list.Count == values.Count);
    });
}

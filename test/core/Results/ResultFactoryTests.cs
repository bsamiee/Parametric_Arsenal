using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Tests ResultFactory creation, validation, lifting, and traversal operations with comprehensive property-based testing.</summary>
public sealed class ResultFactoryTests {
    private static readonly SystemError TestError1 = new(ErrorDomain.Results, 1001, "Test error 1");
    private static readonly SystemError TestError2 = new(ErrorDomain.Results, 1002, "Test error 2");
    private static readonly SystemError TestError3 = new(ErrorDomain.Results, 1003, "Test error 3");

    /// <summary>Tests Create with value, errors, deferred, conditionals, and nested parameters using property-based testing.</summary>
    [Fact]
    public void Create_AllParameterCombinations_BehavesCorrectly() {
        // Value parameter
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value);
            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }, iter: 50);

        // Single error parameter
        ResultGenerators.SystemErrorGen.Sample(error => {
            var result = ResultFactory.Create<int>(error: error);
            Assert.False(result.IsSuccess);
            Assert.Contains(error, result.Errors);
        }, iter: 50);

        // Errors array parameter
        ResultGenerators.SystemErrorArrayGen.Sample(errors => {
            var result = ResultFactory.Create<int>(errors: errors);
            Assert.False(result.IsSuccess);
            Assert.Equal(errors.Length, result.Errors.Count);
        }, iter: 50);

        // Deferred parameter
        Gen.Int.Sample(value => {
            bool executed = false;
            var result = ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create(value: value); });
            Assert.True(result.IsDeferred);
            Assert.False(executed);
            Assert.Equal(value, result.Value);
            Assert.True(executed);
        }, iter: 50);

        // Conditionals parameter
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value, conditionals: [(x => x > 0, TestError1)]);
            Assert.Equal(value > 0, result.IsSuccess);
        }, iter: 50);

        // Nested parameter
        ResultGenerators.NestedResultGen<int>().Sample(nested => {
            var result = ResultFactory.Create(nested: nested);
            Assert.Equal(nested.IsSuccess && nested.Value.IsSuccess, result.IsSuccess);
        }, iter: 50);
    }

    /// <summary>Tests Validate with predicate, unless, premise/conclusion, batch validations, and geometry validation.</summary>
    [Fact]
    public void Validate_AllParameterCombinations_ValidatesCorrectly() {
        // Predicate validation - basic case
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .Validate(predicate: x => x > 0, error: TestError1);
            Assert.Equal(value > 0, result.IsSuccess);
            if (!result.IsSuccess) Assert.Contains(TestError1, result.Errors);
        }, iter: 50);

        // Unless parameter - inverts predicate logic
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .Validate(predicate: x => x < 0, error: TestError1, unless: true);
            Assert.Equal(value >= 0, result.IsSuccess); // unless inverts: !predicate == success
        }, iter: 50);

        // Premise and conclusion - logical implication (premise → conclusion)
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .Validate(premise: x => x > 10, conclusion: x => x < 100, error: TestError1);
            // Passes if: premise false OR conclusion true
            var expected = value <= 10 || value < 100;
            Assert.Equal(expected, result.IsSuccess);
        }, iter: 50);

        // Batch validations - accumulates all errors
        Gen.Int.Sample(value => {
            var validations = new[] {
                (Func<int, bool>)(x => x > 0), TestError1),
                (Func<int, bool>)(x => x < 100), TestError2),
                (Func<int, bool>)(x => x % 2 == 0), TestError3)
            };
            var result = ResultFactory.Create(value: value).Validate(validations: validations);

            var expectedErrors = new List<SystemError>();
            if (value <= 0) expectedErrors.Add(TestError1);
            if (value >= 100) expectedErrors.Add(TestError2);
            if (value % 2 != 0) expectedErrors.Add(TestError3);

            Assert.Equal(expectedErrors.Count == 0, result.IsSuccess);
            if (!result.IsSuccess) {
                Assert.Equal(expectedErrors.Count, result.Errors.Count);
            }
        }, iter: 50);

        // Monadic validation - uses validation function
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(value: x * 2));

            if (value > 10) {
                Assert.True(result.IsSuccess);
                Assert.Equal(value * 2, result.Value);
            } else {
                Assert.True(result.IsSuccess);
                Assert.Equal(value, result.Value);
            }
        }, iter: 50);

        // Args parameter with predicate and error
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .Validate(args: [(Func<int, bool>)(x => x > 0), TestError1]);
            Assert.Equal(value > 0, result.IsSuccess);
        }, iter: 50);
    }

    /// <summary>Tests Lift with partial application, full application, and error accumulation in applicative style.</summary>
    [Fact]
    public void Lift_FunctionLifting_AccumulatesErrorsApplicatively() {
        // Full application with all successful Results
        Gen.Select(Gen.Int, Gen.Int).Sample(t => {
            var (a, b) = t;
            Func<int, int, int> add = (x, y) => x + y;
            var result = (Result<int>)ResultFactory.Lift<int>(add,
                ResultFactory.Create(value: a),
                ResultFactory.Create(value: b));

            Assert.True(result.IsSuccess);
            Assert.Equal(a + b, result.Value);
        }, iter: 50);

        // Full application with one failed Result
        Gen.Select(Gen.Int, ResultGenerators.SystemErrorGen).Sample(t => {
            var (value, error) = t;
            Func<int, int, int> add = (x, y) => x + y;
            var result = (Result<int>)ResultFactory.Lift<int>(add,
                ResultFactory.Create(value: value),
                ResultFactory.Create<int>(error: error));

            Assert.False(result.IsSuccess);
            Assert.Contains(error, result.Errors);
        }, iter: 50);

        // Full application with all failed Results - accumulates all errors
        Gen.Select(ResultGenerators.SystemErrorGen, ResultGenerators.SystemErrorGen).Sample(t => {
            var (error1, error2) = t;
            Func<int, int, int> add = (x, y) => x + y;
            var result = (Result<int>)ResultFactory.Lift<int>(add,
                ResultFactory.Create<int>(error: error1),
                ResultFactory.Create<int>(error: error2));

            Assert.False(result.IsSuccess);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(error1, result.Errors);
            Assert.Contains(error2, result.Errors);
        }, iter: 50);

        // Partial application
        Gen.Int.Sample(value => {
            Func<int, int, int, int> add3 = (x, y, z) => x + y + z;
            var partialResult = (Result<Func<object[], int>>)ResultFactory.Lift<int>(add3,
                ResultFactory.Create(value: value));

            Assert.True(partialResult.IsSuccess);
        }, iter: 50);
    }

    /// <summary>Tests TraverseElements with success/failure cases and comprehensive error accumulation.</summary>
    [Fact]
    public void TraverseElements_CollectionTransformation_AccumulatesErrors() {
        // All successful transformations
        Gen.Int.List[1, 10].Sample(items => {
            var result = ResultFactory.Create<IEnumerable<int>>(value: items)
                .TraverseElements(x => ResultFactory.Create(value: x * 2));

            Assert.True(result.IsSuccess);
            Assert.Equal(items.Count, result.Value.Count);
            Assert.Equal(items.Select(x => x * 2), result.Value);
        }, iter: 50);

        // Some failed transformations - short-circuits on first failure
        Gen.Int.List[1, 10].Sample(items => {
            var result = ResultFactory.Create<IEnumerable<int>>(value: items)
                .TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x)
                    : ResultFactory.Create<int>(error: TestError1));

            bool anyOdd = items.Any(x => x % 2 != 0);
            Assert.Equal(!anyOdd, result.IsSuccess);
        }, iter: 50);

        // Failed outer Result propagates
        ResultGenerators.SystemErrorGen.Sample(error => {
            var result = ResultFactory.Create<IEnumerable<int>>(error: error)
                .TraverseElements(x => ResultFactory.Create(value: x * 2));

            Assert.False(result.IsSuccess);
            Assert.Contains(error, result.Errors);
        }, iter: 50);

        // Empty collection
        var emptyResult = ResultFactory.Create<IEnumerable<int>>(value: [])
            .TraverseElements(x => ResultFactory.Create(value: x * 2));
        Assert.True(emptyResult.IsSuccess);
        Assert.Empty(emptyResult.Value);
    }

    /// <summary>Tests null argument validation and state access violations across all factory methods.</summary>
    [Fact]
    public void NullArguments_ThrowCorrectly() {
        var success = ResultFactory.Create(value: 42);
        var failure = ResultFactory.Create<int>(error: TestError1);

        // Result method null checks
        Assert.Throws<ArgumentNullException>(() => success.Map((Func<int, int>)null!));
        Assert.Throws<ArgumentNullException>(() => success.Bind((Func<int, Result<int>>)null!));
        Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => 0));
        Assert.Throws<ArgumentNullException>(() => success.Match(_ => 0, null!));
        Assert.Throws<ArgumentNullException>(() => success.Filter(null!, TestError1));
        Assert.Throws<ArgumentNullException>(() => success.Traverse((Func<int, Result<int>>)null!));

        // Factory method null checks
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Lift<int>(null!, 1, 2));
        Assert.Throws<ArgumentNullException>(() => ResultFactory.Lift<int>((a, b) => 0, null!));
        Assert.Throws<ArgumentNullException>(() => success.Bind(x => (Result<int>)null!));

        var collectionResult = ResultFactory.Create<IEnumerable<int>>(value: [1, 2, 3]);
        Assert.Throws<ArgumentNullException>(() => collectionResult.TraverseElements((Func<int, Result<int>>)null!));

        // State access violations
        Assert.Throws<InvalidOperationException>(() => failure.Value);
        Assert.NotEqual(default, failure.Error);
        Assert.NotEmpty(failure.Errors);
    }

    /// <summary>Tests error transformation, recovery, and chaining across multiple operations.</summary>
    [Fact]
    public void ErrorHandling_TransformationAndRecovery_BehavesCorrectly() {
        // Error transformation
        ResultGenerators.SystemErrorGen.Sample(originalError => {
            var result = ResultFactory.Create<int>(error: originalError)
                .OnError(mapError: errors => [TestError2]);

            Assert.False(result.IsSuccess);
            Assert.Contains(TestError2, result.Errors);
            Assert.DoesNotContain(originalError, result.Errors);
        }, iter: 50);

        // Error recovery
        ResultGenerators.SystemErrorGen.Sample(error => {
            var result = ResultFactory.Create<int>(error: error)
                .OnError(recover: _ => 42);

            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Value);
        }, iter: 50);

        // Monadic error recovery
        ResultGenerators.SystemErrorGen.Sample(error => {
            var result = ResultFactory.Create<int>(error: error)
                .OnError(recoverWith: _ => ResultFactory.Create(value: 99));

            Assert.True(result.IsSuccess);
            Assert.Equal(99, result.Value);
        }, iter: 50);

        // Chained operations preserve errors
        var chainedResult = ResultFactory.Create<int>(error: TestError1)
            .Map(x => x * 2)
            .Bind(x => ResultFactory.Create(value: x + 10))
            .Filter(x => x > 0, TestError2);

        Assert.False(chainedResult.IsSuccess);
        Assert.Contains(TestError1, chainedResult.Errors);
    }
}

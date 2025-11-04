using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Tests ResultFactory creation methods and error handling.</summary>
public sealed class ResultFactoryTests {
    /// <summary>Tests Create method with all parameter combinations.</summary>
    [Theory]
    [MemberData(nameof(ResultTestData.FactoryParameterCases), MemberType = typeof(ResultTestData))]
    public void CreateAllParametersReturnsExpected(
        object? value, SystemError[]? errors, SystemError? error,
        Func<Result<int>>? deferred, (Func<int, bool>, SystemError)[]? conditionals,
        Result<Result<int>>? nested, bool expectedSuccess) {

        // Direct call to ResultFactory.Create<int> with proper parameter handling
        Result<int> result = value switch {
            int intValue => ResultFactory.Create<int>(value: intValue, errors: errors, error: error, deferred: deferred, conditionals: conditionals, nested: nested),
            _ => ResultFactory.Create<int>(errors: errors, error: error, deferred: deferred, conditionals: conditionals, nested: nested)
        };

        bool actualSuccess = result.IsSuccess;

        Assert.Equal(expectedSuccess, actualSuccess);
    }

    /// <summary>Tests null argument handling and state access violations.</summary>
    [Fact]
    public void NullHandlingThrowsCorrectly() {
        Result<int> success = ResultFactory.Create(value: 42);
        Result<int> failure = ResultFactory.Create<int>(error: new SystemError(ErrorDomain.Results, 1001, "Test"));

        // Null function parameters should throw
        Assert.Throws<ArgumentNullException>(() => success.Map((Func<int, int>)null!));
        Assert.Throws<ArgumentNullException>(() => success.Bind((Func<int, Result<int>>)null!));
        Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => 0));

        // State access violations
        Assert.Throws<InvalidOperationException>(() => failure.Value);
        Assert.NotEqual(default, failure.Error); // Should contain the actual error
    }

    /// <summary>Tests error transformation and accumulation in chained operations.</summary>
    [Fact]
    public void ErrorAccumulationTransformsCorrectly() {
        SystemError originalError = new(ErrorDomain.Results, 1001, "Original");
        SystemError transformedError = new(ErrorDomain.Results, 1002, "Transformed");

        Result<int> result = ResultFactory.Create<int>(error: originalError)
            .OnError(mapError: errors => [transformedError])
            .Bind(x => ResultFactory.Create(value: x * 2))
            .Filter(x => x > 0, new SystemError(ErrorDomain.Results, 1003, "Filter"));

        Assert.False(result.IsSuccess);
        Assert.Contains(transformedError, result.Errors);
        Assert.DoesNotContain(originalError, result.Errors);
    }
}

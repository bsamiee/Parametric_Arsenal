using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Xunit;

namespace Arsenal.Core.Tests.Diagnostics;

/// <summary>Tests verifying DebuggerDisplay attributes without side effects.</summary>
public sealed class DebuggerDisplayTests {
    [Fact]
    public void ResultSuccessDebuggerDisplayShowsValue() {
        string display = GetDebuggerDisplay(ResultFactory.Create(value: 42));
        Assert.Contains("Success", display, StringComparison.Ordinal);
        Assert.Contains("42", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultErrorDebuggerDisplayShowsError() {
        string display = GetDebuggerDisplay(ResultFactory.Create<int>(error: E.Validation.GeometryInvalid));
        Assert.Contains("Error", display, StringComparison.Ordinal);
        Assert.Contains("Validation", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultDeferredDebuggerDisplayShowsDeferred() {
        Result<int> result = ResultFactory.Create(deferred: () => ResultFactory.Create(value: 42));
        Assert.Contains("Deferred", GetDebuggerDisplay(result), StringComparison.Ordinal);
        Assert.True(result.IsDeferred);
    }

    [Fact]
    public void ResultMultipleErrorsDebuggerDisplayShowsCount() {
        string display = GetDebuggerDisplay(ResultFactory.Create<int>(errors: [
            E.Validation.GeometryInvalid,
            E.Validation.ToleranceAbsoluteInvalid,
        ]));
        Assert.Contains("Errors", display, StringComparison.Ordinal);
        Assert.Contains("2", display, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemErrorDebuggerDisplayShowsDomainCodeMessage() {
        string display = GetDebuggerDisplay(E.Validation.GeometryInvalid);
        Assert.Contains("Validation", display, StringComparison.Ordinal);
        Assert.Contains("3000", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationModeDebuggerDisplayShowsMode() =>
        Assert.Contains("Combined", GetDebuggerDisplay(V.Standard | V.Topology), StringComparison.Ordinal);

    [Fact]
    public void DeferredResultDebuggerDisplayDoesNotEvaluate() {
        int callCount = 0;
        Result<int> result = ResultFactory.Create(deferred: () => { callCount++; return ResultFactory.Create(value: 42); });
        _ = GetDebuggerDisplay(result);
        Assert.Equal(0, callCount);
        Assert.True(result.IsDeferred);
    }

    private static string GetDebuggerDisplay<T>(T value) {
        System.Reflection.PropertyInfo? prop = typeof(T).GetProperty(
            "DebuggerDisplay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return (prop, value) switch {
            (not null, not null) => (string?)prop.GetValue(value) ?? string.Empty,
            _ => string.Empty,
        };
    }
}

using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Arsenal.Core.Operations;
using Arsenal.Core.Context;
using Xunit;

namespace Arsenal.Core.Tests.Diagnostics;

/// <summary>Tests verifying DebuggerDisplay attributes work correctly without side effects.</summary>
public sealed class DebuggerDisplayTests {
    [Fact]
    public void ResultSuccessDebuggerDisplayShowsValue() {
        Result<int> result = ResultFactory.Create(value: 42);
        string display = GetDebuggerDisplay(result);

        Assert.Contains("Success", display, StringComparison.Ordinal);
        Assert.Contains("42", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultErrorDebuggerDisplayShowsError() {
        Result<int> result = ResultFactory.Create<int>(error: E.Validation.GeometryInvalid);
        string display = GetDebuggerDisplay(result);

        Assert.Contains("Error", display, StringComparison.Ordinal);
        Assert.Contains("Validation", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultDeferredDebuggerDisplayShowsDeferred() {
        Result<int> result = ResultFactory.Create(deferred: () => ResultFactory.Create(value: 42));
        string display = GetDebuggerDisplay(result);

        Assert.Contains("Deferred", display, StringComparison.Ordinal);
        Assert.True(result.IsDeferred);
    }

    [Fact]
    public void ResultMultipleErrorsDebuggerDisplayShowsCount() {
        Result<int> result = ResultFactory.Create<int>(errors: [
            E.Validation.GeometryInvalid,
            E.Validation.ToleranceAbsoluteInvalid,
        ]);
        string display = GetDebuggerDisplay(result);

        Assert.Contains("Errors", display, StringComparison.Ordinal);
        Assert.Contains("2", display, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemErrorDebuggerDisplayShowsDomainCodeMessage() {
        SystemError error = E.Validation.GeometryInvalid;
        string display = GetDebuggerDisplay(error);

        Assert.Contains("Validation", display, StringComparison.Ordinal);
        Assert.Contains("3000", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationModeDebuggerDisplayShowsMode() {
        V mode = V.Standard | V.Topology;
        string display = GetDebuggerDisplay(mode);

        Assert.Contains("Combined", display, StringComparison.Ordinal);
    }

    [Fact]
    public void DeferredResultDebuggerDisplayDoesNotEvaluate() {
        int callCount = 0;
        Result<int> result = ResultFactory.Create(deferred: () => {
            callCount++;
            return ResultFactory.Create(value: 42);
        });

        // Get debugger display should not evaluate the deferred result
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

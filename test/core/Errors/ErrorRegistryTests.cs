using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Errors;

/// <summary>Property-based tests for E.Get domain classification and message lookup.</summary>
public sealed class ErrorRegistryTests {
    /// <summary>E.Get classifies codes into correct domains by range.</summary>
    [Fact]
    public void DomainClassificationByCodeRange() =>
        Gen.OneOf(
            ErrorGenerators.CodeInRangeGen(1000, 1999).Select(code => (code, (byte)1)),
            ErrorGenerators.CodeInRangeGen(2000, 2999).Select(code => (code, (byte)2)),
            ErrorGenerators.CodeInRangeGen(3000, 3999).Select(code => (code, (byte)3)),
            ErrorGenerators.CodeInRangeGen(4000, 4999).Select(code => (code, (byte)4)),
            ErrorGenerators.CodeInRangeGen(5000, 5999).Select(code => (code, (byte)5)),
            ErrorGenerators.CodeOutsideRangesGen.Select(code => (code, (byte)0)))
        .Run((Action<(int code, byte expectedDomain)>)(pair => {
            SystemError error = E.Get(pair.code);
            Assert.Equal(pair.expectedDomain, error.Domain);
        }), 100);

    /// <summary>E.Get returns fallback message for unregistered codes.</summary>
    [Fact]
    public void UnregisteredCodeFallbackMessage() =>
        ErrorGenerators.CodeOutsideRangesGen.Run((Action<int>)(code => {
            SystemError error = E.Get(code);
            string expected = $"Unknown error code: {code.ToString(CultureInfo.InvariantCulture)}";
            Assert.Equal(expected, error.Message);
        }), 100);

    /// <summary>E.Get returns identical SystemErrors for repeated calls.</summary>
    [Fact]
    public void GetDeterminism() =>
        Gen.Int[1000, 5999].Run((Action<int>)(code => {
            SystemError first = E.Get(code);
            SystemError second = E.Get(code);
            Assert.Equal(first, second);
        }), 100);

    /// <summary>E.Get produces consistent results across concurrent calls.</summary>
    [Fact]
    public void GetThreadSafety() =>
        Gen.Int[1000, 5999].Run((Action<int>)(code => {
            SystemError[] results = new SystemError[10];
            Task[] tasks = new Task[10];
            for (int i = 0; i < 10; i++) {
                int index = i;
                tasks[index] = Task.Run(() => results[index] = E.Get(code));
            }
            Task.WaitAll(tasks, CancellationToken.None);
            SystemError reference = results[0];
            foreach (SystemError result in results) {
                Assert.Equal(reference, result);
            }
        }), 100);

    /// <summary>E.Get with context does not mutate shared state.</summary>
    [Fact]
    public void GetImmutability() =>
        Gen.Int[1000, 5999]
            .Select(ErrorGenerators.ContextGen)
            .Run((int code, string context) => {
                SystemError withoutContext = E.Get(code);
                SystemError withContext = E.Get(code, context);
                SystemError afterContext = E.Get(code);
                Test.RunAll(
                    () => Assert.Equal(withoutContext, afterContext),
                    () => Assert.NotEqual(withContext, afterContext));
            }, 100);
}

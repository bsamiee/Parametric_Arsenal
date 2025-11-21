using System.Globalization;
using System.Reflection;
using Arsenal.Core.Errors;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Errors;

/// <summary>Property-based tests for SystemError value semantics and algebraic laws.</summary>
public sealed class SystemErrorTests {
    /// <summary>Identical field values produce equal instances with matching hash codes.</summary>
    [Fact]
    public void ValueEqualityAndHashConsistency() =>
        ErrorGenerators.SystemErrorGen.Run((Action<SystemError>)(error => {
            SystemError duplicate = new(error.Domain, error.Code, error.Message);
            Test.RunAll(
                () => Assert.True(error.Equals(duplicate)),
                () => Assert.Equal(error.GetHashCode(), duplicate.GetHashCode()),
                () => Assert.True(error == duplicate),
                () => Assert.False(error != duplicate));
        }), 100);

    /// <summary>WithContext preserves Domain and Code while appending context to Message.</summary>
    [Fact]
    public void WithContextPreservesIdentity() =>
        ErrorGenerators.SystemErrorGen
            .Select(ErrorGenerators.ContextGen)
            .Run((SystemError error, string context) => {
                SystemError withContext = error.WithContext(context);
                Test.RunAll(
                    () => Assert.Equal(error.Domain, withContext.Domain),
                    () => Assert.Equal(error.Code, withContext.Code),
                    () => Assert.Equal($"{error.Message} (Context: {context})", withContext.Message));
            }, 100);

    /// <summary>ToString produces "[DomainName:Code] Message" format with resolved domain names.</summary>
    [Fact]
    public void ToStringFormatConsistency() =>
        ErrorGenerators.SystemErrorGen.Run((Action<SystemError>)(error => {
            string formatted = error.ToString();
            string expectedDomain = error.Domain switch {
                1 => "Results",
                2 => "Geometry",
                3 => "Validation",
                4 => "Spatial",
                5 => "Topology",
                _ => "Unknown",
            };
            string expected = $"[{expectedDomain}:{error.Code.ToString(CultureInfo.InvariantCulture)}] {error.Message}";
            Assert.Equal(expected, formatted);
        }), 100);

    /// <summary>HashSet and Dictionary deduplicate structurally equal errors.</summary>
    [Fact]
    public void CollectionStructuralEquality() =>
        ErrorGenerators.SystemErrorGen.Run((Action<SystemError>)(error => {
            SystemError duplicate = new(error.Domain, error.Code, error.Message);
            HashSet<SystemError> set = [error, duplicate];
            Dictionary<SystemError, int> dict = new() { [error] = 1, [duplicate] = 2 };
            Test.RunAll(
                () => Assert.Single(set),
                () => Assert.Single(dict),
                () => Assert.Equal(2, dict[error]));
        }), 100);

    /// <summary>Errors differing in any field are not equal.</summary>
    [Fact]
    public void InequalityForDifferentErrors() =>
        ErrorGenerators.DifferentErrorPairGen.Run((Action<(SystemError, SystemError)>)(pair => {
            bool anyDifference = pair.Item1.Domain != pair.Item2.Domain ||
                                 pair.Item1.Code != pair.Item2.Code ||
                                 !string.Equals(pair.Item1.Message, pair.Item2.Message, StringComparison.Ordinal);
            Test.RunAll(
                () => Assert.True(anyDifference),
                () => Assert.False(pair.Item1.Equals(pair.Item2)),
                () => Assert.True(pair.Item1 != pair.Item2));
        }), 100);

    /// <summary>Different errors produce different hash codes with >98% probability.</summary>
    [Fact]
    public void HashDistributionForDifferentErrors() {
        int totalPairs = 0;
        int differentHashes = 0;
        ErrorGenerators.DifferentErrorPairGen.Run((Action<(SystemError, SystemError)>)(pair => {
            totalPairs++;
            differentHashes = pair.Item1.GetHashCode() != pair.Item2.GetHashCode() ? differentHashes + 1 : differentHashes;
        }), 100);
        double distributionRate = (double)differentHashes / totalPairs;
        Assert.True(distributionRate >= 0.98, string.Create(CultureInfo.InvariantCulture, $"Hash distribution {distributionRate:P2} below 98%"));
    }

    /// <summary>Symmetric equality: x.Equals(y) equals y.Equals(x).</summary>
    [Fact]
    public void EqualitySymmetry() =>
        ErrorGenerators.SystemErrorGen
            .Select(ErrorGenerators.SystemErrorGen)
            .Run((SystemError x, SystemError y) =>
                Test.RunAll(
                    () => Assert.Equal(x.Equals(y), y.Equals(x)),
                    () => Assert.Equal(x == y, y == x)), 100);

    /// <summary>Transitive equality: x.Equals(y) and y.Equals(z) implies x.Equals(z).</summary>
    [Fact]
    public void EqualityTransitivity() =>
        ErrorGenerators.EqualErrorTripleGen.Run((Action<(SystemError, SystemError, SystemError)>)(triple => {
            bool xyEqual = triple.Item1.Equals(triple.Item2);
            bool yzEqual = triple.Item2.Equals(triple.Item3);
            bool xzEqual = triple.Item1.Equals(triple.Item3);
            Test.RunAll(
                () => Assert.True(xyEqual && yzEqual && xzEqual),
                () => Assert.Equal(triple.Item1.GetHashCode(), triple.Item2.GetHashCode()),
                () => Assert.Equal(triple.Item2.GetHashCode(), triple.Item3.GetHashCode()));
        }), 100);

    /// <summary>Chained WithContext calls accumulate all contexts in message.</summary>
    [Fact]
    public void WithContextAccumulation() =>
        ErrorGenerators.SystemErrorGen
            .Select(ErrorGenerators.ContextGen.List[1, 5])
            .Run((SystemError error, List<string> contexts) => {
                SystemError accumulated = error;
                foreach (string context in contexts) {
                    accumulated = accumulated.WithContext(context);
                }
                Test.RunAll(
                    () => Assert.Equal(error.Domain, accumulated.Domain),
                    () => Assert.Equal(error.Code, accumulated.Code),
                    () => Assert.StartsWith(error.Message, accumulated.Message, StringComparison.Ordinal));
                foreach (string context in contexts) {
                    Assert.Contains($"(Context: {context})", accumulated.Message, StringComparison.Ordinal);
                }
            }, 100);

    /// <summary>DebuggerDisplay attribute produces identical format to ToString.</summary>
    [Fact]
    public void DebuggerDisplayMatchesToString() =>
        ErrorGenerators.SystemErrorGen.Run((Action<SystemError>)(error => {
            string toStringResult = error.ToString();
            PropertyInfo? debuggerDisplayProp = typeof(SystemError).GetProperty("DebuggerDisplay", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            string? debuggerDisplayResult = debuggerDisplayProp?.GetValue(error) as string;
            Assert.Equal(toStringResult, debuggerDisplayResult);
        }), 100);

    /// <summary>WithContext handles null and empty contexts without message corruption.</summary>
    [Fact]
    public void WithContextHandlesEdgeCases() =>
        ErrorGenerators.SystemErrorGen.Run((Action<SystemError>)(error => {
            SystemError withNull = error.WithContext(null!);
            SystemError withEmpty = error.WithContext(string.Empty);
            Test.RunAll(
                () => Assert.Equal(error.Domain, withNull.Domain),
                () => Assert.Equal(error.Code, withNull.Code),
                () => Assert.Equal(error.Domain, withEmpty.Domain),
                () => Assert.Equal(error.Code, withEmpty.Code),
                () => Assert.Contains(error.Message, withNull.Message, StringComparison.Ordinal),
                () => Assert.Contains(error.Message, withEmpty.Message, StringComparison.Ordinal));
        }), 100);
}

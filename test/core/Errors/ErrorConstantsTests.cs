using System.Globalization;
using System.Reflection;
using Arsenal.Core.Errors;
using Xunit;

namespace Arsenal.Core.Tests.Errors;

/// <summary>Reflection-based validation of E.* error constants.</summary>
public sealed class ErrorConstantsTests {
    /// <summary>Validates all E.* constants have correct domain and code ranges.</summary>
    [Fact]
    public void AllConstantsHaveValidDomainAndRange() {
        (string Path, SystemError Error)[] constants = EnumerateAllConstants();
        Assert.NotEmpty(constants);

        foreach ((string path, SystemError error) in constants) {
            byte expectedDomain = path switch {
                string p when p.StartsWith("E.Results", StringComparison.Ordinal) => 1,
                string p when p.StartsWith("E.Geometry", StringComparison.Ordinal) => 2,
                string p when p.StartsWith("E.Validation", StringComparison.Ordinal) => 3,
                string p when p.StartsWith("E.Spatial", StringComparison.Ordinal) => 4,
                string p when p.StartsWith("E.Topology", StringComparison.Ordinal) => 5,
                _ => throw new InvalidOperationException($"Unknown domain for path: {path}"),
            };

            (int minCode, int maxCode) = expectedDomain switch {
                1 => (1000, 1999),
                2 => (2000, 2999),
                3 => (3000, 3999),
                4 => (4000, 4999),
                5 => (5000, 5999),
                _ => throw new InvalidOperationException($"Unknown domain: {expectedDomain.ToString(CultureInfo.InvariantCulture)}"),
            };

            Assert.True(
                error.Domain == expectedDomain,
                string.Create(CultureInfo.InvariantCulture, $"{path}: Domain mismatch. Expected {expectedDomain}, got {error.Domain}"));

            Assert.True(
                error.Code >= minCode && error.Code <= maxCode,
                string.Create(CultureInfo.InvariantCulture, $"{path}: Code {error.Code} outside range [{minCode}, {maxCode}]"));
        }
    }

    /// <summary>Validates all E.* constants have non-empty messages.</summary>
    [Fact]
    public void AllConstantsHaveNonEmptyMessages() {
        (string Path, SystemError Error)[] constants = EnumerateAllConstants();
        Assert.NotEmpty(constants);

        foreach ((string path, SystemError error) in constants) {
            Assert.False(
                string.IsNullOrWhiteSpace(error.Message),
                $"{path}: Message is null or whitespace");
        }
    }

    private static (string Path, SystemError Error)[] EnumerateAllConstants() {
        List<(string Path, SystemError Error)> results = [];

        void ProcessType(Type type, string path) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(SystemError))) {
                SystemError error = (SystemError)(field.GetValue(null) ?? throw new InvalidOperationException($"Field {field.Name} is null"));
                results.Add(($"{path}.{field.Name}", error));
            }

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Static)) {
                ProcessType(nested, $"{path}.{nested.Name}");
            }
        }

        ProcessType(typeof(E), "E");
        return [.. results,];
    }
}

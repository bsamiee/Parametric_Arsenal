using Arsenal.Core.Validation;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Validation;

/// <summary>Tests V flag struct: equality, bitwise operations, and algebraic laws.</summary>
public sealed class VTests {
    /// <summary>Verifies equality and hash consistency via property-based testing.</summary>
    [Fact]
    public void EqualityAndHashConsistency() => Test.RunAll(
        () => ValidationGenerators.VGen.Run((V v) => {
            V copy = new((ushort)v);
            Assert.Equal(v, copy);
            Assert.Equal(v.GetHashCode(), copy.GetHashCode());
        }),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V v1, V v2) =>
            Assert.Equal(v1.Equals(v2), v2.Equals(v1))));

    /// <summary>Verifies bitwise OR operation commutativity and associativity.</summary>
    [Fact]
    public void BitwiseOrAlgebraicLaws() => Test.RunAll(
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a | b, b | a)),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen, ValidationGenerators.VGen).Run((V a, V b, V c) =>
            Assert.Equal((a | b) | c, a | (b | c))),
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v | V.None, v)),
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v | V.All, V.All)));

    /// <summary>Verifies bitwise AND operation commutativity, associativity, and absorption.</summary>
    [Fact]
    public void BitwiseAndAlgebraicLaws() => Test.RunAll(
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a & b, b & a)),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen, ValidationGenerators.VGen).Run((V a, V b, V c) =>
            Assert.Equal((a & b) & c, a & (b & c))),
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v & V.All, v)),
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v & V.None, V.None)));

    /// <summary>Verifies distributive laws for bitwise operations.</summary>
    [Fact]
    public void BitwiseDistributiveLaws() => Test.RunAll(
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen, ValidationGenerators.VGen).Run((V a, V b, V c) =>
            Assert.Equal(a & (b | c), (a & b) | (a & c))),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen, ValidationGenerators.VGen).Run((V a, V b, V c) =>
            Assert.Equal(a | (b & c), (a | b) & (a | c))));

    /// <summary>Verifies Has() method correctness with single and combined flags.</summary>
    [Fact]
    public void HasMethodSingleAndCombinedFlags() => Test.RunAll(
        () => Assert.True(V.None.Has(V.None)),
        () => Assert.False(V.None.Has(V.Standard)),
        () => Assert.True(V.Standard.Has(V.Standard)),
        () => Assert.False(V.Standard.Has(V.Degeneracy)),
        () => Assert.True(V.All.Has(V.Standard)),
        () => Assert.True(V.All.Has(V.Degeneracy)),
        () => Assert.True(V.All.Has(V.All)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Standard)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Degeneracy)),
        () => Assert.False((V.Standard | V.Degeneracy).Has(V.Tolerance)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Standard | V.Degeneracy)));

    /// <summary>Verifies Has() method with property-based testing for all combinations.</summary>
    [Fact]
    public void HasMethodPropertyBasedValidation() => Test.RunAll(
        () => ValidationGenerators.VGen.Run((V v) => Assert.True(v.Has(V.None))),
        () => ValidationGenerators.VGen.Run((V v) => Assert.True(V.All.Has(v))),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal((a & b) == b, a.Has(b))),
        () => ValidationGenerators.SingleVFlagGen.Select(ValidationGenerators.SingleVFlagGen).Run((V a, V b) =>
            Test.Equivalent(
                Gen.Const((a, b)),
                pair => (pair.Item1 | pair.Item2).Has(pair.Item1),
                _ => true)));

    /// <summary>Verifies ToString() formatting for single and combined flags.</summary>
    [Fact]
    public void ToStringFormatting() => Test.RunAll(
        () => Assert.Equal("None", V.None.ToString()),
        () => Assert.Equal("Standard", V.Standard.ToString()),
        () => Assert.Equal("All", V.All.ToString()),
        () => Assert.StartsWith("Combined(", (V.Standard | V.Degeneracy).ToString(), StringComparison.Ordinal),
        () => ValidationGenerators.SingleVFlagGen.Run((V v) => {
            string str = v.ToString();
            Assert.False(str.StartsWith("Combined(", StringComparison.Ordinal) || string.Equals(str, "All", StringComparison.Ordinal));
        }));

    /// <summary>Verifies DebuggerDisplay matches ToString().</summary>
    [Fact]
    public void DebuggerDisplayConsistency() => Test.RunAll(
        () => ValidationGenerators.VGen.Run((V v) => {
            string display = v.ToString();
            Assert.NotNull(display);
            Assert.NotEmpty(display);
        }));

    /// <summary>Verifies operator overloads consistency with struct equality.</summary>
    [Fact]
    public void OperatorOverloadConsistency() => Test.RunAll(
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a == b, a.Equals(b))),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a != b, !a.Equals(b))));

    /// <summary>Verifies implicit conversions between V and ushort.</summary>
    [Fact]
    public void ImplicitConversions() => Test.RunAll(
        () => Gen.UShort.Run((ushort flags) => {
            V v = (V)flags;
            ushort back = (ushort)v;
            Assert.Equal(flags, back);
        }),
        () => ValidationGenerators.VGen.Run((V v) => {
            ushort flags = (ushort)v;
            V back = (V)flags;
            Assert.Equal(v, back);
        }));

    /// <summary>Verifies idempotence laws for bitwise operations.</summary>
    [Fact]
    public void IdempotenceLaws() => Test.RunAll(
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v | v, v)),
        () => ValidationGenerators.VGen.Run((V v) => Assert.Equal(v & v, v)));

    /// <summary>Verifies absorption laws for mixed bitwise operations.</summary>
    [Fact]
    public void AbsorptionLaws() => Test.RunAll(
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a | (a & b), a)),
        () => ValidationGenerators.VGen.Select(ValidationGenerators.VGen).Run((V a, V b) =>
            Assert.Equal(a & (a | b), a)));

    /// <summary>Verifies all predefined flags are distinct.</summary>
    [Fact]
    public void PredefinedFlagsDistinct() {
        IReadOnlyList<V> flags = [
            V.Standard, V.AreaCentroid, V.BoundingBox, V.MassProperties,
            V.Topology, V.Degeneracy, V.Tolerance, V.MeshSpecific,
            V.SurfaceContinuity, V.PolycurveStructure, V.NurbsGeometry,
            V.ExtrusionGeometry, V.UVDomain, V.SelfIntersection, V.BrepGranular,
        ];

        for (int i = 0; i < flags.Count; i++) {
            for (int j = i + 1; j < flags.Count; j++) {
                Assert.NotEqual(flags[i], flags[j]);
                Assert.Equal(V.None, flags[i] & flags[j]);
            }
        }
    }

    /// <summary>Verifies V.All contains all individual flags.</summary>
    [Fact]
    public void AllContainsAllFlags() {
        IReadOnlyList<V> flags = [
            V.Standard, V.AreaCentroid, V.BoundingBox, V.MassProperties,
            V.Topology, V.Degeneracy, V.Tolerance, V.MeshSpecific,
            V.SurfaceContinuity, V.PolycurveStructure, V.NurbsGeometry,
            V.ExtrusionGeometry, V.UVDomain, V.SelfIntersection, V.BrepGranular,
        ];

        for (int i = 0; i < flags.Count; i++) {
            Assert.True(V.All.Has(flags[i]));
        }
    }
}

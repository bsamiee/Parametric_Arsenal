using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Validation;

/// <summary>Comprehensive property-based tests for V flag operations covering bitwise algebra, equality laws, and FrozenSet integrity.</summary>
public sealed class VFlagsTests {
    /// <summary>Verifies equality laws (reflexive, symmetric, transitive) and hash consistency for V flags.</summary>
    [Fact]
    public void EqualityLaws() => TestGen.RunAll(
        () => VGen.Run((Action<V>)(v => Assert.True(v.Equals(v))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.Equal(a.Equals(b), b.Equals(a))), 100),
        () => VGen.Run((Action<V>)(v => {
            V v2 = v;
            Assert.Equal(v.Equals(v2), v.GetHashCode() == v2.GetHashCode());
        }), 100),
        () => VGen.Select(VGen, VGen).Run((Action<V, V, V>)((a, b, c) => {
            bool transitiveHolds = !a.Equals(b) || !b.Equals(c) || a.Equals(c);
            Assert.True(transitiveHolds);
        }), 50));

    /// <summary>Verifies bitwise OR operation is commutative, associative, and has identity element (None).</summary>
    [Fact]
    public void BitwiseOrAlgebraicProperties() => TestGen.RunAll(
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.Equal(a | b, b | a)), 100),
        () => VGen.Select(VGen, VGen).Run((Action<V, V, V>)((a, b, c) =>
            Assert.Equal((a | b) | c, a | (b | c))), 50),
        () => VGen.Run((Action<V>)(v => Assert.Equal(v, v | V.None)), 100),
        () => VGen.Run((Action<V>)(v => Assert.Equal(V.All, v | V.All)), 100),
        () => VGen.Run((Action<V>)(v => Assert.Equal(v, v | v)), 100));

    /// <summary>Verifies bitwise AND operation is commutative, associative, and has identity element (All).</summary>
    [Fact]
    public void BitwiseAndAlgebraicProperties() => TestGen.RunAll(
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.Equal(a & b, b & a)), 100),
        () => VGen.Select(VGen, VGen).Run((Action<V, V, V>)((a, b, c) =>
            Assert.Equal((a & b) & c, a & (b & c))), 50),
        () => VGen.Run((Action<V>)(v => Assert.Equal(v, v & V.All)), 100),
        () => VGen.Run((Action<V>)(v => Assert.Equal(V.None, v & V.None)), 100),
        () => VGen.Run((Action<V>)(v => Assert.Equal(v, v & v)), 100));

    /// <summary>Verifies absorption and distributive laws for combined bitwise operations.</summary>
    [Fact]
    public void BitwiseAbsorptionAndDistributiveLaws() => TestGen.RunAll(
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.Equal(a, a | (a & b))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.Equal(a, a & (a | b))), 100),
        () => VGen.Select(VGen, VGen).Run((Action<V, V, V>)((a, b, c) =>
            Assert.Equal(a | (b & c), (a | b) & (a | c))), 50),
        () => VGen.Select(VGen, VGen).Run((Action<V, V, V>)((a, b, c) =>
            Assert.Equal(a & (b | c), (a & b) | (a & c))), 50));

    /// <summary>Verifies Has() predicate correctness with exhaustive single and combined flag tests.</summary>
    [Fact]
    public void HasPredicateCorrectness() => TestGen.RunAll(
        () => Assert.True(V.None.Has(V.None)),
        () => Assert.False(V.None.Has(V.Standard)),
        () => Assert.True(V.All.Has(V.Standard)),
        () => Assert.True(V.All.Has(V.All)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Standard)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Degeneracy)),
        () => Assert.False((V.Standard | V.Degeneracy).Has(V.Topology)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.Standard | V.Degeneracy)),
        () => Assert.False((V.Standard | V.Degeneracy).Has(V.All)),
        () => VGen.Run((Action<V>)(v => Assert.True(V.All.Has(v))), 100),
        () => VGen.Run((Action<V>)(v => Assert.True(v.Has(V.None))), 100),
        () => VGen.Run((Action<V>)(v => Assert.True(v.Has(v))), 100),
        () => SingleFlagGen.Select(SingleFlagGen).Run((Action<V, V>)((a, b) =>
            Assert.Equal(a == b, (a | b).Has(a) && (a | b).Has(b) && a.Has(a & b))), 50));

    /// <summary>Verifies ToString() produces correct representations for single, combined, and boundary flags.</summary>
    [Fact]
    public void ToStringRepresentation() => TestGen.RunAll(
        () => Assert.Equal("None", V.None.ToString()),
        () => Assert.Equal("Standard", V.Standard.ToString()),
        () => Assert.Equal("AreaCentroid", V.AreaCentroid.ToString()),
        () => Assert.Equal("BoundingBox", V.BoundingBox.ToString()),
        () => Assert.Equal("MassProperties", V.MassProperties.ToString()),
        () => Assert.Equal("Topology", V.Topology.ToString()),
        () => Assert.Equal("Degeneracy", V.Degeneracy.ToString()),
        () => Assert.Equal("Tolerance", V.Tolerance.ToString()),
        () => Assert.Equal("SelfIntersection", V.SelfIntersection.ToString()),
        () => Assert.Equal("MeshSpecific", V.MeshSpecific.ToString()),
        () => Assert.Equal("SurfaceContinuity", V.SurfaceContinuity.ToString()),
        () => Assert.Equal("All", V.All.ToString()),
        () => Assert.Contains("Combined", (V.Standard | V.Degeneracy).ToString(), StringComparison.Ordinal),
        () => Assert.Contains("Combined", (V.Topology | V.Tolerance | V.BoundingBox).ToString(), StringComparison.Ordinal),
        () => VGen.Run((Action<V>)(v => Assert.NotEmpty(v.ToString())), 100));

    /// <summary>Verifies AllFlags frozen set contains exactly the expected single flags and no duplicates.</summary>
    [Fact]
    public void AllFlagsFrozenSetIntegrity() {
        V[] expected = [V.Standard, V.AreaCentroid, V.BoundingBox, V.MassProperties, V.Topology, V.Degeneracy, V.Tolerance, V.SelfIntersection, V.MeshSpecific, V.SurfaceContinuity,];
        V[] notExpected = [V.None, V.All, V.Standard | V.Degeneracy,];

        TestGen.RunAll(
            () => Assert.Equal(10, V.AllFlags.Count),
            () => Assert.All(expected, flag => Assert.Contains(flag, (IEnumerable<V>)V.AllFlags)),
            () => Assert.All(notExpected, flag => Assert.DoesNotContain(flag, (IEnumerable<V>)V.AllFlags)),
            () => Assert.Equal(10, V.AllFlags.Distinct().Count()));
    }

    /// <summary>Verifies implicit conversions between V and ushort preserve value semantics.</summary>
    [Fact]
    public void ImplicitConversionPreservesValueSemantics() => TestGen.RunAll(
        () => Assert.Equal(0, (ushort)V.None),
        () => Assert.Equal(1, (ushort)V.Standard),
        () => Assert.Equal(2, (ushort)V.AreaCentroid),
        () => Assert.Equal(4, (ushort)V.BoundingBox),
        () => Assert.Equal(8, (ushort)V.MassProperties),
        () => Assert.Equal(16, (ushort)V.Topology),
        () => Assert.Equal(32, (ushort)V.Degeneracy),
        () => Assert.Equal(64, (ushort)V.Tolerance),
        () => Assert.Equal(128, (ushort)V.SelfIntersection),
        () => Assert.Equal(256, (ushort)V.MeshSpecific),
        () => Assert.Equal(512, (ushort)V.SurfaceContinuity),
        () => Gen.UShort[0, 1023].Run((Action<ushort>)(u =>
            Assert.Equal(u, (ushort)(V)u)), 100),
        () => Assert.Equal(3, (ushort)(V.Standard | V.AreaCentroid)),
        () => Assert.Equal(5, (ushort)(V.Standard | V.BoundingBox)),
        () => Assert.Equal(7, (ushort)(V.Standard | V.AreaCentroid | V.BoundingBox)));

    /// <summary>Verifies equality operators (==, !=) match Equals() semantics.</summary>
    [Fact]
    public void EqualityOperatorConsistency() => TestGen.RunAll(
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) =>
            Assert.Equal(a.Equals(b), a == b)), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) =>
            Assert.Equal(!a.Equals(b), a != b)), 100),
        () => {
            V none1 = V.None;
            V none2 = V.None;
            Assert.True(none1 == none2);
        },
        () => {
            V all1 = V.All;
            V all2 = V.All;
            Assert.True(all1 == all2);
        },
        () => Assert.False(V.None == V.Standard),
        () => Assert.True(V.None != V.Standard),
        () => Assert.True((V.Standard | V.Degeneracy) == (V.Standard | V.Degeneracy)),
        () => Assert.False((V.Standard | V.Degeneracy) == (V.Standard | V.Topology)));

    /// <summary>Verifies GetHashCode() consistency with equality and deterministic behavior.</summary>
    [Fact]
    public void GetHashCodeConsistencyAndDeterminism() => TestGen.RunAll(
        () => VGen.Run((Action<V>)(v => Assert.Equal(v.GetHashCode(), v.GetHashCode())), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) =>
            Assert.True(a.GetHashCode() != b.GetHashCode() || a == b)), 100),
        () => Assert.Equal(V.None.GetHashCode(), V.None.GetHashCode()),
        () => Assert.Equal(V.Standard.GetHashCode(), V.Standard.GetHashCode()),
        () => Assert.Equal((V.Standard | V.Degeneracy).GetHashCode(), (V.Standard | V.Degeneracy).GetHashCode()),
        () => VGen.Run((Action<V>)(v => {
            ushort u = v;
            Assert.Equal(u.GetHashCode(), v.GetHashCode());
        }), 100));

    /// <summary>Verifies flag composition completeness: All equals union of all single flags.</summary>
    [Fact]
    public void AllFlagCompositionCompleteness() {
        V computed = V.Standard | V.AreaCentroid | V.BoundingBox | V.MassProperties |
                     V.Topology | V.Degeneracy | V.Tolerance | V.SelfIntersection |
                     V.MeshSpecific | V.SurfaceContinuity;

        TestGen.RunAll(
            () => Assert.Equal(V.All, computed),
            () => Assert.True(V.All.Has(V.Standard)),
            () => Assert.True(V.All.Has(V.AreaCentroid)),
            () => Assert.True(V.All.Has(V.BoundingBox)),
            () => Assert.True(V.All.Has(V.MassProperties)),
            () => Assert.True(V.All.Has(V.Topology)),
            () => Assert.True(V.All.Has(V.Degeneracy)),
            () => Assert.True(V.All.Has(V.Tolerance)),
            () => Assert.True(V.All.Has(V.SelfIntersection)),
            () => Assert.True(V.All.Has(V.MeshSpecific)),
            () => Assert.True(V.All.Has(V.SurfaceContinuity)),
            () => Assert.Equal((ushort)1023, (ushort)V.All),
            () => Assert.Equal((ushort)1023, (ushort)computed));
    }

    /// <summary>Verifies Has() with None flag exhibits correct zero-element semantics.</summary>
    [Fact]
    public void HasNoneFlagZeroElementSemantics() => TestGen.RunAll(
        () => Assert.True(V.None.Has(V.None)),
        () => Assert.True(V.Standard.Has(V.None)),
        () => Assert.True(V.All.Has(V.None)),
        () => Assert.True((V.Standard | V.Degeneracy).Has(V.None)),
        () => VGen.Run((Action<V>)(v => Assert.True(v.Has(V.None))), 100),
        () => Assert.False(V.None.Has(V.Standard)),
        () => Assert.False(V.None.Has(V.All)));

    /// <summary>Verifies structural equality via Equals(object) with type checking.</summary>
    [Fact]
    public void EqualsObjectStructuralEquality() => TestGen.RunAll(
        () => VGen.Run((Action<V>)(v => Assert.True(v.Equals((object)v))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => {
            object bObj = b;
            Assert.Equal(a.Equals(b), a.Equals(bObj));
        }), 100),
        () => Assert.False(V.Standard.Equals(42)),
        () => {
            object s = "Standard";
            Assert.False(V.Standard.Equals(s));
        },
        () => Assert.False(V.Standard.Equals(null)),
        () => Assert.True(V.None.Equals((object)V.None)));

    /// <summary>Verifies bitwise operations maintain monotonicity: a ⊆ (a | b) and (a & b) ⊆ a.</summary>
    [Fact]
    public void BitwiseOperationsMonotonicity() => TestGen.RunAll(
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.True((a | b).Has(a))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.True((a | b).Has(b))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.True(a.Has(a & b))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) => Assert.True(b.Has(a & b))), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) =>
            Assert.True((ushort)(a & b) <= (ushort)a && (ushort)(a & b) <= (ushort)b)), 100),
        () => VGen.Select(VGen).Run((Action<V, V>)((a, b) =>
            Assert.True((ushort)a <= (ushort)(a | b) && (ushort)b <= (ushort)(a | b))), 100));

    /// <summary>Verifies De Morgan's laws hold for bitwise operations: ¬(a ∨ b) = ¬a ∧ ¬b, ¬(a ∧ b) = ¬a ∨ ¬b (via exhaustive enumeration).</summary>
    [Fact]
    public void DeMorgansLawsViaExhaustiveEnumeration() {
        V[] allValues = [V.None, V.Standard, V.AreaCentroid, V.BoundingBox, V.MassProperties,
                         V.Topology, V.Degeneracy, V.Tolerance, V.SelfIntersection,
                         V.MeshSpecific, V.SurfaceContinuity, V.All,];

        foreach (V a in allValues) {
            foreach (V b in allValues) {
                ushort aU = a;
                ushort bU = b;
                ushort aOrB = (ushort)(a | b);
                ushort aAndB = (ushort)(a & b);
                ushort notAOrB = (ushort)~aOrB;
                ushort notA = (ushort)~aU;
                ushort notB = (ushort)~bU;
                ushort notAAndNotB = (ushort)(notA & notB);
                ushort notAAndB = (ushort)~aAndB;
                ushort notAOrNotB = (ushort)(notA | notB);

                Assert.Equal(notAOrB, notAAndNotB);
                Assert.Equal(notAAndB, notAOrNotB);
            }
        }
    }

    private static readonly Gen<V> SingleFlagGen = Gen.OneOf(
        Gen.Const(V.Standard),
        Gen.Const(V.AreaCentroid),
        Gen.Const(V.BoundingBox),
        Gen.Const(V.MassProperties),
        Gen.Const(V.Topology),
        Gen.Const(V.Degeneracy),
        Gen.Const(V.Tolerance),
        Gen.Const(V.SelfIntersection),
        Gen.Const(V.MeshSpecific),
        Gen.Const(V.SurfaceContinuity));

    private static readonly Gen<V> VGen = Gen.Frequency([
        (2, Gen.Const(V.None)),
        (2, Gen.Const(V.All)),
        (15, SingleFlagGen),
        (8, SingleFlagGen.Select(SingleFlagGen).Select(static (a, b) => a | b)),
        (3, SingleFlagGen.Select(SingleFlagGen, SingleFlagGen).Select(static (a, b, c) => a | b | c)),
    ]);
}

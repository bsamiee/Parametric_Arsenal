using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using CsCheck;

namespace Arsenal.Core.Tests.Validation;

/// <summary>Validation generators with zero-allocation static lambdas.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Test generators used for property-based testing")]
public static class ValidationGenerators {
    private static readonly V[] SingleFlags = [
        V.Standard,
        V.AreaCentroid,
        V.BoundingBox,
        V.MassProperties,
        V.Topology,
        V.Degeneracy,
        V.Tolerance,
        V.MeshSpecific,
        V.SurfaceContinuity,
        V.PolycurveStructure,
        V.NurbsGeometry,
        V.ExtrusionGeometry,
        V.UVDomain,
        V.SelfIntersection,
        V.BrepGranular,
    ];

    /// <summary>Generates V flags with uniform distribution across None, single flags, combinations, and All.</summary>
    [Pure] public static Gen<V> VGen => Gen.Frequency([
        (1, Gen.Const(V.None)),
        (3, Gen.OneOfConst(SingleFlags)),
        (2, CombinedVFlagGen),
        (1, Gen.Const(V.All)),
    ]);

    /// <summary>Generates single V flags (excluding None and All).</summary>
    [Pure] public static Gen<V> SingleVFlagGen => Gen.OneOfConst(SingleFlags);

    /// <summary>Generates combined V flags via bitwise OR of 2-5 random flags.</summary>
    [Pure] public static Gen<V> CombinedVFlagGen =>
        Gen.Int[2, 5].SelectMany(count =>
            Gen.Shuffle(SingleFlags, count).Select(static flags => {
                V result = V.None;
                for (int i = 0; i < flags.Length; i++) {
                    result |= flags[i];
                }
                return result;
            }));

    /// <summary>Generates V flags from raw ushort values for edge case testing.</summary>
    [Pure] public static Gen<V> RawVFlagGen => Gen.UShort.Select(static flags => new V(flags));
}

using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Operation metadata: validation, name, parameter type.</summary>
    internal static readonly FrozenDictionary<(byte Op, Type Type), (V Validation, string Name)> Operations =
        new Dictionary<(byte, Type), (V, string)> {
            [(1, typeof(Mesh))] = (V.Standard | V.Topology, "CageDeform"),
            [(1, typeof(Brep))] = (V.Standard | V.Topology, "CageDeform"),
            [(2, typeof(Mesh))] = (V.Standard | V.MeshSpecific | V.Topology, "SubdivideCatmullClark"),
            [(3, typeof(Mesh))] = (V.Standard | V.MeshSpecific | V.Topology, "SubdivideLoop"),
            [(4, typeof(Mesh))] = (V.Standard | V.MeshSpecific | V.Topology, "SubdivideButterfly"),
            [(10, typeof(Mesh))] = (V.Standard | V.MeshSpecific, "SmoothLaplacian"),
            [(11, typeof(Mesh))] = (V.Standard | V.MeshSpecific, "SmoothTaubin"),
            [(12, typeof(Mesh))] = (V.Standard | V.MeshSpecific, "MeshOffset"),
            [(13, typeof(Mesh))] = (V.Standard | V.MeshSpecific | V.Topology, "MeshReduce"),
            [(14, typeof(Mesh))] = (V.Standard | V.MeshSpecific, "MeshRemesh"),
            [(20, typeof(Mesh))] = (V.Standard | V.MeshSpecific, "EvolveMeanCurvature"),
        }.ToFrozenDictionary();

    /// <summary>Operation names by ID for O(1) lookup, derived from Operations.</summary>
    private static readonly FrozenDictionary<byte, string> OperationNames =
        Operations
            .GroupBy(static kv => kv.Key.Op)
            .ToDictionary(static g => g.Key, static g => g.First().Value.Name)
            .ToFrozenDictionary();

    [Pure] internal static V ValidationMode(byte op, Type type) => Operations.TryGetValue((op, type), out (V v, string _) meta) ? meta.v : V.Standard;
    [Pure] internal static string OperationName(byte op) => OperationNames.TryGetValue(op, out string? name) ? name : $"Op{op}";

    /// <summary>Operation ID constants.</summary>
    internal const byte OpCageDeform = 1;
    internal const byte OpSubdivideCatmullClark = 2;
    internal const byte OpSubdivideLoop = 3;
    internal const byte OpSubdivideButterfly = 4;
    internal const byte OpSmoothLaplacian = 10;
    internal const byte OpSmoothTaubin = 11;
    internal const byte OpOffset = 12;
    internal const byte OpReduce = 13;
    internal const byte OpRemesh = 14;
    internal const byte OpEvolveMeanCurvature = 20;

    /// <summary>Subdivision algorithms requiring triangulated meshes.</summary>
    internal static readonly FrozenSet<byte> TriangulatedSubdivisionOps = new HashSet<byte> { OpSubdivideLoop, OpSubdivideButterfly, }.ToFrozenSet();

    /// <summary>Cage deformation configuration.</summary>
    internal const int MinCageControlPoints = 8;

    /// <summary>Subdivision configuration.</summary>
    internal const int MaxSubdivisionLevels = 5;

    /// <summary>Loop subdivision weights.</summary>
    internal const double LoopBetaValence3 = 0.1875;
    internal const double LoopBetaValence6 = 0.0625;
    internal const double LoopCenterWeight = 0.625;
    internal const double LoopNeighborBase = 0.375;
    internal const double LoopCosineMultiplier = 0.25;
    internal const double LoopEdgeMidpointWeight = 0.375;
    internal const double LoopEdgeOppositeWeight = 0.125;

    /// <summary>Butterfly subdivision weights.</summary>
    internal const double ButterflyMidpointWeight = 0.5;
    internal const double ButterflyOppositeWeight = 0.125;
    internal const double ButterflyWingWeight = 0.0625;

    /// <summary>Smoothing configuration.</summary>
    internal const int MaxSmoothingIterations = 1000;
    internal const double ConvergenceMultiplier = 100.0;
    internal const double UniformLaplacianWeight = 1.0;

    /// <summary>Mesh quality validation thresholds.</summary>
    internal static readonly double MinAngleRadiansThreshold = RhinoMath.ToRadians(5.0);
    internal const double AspectRatioThreshold = 10.0;

    /// <summary>Mesh offset configuration.</summary>
    internal const double MinOffsetDistance = 0.001;
    internal const double MaxOffsetDistance = 1000.0;

    /// <summary>Mesh reduction configuration.</summary>
    internal const int MinReductionFaceCount = 4;
    internal const double MinReductionAccuracy = 0.0;
    internal const double MaxReductionAccuracy = 1.0;
    internal const double DefaultReductionAccuracy = 0.5;
    internal const int ReductionAccuracyScale = 10;
    internal const double ReductionTargetTolerance = 1.1;

    /// <summary>Remeshing configuration.</summary>
    internal const double RemeshMinEdgeLengthFactor = 0.1;
    internal const double RemeshMaxEdgeLengthFactor = 0.5;
    internal const int MaxRemeshIterations = 100;
    internal const double RemeshSplitThresholdFactor = 1.33;
    internal const double RemeshUniformityWeight = 0.8;
    internal const double RemeshConvergenceThreshold = 0.1;
    internal const double EdgeMidpointParameter = 0.5;
}

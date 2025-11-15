using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Validation mode dispatch: (operation ID, input type) → validation flags.</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type InputType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.Topology,
            [(1, typeof(global::Rhino.Geometry.Brep))] = V.Standard | V.Topology,
            [(2, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(3, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(4, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(10, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(11, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(12, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(13, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(14, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(20, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Operation names for diagnostics.</summary>
    internal static readonly FrozenDictionary<byte, string> OperationNames =
        new Dictionary<byte, string> {
            [1] = "CageDeform",
            [2] = "SubdivideCatmullClark",
            [3] = "SubdivideLoop",
            [4] = "SubdivideButterfly",
            [10] = "SmoothLaplacian",
            [11] = "SmoothTaubin",
            [12] = "MeshOffset",
            [13] = "MeshReduce",
            [14] = "MeshRemesh",
            [20] = "EvolveMeanCurvature",
        }.ToFrozenDictionary();

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

    /// <summary>Taubin smoothing parameters.</summary>
    internal const double TaubinLambda = 0.6307;
    internal const double TaubinMu = -0.6732;

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

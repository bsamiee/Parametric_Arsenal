using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Minimum cage control points for trilinear interpolation (cube=8).</summary>
    internal const int MinCageControlPoints = 8;

    /// <summary>Maximum subdivision levels to prevent exponential face growth.</summary>
    internal const int MaxSubdivisionLevels = 5;

    /// <summary>Maximum smoothing iterations to prevent infinite loops.</summary>
    internal const int MaxSmoothingIterations = 1000;

    /// <summary>Feature edge detection threshold: 30° in radians.</summary>
    internal static readonly double FeatureAngleRadians = RhinoMath.ToRadians(30.0);

    /// <summary>Minimum triangle angle threshold for quality validation: 5° in radians.</summary>
    internal static readonly double MinAngleRadiansThreshold = RhinoMath.ToRadians(5.0);

    /// <summary>Degenerate triangle detection: maximum aspect ratio (max/min edge).</summary>
    internal const double AspectRatioThreshold = 10.0;

    /// <summary>Taubin smoothing: λ parameter for positive smoothing step.</summary>
    internal const double TaubinLambda = 0.6307;

    /// <summary>Taubin smoothing: μ parameter for negative unshrinking step (must be &lt; -λ).</summary>
    internal const double TaubinMu = -0.6732;

    /// <summary>RMS convergence threshold: multiplier × absolute tolerance.</summary>
    internal const double ConvergenceMultiplier = 100.0;

    /// <summary>Mean curvature flow timestep safety factor.</summary>
    internal const double CurvatureFlowTimestepFactor = 0.01;

    /// <summary>Loop subdivision: β-weight for valence-3 vertices (3/16).</summary>
    internal const double LoopBetaValence3 = 0.1875;

    /// <summary>Loop subdivision: β-weight for valence-6 vertices (1/16).</summary>
    internal const double LoopBetaValence6 = 0.0625;

    /// <summary>Loop subdivision: centering weight (5/8).</summary>
    internal const double LoopCenterWeight = 0.625;

    /// <summary>Loop subdivision: neighbor contribution base (3/8).</summary>
    internal const double LoopNeighborBase = 0.375;

    /// <summary>Loop subdivision: cosine multiplier for irregular valence (1/4).</summary>
    internal const double LoopCosineMultiplier = 0.25;

    /// <summary>Loop subdivision: edge midpoint weight (3/8 per endpoint).</summary>
    internal const double LoopEdgeMidpointWeight = 0.375;

    /// <summary>Loop subdivision: edge opposite weight (1/8 per vertex).</summary>
    internal const double LoopEdgeOppositeWeight = 0.125;

    /// <summary>Butterfly subdivision: midpoint weight (1/2).</summary>
    internal const double ButterflyMidpointWeight = 0.5;

    /// <summary>Butterfly subdivision: opposite vertex weight (1/8 each).</summary>
    internal const double ButterflyOppositeWeight = 0.125;

    /// <summary>Butterfly subdivision: wing vertex weight (-1/16 each).</summary>
    internal const double ButterflyWingWeight = 0.0625;

    /// <summary>Uniform Laplacian weight (neighbor average).</summary>
    internal const double UniformLaplacianWeight = 1.0;

    /// <summary>Mesh offset minimum distance threshold (0.001 mm).</summary>
    internal const double MinOffsetDistance = 0.001;

    /// <summary>Mesh offset maximum distance threshold (1000 mm).</summary>
    internal const double MaxOffsetDistance = 1000.0;

    /// <summary>Mesh reduction minimum target face count.</summary>
    internal const int MinReductionFaceCount = 4;

    /// <summary>Mesh reduction: accuracy range [0.0=fast, 1.0=accurate].</summary>
    internal const double MinReductionAccuracy = 0.0;
    internal const double MaxReductionAccuracy = 1.0;
    internal const double DefaultReductionAccuracy = 0.5;

    /// <summary>Remesh minimum edge length (absolute tolerance multiplier).</summary>
    internal const double RemeshMinEdgeLengthFactor = 0.1;

    /// <summary>Remesh maximum edge length (bounding box diagonal fraction).</summary>
    internal const double RemeshMaxEdgeLengthFactor = 0.5;

    /// <summary>Remesh maximum iterations to prevent infinite loops.</summary>
    internal const int MaxRemeshIterations = 100;

    /// <summary>Remesh edge split length threshold (target × factor).</summary>
    internal const double RemeshSplitThresholdFactor = 1.33;

    /// <summary>Remesh edge collapse length threshold (target × factor).</summary>
    internal const double RemeshCollapseThresholdFactor = 0.75;

    /// <summary>Remeshing: uniformity score weight for edge length deviation.</summary>
    internal const double RemeshUniformityWeight = 0.8;

    /// <summary>RBF Gaussian kernel width parameter.</summary>
    internal const double RBFKernelWidth = 1.0;

    /// <summary>RBF regularization parameter (Tikhonov).</summary>
    internal const double RBFRegularization = 1e-6;

    /// <summary>Cotangent weight clamping threshold for obtuse triangles.</summary>
    internal const double CotangentClampMin = 0.0;

    /// <summary>Adaptive subdivision: maximum aspect ratio quality threshold.</summary>
    internal const double AdaptiveAspectRatioThreshold = 3.0;

    /// <summary>Adaptive subdivision: minimum angle threshold 15° in radians.</summary>
    internal static readonly double AdaptiveMinAngleThreshold = RhinoMath.ToRadians(15.0);

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
            [(15, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(20, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(21, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Operation names for diagnostics.</summary>
    internal static readonly FrozenDictionary<byte, string> OperationNames =
        new Dictionary<byte, string> {
            [1] = "CageDeform",
            [2] = "SubdivideCatmullClark",
            [3] = "SubdivideLoop",
            [4] = "SubdivideButterfly",
            [5] = "CageDeformRBF",
            [10] = "SmoothLaplacian",
            [11] = "SmoothTaubin",
            [12] = "MeshOffset",
            [13] = "MeshReduce",
            [14] = "MeshRemesh",
            [15] = "AdaptiveSubdivision",
            [20] = "EvolveMeanCurvature",
            [21] = "SmoothLaplacianCotangent",
        }.ToFrozenDictionary();

    /// <summary>Operation ID constants for internal use.</summary>
    internal const byte OpCageDeform = 1;
    internal const byte OpSubdivideCatmullClark = 2;
    internal const byte OpSubdivideLoop = 3;
    internal const byte OpSubdivideButterfly = 4;
    internal const byte OpCageDeformRBF = 5;
    internal const byte OpSmoothLaplacian = 10;
    internal const byte OpSmoothTaubin = 11;
    internal const byte OpOffset = 12;
    internal const byte OpReduce = 13;
    internal const byte OpRemesh = 14;
    internal const byte OpAdaptiveSubdivision = 15;
    internal const byte OpEvolveMeanCurvature = 20;
    internal const byte OpSmoothLaplacianCotangent = 21;
}

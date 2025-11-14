using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration: constants, validation modes, operation mappings.</summary>
internal static class MorphologyConfig {
    /// <summary>Minimum cage control points (cube=8 vertices).</summary>
    internal const int MinCageControlPoints = 8;

    /// <summary>Maximum subdivision levels to prevent exponential growth.</summary>
    internal const int MaxSubdivisionLevels = 5;

    /// <summary>Maximum smoothing iterations to prevent infinite loops.</summary>
    internal const int MaxSmoothingIterations = 1000;

    /// <summary>Sharp edge detection threshold (30°).</summary>
    internal static readonly double FeatureAngleRadians = RhinoMath.ToRadians(30.0);

    /// <summary>Minimum triangle angle for quality validation (5°).</summary>
    internal static readonly double MinAngleRadiansThreshold = RhinoMath.ToRadians(5.0);

    /// <summary>Degenerate triangle threshold (max/min edge ratio).</summary>
    internal const double AspectRatioThreshold = 10.0;

    /// <summary>Taubin λ parameter (positive smoothing).</summary>
    internal const double TaubinLambda = 0.6307;

    /// <summary>Taubin μ parameter (negative unshrinking, must be &lt; -λ).</summary>
    internal const double TaubinMu = -0.6732;

    /// <summary>RMS convergence threshold multiplier (×tolerance).</summary>
    internal const double ConvergenceMultiplier = 100.0;

    /// <summary>Mean curvature flow timestep safety factor.</summary>
    internal const double CurvatureFlowTimestepFactor = 0.01;

    /// <summary>Loop β-weight for valence-3 vertices (3/16).</summary>
    internal const double LoopBetaValence3 = 0.1875;

    /// <summary>Loop β-weight for valence-6 vertices (1/16).</summary>
    internal const double LoopBetaValence6 = 0.0625;

    /// <summary>Loop centering weight (5/8).</summary>
    internal const double LoopCenterWeight = 0.625;

    /// <summary>Loop neighbor contribution base (3/8).</summary>
    internal const double LoopNeighborBase = 0.375;

    /// <summary>Loop cosine multiplier for irregular valence (1/4).</summary>
    internal const double LoopCosineMultiplier = 0.25;

    /// <summary>Loop edge midpoint weight (3/8 per endpoint).</summary>
    internal const double LoopEdgeMidpointWeight = 0.375;

    /// <summary>Loop edge opposite weight (1/8 per vertex).</summary>
    internal const double LoopEdgeOppositeWeight = 0.125;

    /// <summary>Butterfly midpoint weight (1/2).</summary>
    internal const double ButterflyMidpointWeight = 0.5;

    /// <summary>Butterfly opposite vertex weight (1/8 each).</summary>
    internal const double ButterflyOppositeWeight = 0.125;

    /// <summary>Butterfly wing vertex weight (-1/16 each).</summary>
    internal const double ButterflyWingWeight = 0.0625;

    /// <summary>Uniform Laplacian weight (neighbor average).</summary>
    internal const double UniformLaplacianWeight = 1.0;

    /// <summary>Operation validation mode dispatch by (operation ID, input type).</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type InputType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.Topology,
            [(1, typeof(global::Rhino.Geometry.Brep))] = V.Standard | V.Topology,
            [(2, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(3, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(4, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(10, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
            [(11, typeof(global::Rhino.Geometry.Mesh))] = V.Standard | V.MeshSpecific,
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
            [20] = "EvolveMeanCurvature",
        }.ToFrozenDictionary();

    /// <summary>Operation ID constants for internal use.</summary>
    internal const byte OpCageDeform = 1;
    internal const byte OpSubdivideCatmullClark = 2;
    internal const byte OpSubdivideLoop = 3;
    internal const byte OpSubdivideButterfly = 4;
    internal const byte OpSmoothLaplacian = 10;
    internal const byte OpSmoothTaubin = 11;
    internal const byte OpEvolveMeanCurvature = 20;
}

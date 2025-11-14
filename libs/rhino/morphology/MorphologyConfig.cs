using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration: constants, validation modes, operation mappings.</summary>
internal static class MorphologyConfig {
    /// <summary>Minimum control points required for cage deformation (cube vertices).</summary>
    internal const int MinCageControlPoints = 8;

    /// <summary>Maximum subdivision levels to prevent exponential face explosion.</summary>
    internal const int MaxSubdivisionLevels = 5;

    /// <summary>Maximum iterations for iterative smoothing algorithms to prevent infinite loops.</summary>
    internal const int MaxSmoothingIterations = 1000;

    /// <summary>Feature edge angle threshold in radians (30 degrees) for sharp edge detection.</summary>
    internal static readonly double FeatureAngleRadians = RhinoMath.ToRadians(30.0);

    /// <summary>Minimum acceptable triangle angle in radians (5 degrees) for quality validation.</summary>
    internal static readonly double MinAngleRadiansThreshold = RhinoMath.ToRadians(5.0);

    /// <summary>Aspect ratio threshold (max_edge / min_edge) indicating degenerate triangles.</summary>
    internal const double AspectRatioThreshold = 10.0;

    /// <summary>Taubin smoothing lambda parameter (positive smoothing weight).</summary>
    internal const double TaubinLambda = 0.6307;

    /// <summary>Taubin smoothing mu parameter (negative unshrinking weight, must be &lt; -lambda).</summary>
    internal const double TaubinMu = -0.6732;

    /// <summary>Convergence multiplier for RMS displacement threshold (context.AbsoluteTolerance * multiplier).</summary>
    internal const double ConvergenceMultiplier = 100.0;

    /// <summary>Mean curvature flow timestep safety factor relative to minimum edge length.</summary>
    internal const double CurvatureFlowTimestepFactor = 0.01;

    /// <summary>Validation mode dispatch: maps (operation ID, input type) to validation flags.</summary>
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

    /// <summary>Operation name mapping for diagnostics and error messages.</summary>
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

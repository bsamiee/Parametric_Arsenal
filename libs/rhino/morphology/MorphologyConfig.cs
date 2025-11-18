using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Gets validation mode for a morphology request and geometry type.</summary>
    [Pure]
    internal static V GetValidationMode(Morphology.MorphologyRequest request, Type geometryType) =>
        (request, geometryType) switch {
            (Morphology.CageDeformation, Type t) when t == typeof(Mesh) || t == typeof(Brep) => V.Standard | V.Topology,
            (Morphology.CatmullClarkSubdivision, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific | V.Topology,
            (Morphology.LoopSubdivision, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific | V.Topology,
            (Morphology.ButterflySubdivision, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific | V.Topology,
            (Morphology.LaplacianSmoothing, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.TaubinSmoothing, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.MeanCurvatureEvolution, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.MeshOffsetRequest, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.MeshReduction, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific | V.Topology,
            (Morphology.IsotropicRemeshing, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.BrepConversion, Type t) when t == typeof(Brep) => V.Standard | V.BoundingBox,
            (Morphology.MeshRepairRequest, Type t) when t == typeof(Mesh) => V.Standard | V.Topology | V.MeshSpecific,
            (Morphology.MeshThickening, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.MeshUnwrapping, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            (Morphology.MeshSeparation, Type t) when t == typeof(Mesh) => V.Standard | V.Topology,
            (Morphology.MeshWelding, Type t) when t == typeof(Mesh) => V.Standard | V.MeshSpecific,
            _ => V.Standard,
        };

    /// <summary>Gets operation name for a morphology request.</summary>
    [Pure]
    internal static string GetOperationName(Morphology.MorphologyRequest request) =>
        request switch {
            Morphology.CageDeformation => "CageDeform",
            Morphology.CatmullClarkSubdivision => "SubdivideCatmullClark",
            Morphology.LoopSubdivision => "SubdivideLoop",
            Morphology.ButterflySubdivision => "SubdivideButterfly",
            Morphology.LaplacianSmoothing => "SmoothLaplacian",
            Morphology.TaubinSmoothing => "SmoothTaubin",
            Morphology.MeanCurvatureEvolution => "EvolveMeanCurvature",
            Morphology.MeshOffsetRequest => "MeshOffset",
            Morphology.MeshReduction => "MeshReduce",
            Morphology.IsotropicRemeshing => "MeshRemesh",
            Morphology.BrepConversion => "BrepToMesh",
            Morphology.MeshRepairRequest => "MeshRepair",
            Morphology.MeshThickening => "MeshThicken",
            Morphology.MeshUnwrapping => "MeshUnwrap",
            Morphology.MeshSeparation => "MeshSeparate",
            Morphology.MeshWelding => "MeshWeld",
            _ => request.GetType().Name,
        };

    /// <summary>Subdivision algorithms requiring triangulated meshes.</summary>
    [Pure]
    internal static bool RequiresTriangulatedMesh(Morphology.SubdivisionRequest request) =>
        request is Morphology.LoopSubdivision or Morphology.ButterflySubdivision;

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

    /// <summary>Mesh repair configuration.</summary>
    internal const double MinWeldTolerance = 0.0001;
    internal const double MaxWeldTolerance = 100.0;
    internal const double DefaultWeldTolerance = 0.01;

    /// <summary>Brep to mesh conversion configuration.</summary>
    internal const double MaxAcceptableAspectRatio = 10.0;
    internal static readonly double IdealTriangleAngleRadians = RhinoMath.ToRadians(60.0);

    /// <summary>Mesh thickening configuration.</summary>
    internal const double MinThickenDistance = 0.0001;
    internal const double MaxThickenDistance = 10000.0;

    /// <summary>Mesh repair operation dispatch: operation type → mesh action.</summary>
    internal static readonly FrozenDictionary<string, Func<Mesh, double, bool>> RepairActions =
        new Dictionary<string, Func<Mesh, double, bool>> {
            ["FillHoles"] = static (m, _) => m.FillHoles(),
            ["UnifyNormals"] = static (m, _) => m.UnifyNormals() >= 0,
            ["CullDegenerateFaces"] = static (m, _) => m.Faces.CullDegenerateFaces() >= 0,
            ["Compact"] = static (m, _) => m.Compact(),
            ["Weld"] = static (m, _) => m.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true),
        }.ToFrozenDictionary();
}

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Resolves validation and operation names for each algebraic request.</summary>
    [Pure]
    internal static bool TryResolveMetadata(
        Morphology.MorphologyRequest request,
        out (V Validation, string Name) metadata) {
        metadata = request switch
        {
            Morphology.MeshCageDeformationRequest => (V.Standard | V.Topology, "CageDeform"),
            Morphology.BrepCageDeformationRequest => (V.Standard | V.Topology, "CageDeform"),
            Morphology.SubdivisionRequest subdivision => subdivision.Strategy switch
            {
                Morphology.CatmullClarkSubdivisionStrategy => (V.Standard | V.MeshSpecific | V.Topology, "SubdivideCatmullClark"),
                Morphology.LoopSubdivisionStrategy => (V.Standard | V.MeshSpecific | V.Topology, "SubdivideLoop"),
                Morphology.ButterflySubdivisionStrategy => (V.Standard | V.MeshSpecific | V.Topology, "SubdivideButterfly"),
                _ => default,
            },
            Morphology.SmoothingRequest smoothing => smoothing.Strategy switch
            {
                Morphology.LaplacianSmoothingStrategy => (V.Standard | V.MeshSpecific, "SmoothLaplacian"),
                Morphology.TaubinSmoothingStrategy => (V.Standard | V.MeshSpecific, "SmoothTaubin"),
                Morphology.MeanCurvatureFlowStrategy => (V.Standard | V.MeshSpecific, "EvolveMeanCurvature"),
                _ => default,
            },
            Morphology.MeshOffsetRequest => (V.Standard | V.MeshSpecific, "MeshOffset"),
            Morphology.MeshReductionRequest => (V.Standard | V.MeshSpecific, "MeshReduce"),
            Morphology.MeshRemeshRequest => (V.Standard | V.MeshSpecific, "MeshRemesh"),
            Morphology.BrepMeshingRequest => (V.Standard | V.BoundingBox, "BrepToMesh"),
            Morphology.MeshRepairRequest => (V.Standard | V.MeshSpecific | V.Topology, "MeshRepair"),
            Morphology.MeshThickenRequest => (V.Standard | V.MeshSpecific, "MeshThicken"),
            Morphology.MeshUnwrapRequest unwrap => unwrap.Strategy switch
            {
                Morphology.AngleBasedUnwrapStrategy => (V.Standard | V.MeshSpecific, "MeshUnwrap.AngleBased"),
                Morphology.ConformalEnergyUnwrapStrategy => (V.Standard | V.MeshSpecific, "MeshUnwrap.Conformal"),
                _ => default,
            },
            Morphology.MeshSeparationRequest => (V.Standard | V.Topology, "MeshSeparate"),
            Morphology.MeshWeldRequest => (V.Standard | V.MeshSpecific, "MeshWeld"),
            _ => default,
        };
        return metadata.Name is not null;
    }

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

    /// <summary>Mesh repair operation dispatch keyed by algebraic operation type.</summary>
    internal static readonly FrozenDictionary<Type, (string Name, Func<Mesh, double, bool> Action)> RepairOperations =
        new Dictionary<Type, (string, Func<Mesh, double, bool>)> {
            [typeof(Morphology.FillHolesRepair)] = ("FillHoles", static (mesh, _) => mesh.FillHoles()),
            [typeof(Morphology.UnifyNormalsRepair)] = ("UnifyNormals", static (mesh, _) => mesh.UnifyNormals() >= 0),
            [typeof(Morphology.CullDegenerateFacesRepair)] = ("CullDegenerateFaces", static (mesh, _) => mesh.Faces.CullDegenerateFaces() >= 0),
            [typeof(Morphology.CompactRepair)] = ("Compact", static (mesh, _) => mesh.Compact()),
            [typeof(Morphology.WeldVerticesRepair)] = ("Weld", static (mesh, _) => mesh.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true)),
        }.ToFrozenDictionary();
}

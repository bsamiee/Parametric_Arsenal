using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and unified singular dispatch table.</summary>
internal static class MorphologyConfig {
    /// <summary>Unified morphology operation metadata with discriminators for algorithm codes and repair actions.</summary>
    internal sealed record MorphologyOperationMetadata(
        V ValidationMode,
        string OperationName,
        byte? AlgorithmCode = null,
        byte? RepairFlags = null,
        double? DefaultTolerance = null,
        Func<Mesh, double, bool>? RepairAction = null);

    /// <summary>Singular unified operation dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, MorphologyOperationMetadata> Operations =
        new Dictionary<Type, MorphologyOperationMetadata> {
            [typeof(Morphology.CatmullClarkSubdivision)] = new(V.Standard | V.MeshSpecific | V.Topology, "Morphology.Subdivision.CatmullClark", AlgorithmCode: OpSubdivideCatmullClark),
            [typeof(Morphology.LoopSubdivision)] = new(V.Standard | V.MeshSpecific | V.Topology, "Morphology.Subdivision.Loop", AlgorithmCode: OpSubdivideLoop),
            [typeof(Morphology.ButterflySubdivision)] = new(V.Standard | V.MeshSpecific | V.Topology, "Morphology.Subdivision.Butterfly", AlgorithmCode: OpSubdivideButterfly),
            [typeof(Morphology.PlanarUnwrap)] = new(V.Standard | V.MeshSpecific, "Morphology.Unwrap.Planar", AlgorithmCode: OpUnwrapPlanar),
            [typeof(Morphology.FillHolesRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.Repair.FillHoles", RepairFlags: RepairFillHoles, DefaultTolerance: DefaultWeldTolerance, RepairAction: static (m, _) => m.FillHoles()),
            [typeof(Morphology.UnifyNormalsRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.Repair.UnifyNormals", RepairFlags: RepairUnifyNormals, DefaultTolerance: DefaultWeldTolerance, RepairAction: static (m, _) => m.UnifyNormals() >= 0),
            [typeof(Morphology.CullDegenerateFacesRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.Repair.CullDegenerateFaces", RepairFlags: RepairCullDegenerateFaces, DefaultTolerance: DefaultWeldTolerance, RepairAction: static (m, _) => m.Faces.CullDegenerateFaces() >= 0),
            [typeof(Morphology.CompactRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.Repair.Compact", RepairFlags: RepairCompact, DefaultTolerance: DefaultWeldTolerance, RepairAction: static (m, _) => m.Compact()),
            [typeof(Morphology.WeldRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.Repair.Weld", RepairFlags: RepairWeld, DefaultTolerance: DefaultWeldTolerance, RepairAction: static (m, _) => m.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true)),
            [typeof(Morphology.CageDeformOperation)] = new(V.Standard | V.Topology, "Morphology.CageDeform"),
            [typeof(Morphology.LaplacianSmoothing)] = new(V.Standard | V.MeshSpecific, "Morphology.LaplacianSmoothing"),
            [typeof(Morphology.TaubinSmoothing)] = new(V.Standard | V.MeshSpecific, "Morphology.TaubinSmoothing"),
            [typeof(Morphology.MeanCurvatureFlowSmoothing)] = new(V.Standard | V.MeshSpecific, "Morphology.MeanCurvatureFlow"),
            [typeof(Morphology.MeshOffsetOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshOffset"),
            [typeof(Morphology.MeshReductionOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshReduction"),
            [typeof(Morphology.IsotropicRemeshOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshRemesh"),
            [typeof(Morphology.BrepToMeshOperation)] = new(V.Standard | V.Topology, "Morphology.BrepToMesh"),
            [typeof(Morphology.MeshThickenOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshThicken"),
            [typeof(Morphology.MeshSeparateOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshSeparate"),
            [typeof(Morphology.MeshWeldOperation)] = new(V.Standard | V.MeshSpecific, "Morphology.MeshWeld"),
            [typeof(Morphology.CompositeRepair)] = new(V.Standard | V.MeshSpecific, "Morphology.CompositeRepair"),
        }.ToFrozenDictionary();

    /// <summary>Reverse lookup: repair flags → metadata for O(1) access from MorphologyCompute.</summary>
    internal static readonly FrozenDictionary<byte, MorphologyOperationMetadata> RepairFlagToMetadata =
        Operations.Values
            .Where(m => m.RepairFlags.HasValue)
            .ToFrozenDictionary(m => m.RepairFlags!.Value);

    /// <summary>Internal operation ID constants for compute layer (used by MorphologyCompute).</summary>
    internal const byte OpSubdivideCatmullClark = 2;
    internal const byte OpSubdivideLoop = 3;
    internal const byte OpSubdivideButterfly = 4;
    internal const byte OpUnwrapPlanar = 0;

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
    internal static readonly double IdealTriangleAngleRadians = RhinoMath.ToRadians(60.0);

    /// <summary>Mesh thickening configuration.</summary>
    internal const double MinThickenDistance = 0.0001;
    internal const double MaxThickenDistance = 10000.0;

    /// <summary>Mesh repair operation flags for bitwise composition.</summary>
    internal const byte RepairNone = 0;
    internal const byte RepairFillHoles = 1;
    internal const byte RepairUnifyNormals = 2;
    internal const byte RepairCullDegenerateFaces = 4;
    internal const byte RepairCompact = 8;
    internal const byte RepairWeld = 16;
    internal const byte RepairAll = RepairFillHoles | RepairUnifyNormals | RepairCullDegenerateFaces | RepairCompact | RepairWeld;
}

using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    /// <summary>Operation metadata: validation mode for each request type and geometry type combination.</summary>
    internal static readonly FrozenDictionary<(Type RequestType, Type GeometryType), V> ValidationModes =
        new Dictionary<(Type, Type), V> {
            [(typeof(Morphology.CageDeformRequest), typeof(Mesh))] = V.Standard | V.Topology,
            [(typeof(Morphology.CageDeformRequest), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Morphology.SubdivideCatmullClarkRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(typeof(Morphology.SubdivideLoopRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(typeof(Morphology.SubdiveButterflyRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(typeof(Morphology.SmoothLaplacianRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.SmoothTaubinRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.OffsetRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.ReduceRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(typeof(Morphology.RemeshRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.BrepToMeshRequest), typeof(Brep))] = V.Standard | V.BoundingBox,
            [(typeof(Morphology.MeshRepairRequest), typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific,
            [(typeof(Morphology.MeshThickenRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.MeshUnwrapRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.MeshSeparateRequest), typeof(Mesh))] = V.Standard | V.Topology,
            [(typeof(Morphology.EvolveMeanCurvatureRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Morphology.MeshWeldRequest), typeof(Mesh))] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    [Pure]
    internal static V GetValidationMode(Type requestType, Type geometryType) =>
        ValidationModes.TryGetValue((requestType, geometryType), out V mode)
            ? mode
            : V.Standard;

    /// <summary>Subdivision algorithms requiring triangulated meshes.</summary>
    internal static readonly FrozenSet<Type> TriangulatedSubdivisionRequests =
        new HashSet<Type> {
            typeof(Morphology.SubdivideLoopRequest),
            typeof(Morphology.SubdiveButterflyRequest),
        }.ToFrozenSet();

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

    /// <summary>Mesh repair operation dispatch: operation type → (operation name, mesh action).</summary>
    internal static readonly FrozenDictionary<Type, (string Name, Func<Mesh, double, bool> Action)> RepairOperations =
        new Dictionary<Type, (string, Func<Mesh, double, bool>)> {
            [typeof(Morphology.FillHolesRepairOperation)] = ("FillHoles", static (m, _) => m.FillHoles()),
            [typeof(Morphology.UnifyNormalsRepairOperation)] = ("UnifyNormals", static (m, _) => m.UnifyNormals() >= 0),
            [typeof(Morphology.CullDegenerateFacesRepairOperation)] = ("CullDegenerateFaces", static (m, _) => m.Faces.CullDegenerateFaces() >= 0),
            [typeof(Morphology.CompactRepairOperation)] = ("Compact", static (m, _) => m.Compact()),
            [typeof(Morphology.WeldVerticesRepairOperation)] = ("Weld", static (m, _) => m.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true)),
        }.ToFrozenDictionary();

    /// <summary>Mesh unwrap method dispatch: strategy type → RhinoCommon method.</summary>
    internal static readonly FrozenDictionary<Type, MeshUnwrapMethod> UnwrapMethods =
        new Dictionary<Type, MeshUnwrapMethod> {
            [typeof(Morphology.AngleBasedUnwrapStrategy)] = 0,
            [typeof(Morphology.ConformalEnergyMinimizationUnwrapStrategy)] = (MeshUnwrapMethod)1,
        }.ToFrozenDictionary();
}

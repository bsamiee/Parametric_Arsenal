using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation configuration constants and dispatch tables.</summary>
internal static class MorphologyConfig {
    internal readonly record struct OperationMetadata(V Validation, string Name);

    internal static readonly FrozenDictionary<(Type RequestType, Type GeometryType), OperationMetadata> OperationTable =
        new Dictionary<(Type, Type), OperationMetadata> {
            [(typeof(Morphology.CageDeformRequest), typeof(Mesh))] = new(V.Standard | V.Topology, "CageDeform"),
            [(typeof(Morphology.CageDeformRequest), typeof(Brep))] = new(V.Standard | V.Topology, "CageDeform"),
            [(typeof(Morphology.CatmullClarkSubdivisionRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific | V.Topology, "SubdivideCatmullClark"),
            [(typeof(Morphology.LoopSubdivisionRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific | V.Topology, "SubdivideLoop"),
            [(typeof(Morphology.ButterflySubdivisionRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific | V.Topology, "SubdivideButterfly"),
            [(typeof(Morphology.LaplacianSmoothingRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "SmoothLaplacian"),
            [(typeof(Morphology.TaubinSmoothingRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "SmoothTaubin"),
            [(typeof(Morphology.MeanCurvatureFlowRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "EvolveMeanCurvature"),
            [(typeof(Morphology.MeshOffsetRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "MeshOffset"),
            [(typeof(Morphology.MeshReductionRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific | V.Topology, "MeshReduce"),
            [(typeof(Morphology.RemeshRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "MeshRemesh"),
            [(typeof(Morphology.BrepToMeshRequest), typeof(Brep))] = new(V.Standard | V.BoundingBox, "BrepToMesh"),
            [(typeof(Morphology.MeshRepairRequest), typeof(Mesh))] = new(V.Standard | V.Topology | V.MeshSpecific, "MeshRepair"),
            [(typeof(Morphology.MeshThickenRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "MeshThicken"),
            [(typeof(Morphology.MeshUnwrapRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "MeshUnwrap"),
            [(typeof(Morphology.MeshSeparationRequest), typeof(Mesh))] = new(V.Standard | V.Topology, "MeshSeparate"),
            [(typeof(Morphology.MeshWeldRequest), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "MeshWeld"),
        }.ToFrozenDictionary();

    [Pure]
    internal static bool TryGetOperationMetadata(Type requestType, Type geometryType, out OperationMetadata metadata) =>
        OperationTable.TryGetValue((requestType, geometryType), out metadata);

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
    internal const byte OpBrepToMesh = 15;
    internal const byte OpMeshRepair = 16;
    internal const byte OpMeshThicken = 17;
    internal const byte OpMeshUnwrap = 18;
    internal const byte OpMeshSeparate = 19;
    internal const byte OpEvolveMeanCurvature = 20;
    internal const byte OpMeshWeld = 21;

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
    internal static readonly FrozenDictionary<Type, Func<Mesh, double, bool>> RepairOperationHandlers =
        new Dictionary<Type, Func<Mesh, double, bool>> {
            [typeof(Morphology.FillHolesRepairOperation)] = static (m, _) => m.FillHoles(),
            [typeof(Morphology.UnifyNormalsRepairOperation)] = static (m, _) => m.UnifyNormals() >= 0,
            [typeof(Morphology.CullDegenerateFacesRepairOperation)] = static (m, _) => m.Faces.CullDegenerateFaces() >= 0,
            [typeof(Morphology.CompactRepairOperation)] = static (m, _) => m.Compact(),
            [typeof(Morphology.WeldRepairOperation)] = static (m, _) => m.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true),
        }.ToFrozenDictionary();
}

using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types, validation dispatch, and healing configuration for topology.</summary>
[Pure] internal static class TopologyConfig {
    /// <summary>Strategy type to tolerance multiplier mapping for healing operations.</summary>
    internal static readonly FrozenDictionary<Type, double> StrategyToleranceMultipliers =
        new Dictionary<Type, double> {
            [typeof(Topology.ConservativeRepairStrategy)] = 0.1,
            [typeof(Topology.ModerateJoinStrategy)] = 1.0,
            [typeof(Topology.AggressiveJoinStrategy)] = 10.0,
            [typeof(Topology.CombinedStrategy)] = 1.0,
            [typeof(Topology.TargetedJoinStrategy)] = 10.0,
            [typeof(Topology.ComponentJoinStrategy)] = 1.0,
        }.ToFrozenDictionary();

    /// <summary>Operation metadata: (Type, OpType) to (ValidationMode, Name) mapping.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), OperationMetadata> OperationMeta =
        new Dictionary<(Type, OpType), OperationMetadata> {
            [(typeof(Brep), OpType.NakedEdges)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetNakedEdges.Brep"),
            [(typeof(Mesh), OpType.NakedEdges)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetNakedEdges.Mesh"),
            [(typeof(Brep), OpType.BoundaryLoops)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetBoundaryLoops.Brep"),
            [(typeof(Mesh), OpType.BoundaryLoops)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetBoundaryLoops.Mesh"),
            [(typeof(Brep), OpType.NonManifold)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetNonManifold.Brep"),
            [(typeof(Mesh), OpType.NonManifold)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetNonManifold.Mesh"),
            [(typeof(Brep), OpType.Connectivity)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetConnectivity.Brep"),
            [(typeof(Mesh), OpType.Connectivity)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetConnectivity.Mesh"),
            [(typeof(Brep), OpType.EdgeClassification)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.ClassifyEdges.Brep"),
            [(typeof(Mesh), OpType.EdgeClassification)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.ClassifyEdges.Mesh"),
            [(typeof(Brep), OpType.Adjacency)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetAdjacency.Brep"),
            [(typeof(Mesh), OpType.Adjacency)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetAdjacency.Mesh"),
            [(typeof(Brep), OpType.VertexData)] = new OperationMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.GetVertexData.Brep"),
            [(typeof(Mesh), OpType.VertexData)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetVertexData.Mesh"),
            [(typeof(Mesh), OpType.NgonTopology)] = new OperationMetadata(ValidationMode: V.Standard | V.MeshSpecific, OpName: "Topology.GetNgonTopology.Mesh"),
        }.ToFrozenDictionary();

    /// <summary>Diagnostic operation metadata configurations.</summary>
    internal static readonly FrozenDictionary<OpType, DiagnosticMetadata> DiagnosticOps =
        new Dictionary<OpType, DiagnosticMetadata> {
            [OpType.Diagnose] = new DiagnosticMetadata(ValidationMode: V.Standard | V.Topology | V.BrepGranular, OpName: "Topology.Diagnose", NearMissMultiplier: 100.0, MaxEdgeThreshold: 100),
        }.ToFrozenDictionary();

    /// <summary>Feature extraction operation metadata configurations.</summary>
    internal static readonly FrozenDictionary<OpType, FeaturesMetadata> FeaturesOps =
        new Dictionary<OpType, FeaturesMetadata> {
            [OpType.ExtractFeatures] = new FeaturesMetadata(ValidationMode: V.Standard | V.Topology | V.MassProperties, OpName: "Topology.ExtractFeatures"),
        }.ToFrozenDictionary();

    /// <summary>Healing operation metadata configurations.</summary>
    internal static readonly FrozenDictionary<OpType, HealingMetadata> HealingOps =
        new Dictionary<OpType, HealingMetadata> {
            [OpType.Heal] = new HealingMetadata(ValidationMode: V.Standard | V.Topology, OpName: "Topology.Heal", MaxTargetedJoinIterations: 100),
        }.ToFrozenDictionary();

    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7, Diagnose = 8, ExtractFeatures = 9, Heal = 10 }

    /// <summary>Base operation metadata with validation mode and operation name.</summary>
    internal sealed record OperationMetadata(V ValidationMode, string OpName);

    /// <summary>Diagnostic operation metadata with near-miss configuration.</summary>
    internal sealed record DiagnosticMetadata(V ValidationMode, string OpName, double NearMissMultiplier, int MaxEdgeThreshold);

    /// <summary>Feature extraction metadata.</summary>
    internal sealed record FeaturesMetadata(V ValidationMode, string OpName);

    /// <summary>Healing operation metadata with iteration limits and tolerance configuration.</summary>
    internal sealed record HealingMetadata(V ValidationMode, string OpName, int MaxTargetedJoinIterations);
}

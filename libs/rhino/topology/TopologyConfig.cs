using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types, validation dispatch, and healing configuration for topology.</summary>
[Pure]
internal static class TopologyConfig {
    /// <summary>Topology operation metadata containing validation mode and operation name.</summary>
    internal sealed record TopologyOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Healing strategy metadata containing tolerance multiplier.</summary>
    internal sealed record HealingStrategyMetadata(
        double ToleranceMultiplier);

    /// <summary>Strategy type to tolerance multiplier mapping for healing operations.</summary>
    internal static readonly FrozenDictionary<Type, HealingStrategyMetadata> StrategyMetadata =
        new Dictionary<Type, HealingStrategyMetadata> {
            [typeof(Topology.ConservativeRepairStrategy)] = new(ToleranceMultiplier: 0.1),
            [typeof(Topology.ModerateJoinStrategy)] = new(ToleranceMultiplier: 1.0),
            [typeof(Topology.AggressiveJoinStrategy)] = new(ToleranceMultiplier: 10.0),
            [typeof(Topology.CombinedStrategy)] = new(ToleranceMultiplier: 1.0),
            [typeof(Topology.TargetedJoinStrategy)] = new(ToleranceMultiplier: 100.0),
            [typeof(Topology.ComponentJoinStrategy)] = new(ToleranceMultiplier: 1.0),
        }.ToFrozenDictionary();

    /// <summary>Operation metadata: (Type, OpType) to metadata mapping.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), TopologyOperationMetadata> OperationMetadata =
        new Dictionary<(Type, OpType), TopologyOperationMetadata> {
            [(typeof(Brep), OpType.NakedEdges)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetNakedEdges.Brep"),
            [(typeof(Mesh), OpType.NakedEdges)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetNakedEdges.Mesh"),
            [(typeof(Brep), OpType.BoundaryLoops)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetBoundaryLoops.Brep"),
            [(typeof(Mesh), OpType.BoundaryLoops)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetBoundaryLoops.Mesh"),
            [(typeof(Brep), OpType.NonManifold)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetNonManifold.Brep"),
            [(typeof(Mesh), OpType.NonManifold)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetNonManifold.Mesh"),
            [(typeof(Brep), OpType.Connectivity)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetConnectivity.Brep"),
            [(typeof(Mesh), OpType.Connectivity)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetConnectivity.Mesh"),
            [(typeof(Brep), OpType.EdgeClassification)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.ClassifyEdges.Brep"),
            [(typeof(Mesh), OpType.EdgeClassification)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.ClassifyEdges.Mesh"),
            [(typeof(Brep), OpType.Adjacency)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetAdjacency.Brep"),
            [(typeof(Mesh), OpType.Adjacency)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetAdjacency.Mesh"),
            [(typeof(Brep), OpType.VertexData)] = new(ValidationMode: V.Standard | V.Topology, OperationName: "Topology.GetVertexData.Brep"),
            [(typeof(Mesh), OpType.VertexData)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetVertexData.Mesh"),
            [(typeof(Mesh), OpType.NgonTopology)] = new(ValidationMode: V.Standard | V.MeshSpecific, OperationName: "Topology.GetNgonTopology.Mesh"),
        }.ToFrozenDictionary();

    internal const double CurvatureThresholdRatio = 0.1;
    internal const double NearMissMultiplier = 100.0;
    internal const int MaxEdgesForNearMissAnalysis = 100;

    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }
}

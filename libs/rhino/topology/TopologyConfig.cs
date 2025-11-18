using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types, validation dispatch, and healing configuration for topology.</summary>
[Pure]
internal static class TopologyConfig {
    /// <summary>Operation metadata: (Type, OpType) to (ValidationMode, Name) mapping.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), (V ValidationMode, string OpName)> OperationMeta =
        new Dictionary<(Type, OpType), (V, string)> {
            [(typeof(Brep), OpType.NakedEdges)] = (V.Standard | V.Topology, "Topology.GetNakedEdges.Brep"),
            [(typeof(Mesh), OpType.NakedEdges)] = (V.Standard | V.MeshSpecific, "Topology.GetNakedEdges.Mesh"),
            [(typeof(Brep), OpType.BoundaryLoops)] = (V.Standard | V.Topology, "Topology.GetBoundaryLoops.Brep"),
            [(typeof(Mesh), OpType.BoundaryLoops)] = (V.Standard | V.MeshSpecific, "Topology.GetBoundaryLoops.Mesh"),
            [(typeof(Brep), OpType.NonManifold)] = (V.Standard | V.Topology, "Topology.GetNonManifold.Brep"),
            [(typeof(Mesh), OpType.NonManifold)] = (V.Standard | V.MeshSpecific, "Topology.GetNonManifold.Mesh"),
            [(typeof(Brep), OpType.Connectivity)] = (V.Standard | V.Topology, "Topology.GetConnectivity.Brep"),
            [(typeof(Mesh), OpType.Connectivity)] = (V.Standard | V.MeshSpecific, "Topology.GetConnectivity.Mesh"),
            [(typeof(Brep), OpType.EdgeClassification)] = (V.Standard | V.Topology, "Topology.ClassifyEdges.Brep"),
            [(typeof(Mesh), OpType.EdgeClassification)] = (V.Standard | V.MeshSpecific, "Topology.ClassifyEdges.Mesh"),
            [(typeof(Brep), OpType.Adjacency)] = (V.Standard | V.Topology, "Topology.GetAdjacency.Brep"),
            [(typeof(Mesh), OpType.Adjacency)] = (V.Standard | V.MeshSpecific, "Topology.GetAdjacency.Mesh"),
            [(typeof(Brep), OpType.VertexData)] = (V.Standard | V.Topology, "Topology.GetVertexData.Brep"),
            [(typeof(Mesh), OpType.VertexData)] = (V.Standard | V.MeshSpecific, "Topology.GetVertexData.Mesh"),
            [(typeof(Mesh), OpType.NgonTopology)] = (V.Standard | V.MeshSpecific, "Topology.GetNgonTopology.Mesh"),
        }.ToFrozenDictionary();

    /// <summary>Healing strategy tolerance multipliers for progressive topology repair.</summary>
    internal const double ConservativeRepairMultiplier = 0.1;
    internal const double ModerateJoinMultiplier = 1.0;
    internal const double AggressiveJoinMultiplier = 10.0;

    /// <summary>Strategy type to tolerance multiplier mapping for healing operations.</summary>
    internal static readonly FrozenDictionary<Type, double> StrategyToleranceMultipliers =
        new Dictionary<Type, double> {
            [typeof(Topology.ConservativeRepairStrategy)] = ConservativeRepairMultiplier,
            [typeof(Topology.ModerateJoinStrategy)] = ModerateJoinMultiplier,
            [typeof(Topology.AggressiveJoinStrategy)] = AggressiveJoinMultiplier,
            [typeof(Topology.CombinedStrategy)] = ModerateJoinMultiplier,
            [typeof(Topology.TargetedJoinStrategy)] = NearMissMultiplier,
            [typeof(Topology.ComponentJoinStrategy)] = ModerateJoinMultiplier,
        }.ToFrozenDictionary();

    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }

    internal const double CurvatureThresholdRatio = 0.1;
    internal const double MinLoopLength = 1e-6;
    internal const double NearMissMultiplier = 100.0;
    internal const int MaxEdgesForNearMissAnalysis = 100;
}

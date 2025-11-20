using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types, validation dispatch, and healing configuration for topology.</summary>
[Pure]
internal static class TopologyConfig {
    /// <summary>Metadata for polymorphic topology operations.</summary>
    internal sealed record OperationMetadata(V ValidationMode, string OpName);

    /// <summary>Metadata for topology diagnosis operations.</summary>
    internal sealed record DiagnosticMetadata(
        V ValidationMode,
        string OpName,
        double NearMissMultiplier,
        int MaxEdgeThreshold);

    /// <summary>Metadata for topological feature extraction.</summary>
    internal sealed record FeaturesMetadata(
        V ValidationMode,
        string OpName,
        double MinLoopLength);

    /// <summary>Metadata for topology healing operations.</summary>
    internal sealed record HealingMetadata(
        V ValidationMode,
        string OpName,
        int MaxTargetedJoinIterations);

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

    /// <summary>Operation metadata: (Type, OpType) to metadata mapping.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), OperationMetadata> OperationMeta =
        new Dictionary<(Type, OpType), OperationMetadata> {
            [(typeof(Brep), OpType.NakedEdges)] = new(V.Standard | V.Topology, "Topology.GetNakedEdges.Brep"),
            [(typeof(Mesh), OpType.NakedEdges)] = new(V.Standard | V.MeshSpecific, "Topology.GetNakedEdges.Mesh"),
            [(typeof(Brep), OpType.BoundaryLoops)] = new(V.Standard | V.Topology, "Topology.GetBoundaryLoops.Brep"),
            [(typeof(Mesh), OpType.BoundaryLoops)] = new(V.Standard | V.MeshSpecific, "Topology.GetBoundaryLoops.Mesh"),
            [(typeof(Brep), OpType.NonManifold)] = new(V.Standard | V.Topology, "Topology.GetNonManifold.Brep"),
            [(typeof(Mesh), OpType.NonManifold)] = new(V.Standard | V.MeshSpecific, "Topology.GetNonManifold.Mesh"),
            [(typeof(Brep), OpType.Connectivity)] = new(V.Standard | V.Topology, "Topology.GetConnectivity.Brep"),
            [(typeof(Mesh), OpType.Connectivity)] = new(V.Standard | V.MeshSpecific, "Topology.GetConnectivity.Mesh"),
            [(typeof(Brep), OpType.EdgeClassification)] = new(V.Standard | V.Topology, "Topology.ClassifyEdges.Brep"),
            [(typeof(Mesh), OpType.EdgeClassification)] = new(V.Standard | V.MeshSpecific, "Topology.ClassifyEdges.Mesh"),
            [(typeof(Brep), OpType.Adjacency)] = new(V.Standard | V.Topology, "Topology.GetAdjacency.Brep"),
            [(typeof(Mesh), OpType.Adjacency)] = new(V.Standard | V.MeshSpecific, "Topology.GetAdjacency.Mesh"),
            [(typeof(Brep), OpType.VertexData)] = new(V.Standard | V.Topology, "Topology.GetVertexData.Brep"),
            [(typeof(Mesh), OpType.VertexData)] = new(V.Standard | V.MeshSpecific, "Topology.GetVertexData.Mesh"),
            [(typeof(Mesh), OpType.NgonTopology)] = new(V.Standard | V.MeshSpecific, "Topology.GetNgonTopology.Mesh"),
        }.ToFrozenDictionary();

    /// <summary>Diagnostic operation metadata for Brep topology analysis.</summary>
    internal static readonly FrozenDictionary<Type, DiagnosticMetadata> DiagnosticOps =
        new Dictionary<Type, DiagnosticMetadata> {
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology | V.BrepGranular,
                OpName: "Topology.Diagnose.Brep",
                NearMissMultiplier: 10.0,
                MaxEdgeThreshold: 100),
        }.ToFrozenDictionary();

    /// <summary>Feature extraction metadata for Brep topological invariants.</summary>
    internal static readonly FrozenDictionary<Type, FeaturesMetadata> FeaturesOps =
        new Dictionary<Type, FeaturesMetadata> {
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology | V.MassProperties,
                OpName: "Topology.ExtractFeatures.Brep",
                MinLoopLength: 1e-6),
        }.ToFrozenDictionary();

    /// <summary>Healing operation metadata for Brep repair.</summary>
    internal static readonly FrozenDictionary<Type, HealingMetadata> HealingOps =
        new Dictionary<Type, HealingMetadata> {
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology,
                OpName: "Topology.Heal.Brep",
                MaxTargetedJoinIterations: 20),
        }.ToFrozenDictionary();

    /// <summary>Curvature angle threshold ratio for mesh edge classification (G2 detection).</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }
}

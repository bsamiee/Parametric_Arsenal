using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types and validation dispatch for topology.</summary>
internal static class TopologyConfig {
    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }

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

    /// <summary>G2 curvature threshold ratio: 10% of angle tolerance.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Near-miss proximity multiplier: 100× tolerance.</summary>
    internal const double NearMissMultiplier = 100.0;

    /// <summary>Maximum edge count for O(n²) near-miss detection. Above this, skip near-miss analysis for performance.</summary>
    internal const int MaxEdgesForNearMissAnalysis = 100;

    /// <summary>Minimum loop length for hole detection.</summary>
    internal const double MinLoopLength = 1e-6;

    /// <summary>Healing strategy: Conservative Repair with 0.1× tolerance.</summary>
    internal const byte StrategyConservativeRepair = 0;

    /// <summary>Healing strategy: Moderate JoinNakedEdges with 1.0× tolerance.</summary>
    internal const byte StrategyModerateJoin = 1;

    /// <summary>Healing strategy: Aggressive JoinNakedEdges with 10.0× tolerance.</summary>
    internal const byte StrategyAggressiveJoin = 2;

    /// <summary>Healing strategy: Combined Repair + JoinNakedEdges.</summary>
    internal const byte StrategyCombined = 3;

    /// <summary>Maximum number of healing strategies available.</summary>
    /// <summary>Maximum healing strategies available (0-3 inclusive).</summary>
    /// <summary>Maximum number of healing strategies (4 total: Conservative, Moderate, Aggressive, Combined).</summary>
    internal const int MaxHealingStrategies = 4;

    /// <summary>Healing strategy tolerance multipliers: [Conservative=0.1×, Moderate=1.0×, Aggressive=10.0×].</summary>
    internal static readonly double[] HealingToleranceMultipliers = [0.1, 1.0, 10.0,];
}

using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology operation type constants and validation mode dispatch configuration.</summary>
internal static class TopologyConfig {
    /// <summary>Topology operation type enumeration for dispatch table lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }

    /// <summary>Per-operation validation and diagnostic configuration metadata.</summary>
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

    /// <summary>G2 curvature threshold: 10% of angle tolerance for smooth-to-curvature edge classification.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Creates index validation error with formatted context.</summary>
    [System.Diagnostics.Contracts.Pure]
    internal static Arsenal.Core.Errors.SystemError IndexError(Arsenal.Core.Errors.SystemError baseError, string indexName, int index, int maxIndex) =>
        baseError.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{indexName}: {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}, Max: {maxIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
}

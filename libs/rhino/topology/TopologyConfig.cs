using System.Collections.Frozen;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Operation types and validation mode dispatch for topology analysis.</summary>
internal static class TopologyConfig {
    /// <summary>Topology operation types for dispatch lookup.</summary>
    internal enum OpType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }

    /// <summary>G2 curvature threshold ratio: 10% of angle tolerance.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Operation registry: (Type, OpType) to (ValidationMode, Name, Executor) mapping.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), (V ValidationMode, string OpName, Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> Execute)> OperationRegistry =
        new Dictionary<(Type, OpType), (V, string, Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>>)> {
            [(typeof(Brep), OpType.NakedEdges)] = (V.Standard | V.Topology, "Topology.GetNakedEdges.Brep", TopologyCore.ExecuteNakedEdges<Brep>()),
            [(typeof(Mesh), OpType.NakedEdges)] = (V.Standard | V.MeshSpecific, "Topology.GetNakedEdges.Mesh", TopologyCore.ExecuteNakedEdges<Mesh>()),
            [(typeof(Brep), OpType.BoundaryLoops)] = (V.Standard | V.Topology, "Topology.GetBoundaryLoops.Brep", TopologyCore.ExecuteBoundaryLoops<Brep>()),
            [(typeof(Mesh), OpType.BoundaryLoops)] = (V.Standard | V.MeshSpecific, "Topology.GetBoundaryLoops.Mesh", TopologyCore.ExecuteBoundaryLoops<Mesh>()),
            [(typeof(Brep), OpType.NonManifold)] = (V.Standard | V.Topology, "Topology.GetNonManifold.Brep", TopologyCore.ExecuteNonManifold<Brep>()),
            [(typeof(Mesh), OpType.NonManifold)] = (V.Standard | V.MeshSpecific, "Topology.GetNonManifold.Mesh", TopologyCore.ExecuteNonManifold<Mesh>()),
            [(typeof(Brep), OpType.Connectivity)] = (V.Standard | V.Topology, "Topology.GetConnectivity.Brep", TopologyCore.ExecuteConnectivity<Brep>()),
            [(typeof(Mesh), OpType.Connectivity)] = (V.Standard | V.MeshSpecific, "Topology.GetConnectivity.Mesh", TopologyCore.ExecuteConnectivity<Mesh>()),
            [(typeof(Brep), OpType.EdgeClassification)] = (V.Standard | V.Topology, "Topology.ClassifyEdges.Brep", TopologyCore.ExecuteEdgeClassification<Brep>()),
            [(typeof(Mesh), OpType.EdgeClassification)] = (V.Standard | V.MeshSpecific, "Topology.ClassifyEdges.Mesh", TopologyCore.ExecuteEdgeClassification<Mesh>()),
            [(typeof(Brep), OpType.Adjacency)] = (V.Standard | V.Topology, "Topology.GetAdjacency.Brep", TopologyCore.ExecuteAdjacency<Brep>()),
            [(typeof(Mesh), OpType.Adjacency)] = (V.Standard | V.MeshSpecific, "Topology.GetAdjacency.Mesh", TopologyCore.ExecuteAdjacency<Mesh>()),
            [(typeof(Brep), OpType.VertexData)] = (V.Standard | V.Topology, "Topology.GetVertexData.Brep", TopologyCore.ExecuteVertexData<Brep>()),
            [(typeof(Mesh), OpType.VertexData)] = (V.Standard | V.MeshSpecific, "Topology.GetVertexData.Mesh", TopologyCore.ExecuteVertexData<Mesh>()),
            [(typeof(Mesh), OpType.NgonTopology)] = (V.Standard | V.MeshSpecific, "Topology.GetNgonTopology.Mesh", TopologyCore.ExecuteNgonTopology<Mesh>()),
        }.ToFrozenDictionary();
}

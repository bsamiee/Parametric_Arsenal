using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology engine with type-based overload dispatch for structural and connectivity analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult;

    /// <summary>Extracts naked (boundary) edges from Brep geometry with valence=1 classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges(
        Brep geometry,
        IGeometryContext context,
        bool orderLoops = false,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops, enableDiagnostics: enableDiagnostics);

    /// <summary>Extracts naked (boundary) edges from Mesh geometry with topological edge classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges(
        Mesh geometry,
        IGeometryContext context,
        bool orderLoops = false,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops, enableDiagnostics: enableDiagnostics);

    /// <summary>Constructs closed boundary loops from Brep naked edges using curve joining algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Brep geometry,
        IGeometryContext context,
        double? tolerance = null,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance, enableDiagnostics: enableDiagnostics);

    /// <summary>Constructs closed boundary loops from Mesh naked edges using polyline joining algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Mesh geometry,
        IGeometryContext context,
        double? tolerance = null,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance, enableDiagnostics: enableDiagnostics);

    /// <summary>Detects non-manifold vertices and edges in Brep geometry with valence>2 classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData(
        Brep geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteNonManifold(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Detects non-manifold vertices and edges in Mesh geometry with topological manifold analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData(
        Mesh geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteNonManifold(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes connected components and builds adjacency graph for Brep geometry using BFS traversal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity(
        Brep geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteConnectivity(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes connected components and builds adjacency graph for Mesh geometry using BFS traversal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity(
        Mesh geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteConnectivity(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Classifies Brep edges by continuity type (G0/G1/G2) with geometric continuity analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges(
        Brep geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteEdgeClassification(input: geometry, context: context, minimumContinuity: minimumContinuity, enableDiagnostics: enableDiagnostics);

    /// <summary>Classifies Mesh edges by dihedral angle with sharp/smooth discrimination.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges(
        Mesh geometry,
        IGeometryContext context,
        double? angleThreshold = null,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteEdgeClassification(input: geometry, context: context, angleThreshold: angleThreshold, enableDiagnostics: enableDiagnostics);

    /// <summary>Queries face adjacency data for specific Brep edge with dihedral angle computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency(
        Brep geometry,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex, enableDiagnostics: enableDiagnostics);

    /// <summary>Queries face adjacency data for specific Mesh edge with dihedral angle computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency(
        Mesh geometry,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics = false) =>
        TopologyCompute.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex, enableDiagnostics: enableDiagnostics);
}

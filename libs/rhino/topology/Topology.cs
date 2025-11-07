using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Naked edge analysis result containing edge curves and indices with topology metadata.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"NakedEdges: {EdgeCurves.Count}/{TotalEdgeCount} | L={TotalLength:F3} | Ordered={IsOrdered}");
}

/// <summary>Boundary loop analysis result with closed loop curves and join diagnostics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record BoundaryLoopData(
    IReadOnlyList<Curve> Loops,
    IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
    IReadOnlyList<double> LoopLengths,
    IReadOnlyList<bool> IsClosedPerLoop,
    double JoinTolerance,
    int FailedJoins) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"BoundaryLoops: {Loops.Count} | FailedJoins={FailedJoins} | Tol={JoinTolerance:E2}");
}

/// <summary>Non-manifold topology analysis result with diagnostic data for irregular vertices and edges.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NonManifoldData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> VertexIndices,
    IReadOnlyList<int> Valences,
    IReadOnlyList<Point3d> Locations,
    bool IsManifold,
    bool IsOrientable,
    int MaxValence) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={EdgeIndices.Count} | Verts={VertexIndices.Count} | MaxVal={MaxValence}");
}

/// <summary>Connected component analysis result with adjacency graph and spatial bounds.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {TotalComponents} components | Largest={ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type with geometric measures.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
}

/// <summary>Face adjacency query result with neighbor data and dihedral angle computation.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record AdjacencyData(
    int EdgeIndex,
    IReadOnlyList<int> AdjacentFaceIndices,
    IReadOnlyList<Vector3d> FaceNormals,
    double DihedralAngle,
    bool IsManifold,
    bool IsBoundary) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => IsBoundary
        ? string.Create(CultureInfo.InvariantCulture, $"Edge[{EdgeIndex}]: Boundary (valence=1)")
        : IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{EdgeIndex}]: Manifold | Angle={DihedralAngle * 180.0 / Math.PI:F1}°")
            : string.Create(CultureInfo.InvariantCulture, $"Edge[{EdgeIndex}]: NonManifold (valence={AdjacentFaceIndices.Count})");
}

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
        TopologyCore.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops, enableDiagnostics: enableDiagnostics);

    /// <summary>Extracts naked (boundary) edges from Mesh geometry with topological edge classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges(
        Mesh geometry,
        IGeometryContext context,
        bool orderLoops = false,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops, enableDiagnostics: enableDiagnostics);

    /// <summary>Constructs closed boundary loops from Brep naked edges using curve joining algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Brep geometry,
        IGeometryContext context,
        double? tolerance = null,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance, enableDiagnostics: enableDiagnostics);

    /// <summary>Constructs closed boundary loops from Mesh naked edges using polyline joining algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Mesh geometry,
        IGeometryContext context,
        double? tolerance = null,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance, enableDiagnostics: enableDiagnostics);

    /// <summary>Detects non-manifold vertices and edges in Brep geometry with valence>2 classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData(
        Brep geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteNonManifold(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Detects non-manifold vertices and edges in Mesh geometry with topological manifold analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData(
        Mesh geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteNonManifold(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes connected components and builds adjacency graph for Brep geometry using BFS traversal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity(
        Brep geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteConnectivity(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes connected components and builds adjacency graph for Mesh geometry using BFS traversal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity(
        Mesh geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteConnectivity(input: geometry, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Classifies Brep edges by continuity type (G0/G1/G2) with geometric continuity analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges(
        Brep geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteEdgeClassification(input: geometry, context: context, minimumContinuity: minimumContinuity, enableDiagnostics: enableDiagnostics);

    /// <summary>Classifies Mesh edges by dihedral angle with sharp/smooth discrimination.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges(
        Mesh geometry,
        IGeometryContext context,
        double? angleThreshold = null,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteEdgeClassification(input: geometry, context: context, angleThreshold: angleThreshold, enableDiagnostics: enableDiagnostics);

    /// <summary>Queries face adjacency data for specific Brep edge with dihedral angle computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency(
        Brep geometry,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex, enableDiagnostics: enableDiagnostics);

    /// <summary>Queries face adjacency data for specific Mesh edge with dihedral angle computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency(
        Mesh geometry,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics = false) =>
        TopologyCore.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex, enableDiagnostics: enableDiagnostics);
}

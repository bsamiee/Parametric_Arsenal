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

    /// <summary>Edge continuity classification for geometric analysis using struct pattern instead of enum.</summary>
    public readonly struct EdgeContinuityType(byte value) : IEquatable<EdgeContinuityType> {
        internal readonly byte Value = value;

        /// <summary>G0 discontinuous or below minimum continuity threshold.</summary>
        public static readonly EdgeContinuityType Sharp = new(0);
        /// <summary>G1 continuous (tangent continuity).</summary>
        public static readonly EdgeContinuityType Smooth = new(1);
        /// <summary>G2 continuous (curvature continuity).</summary>
        public static readonly EdgeContinuityType Curvature = new(2);
        /// <summary>Interior manifold edge (valence=2, meets continuity requirement).</summary>
        public static readonly EdgeContinuityType Interior = new(3);
        /// <summary>Boundary naked edge (valence=1).</summary>
        public static readonly EdgeContinuityType Boundary = new(4);
        /// <summary>Non-manifold edge (valence>2).</summary>
        public static readonly EdgeContinuityType NonManifold = new(5);

        /// <summary>Equality comparison by value.</summary>
        public bool Equals(EdgeContinuityType other) => this.Value == other.Value;
        /// <summary>Hash code based on value.</summary>
        public override int GetHashCode() => this.Value.GetHashCode();
        /// <summary>Equality override for object comparison.</summary>
        public override bool Equals(object? obj) => obj is EdgeContinuityType other && this.Equals(other);
        /// <summary>Equality operator.</summary>
        public static bool operator ==(EdgeContinuityType left, EdgeContinuityType right) => left.Equals(right);
        /// <summary>Inequality operator.</summary>
        public static bool operator !=(EdgeContinuityType left, EdgeContinuityType right) => !left.Equals(right);
    }

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

    /// <summary>Naked edge analysis result containing edge curves and indices with topology metadata.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Boundary loop analysis result with closed loop curves and join diagnostics.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(
    IReadOnlyList<Curve> Loops,
    IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
    IReadOnlyList<double> LoopLengths,
    IReadOnlyList<bool> IsClosedPerLoop,
    double JoinTolerance,
    int FailedJoins) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
    }

    /// <summary>Non-manifold topology analysis result with diagnostic data for irregular vertices and edges.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NonManifoldData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> VertexIndices,
    IReadOnlyList<int> Valences,
    IReadOnlyList<Point3d> Locations,
    bool IsManifold,
    bool IsOrientable,
    int MaxValence) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Connected component analysis result with adjacency graph and spatial bounds.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    System.Collections.Frozen.FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Edge classification result by continuity type with geometric measures.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<Topology.EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    System.Collections.Frozen.FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(Topology.EdgeContinuityType.Sharp, []).Count}");
    }

    /// <summary>Face adjacency query result with neighbor data and dihedral angle computation.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(
    int EdgeIndex,
    IReadOnlyList<int> AdjacentFaceIndices,
    IReadOnlyList<Vector3d> FaceNormals,
    double DihedralAngle,
    bool IsManifold,
    bool IsBoundary) : Topology.IResult {
        [Pure]
        private string DebuggerDisplay => this.IsBoundary
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
            : this.IsManifold
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }
}

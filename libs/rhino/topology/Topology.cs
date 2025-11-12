using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology analysis via type-based dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker for polymorphic dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult;

    /// <summary>Edge continuity classification for geometric analysis.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct EdgeContinuityType(byte Value) {
        /// <summary>G0 sharp edge below continuity threshold.</summary>
        public static readonly EdgeContinuityType Sharp = new(0);
        /// <summary>G1 smooth tangent continuity.</summary>
        public static readonly EdgeContinuityType Smooth = new(1);
        /// <summary>G2 curvature continuity.</summary>
        public static readonly EdgeContinuityType Curvature = new(2);
        /// <summary>Interior manifold edge valence 2.</summary>
        public static readonly EdgeContinuityType Interior = new(3);
        /// <summary>Boundary naked edge valence 1.</summary>
        public static readonly EdgeContinuityType Boundary = new(4);
        /// <summary>Non-manifold edge valence &gt; 2.</summary>
        public static readonly EdgeContinuityType NonManifold = new(5);
    }

    /// <summary>Naked edge data: curves, indices, valences.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(
        IReadOnlyList<Curve> EdgeCurves,
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> Valences,
        bool IsOrdered,
        int TotalEdgeCount,
        double TotalLength) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Boundary loops: joined edges, closure, diagnostics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(
        IReadOnlyList<Curve> Loops,
        IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
        IReadOnlyList<double> LoopLengths,
        IReadOnlyList<bool> IsClosedPerLoop,
        double JoinTolerance,
        int FailedJoins) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
    }

    /// <summary>Non-manifold vertices/edges: valences, locations.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NonManifoldData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> VertexIndices,
        IReadOnlyList<int> Valences,
        IReadOnlyList<Point3d> Locations,
        bool IsManifold,
        bool IsOrientable,
        int MaxValence) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Connected components: adjacency graph, bounding boxes.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(
        IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
        IReadOnlyList<int> ComponentSizes,
        IReadOnlyList<BoundingBox> ComponentBounds,
        int TotalComponents,
        bool IsFullyConnected,
        FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Edge classification: G0/G1/G2 types, geometric measures.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<EdgeContinuityType> Classifications,
        IReadOnlyList<double> ContinuityMeasures,
        FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
        Continuity MinimumContinuity) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
    }

    /// <summary>Edge adjacency: face normals, dihedral angles, manifold state.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(
        int EdgeIndex,
        IReadOnlyList<int> AdjacentFaceIndices,
        IReadOnlyList<Vector3d> FaceNormals,
        double DihedralAngle,
        bool IsManifold,
        bool IsBoundary) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsBoundary
            ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
            : this.IsManifold
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
                : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }

    /// <summary>Vertex data: connected edges/faces, valence, manifold status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record VertexData(
        int VertexIndex,
        Point3d Location,
        IReadOnlyList<int> ConnectedEdgeIndices,
        IReadOnlyList<int> ConnectedFaceIndices,
        int Valence,
        bool IsBoundary,
        bool IsManifold) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Vertex[{this.VertexIndex}]: Valence={this.Valence} | {(this.IsBoundary ? "Boundary" : "Interior")} | {(this.IsManifold ? "Manifold" : "NonManifold")}");
    }

    /// <summary>Ngon topology: membership, boundaries, centroids.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NgonTopologyData(
        IReadOnlyList<int> NgonIndices,
        IReadOnlyList<IReadOnlyList<int>> FaceIndicesPerNgon,
        IReadOnlyList<IReadOnlyList<int>> BoundaryEdgesPerNgon,
        IReadOnlyList<Point3d> NgonCenters,
        IReadOnlyList<int> EdgeCountPerNgon,
        int TotalNgons,
        int TotalFaces) : IResult {
        [Pure]
        private string DebuggerDisplay => this.TotalNgons == 0
            ? "NgonTopology: No ngons detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NgonTopology: {this.TotalNgons} ngons | {this.TotalFaces} faces | AvgValence={this.EdgeCountPerNgon.Average():F1}");
    }

    /// <summary>Naked boundary edges via polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges<T>(
        T geometry,
        IGeometryContext context,
        bool orderLoops = false) where T : notnull =>
        TopologyCore.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops);

    /// <summary>Closed boundary loops from naked edges.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops<T>(
        T geometry,
        IGeometryContext context,
        double? tolerance = null) where T : notnull =>
        TopologyCore.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance);

    /// <summary>Non-manifold vertex and edge detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNonManifold(input: geometry, context: context);

    /// <summary>Connected components and adjacency graph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteConnectivity(input: geometry, context: context);

    /// <summary>Edge classification by continuity type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges<T>(
        T geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        double? angleThreshold = null) where T : notnull =>
        TopologyCore.ExecuteEdgeClassification(input: geometry, context: context, minimumContinuity: minimumContinuity, angleThreshold: angleThreshold);

    /// <summary>Face adjacency for specific edge.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency<T>(
        T geometry,
        IGeometryContext context,
        int edgeIndex) where T : notnull =>
        TopologyCore.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex);

    /// <summary>Vertex topology: valence, manifold status.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VertexData> GetVertexData<T>(
        T geometry,
        IGeometryContext context,
        int vertexIndex) where T : notnull =>
        TopologyCore.ExecuteVertexData(input: geometry, context: context, vertexIndex: vertexIndex);

    /// <summary>Ngon topology analysis for quad-dominant meshes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NgonTopologyData> GetNgonTopology<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNgonTopology(input: geometry, context: context);

    /// <summary>Diagnose topology problems with edge gaps, near-misses, repair suggestions.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double[] EdgeGaps, (int EdgeA, int EdgeB, double Distance)[] NearMisses, byte[] SuggestedRepairs)> DiagnoseTopology(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.Diagnose(brep: brep, context: context);

    /// <summary>Progressive topology healing and automatic rollback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Brep Healed, byte Strategy, bool Success)> HealTopology(
        Brep brep,
        byte maxStrategy,
        IGeometryContext context) =>
        TopologyCompute.Heal(brep: brep, maxStrategy: maxStrategy, context: context);

    /// <summary>Extract topological features: genus, holes, handles, solid classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractTopologicalFeatures(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.ExtractFeatures(brep: brep, context);
}

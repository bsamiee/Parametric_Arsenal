using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology analysis via type-based dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker for polymorphic dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult;

    /// <summary>Conservative repair with minimal tolerance multiplier (0.1×).</summary>
    public sealed record ConservativeRepairStrategy() : Strategy;

    /// <summary>Moderate edge joining with standard tolerance multiplier (1.0×).</summary>
    public sealed record ModerateJoinStrategy() : Strategy;

    /// <summary>Aggressive edge joining with maximum tolerance multiplier (10.0×).</summary>
    public sealed record AggressiveJoinStrategy() : Strategy;

    /// <summary>Combined strategy: conservative repair followed by moderate join.</summary>
    public sealed record CombinedStrategy() : Strategy;

    /// <summary>Targeted joining of near-miss edge pairs within threshold.</summary>
    public sealed record TargetedJoinStrategy() : Strategy;

    /// <summary>Component-level joining of disconnected brep parts.</summary>
    public sealed record ComponentJoinStrategy() : Strategy;

    /// <summary>Base type for healing strategy selection.</summary>
    public abstract record Strategy {
        internal static readonly ConservativeRepairStrategy ConservativeRepair = new();
        internal static readonly ModerateJoinStrategy ModerateJoin = new();
        internal static readonly AggressiveJoinStrategy AggressiveJoin = new();
        internal static readonly CombinedStrategy Combined = new();
        internal static readonly TargetedJoinStrategy TargetedJoin = new();
        internal static readonly ComponentJoinStrategy ComponentJoin = new();
    }

    /// <summary>Edge continuity classification: G0/G1/G2, interior, boundary, non-manifold.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct EdgeContinuityType(byte Value) {
        public static readonly EdgeContinuityType Sharp = new(0);
        public static readonly EdgeContinuityType Smooth = new(1);
        public static readonly EdgeContinuityType Curvature = new(2);
        public static readonly EdgeContinuityType Interior = new(3);
        public static readonly EdgeContinuityType Boundary = new(4);
        public static readonly EdgeContinuityType NonManifold = new(5);
    }

    /// <summary>Topology diagnostic data with edge gaps, near-misses, and suggested healing strategies.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record TopologyDiagnosis(
        IReadOnlyList<double> EdgeGaps,
        IReadOnlyList<(int EdgeA, int EdgeB, double Distance)> NearMisses,
        IReadOnlyList<Strategy> SuggestedStrategies) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"TopologyDiagnosis: Gaps={this.EdgeGaps.Count} | NearMisses={this.NearMisses.Count} | Strategies={this.SuggestedStrategies.Count}");
    }

    /// <summary>Topology healing result with healed brep, applied strategy, and success status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record HealingResult(
        Brep Healed,
        Strategy AppliedStrategy,
        bool Success) : IResult {
        [Pure]
        private string DebuggerDisplay => this.Success
            ? string.Create(CultureInfo.InvariantCulture, $"HealingResult: Success | Strategy={this.AppliedStrategy.GetType().Name}")
            : string.Create(CultureInfo.InvariantCulture, $"HealingResult: Failed | Strategy={this.AppliedStrategy.GetType().Name}");
    }

    /// <summary>Naked edge analysis: boundary curves, indices, valences, and metrics.</summary>
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

    /// <summary>Topological features: genus, loops, solid classification, and handle count.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record TopologicalFeatures(
        int Genus,
        IReadOnlyList<(int LoopIndex, bool IsHole)> Loops,
        bool IsSolid,
        int HandleCount) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsSolid
            ? string.Create(CultureInfo.InvariantCulture, $"TopologicalFeatures: Solid | Genus={this.Genus} | Handles={this.HandleCount}")
            : string.Create(CultureInfo.InvariantCulture, $"TopologicalFeatures: NonSolid | Loops={this.Loops.Count}");
    }

    /// <summary>Edge continuity classification with measures grouped by type.</summary>
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

    /// <summary>Boundary loops from joined naked edges with closure and join diagnostics.</summary>
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

    /// <summary>Vertex topology with connected edges/faces, valence, and manifold status.</summary>
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

    /// <summary>Connected components via BFS with adjacency graph and bounds.</summary>
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

    /// <summary>Non-manifold detection with edge/vertex indices, valences, and locations.</summary>
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

    /// <summary>Ngon analysis: face membership, boundary edges, and centroids.</summary>
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

    /// <summary>Edge-face adjacency with normals, dihedral angle, and manifold status.</summary>
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
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={RhinoMath.ToDegrees(this.DihedralAngle):F1}°")
                : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }

    /// <summary>Get adjacent faces and dihedral angle for edge index.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency<T>(
        T geometry,
        IGeometryContext context,
        int edgeIndex) where T : notnull =>
        TopologyCore.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex);

    /// <summary>Extract naked boundary edges with optional loop ordering.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges<T>(
        T geometry,
        IGeometryContext context,
        bool orderLoops = false) where T : notnull =>
        TopologyCore.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops);

    /// <summary>Get vertex topology with connected edges, faces, and valence.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VertexData> GetVertexData<T>(
        T geometry,
        IGeometryContext context,
        int vertexIndex) where T : notnull =>
        TopologyCore.ExecuteVertexData(input: geometry, context: context, vertexIndex: vertexIndex);

    /// <summary>Join naked edges into closed boundary loops with tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops<T>(
        T geometry,
        IGeometryContext context,
        double? tolerance = null) where T : notnull =>
        TopologyCore.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance);

    /// <summary>Compute connected components via BFS with adjacency graph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteConnectivity(input: geometry, context: context);

    /// <summary>Detect non-manifold vertices and edges with valences.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNonManifold(input: geometry, context: context);

    /// <summary>Extract ngon topology with face membership and boundaries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NgonTopologyData> GetNgonTopology<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNgonTopology(input: geometry, context: context);

    /// <summary>Diagnose topology with edge gaps, near-misses, and repair strategy suggestions.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyDiagnosis> DiagnoseTopology(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.Diagnose(brep: brep, context: context);

    /// <summary>Classify edges by G0/G1/G2 continuity with angle threshold.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges<T>(
        T geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        double? angleThreshold = null) where T : notnull =>
        TopologyCore.ExecuteEdgeClassification(input: geometry, context: context, minimumContinuity: minimumContinuity, angleThreshold: angleThreshold);

    /// <summary>Extract genus, holes, handles, and solid classification via Euler.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologicalFeatures> ExtractTopologicalFeatures(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.ExtractFeatures(brep: brep, context: context);

    /// <summary>Progressive healing with automatic rollback and strategy selection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<HealingResult> HealTopology(
        Brep brep,
        IReadOnlyList<Strategy> strategies,
        IGeometryContext context) =>
        TopologyCompute.Heal(brep: brep, strategies: strategies, context: context);
}

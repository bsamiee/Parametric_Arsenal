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

    /// <summary>Discriminated union for all topology operation results.</summary>
    public abstract record TopologyResult {
        private TopologyResult() { }
        /// <summary>Connectivity analysis result.</summary>
        public sealed record Connectivity(ConnectivityData Data) : TopologyResult;
        /// <summary>Non-manifold detection result.</summary>
        public sealed record NonManifold(NonManifoldData Data) : TopologyResult;
        /// <summary>Ngon topology result.</summary>
        public sealed record Ngon(NgonTopologyData Data) : TopologyResult;
        /// <summary>Edge adjacency result.</summary>
        public sealed record Adjacency(AdjacencyData Data) : TopologyResult;
        /// <summary>Vertex topology result.</summary>
        public sealed record Vertex(VertexData Data) : TopologyResult;
        /// <summary>Naked edges result.</summary>
        public sealed record NakedEdges(NakedEdgeData Data) : TopologyResult;
        /// <summary>Boundary loops result.</summary>
        public sealed record BoundaryLoops(BoundaryLoopData Data) : TopologyResult;
        /// <summary>Edge classification result.</summary>
        public sealed record EdgeClassification(EdgeClassificationData Data) : TopologyResult;
        /// <summary>Topology diagnosis result.</summary>
        public sealed record Diagnosis(TopologyDiagnosis Data) : TopologyResult;
        /// <summary>Topological features result.</summary>
        public sealed record Features(TopologicalFeatures Data) : TopologyResult;
        /// <summary>Healing result.</summary>
        public sealed record Healing(HealingResult Data) : TopologyResult;
    }

    /// <summary>Base type for topology query operations on geometry (Brep/Mesh).</summary>
    public abstract record QueryOperation;
    /// <summary>Request connectivity analysis via BFS.</summary>
    public sealed record ConnectivityQuery() : QueryOperation;
    /// <summary>Request non-manifold detection.</summary>
    public sealed record NonManifoldQuery() : QueryOperation;
    /// <summary>Request ngon topology extraction (Mesh only).</summary>
    public sealed record NgonQuery() : QueryOperation;
    /// <summary>Request edge adjacency for specific edge index.</summary>
    public sealed record AdjacencyQuery(int EdgeIndex) : QueryOperation;
    /// <summary>Request vertex topology for specific vertex index.</summary>
    public sealed record VertexQuery(int VertexIndex) : QueryOperation;
    /// <summary>Request naked edges with optional loop ordering.</summary>
    public sealed record NakedEdgesQuery(bool OrderLoops = false) : QueryOperation;
    /// <summary>Request boundary loops from joined naked edges.</summary>
    public sealed record BoundaryLoopsQuery(double? Tolerance = null) : QueryOperation;
    /// <summary>Request edge classification by continuity.</summary>
    public sealed record EdgeClassificationQuery(Continuity MinimumContinuity = Continuity.G1_continuous, double? AngleThreshold = null) : QueryOperation;

    /// <summary>Base type for Brep-specific topology operations.</summary>
    public abstract record BrepOperation;
    /// <summary>Request topology diagnosis with gap analysis.</summary>
    public sealed record DiagnoseOperation() : BrepOperation;
    /// <summary>Request topological feature extraction.</summary>
    public sealed record ExtractFeaturesOperation() : BrepOperation;
    /// <summary>Request progressive healing with strategies.</summary>
    public sealed record HealOperation(IReadOnlyList<Strategy> Strategies) : BrepOperation;

    /// <summary>Execute topology query operation on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> Query<T>(T geometry, QueryOperation operation, IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteQuery(input: geometry, operation: operation, context: context);

    /// <summary>Execute Brep-specific topology operation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> Execute(Brep brep, BrepOperation operation, IGeometryContext context) =>
        TopologyCore.ExecuteBrepOperation(brep: brep, operation: operation, context: context);

    /// <summary>Topology diagnostic data with edge gaps, near-misses, and suggested healing strategies.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record TopologyDiagnosis(
        IReadOnlyList<double> EdgeGaps,
        IReadOnlyList<(int EdgeA, int EdgeB, double Distance)> NearMisses,
        IReadOnlyList<Strategy> SuggestedStrategies) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"TopologyDiagnosis: Gaps={this.EdgeGaps.Count} | NearMisses={this.NearMisses.Count} | Strategies={this.SuggestedStrategies.Count}");
    }

    /// <summary>Topology healing result with healed brep, applied strategy, and success status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record HealingResult(Brep Healed, Strategy AppliedStrategy, bool Success) : IResult {
        [Pure] private string DebuggerDisplay => this.Success
            ? string.Create(CultureInfo.InvariantCulture, $"HealingResult: Success | Strategy={this.AppliedStrategy.GetType().Name}")
            : string.Create(CultureInfo.InvariantCulture, $"HealingResult: Failed | Strategy={this.AppliedStrategy.GetType().Name}");
    }

    /// <summary>Naked edge analysis: boundary curves, indices, valences, and metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(IReadOnlyList<Curve> EdgeCurves, IReadOnlyList<int> EdgeIndices, IReadOnlyList<int> Valences, bool IsOrdered, int TotalEdgeCount, double TotalLength) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Topological features: genus, loops, solid classification, and handle count.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record TopologicalFeatures(int Genus, IReadOnlyList<(int LoopIndex, bool IsHole)> Loops, bool IsSolid, int HandleCount) : IResult {
        [Pure] private string DebuggerDisplay => this.IsSolid
            ? string.Create(CultureInfo.InvariantCulture, $"TopologicalFeatures: Solid | Genus={this.Genus} | Handles={this.HandleCount}")
            : string.Create(CultureInfo.InvariantCulture, $"TopologicalFeatures: NonSolid | Loops={this.Loops.Count}");
    }

    /// <summary>Edge continuity classification with measures grouped by type.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(IReadOnlyList<int> EdgeIndices, IReadOnlyList<EdgeContinuityType> Classifications, IReadOnlyList<double> ContinuityMeasures, FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType, Continuity MinimumContinuity) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
    }

    /// <summary>Boundary loops from joined naked edges with closure and join diagnostics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(IReadOnlyList<Curve> Loops, IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop, IReadOnlyList<double> LoopLengths, IReadOnlyList<bool> IsClosedPerLoop, double JoinTolerance, int FailedJoins) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
    }

    /// <summary>Vertex topology with connected edges/faces, valence, and manifold status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record VertexData(int VertexIndex, Point3d Location, IReadOnlyList<int> ConnectedEdgeIndices, IReadOnlyList<int> ConnectedFaceIndices, int Valence, bool IsBoundary, bool IsManifold) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Vertex[{this.VertexIndex}]: Valence={this.Valence} | {(this.IsBoundary ? "Boundary" : "Interior")} | {(this.IsManifold ? "Manifold" : "NonManifold")}");
    }

    /// <summary>Connected components via BFS with adjacency graph and bounds.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(IReadOnlyList<IReadOnlyList<int>> ComponentIndices, IReadOnlyList<int> ComponentSizes, IReadOnlyList<BoundingBox> ComponentBounds, int TotalComponents, bool IsFullyConnected, FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : IResult {
        [Pure] private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(CultureInfo.InvariantCulture, $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Non-manifold detection with edge/vertex indices, valences, and locations.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NonManifoldData(IReadOnlyList<int> EdgeIndices, IReadOnlyList<int> VertexIndices, IReadOnlyList<int> Valences, IReadOnlyList<Point3d> Locations, bool IsManifold, bool IsOrientable, int MaxValence) : IResult {
        [Pure] private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(CultureInfo.InvariantCulture, $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Ngon analysis: face membership, boundary edges, and centroids.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NgonTopologyData(IReadOnlyList<int> NgonIndices, IReadOnlyList<IReadOnlyList<int>> FaceIndicesPerNgon, IReadOnlyList<IReadOnlyList<int>> BoundaryEdgesPerNgon, IReadOnlyList<Point3d> NgonCenters, IReadOnlyList<int> EdgeCountPerNgon, int TotalNgons, int TotalFaces) : IResult {
        [Pure] private string DebuggerDisplay => this.TotalNgons == 0
            ? "NgonTopology: No ngons detected"
            : string.Create(CultureInfo.InvariantCulture, $"NgonTopology: {this.TotalNgons} ngons | {this.TotalFaces} faces | AvgValence={this.EdgeCountPerNgon.Average():F1}");
    }

    /// <summary>Edge-face adjacency with normals, dihedral angle, and manifold status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(int EdgeIndex, IReadOnlyList<int> AdjacentFaceIndices, IReadOnlyList<Vector3d> FaceNormals, double DihedralAngle, bool IsManifold, bool IsBoundary) : IResult {
        [Pure] private string DebuggerDisplay => this.IsBoundary
            ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
            : this.IsManifold
                ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Manifold | Angle={RhinoMath.ToDegrees(this.DihedralAngle):F1}°")
                : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }
}

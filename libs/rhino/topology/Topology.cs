using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology analysis with type-based dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Edge continuity classification for geometric analysis.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct EdgeContinuityType(byte value) : IEquatable<EdgeContinuityType> {
        internal readonly byte Value = value;

        /// <summary>G0 sharp edge below continuity threshold.</summary>
        public static readonly EdgeContinuityType Sharp = new(0);
        /// <summary>G1 smooth with tangent continuity.</summary>
        public static readonly EdgeContinuityType Smooth = new(1);
        /// <summary>G2 curvature continuity.</summary>
        public static readonly EdgeContinuityType Curvature = new(2);
        /// <summary>Interior manifold edge with valence 2.</summary>
        public static readonly EdgeContinuityType Interior = new(3);
        /// <summary>Boundary naked edge with valence 1.</summary>
        public static readonly EdgeContinuityType Boundary = new(4);
        /// <summary>Non-manifold edge with valence &gt; 2.</summary>
        public static readonly EdgeContinuityType NonManifold = new(5);

        /// <summary>Value equality.</summary>
        public bool Equals(EdgeContinuityType other) => this.Value == other.Value;
        /// <summary>Value-based hash.</summary>
        public override int GetHashCode() => this.Value;
        /// <summary>Object equality.</summary>
        public override bool Equals(object? obj) => obj is EdgeContinuityType other && this.Equals(other);
        /// <summary>Equality operator.</summary>
        public static bool operator ==(EdgeContinuityType left, EdgeContinuityType right) => left.Equals(right);
        /// <summary>Inequality operator.</summary>
        public static bool operator !=(EdgeContinuityType left, EdgeContinuityType right) => !left.Equals(right);
    }

    /// <summary>Topology result type discriminator.</summary>
    public enum ResultType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5, VertexData = 6, NgonTopology = 7 }

    /// <summary>Unified topology result with discriminated union pattern.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record TopologyResult(
        ResultType Type,
        IReadOnlyList<Curve>? EdgeCurves = null,
        IReadOnlyList<int>? EdgeIndices = null,
        IReadOnlyList<int>? Valences = null,
        IReadOnlyList<Point3d>? Locations = null,
        IReadOnlyList<Vector3d>? FaceNormals = null,
        IReadOnlyList<EdgeContinuityType>? Classifications = null,
        IReadOnlyList<double>? ContinuityMeasures = null,
        IReadOnlyList<Curve>? Loops = null,
        IReadOnlyList<IReadOnlyList<int>>? EdgeIndicesPerLoop = null,
        IReadOnlyList<double>? LoopLengths = null,
        IReadOnlyList<bool>? IsClosedPerLoop = null,
        IReadOnlyList<int>? VertexIndices = null,
        IReadOnlyList<IReadOnlyList<int>>? ComponentIndices = null,
        IReadOnlyList<int>? ComponentSizes = null,
        IReadOnlyList<BoundingBox>? ComponentBounds = null,
        IReadOnlyList<int>? AdjacentFaceIndices = null,
        IReadOnlyList<int>? ConnectedEdgeIndices = null,
        IReadOnlyList<int>? ConnectedFaceIndices = null,
        IReadOnlyList<int>? NgonIndices = null,
        IReadOnlyList<IReadOnlyList<int>>? FaceIndicesPerNgon = null,
        IReadOnlyList<IReadOnlyList<int>>? BoundaryEdgesPerNgon = null,
        IReadOnlyList<Point3d>? NgonCenters = null,
        IReadOnlyList<int>? EdgeCountPerNgon = null,
        FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>>? GroupedByType = null,
        FrozenDictionary<int, IReadOnlyList<int>>? AdjacencyGraph = null,
        Continuity? MinimumContinuity = null,
        Point3d? Location = null,
        bool IsOrdered = false,
        bool IsManifold = false,
        bool IsOrientable = false,
        bool IsBoundary = false,
        bool IsFullyConnected = false,
        int TotalEdgeCount = 0,
        int TotalComponents = 0,
        int TotalNgons = 0,
        int TotalFaces = 0,
        int MaxValence = 0,
        int EdgeIndex = -1,
        int VertexIndex = -1,
        int Valence = 0,
        int FailedJoins = 0,
        double TotalLength = 0.0,
        double DihedralAngle = 0.0,
        double JoinTolerance = 0.0) {
        [Pure]
        private string DebuggerDisplay => this.Type switch {
            ResultType.NakedEdges => string.Create(CultureInfo.InvariantCulture, $"NakedEdges: {this.EdgeCurves?.Count ?? 0}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}"),
            ResultType.BoundaryLoops => string.Create(CultureInfo.InvariantCulture, $"BoundaryLoops: {this.Loops?.Count ?? 0} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}"),
            ResultType.NonManifold => this.IsManifold ? "Manifold: No issues detected" : string.Create(CultureInfo.InvariantCulture, $"NonManifold: Edges={this.EdgeIndices?.Count ?? 0} | Verts={this.VertexIndices?.Count ?? 0} | MaxVal={this.MaxValence}"),
            ResultType.Connectivity => this.IsFullyConnected ? "Connectivity: Single connected component" : string.Create(CultureInfo.InvariantCulture, $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes?.Max() ?? 0}"),
            ResultType.EdgeClassification => string.Create(CultureInfo.InvariantCulture, $"EdgeClassification: Total={this.EdgeIndices?.Count ?? 0} | Sharp={this.GroupedByType?.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count ?? 0}"),
            ResultType.Adjacency => this.IsBoundary ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)") : this.IsManifold ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°") : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices?.Count ?? 0})"),
            ResultType.VertexData => string.Create(CultureInfo.InvariantCulture, $"Vertex[{this.VertexIndex}]: Valence={this.Valence} | {(this.IsBoundary ? "Boundary" : "Interior")} | {(this.IsManifold ? "Manifold" : "NonManifold")}"),
            ResultType.NgonTopology => this.TotalNgons == 0 ? "NgonTopology: No ngons detected" : string.Create(CultureInfo.InvariantCulture, $"NgonTopology: {this.TotalNgons} ngons | {this.TotalFaces} faces | AvgValence={this.EdgeCountPerNgon?.Average() ?? 0:F1}"),
            _ => "TopologyResult: Unknown type",
        };
    }

    /// <summary>Naked boundary edges with polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetNakedEdges<T>(
        T geometry,
        IGeometryContext context,
        bool orderLoops = false,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.NakedEdges, enableDiagnostics: enableDiagnostics, parameters: new { orderLoops });

    /// <summary>Closed boundary loops from naked edges.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetBoundaryLoops<T>(
        T geometry,
        IGeometryContext context,
        double? tolerance = null,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.BoundaryLoops, enableDiagnostics: enableDiagnostics, parameters: new { tolerance });

    /// <summary>Non-manifold vertices and edges detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetNonManifoldData<T>(
        T geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.NonManifold, enableDiagnostics: enableDiagnostics, parameters: new { });

    /// <summary>Connected components with adjacency graph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetConnectivity<T>(
        T geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.Connectivity, enableDiagnostics: enableDiagnostics, parameters: new { });

    /// <summary>Edge classification by continuity type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> ClassifyEdges<T>(
        T geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        double? angleThreshold = null,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.EdgeClassification, enableDiagnostics: enableDiagnostics, parameters: new { minimumContinuity, angleThreshold });

    /// <summary>Face adjacency query for specific edge.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetAdjacency<T>(
        T geometry,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.Adjacency, enableDiagnostics: enableDiagnostics, parameters: new { edgeIndex });

    /// <summary>Vertex topology query with valence, manifold status.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetVertexData<T>(
        T geometry,
        IGeometryContext context,
        int vertexIndex,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.VertexData, enableDiagnostics: enableDiagnostics, parameters: new { vertexIndex });

    /// <summary>Ngon topology analysis for quad-dominant meshes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyResult> GetNgonTopology<T>(
        T geometry,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : notnull =>
        TopologyCore.Execute(input: geometry, context: context, opType: TopologyConfig.OpType.NgonTopology, enableDiagnostics: enableDiagnostics, parameters: new { });
}

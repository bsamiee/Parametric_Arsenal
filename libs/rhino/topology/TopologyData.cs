using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology data types and result structures for polymorphic topological analysis.</summary>
public static class TopologyData {
    /// <summary>Base interface for edge classification types with polymorphic dispatch.</summary>
    public interface IEdgeClassification {
        int EdgeIndex { get; }
        double Measure { get; }
    }

    /// <summary>Sharp edge (G0 discontinuous or below minimum continuity threshold).</summary>
    [DebuggerDisplay("Sharp[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record SharpEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Smooth edge (G1 continuous - tangent continuity).</summary>
    [DebuggerDisplay("Smooth[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record SmoothEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Curvature edge (G2 continuous - curvature continuity).</summary>
    [DebuggerDisplay("Curvature[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record CurvatureEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Interior manifold edge (valence=2, meets continuity requirement).</summary>
    [DebuggerDisplay("Interior[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record InteriorEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Boundary naked edge (valence=1).</summary>
    [DebuggerDisplay("Boundary[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record BoundaryEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Non-manifold edge (valence>2).</summary>
    [DebuggerDisplay("NonManifold[{EdgeIndex}] | M={Measure:F3}")]
    public sealed record NonManifoldEdge(int EdgeIndex, double Measure) : IEdgeClassification;

    /// <summary>Naked edge analysis result containing edge curves and indices with topology metadata.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(
        IReadOnlyList<Curve> EdgeCurves,
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> Valences,
        bool IsOrdered,
        int TotalEdgeCount,
        double TotalLength) {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Boundary loop analysis result with closed loop curves and join diagnostics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(
        IReadOnlyList<Curve> Loops,
        IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
        IReadOnlyList<double> LoopLengths,
        IReadOnlyList<bool> IsClosedPerLoop,
        double JoinTolerance,
        int FailedJoins) {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
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
        int MaxValence) {
        [Pure]
        private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Connected component analysis result with adjacency graph and spatial bounds.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(
        IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
        IReadOnlyList<int> ComponentSizes,
        IReadOnlyList<BoundingBox> ComponentBounds,
        int TotalComponents,
        bool IsFullyConnected,
        FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) {
        [Pure]
        private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Edge classification result with polymorphic edge types and grouped collections.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(
        IReadOnlyList<IEdgeClassification> Classifications,
        FrozenDictionary<Type, IReadOnlyList<int>> GroupedByType,
        Continuity MinimumContinuity) {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"EdgeClassification: Total={this.Classifications.Count} | Sharp={this.GroupedByType.GetValueOrDefault(typeof(SharpEdge), []).Count}");
    }

    /// <summary>Face adjacency query result with neighbor data and dihedral angle computation.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(
        int EdgeIndex,
        IReadOnlyList<int> AdjacentFaceIndices,
        IReadOnlyList<Vector3d> FaceNormals,
        double DihedralAngle,
        bool IsManifold,
        bool IsBoundary) {
        [Pure]
        private string DebuggerDisplay => this.IsBoundary
            ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
            : this.IsManifold
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
                : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }
}

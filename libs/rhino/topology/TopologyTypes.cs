using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Edge continuity classification marker using readonly struct pattern for zero-enum architecture.</summary>
public readonly struct EdgeContinuity(byte kind) {
    internal readonly byte Kind = kind;

    /// <summary>G0 discontinuous or below minimum continuity threshold.</summary>
    public static readonly EdgeContinuity Sharp = new(0);

    /// <summary>G1 continuous (tangent continuity).</summary>
    public static readonly EdgeContinuity Smooth = new(1);

    /// <summary>G2 continuous (curvature continuity).</summary>
    public static readonly EdgeContinuity Curvature = new(2);

    /// <summary>Interior manifold edge (valence=2, meets continuity requirement).</summary>
    public static readonly EdgeContinuity Interior = new(3);

    /// <summary>Boundary naked edge (valence=1).</summary>
    public static readonly EdgeContinuity Boundary = new(4);

    /// <summary>Non-manifold edge (valence>2).</summary>
    public static readonly EdgeContinuity NonManifold = new(5);
}

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
    int FailedJoins) : Topology.IResult {
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
    int MaxValence) : Topology.IResult {
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
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => this.IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type with geometric measures.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuity> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuity, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuity.Sharp, []).Count}");
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
    private string DebuggerDisplay => this.IsBoundary
        ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
        : this.IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
            : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
}

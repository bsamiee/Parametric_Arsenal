using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
public interface ITopologyResult;

/// <summary>Edge continuity classification using readonly struct pattern (replaces enum for zero-enum policy).</summary>
public readonly struct EdgeContinuity(byte value) {
    private readonly byte Value = value;

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

    public static bool operator ==(EdgeContinuity left, EdgeContinuity right) => left.Value == right.Value;
    public static bool operator !=(EdgeContinuity left, EdgeContinuity right) => !(left == right);
    public override bool Equals(object? obj) => obj is EdgeContinuity other && this == other;
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Naked edge analysis result containing edge curves and indices with topology metadata.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : ITopologyResult {
    [Pure]
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
    int FailedJoins) : ITopologyResult {
    [Pure]
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
    int MaxValence) : ITopologyResult {
    [Pure]
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
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : ITopologyResult {
    [Pure]
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
    IReadOnlyList<EdgeContinuity> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuity, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : ITopologyResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuity.Sharp, []).Count}");
}

/// <summary>Face adjacency query result with neighbor data and dihedral angle computation.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record AdjacencyData(
    int EdgeIndex,
    IReadOnlyList<int> AdjacentFaceIndices,
    IReadOnlyList<Vector3d> FaceNormals,
    double DihedralAngle,
    bool IsManifold,
    bool IsBoundary) : ITopologyResult {
    [Pure]
    private string DebuggerDisplay => IsBoundary
        ? string.Create(CultureInfo.InvariantCulture, $"Edge[{EdgeIndex}]: Boundary (valence=1)")
        : IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{EdgeIndex}]: Manifold | Angle={DihedralAngle:F1}° | Faces={AdjacentFaceIndices.Count}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{EdgeIndex}]: NonManifold | Faces={AdjacentFaceIndices.Count}");
}

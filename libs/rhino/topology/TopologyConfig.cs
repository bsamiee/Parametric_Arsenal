namespace Arsenal.Rhino.Topology;

/// <summary>Edge continuity classification enumeration for geometric analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "byte enum for performance and memory efficiency")]
public enum EdgeContinuityType : byte {
    /// <summary>G0 discontinuous or below minimum continuity threshold.</summary>
    Sharp = 0,
    /// <summary>G1 continuous (tangent continuity).</summary>
    Smooth = 1,
    /// <summary>G2 continuous (curvature continuity).</summary>
    Curvature = 2,
    /// <summary>Interior manifold edge (valence=2, meets continuity requirement).</summary>
    Interior = 3,
    /// <summary>Boundary naked edge (valence=1).</summary>
    Boundary = 4,
    /// <summary>Non-manifold edge (valence>2).</summary>
    NonManifold = 5,
}

/// <summary>Configuration constants for topology operations.</summary>
internal static class TopologyConfig {
    /// <summary>Empty indices list reused across operations to avoid allocations.</summary>
    internal static readonly IReadOnlyList<int> EmptyIndices = [];
}

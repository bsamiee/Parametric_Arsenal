using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants and topology accessor dispatch for polymorphic operations.</summary>
internal static class TopologyConfig {
    /// <summary>Validation mode mapping for topology geometry types.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Extrusion)] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Default angle threshold ratio for curvature detection in edge classification.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Default BFS traversal queue capacity for connectivity analysis.</summary>
    internal const int ConnectivityQueueCapacity = 256;

    /// <summary>Topology accessor abstraction for polymorphic BFS and adjacency operations.</summary>
    internal readonly record struct TopologyAccessor(
        int FaceCount,
        Func<int, IEnumerable<int>> GetAdjacentFaces,
        Func<int, BoundingBox> GetFaceBoundingBox);

    /// <summary>Edge extraction accessor for naked edge operations.</summary>
    internal readonly record struct EdgeAccessor(
        int EdgeCount,
        Func<IReadOnlyList<(Curve Curve, int Index)>> GetNakedEdges,
        Func<double> GetTotalLength);

    /// <summary>Adjacency query accessor for dihedral angle computation.</summary>
    internal readonly record struct AdjacencyAccessor(
        Func<int, bool> IsValidEdgeIndex,
        Func<int, (IReadOnlyList<int> FaceIndices, IReadOnlyList<Vector3d> Normals, EdgeAdjacency Valence)> GetEdgeAdjacency);

    /// <summary>Topology accessor dispatch for connectivity and BFS operations.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, TopologyAccessor>> TopologyAccessors =
        new Dictionary<Type, Func<object, TopologyAccessor>> {
            [typeof(Brep)] = geo => geo is Brep brep
                ? new TopologyAccessor(
                    FaceCount: brep.Faces.Count,
                    GetAdjacentFaces: fIdx => brep.Faces[fIdx].AdjacentEdges().SelectMany(e => brep.Edges[e].AdjacentFaces()),
                    GetFaceBoundingBox: fIdx => brep.Faces[fIdx].GetBoundingBox(accurate: false))
                : default,
            [typeof(Mesh)] = geo => geo is Mesh mesh
                ? new TopologyAccessor(
                    FaceCount: mesh.Faces.Count,
                    GetAdjacentFaces: fIdx => mesh.Faces.AdjacentFaces(fIdx).Where(f => f >= 0),
                    GetFaceBoundingBox: fIdx => mesh.Faces[fIdx] switch {
                        MeshFace face when face.IsQuad => new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],]),
                        MeshFace face => new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],]),
                    })
                : default,
        }.ToFrozenDictionary();

    /// <summary>Edge accessor dispatch for naked edge extraction.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, EdgeAccessor>> EdgeAccessors =
        new Dictionary<Type, Func<object, EdgeAccessor>> {
            [typeof(Brep)] = geo => geo is Brep brep
                ? new EdgeAccessor(
                    EdgeCount: brep.Edges.Count,
                    GetNakedEdges: () => [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => (brep.Edges[i].DuplicateCurve(), i)),],
                    GetTotalLength: () => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Sum(i => brep.Edges[i].GetLength()))
                : default,
            [typeof(Mesh)] = geo => geo is Mesh mesh
                ? new EdgeAccessor(
                    EdgeCount: mesh.TopologyEdges.Count,
                    GetNakedEdges: () => [.. (mesh.GetNakedEdges() ?? []).Select((pl, i) => (pl.ToNurbsCurve(), i)),],
                    GetTotalLength: () => (mesh.GetNakedEdges() ?? []).Sum(pl => pl.Length))
                : default,
        }.ToFrozenDictionary();

    /// <summary>Adjacency accessor dispatch for dihedral angle queries.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, AdjacencyAccessor>> AdjacencyAccessors =
        new Dictionary<Type, Func<object, AdjacencyAccessor>> {
            [typeof(Brep)] = geo => geo is Brep brep
                ? new AdjacencyAccessor(
                    IsValidEdgeIndex: idx => idx >= 0 && idx < brep.Edges.Count,
                    GetEdgeAdjacency: idx => brep.Edges[idx] switch {
                        BrepEdge e => ([.. e.AdjacentFaces(),], [.. e.AdjacentFaces().Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)),], e.Valence),
                    })
                : default,
            [typeof(Mesh)] = geo => geo is Mesh mesh
                ? new AdjacencyAccessor(
                    IsValidEdgeIndex: idx => idx >= 0 && idx < mesh.TopologyEdges.Count,
                    GetEdgeAdjacency: idx => mesh.TopologyEdges.GetConnectedFaces(idx) switch {
                        int[] af => ([.. af,], [.. af.Select(i => mesh.FaceNormals[i]),], af.Length switch { 1 => EdgeAdjacency.Naked, 2 => EdgeAdjacency.Interior, _ => EdgeAdjacency.NonManifold }),
                    })
                : default,
        }.ToFrozenDictionary();
}

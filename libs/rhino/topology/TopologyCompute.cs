using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Internal topology computation engine with FrozenDictionary dispatch and inline strategies.</summary>
internal static class TopologyCompute {
    /// <summary>Strategy configuration mapping geometry type and mode to validation and computation functions.</summary>
    internal static readonly FrozenDictionary<(Type, TopologyMode), (V Mode, Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> StrategyConfig =
        new Dictionary<(Type, TopologyMode), (V, Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
            [(typeof(Brep), TopologyMode.NakedEdges)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep && args.Length > 0 && args[0] is bool orderLoops
                    ? (IReadOnlyList<(int idx, Curve curve)>)[.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == 1)
                        .Select(i => (i, brep.Edges[i].DuplicateCurve())),] is var nakedData
                        ? ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                            EdgeCurves: [.. nakedData.Select(x => x.curve),],
                            EdgeIndices: [.. nakedData.Select(x => x.idx),],
                            Valences: [.. nakedData.Select(_ => 1),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: brep.Edges.Count,
                            TotalLength: nakedData.Sum(x => x.curve.GetLength())))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NakedEdgeFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NakedEdgeFailed)),

            [(typeof(Mesh), TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh && (mesh.GetNakedEdges() ?? []) is int[] nakedIndices
                    ? (IReadOnlyList<(int idx, Curve curve)>)[.. nakedIndices.Select(i =>
                        mesh.TopologyEdges.GetTopologyVertices(i) is (int vi, int vj) verts
                            ? (i, new Polyline([
                                (Point3d)mesh.TopologyVertices[verts.vi],
                                (Point3d)mesh.TopologyVertices[verts.vj],
                            ]).ToNurbsCurve())
                            : (i, (Curve)null!)),] is var nakedData
                        ? ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                            EdgeCurves: [.. nakedData.Select(x => x.curve),],
                            EdgeIndices: [.. nakedData.Select(x => x.idx),],
                            Valences: [.. nakedData.Select(_ => 1),],
                            IsOrdered: args.Length > 0 && args[0] is bool b && b,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: nakedData.Sum(x => x.curve.GetLength())))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NakedEdgeFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NakedEdgeFailed)),

            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep && (args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance) is double tol
                    ? brep.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray() is Curve[] naked
                        && Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) is Curve[] joined
                        ? ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                            Loops: [.. joined,],
                            EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                            LoopLengths: [.. joined.Select(c => c.GetLength()),],
                            IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                            JoinTolerance: tol,
                            FailedJoins: naked.Length - joined.Length))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.BoundaryLoopFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.BoundaryLoopFailed)),

            [(typeof(Mesh), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh && (args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance) is double tol
                    && (mesh.GetNakedEdges() ?? []) is int[] nakedIndices
                    ? nakedIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) is (int vi, int vj) verts
                        ? new Polyline([
                            (Point3d)mesh.TopologyVertices[verts.vi],
                            (Point3d)mesh.TopologyVertices[verts.vj],
                        ]).ToNurbsCurve()
                        : (Curve)null!).ToArray() is Curve[] naked
                        && Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) is Curve[] joined
                        ? ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                            Loops: [.. joined,],
                            EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                            LoopLengths: [.. joined.Select(c => c.GetLength()),],
                            IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                            JoinTolerance: tol,
                            FailedJoins: naked.Length - joined.Length))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.BoundaryLoopFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.BoundaryLoopFailed)),

            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep
                    ? (IReadOnlyList<(int idx, int valence, Point3d loc)>)[.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence > 2)
                        .Select(i => (i, brep.Edges[i].Valence, brep.Edges[i].PointAtStart)),] is var nonManifold
                        ? ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                            EdgeIndices: [.. nonManifold.Select(x => x.idx),],
                            VertexIndices: [],
                            Valences: [.. nonManifold.Select(x => x.valence),],
                            Locations: [.. nonManifold.Select(x => x.loc),],
                            IsManifold: nonManifold.Count == 0,
                            IsOrientable: brep.IsSolid,
                            MaxValence: nonManifold.Count > 0 ? nonManifold.Max(x => x.valence) : 2))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NonManifoldEdge)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NonManifoldEdge)),

            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh
                    && mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _) is bool topological
                    ? (IReadOnlyList<(int idx, int[] faces, Point3d loc)>)[.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Select(i => (i, mesh.TopologyEdges.GetConnectedFaces(i), mesh.TopologyEdges.GetTopologyVertices(i)))
                        .Where(x => x.Item2.Length > 2)
                        .Select(x => (x.i, x.Item2, (Point3d)mesh.TopologyVertices[x.Item3.I])),] is var nonManifold
                        ? ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                            EdgeIndices: [.. nonManifold.Select(x => x.idx),],
                            VertexIndices: [],
                            Valences: [.. nonManifold.Select(x => x.faces.Length),],
                            Locations: [.. nonManifold.Select(x => x.loc),],
                            IsManifold: topological,
                            IsOrientable: isOriented,
                            MaxValence: nonManifold.Count > 0 ? nonManifold.Max(x => x.faces.Length) : 2))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NonManifoldEdge)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.NonManifoldEdge)),

            [(typeof(Brep), TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep
                    ? (int[] ids, int count) = (new int[brep.Faces.Count], 0) is var state
                        && (Array.Fill(state.ids, -1), Enumerable.Range(0, brep.Faces.Count).Aggregate(0, (compCount, seed) =>
                            state.ids[seed] != -1 ? compCount : ((Queue<int> queue) => {
                                state.ids[seed] = compCount;
                                queue.Enqueue(seed);
                                _ = Enumerable.Range(0, brep.Faces.Count).TakeWhile(_ => queue.Count > 0).Select(_ =>
                                    queue.Dequeue() is int faceIdx
                                        ? brep.Faces[faceIdx].AdjacentEdges()
                                            .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                            .Where(adj => state.ids[adj] == -1)
                                            .Select(adj => (state.ids[adj] = compCount, queue.Enqueue(adj), 0).Item3)
                                            .ToArray()
                                        : []).ToArray();
                                return compCount + 1;
                            })(new([seed,])))) is var componentCount
                        ? (IReadOnlyList<IReadOnlyList<int>>)[.. Enumerable.Range(0, componentCount)
                            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => state.ids[f] == c),]),] is var components
                            ? ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                                ComponentIndices: components,
                                ComponentSizes: [.. components.Select(c => c.Count),],
                                ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => brep.Faces[i].GetBoundingBox(accurate: false)))),],
                                TotalComponents: componentCount,
                                IsFullyConnected: componentCount == 1,
                                AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                                    .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                        .Where(adj => adj != f),]))
                                    .ToFrozenDictionary(x => x.f, x => x.Item2)))
                            : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)),

            [(typeof(Mesh), TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh
                    ? (int[] ids, int count) = (new int[mesh.Faces.Count], 0) is var state
                        && (Array.Fill(state.ids, -1), Enumerable.Range(0, mesh.Faces.Count).Aggregate(0, (compCount, seed) =>
                            state.ids[seed] != -1 ? compCount : ((Queue<int> queue) => {
                                state.ids[seed] = compCount;
                                queue.Enqueue(seed);
                                _ = Enumerable.Range(0, mesh.Faces.Count).TakeWhile(_ => queue.Count > 0).Select(_ =>
                                    queue.Dequeue() is int faceIdx
                                        ? mesh.TopologyEdges.GetEdgesForFace(faceIdx)
                                            .SelectMany(e => mesh.TopologyEdges.GetConnectedFaces(e))
                                            .Where(adj => state.ids[adj] == -1)
                                            .Select(adj => (state.ids[adj] = compCount, queue.Enqueue(adj), 0).Item3)
                                            .ToArray()
                                        : []).ToArray();
                                return compCount + 1;
                            })(new([seed,])))) is var componentCount
                        ? (IReadOnlyList<IReadOnlyList<int>>)[.. Enumerable.Range(0, componentCount)
                            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => state.ids[f] == c),]),] is var components
                            ? ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                                ComponentIndices: components,
                                ComponentSizes: [.. components.Select(c => c.Count),],
                                ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => mesh.Faces[i].GetBoundingBox(accurate: false)))),],
                                TotalComponents: componentCount,
                                IsFullyConnected: componentCount == 1,
                                AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                                    .Select(f => (f, (IReadOnlyList<int>)[.. mesh.TopologyEdges.GetEdgesForFace(f)
                                        .SelectMany(e => mesh.TopologyEdges.GetConnectedFaces(e))
                                        .Where(adj => adj != f),]))
                                    .ToFrozenDictionary(x => x.f, x => x.Item2)))
                            : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.ConnectivityFailed)),

            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep && (args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous) is Continuity minContinuity
                    ? (IReadOnlyList<(int idx, EdgeContinuityType type, double measure)>)[.. Enumerable.Range(0, brep.Edges.Count)
                        .Select(i => (brep.Edges[i].Valence, brep.Edges[i].AdjacentFaces(), brep.Edges[i].EdgeCurve) switch {
                            (1, _, _) => (i, EdgeContinuityType.Boundary, 0.0),
                            (> 2, _, _) => (i, EdgeContinuityType.NonManifold, 0.0),
                            (2, int[] faces, Curve curve) when faces.Length == 2 => curve.IsContinuous(minContinuity)
                                ? (i, minContinuity == Continuity.G2_continuous ? EdgeContinuityType.Curvature : EdgeContinuityType.Smooth,
                                    Vector3d.VectorAngle(brep.Faces[faces[0]].NormalAt(brep.Faces[faces[0]].Domain(0).Mid, brep.Faces[faces[0]].Domain(1).Mid),
                                        brep.Faces[faces[1]].NormalAt(brep.Faces[faces[1]].Domain(0).Mid, brep.Faces[faces[1]].Domain(1).Mid)))
                                : (i, EdgeContinuityType.Sharp,
                                    Vector3d.VectorAngle(brep.Faces[faces[0]].NormalAt(brep.Faces[faces[0]].Domain(0).Mid, brep.Faces[faces[0]].Domain(1).Mid),
                                        brep.Faces[faces[1]].NormalAt(brep.Faces[faces[1]].Domain(0).Mid, brep.Faces[faces[1]].Domain(1).Mid))),
                            _ => (i, EdgeContinuityType.Interior, 0.0),
                        }),] is var classified
                        ? ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                            EdgeIndices: [.. classified.Select(x => x.idx),],
                            Classifications: [.. classified.Select(x => x.type),],
                            ContinuityMeasures: [.. classified.Select(x => x.measure),],
                            GroupedByType: classified
                                .GroupBy(x => x.type)
                                .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(x => x.idx),]),
                            MinimumContinuity: minContinuity))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.EdgeClassificationFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.EdgeClassificationFailed)),

            [(typeof(Mesh), TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh && (args.Length > 0 && args[0] is double a ? a : ctx.AngleTolerance) is double angleThreshold
                    ? (IReadOnlyList<(int idx, EdgeContinuityType type, double measure)>)[.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Select(i => mesh.TopologyEdges.GetConnectedFaces(i) is int[] faces
                            ? faces.Length switch {
                                1 => (i, EdgeContinuityType.Boundary, 0.0),
                                > 2 => (i, EdgeContinuityType.NonManifold, 0.0),
                                2 => Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]) is double angle
                                    ? (i, Math.Abs(angle) > angleThreshold ? EdgeContinuityType.Sharp : EdgeContinuityType.Smooth, angle)
                                    : (i, EdgeContinuityType.Interior, 0.0),
                                _ => (i, EdgeContinuityType.Interior, 0.0),
                            }
                            : (i, EdgeContinuityType.Interior, 0.0)),] is var classified
                        ? ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                            EdgeIndices: [.. classified.Select(x => x.idx),],
                            Classifications: [.. classified.Select(x => x.type),],
                            ContinuityMeasures: [.. classified.Select(x => x.measure),],
                            GroupedByType: classified
                                .GroupBy(x => x.type)
                                .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(x => x.idx),]),
                            MinimumContinuity: Continuity.G1_continuous))
                        : ResultFactory.Create<Topology.IResult>(error: E.Geometry.EdgeClassificationFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.EdgeClassificationFailed)),

            [(typeof(Brep), TopologyMode.Adjacency)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep)g is Brep brep && (args.Length > 0 && args[0] is int idx ? idx : 0) is int edgeIndex
                    ? edgeIndex < 0 || edgeIndex >= brep.Edges.Count
                        ? ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {brep.Edges.Count - 1}"))
                        : brep.Edges[edgeIndex].AdjacentFaces() is int[] adjacentFaces
                            ? (IReadOnlyList<(int faceIdx, Vector3d normal)>)[.. adjacentFaces.Select(f =>
                                (f, brep.Faces[f].NormalAt(brep.Faces[f].Domain(0).Mid, brep.Faces[f].Domain(1).Mid))),] is var faceData
                                ? ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                    EdgeIndex: edgeIndex,
                                    AdjacentFaceIndices: [.. faceData.Select(x => x.faceIdx),],
                                    FaceNormals: [.. faceData.Select(x => x.normal),],
                                    DihedralAngle: faceData.Count == 2 ? Vector3d.VectorAngle(faceData[0].normal, faceData[1].normal) : 0.0,
                                    IsManifold: brep.Edges[edgeIndex].Valence == 2,
                                    IsBoundary: brep.Edges[edgeIndex].Valence == 1))
                                : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)
                            : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)),

            [(typeof(Mesh), TopologyMode.Adjacency)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh)g is Mesh mesh && (args.Length > 0 && args[0] is int idx ? idx : 0) is int edgeIndex
                    ? edgeIndex < 0 || edgeIndex >= mesh.TopologyEdges.Count
                        ? ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))
                        : mesh.TopologyEdges.GetConnectedFaces(edgeIndex) is int[] adjacentFaces
                            ? (IReadOnlyList<(int faceIdx, Vector3d normal)>)[.. adjacentFaces.Select(f => (f, mesh.FaceNormals[f])),] is var faceData
                                ? ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                    EdgeIndex: edgeIndex,
                                    AdjacentFaceIndices: [.. faceData.Select(x => x.faceIdx),],
                                    FaceNormals: [.. faceData.Select(x => x.normal),],
                                    DihedralAngle: faceData.Count == 2 ? Vector3d.VectorAngle(faceData[0].normal, faceData[1].normal) : 0.0,
                                    IsManifold: adjacentFaces.Length == 2,
                                    IsBoundary: adjacentFaces.Length == 1))
                                : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)
                            : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)
                    : ResultFactory.Create<Topology.IResult>(error: E.Geometry.AdjacencyFailed)),
        }.ToFrozenDictionary();
}

/// <summary>Naked edge analysis result containing edge curves and indices.</summary>
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
        $"NakedEdges: {EdgeCurves.Count}/{TotalEdgeCount} | L={TotalLength:F3} | Ordered={IsOrdered}");
}

/// <summary>Boundary loop analysis result with closed loop curves.</summary>
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
        $"BoundaryLoops: {Loops.Count} | FailedJoins={FailedJoins} | Tol={JoinTolerance:E2}");
}

/// <summary>Non-manifold topology analysis result with diagnostic data.</summary>
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
    private string DebuggerDisplay => IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={EdgeIndices.Count} | Verts={VertexIndices.Count} | MaxVal={MaxValence}");
}

/// <summary>Connected component analysis result with adjacency graph data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {TotalComponents} components | Largest={ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
}

/// <summary>Face adjacency query result with neighbor data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record AdjacencyData(
    int EdgeIndex,
    IReadOnlyList<int> AdjacentFaceIndices,
    IReadOnlyList<Vector3d> FaceNormals,
    double DihedralAngle,
    bool IsManifold,
    bool IsBoundary) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsBoundary
        ? $"Edge[{EdgeIndex}]: Boundary (valence=1)"
        : IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{EdgeIndex}]: Manifold | Angle={DihedralAngle * 180.0 / Math.PI:F1}°")
            : $"Edge[{EdgeIndex}]: NonManifold (valence={AdjacentFaceIndices.Count})";
}

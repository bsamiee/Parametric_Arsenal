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
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    bool orderLoops = args.Length > 0 && args[0] is bool b && b;
                    IReadOnlyList<int> nakedIndices = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == 1),];
                    IReadOnlyList<Curve> curves = [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: nakedIndices,
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: orderLoops,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Mesh), TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                    IReadOnlyList<Curve> curves = [.. nakedIndices.Select(i => {
                        (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                        return new Polyline([
                            (Point3d)mesh.TopologyVertices[vi],
                            (Point3d)mesh.TopologyVertices[vj],
                        ]).ToNurbsCurve();
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: [.. nakedIndices,],
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: args.Length > 0 && args[0] is bool b && b,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    Curve[] nakedCurves = brep.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray();
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
                }),

            [(typeof(Mesh), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                    Curve[] nakedCurves = nakedIndices.Select(i => {
                        (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                        return new Polyline([
                            (Point3d)mesh.TopologyVertices[vi],
                            (Point3d)mesh.TopologyVertices[vj],
                        ]).ToNurbsCurve();
                    }).ToArray();
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
                }),

            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => brep.Edges[i].Valence),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdges.Select(i => brep.Edges[i].PointAtStart),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: nonManifoldEdges.Count == 0,
                        IsOrientable: brep.IsSolid,
                        MaxValence: valences.Count > 0 ? valences.Max() : 2));
                }),

            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    bool topological = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool isConnected);
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdges.Select(i => {
                        (int vi, int _) = mesh.TopologyEdges.GetTopologyVertices(i);
                        return (Point3d)mesh.TopologyVertices[vi];
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: topological,
                        IsOrientable: isOriented,
                        MaxValence: valences.Count > 0 ? valences.Max() : 2));
                }),

            [(typeof(Brep), TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    int[] componentIds = new int[brep.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = Enumerable.Range(0, brep.Faces.Count).Aggregate(0, (compCount, seed) => componentIds[seed] != -1 ? compCount : (Action)(() => {
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = compCount;
                        _ = Enumerable.Range(0, brep.Faces.Count).TakeWhile(_ => queue.Count > 0).Select(_ => {
                            int faceIdx = queue.Dequeue();
                            _ = brep.Faces[faceIdx].AdjacentEdges()
                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                .Where(adjFace => componentIds[adjFace] == -1)
                                .Select(adjFace => { componentIds[adjFace] = compCount; queue.Enqueue(adjFace); return 0; })
                                .ToArray();
                            return 0;
                        }).ToArray();
                    }) is Action a ? (a(), compCount + 1) : compCount);
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => brep.Faces[i].GetBoundingBox(accurate: false)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                .Where(adj => adj != f),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Mesh), TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int[] componentIds = new int[mesh.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = Enumerable.Range(0, mesh.Faces.Count).Aggregate(0, (compCount, seed) => componentIds[seed] != -1 ? compCount : (Action)(() => {
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = compCount;
                        _ = Enumerable.Range(0, mesh.Faces.Count).TakeWhile(_ => queue.Count > 0).Select(_ => {
                            int faceIdx = queue.Dequeue();
                            _ = mesh.TopologyEdges.GetEdgesForFace(faceIdx)
                                .SelectMany(e => mesh.TopologyEdges.GetConnectedFaces(e))
                                .Where(adjFace => componentIds[adjFace] == -1)
                                .Select(adjFace => { componentIds[adjFace] = compCount; queue.Enqueue(adjFace); return 0; })
                                .ToArray();
                            return 0;
                        }).ToArray();
                    }) is Action a ? (a(), compCount + 1) : compCount);
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => mesh.Faces[i].GetBoundingBox(accurate: false)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. mesh.TopologyEdges.GetEdgesForFace(f)
                                .SelectMany(e => mesh.TopologyEdges.GetConnectedFaces(e))
                                .Where(adj => adj != f),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    Continuity minContinuity = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous;
                    IReadOnlyList<int> allEdges = [.. Enumerable.Range(0, brep.Edges.Count),];
                    IReadOnlyList<EdgeContinuityType> classifications = [.. allEdges.Select(i => brep.Edges[i].Valence switch {
                        1 => EdgeContinuityType.Boundary,
                        > 2 => EdgeContinuityType.NonManifold,
                        _ => brep.Edges[i].EdgeCurve is Curve curve && brep.Edges[i].AdjacentFaces().Length == 2
                            ? curve.IsContinuous(minContinuity)
                                ? minContinuity == Continuity.G2_continuous ? EdgeContinuityType.Curvature : EdgeContinuityType.Smooth
                                : EdgeContinuityType.Sharp
                            : EdgeContinuityType.Interior,
                    }),];
                    IReadOnlyList<double> measures = [.. allEdges.Select(_ => 0.0),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: allEdges,
                        Classifications: classifications,
                        ContinuityMeasures: measures,
                        GroupedByType: classifications.Select((c, i) => (c, i))
                            .GroupBy(x => x.c)
                            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(x => x.i),]),
                        MinimumContinuity: minContinuity));
                }),

            [(typeof(Mesh), TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double angleThreshold = args.Length > 0 && args[0] is double a ? a : ctx.AngleTolerance;
                    IReadOnlyList<int> allEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
                    IReadOnlyList<EdgeContinuityType> classifications = [.. allEdges.Select(i => {
                        int[] faces = mesh.TopologyEdges.GetConnectedFaces(i);
                        return faces.Length switch {
                            1 => EdgeContinuityType.Boundary,
                            > 2 => EdgeContinuityType.NonManifold,
                            2 => Math.Abs(Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]])) > angleThreshold
                                ? EdgeContinuityType.Sharp
                                : EdgeContinuityType.Smooth,
                            _ => EdgeContinuityType.Interior,
                        };
                    }),];
                    IReadOnlyList<double> measures = [.. allEdges.Select(i => {
                        int[] faces = mesh.TopologyEdges.GetConnectedFaces(i);
                        return faces.Length == 2
                            ? Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]])
                            : 0.0;
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: allEdges,
                        Classifications: classifications,
                        ContinuityMeasures: measures,
                        GroupedByType: classifications.Select((c, i) => (c, i))
                            .GroupBy(x => x.c)
                            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(x => x.i),]),
                        MinimumContinuity: Continuity.G1_continuous));
                }),

            [(typeof(Brep), TopologyMode.Adjacency)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex < 0 || edgeIndex >= brep.Edges.Count
                        ? ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {brep.Edges.Count - 1}"))
                        : ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                            EdgeIndex: edgeIndex,
                            AdjacentFaceIndices: [.. brep.Edges[edgeIndex].AdjacentFaces(),],
                            FaceNormals: [.. brep.Edges[edgeIndex].AdjacentFaces().Select(f => brep.Faces[f].NormalAt(brep.Faces[f].Domain(0).Mid, brep.Faces[f].Domain(1).Mid)),],
                            DihedralAngle: brep.Edges[edgeIndex].AdjacentFaces().Length == 2
                                ? Vector3d.VectorAngle(
                                    brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[0]].NormalAt(brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[0]].Domain(0).Mid, brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[0]].Domain(1).Mid),
                                    brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[1]].NormalAt(brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[1]].Domain(0).Mid, brep.Faces[brep.Edges[edgeIndex].AdjacentFaces()[1]].Domain(1).Mid))
                                : 0.0,
                            IsManifold: brep.Edges[edgeIndex].Valence == 2,
                            IsBoundary: brep.Edges[edgeIndex].Valence == 1));
                }),

            [(typeof(Mesh), TopologyMode.Adjacency)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex < 0 || edgeIndex >= mesh.TopologyEdges.Count
                        ? ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))
                        : ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                            EdgeIndex: edgeIndex,
                            AdjacentFaceIndices: [.. mesh.TopologyEdges.GetConnectedFaces(edgeIndex),],
                            FaceNormals: [.. mesh.TopologyEdges.GetConnectedFaces(edgeIndex).Select(f => mesh.FaceNormals[f]),],
                            DihedralAngle: mesh.TopologyEdges.GetConnectedFaces(edgeIndex) is int[] faces && faces.Length == 2
                                ? Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]])
                                : 0.0,
                            IsManifold: mesh.TopologyEdges.GetConnectedFaces(edgeIndex).Length == 2,
                            IsBoundary: mesh.TopologyEdges.GetConnectedFaces(edgeIndex).Length == 1));
                }),
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

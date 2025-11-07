using System.Buffers;
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
        $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
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
        $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
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
    private string DebuggerDisplay => this.IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
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
    private string DebuggerDisplay => this.IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
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
        $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
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
    private string DebuggerDisplay => this.IsBoundary
        ? $"Edge[{this.EdgeIndex}]: Boundary (valence=1)"
        : this.IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
            : $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})";
}

/// <summary>Internal topology computation algorithms with FrozenDictionary-based type dispatch and zero-allocation execution.</summary>
internal static class TopologyCompute {
    /// <summary>Strategy configuration mapping (Type, TopologyMode) pairs to validation modes and computation functions.</summary>
    internal static readonly FrozenDictionary<(Type, TopologyMode), (V Mode, Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> StrategyConfig =
        new Dictionary<(Type, TopologyMode), (V, Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
            [(typeof(Brep), TopologyMode.NakedEdges)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    bool orderLoops = args.Length > 0 && args[0] is bool b && b;
                    IReadOnlyList<int> nakedIndices = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == 1),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),],
                        EdgeIndices: nakedIndices,
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: orderLoops,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())));
                }),
            [(typeof(Mesh), TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                    Point3d toPoint(int vi) => (Point3d)mesh.TopologyVertices[vi];
                    Curve toCurve(int edgeIdx) => (mesh.TopologyEdges.GetTopologyVertices(edgeIdx) switch {
                        (int vi, int vj) => new Polyline([toPoint(vi), toPoint(vj),])
                    }).ToNurbsCurve();
                    double edgeLength(int edgeIdx) => mesh.TopologyEdges.GetTopologyVertices(edgeIdx) switch {
                        (int vi, int vj) => toPoint(vi).DistanceTo(toPoint(vj))
                    };
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: [.. nakedIndices.Select(toCurve),],
                        EdgeIndices: [.. nakedIndices,],
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: args.Length > 0 && args[0] is bool b && b,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: nakedIndices.Sum(edgeLength)));
                }),
            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    Curve[] nakedCurves = [.. brep.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()),];
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Count(c => c.IsClosed)));
                }),
            [(typeof(Mesh), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                    Point3d toPoint(int vi) => (Point3d)mesh.TopologyVertices[vi];
                    Curve toCurve(int edgeIdx) => (mesh.TopologyEdges.GetTopologyVertices(edgeIdx) switch {
                        (int vi, int vj) => new Polyline([toPoint(vi), toPoint(vj),])
                    }).ToNurbsCurve();
                    Curve[] nakedCurves = [.. nakedIndices.Select(toCurve),];
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Count(c => c.IsClosed)));
                }),
            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => brep.Edges[i].Valence),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: [.. nonManifoldEdges.Select(i => brep.Edges[i].PointAtStart),],
                        IsManifold: nonManifoldEdges.Count == 0,
                        IsOrientable: brep.IsSolid,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),
            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    bool topologicalTest = true;
                    bool isOrientable = mesh.IsManifold(topologicalTest, out bool orientedManifold, out _);
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: [.. nonManifoldEdges.Select(i => (Point3d)mesh.TopologyVertices[mesh.TopologyEdges.GetTopologyVertices(i).I]),],
                        IsManifold: isOrientable,
                        IsOrientable: orientedManifold,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),
            [(typeof(Brep), TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    int[] componentIds = new int[brep.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    IEnumerable<int> getNeighbors(int faceIdx) =>
                        brep.Faces[faceIdx].AdjacentEdges().SelectMany(e => brep.Edges[e].AdjacentFaces()).Where(adj => adj != faceIdx);
                    for (int seed = 0; seed < brep.Faces.Count; seed++) {
                        if (componentIds[seed] != -1) continue;
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = componentCount;
                        while (queue.Count > 0) {
                            foreach (int adjFace in getNeighbors(queue.Dequeue())) {
                                if (componentIds[adjFace] == -1) {
                                    componentIds[adjFace] = componentCount;
                                    queue.Enqueue(adjFace);
                                }
                            }
                        }
                        componentCount++;
                    }
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => brep.Faces[i].GetBoundingBox(accurate: false)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. getNeighbors(f),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),
            [(typeof(Mesh), TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int[] componentIds = new int[mesh.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    IEnumerable<int> getNeighbors(int faceIdx) =>
                        mesh.TopologyEdges.GetEdgesForFace(faceIdx).SelectMany(e => mesh.TopologyEdges.GetConnectedFaces(e)).Where(adj => adj != faceIdx);
                    for (int seed = 0; seed < mesh.Faces.Count; seed++) {
                        if (componentIds[seed] != -1) continue;
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = componentCount;
                        while (queue.Count > 0) {
                            foreach (int adjFace in getNeighbors(queue.Dequeue())) {
                                if (componentIds[adjFace] == -1) {
                                    componentIds[adjFace] = componentCount;
                                    queue.Enqueue(adjFace);
                                }
                            }
                        }
                        componentCount++;
                    }
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => mesh.Faces.GetFaceCenter(i)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. getNeighbors(f),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),
            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    Continuity minContinuity = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous;
                    IReadOnlyList<(int idx, EdgeContinuityType type, double measure)> classifications = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Select(i => brep.Edges[i] switch {
                            BrepEdge edge when edge.Valence == 1 => (i, EdgeContinuityType.Boundary, 0.0),
                            BrepEdge edge when edge.Valence > 2 => (i, EdgeContinuityType.NonManifold, 0.0),
                            BrepEdge edge when edge.Valence == 2 && edge.IsSmoothManifoldEdge(ctx.AngleToleranceRadians) => (i, EdgeContinuityType.Smooth, 1.0),
                            BrepEdge _ => (i, EdgeContinuityType.Sharp, 0.0),
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: [.. classifications.Select(c => c.idx),],
                        Classifications: [.. classifications.Select(c => c.type),],
                        ContinuityMeasures: [.. classifications.Select(c => c.measure),],
                        GroupedByType: classifications
                            .GroupBy(c => c.type)
                            .Select(g => (g.Key, (IReadOnlyList<int>)[.. g.Select(x => x.idx),]))
                            .ToFrozenDictionary(x => x.Key, x => x.Item2),
                        MinimumContinuity: minContinuity));
                }),
            [(typeof(Mesh), TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double angleThreshold = args.Length > 0 && args[0] is double d ? d : ctx.AngleToleranceRadians;
                    IReadOnlyList<(int idx, EdgeContinuityType type, double measure)> classifications = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Select(i => mesh.TopologyEdges.GetConnectedFaces(i) switch {
                            int[] faces when faces.Length == 1 => (i, EdgeContinuityType.Boundary, 0.0),
                            int[] faces when faces.Length > 2 => (i, EdgeContinuityType.NonManifold, 0.0),
                            int[] faces when faces.Length == 2 => Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]) switch {
                                double angle when angle < angleThreshold => (i, EdgeContinuityType.Smooth, angle),
                                double angle => (i, EdgeContinuityType.Sharp, angle)
                            },
                            _ => (i, EdgeContinuityType.Sharp, 0.0),
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: [.. classifications.Select(c => c.idx),],
                        Classifications: [.. classifications.Select(c => c.type),],
                        ContinuityMeasures: [.. classifications.Select(c => c.measure),],
                        GroupedByType: classifications
                            .GroupBy(c => c.type)
                            .Select(g => (g.Key, (IReadOnlyList<int>)[.. g.Select(x => x.idx),]))
                            .ToFrozenDictionary(x => x.Key, x => x.Item2),
                        MinimumContinuity: Continuity.C0_continuous));
                }),
            [(typeof(Brep), TopologyMode.Adjacency)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex < 0 || edgeIndex >= brep.Edges.Count
                        ? ResultFactory.Create<Topology.IResult>(
                            error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {brep.Edges.Count - 1}"))
                        : brep.Edges[edgeIndex] switch {
                            BrepEdge edge when edge.Valence == 1 => edge.AdjacentFaces() switch {
                                int[] adjacentFaces => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                    EdgeIndex: edgeIndex,
                                    AdjacentFaceIndices: [.. adjacentFaces,],
                                    FaceNormals: [brep.Faces[adjacentFaces[0]].NormalAt(0.5, 0.5),],
                                    DihedralAngle: 0.0,
                                    IsManifold: false,
                                    IsBoundary: true))
                            },
                            BrepEdge edge when edge.Valence == 2 => edge.AdjacentFaces() switch {
                                int[] adjacentFaces => (brep.Faces[adjacentFaces[0]].NormalAt(0.5, 0.5), brep.Faces[adjacentFaces[1]].NormalAt(0.5, 0.5)) switch {
                                    (Vector3d n1, Vector3d n2) => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                        EdgeIndex: edgeIndex,
                                        AdjacentFaceIndices: [.. adjacentFaces,],
                                        FaceNormals: [n1, n2,],
                                        DihedralAngle: Vector3d.VectorAngle(n1, n2),
                                        IsManifold: true,
                                        IsBoundary: false))
                                }
                            },
                            BrepEdge edge => edge.AdjacentFaces() switch {
                                int[] adjacentFaces => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                    EdgeIndex: edgeIndex,
                                    AdjacentFaceIndices: [.. adjacentFaces,],
                                    FaceNormals: [.. adjacentFaces.Select(f => brep.Faces[f].NormalAt(0.5, 0.5)),],
                                    DihedralAngle: 0.0,
                                    IsManifold: false,
                                    IsBoundary: false))
                            },
                            _ => ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdge),
                        };
                }),
            [(typeof(Mesh), TopologyMode.Adjacency)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex < 0 || edgeIndex >= mesh.TopologyEdges.Count
                        ? ResultFactory.Create<Topology.IResult>(
                            error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))
                        : mesh.TopologyEdges.GetConnectedFaces(edgeIndex) switch {
                            int[] faces when faces.Length == 1 => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [mesh.FaceNormals[faces[0]],],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: true)),
                            int[] faces when faces.Length == 2 => (mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]) switch {
                                (Vector3d n1, Vector3d n2) => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                    EdgeIndex: edgeIndex,
                                    AdjacentFaceIndices: [.. faces,],
                                    FaceNormals: [n1, n2,],
                                    DihedralAngle: Vector3d.VectorAngle(n1, n2),
                                    IsManifold: true,
                                    IsBoundary: false))
                            },
                            int[] faces => ResultFactory.Create(value: (Topology.IResult)new AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => mesh.FaceNormals[f]),],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: false)),
                        };
                }),
        }.ToFrozenDictionary();
}

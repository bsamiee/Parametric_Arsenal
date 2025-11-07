using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Internal topology computation engine with FrozenDictionary dispatch and inline algorithmic strategies.</summary>
internal static class TopologyCompute {
    /// <summary>Strategy configuration mapping (Type, TopologyMode) to validation mode and computation function.</summary>
    internal static readonly FrozenDictionary<(Type, Topology.TopologyMode), (V Mode, Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> StrategyConfig =
        new Dictionary<(Type, Topology.TopologyMode), (V, Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
            [(typeof(Brep), Topology.TopologyMode.NakedEdges)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    bool orderLoops = args.Length > 0 && args[0] is bool b && b;
                    IReadOnlyList<int> nakedIndices = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == 1),];
                    IReadOnlyList<Curve> curves = [.. nakedIndices
                        .Select(i => brep.Edges[i].DuplicateCurve()),];
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: nakedIndices,
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: orderLoops,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Mesh), Topology.TopologyMode.NakedEdges)] = (
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
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: [.. nakedIndices,],
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: args.Length > 0 && args[0] is bool b && b,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Brep), Topology.TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    Curve[] nakedCurves = brep.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray();
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
                }),

            [(typeof(Mesh), Topology.TopologyMode.BoundaryLoops)] = (
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
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
                }),

            [(typeof(Brep), Topology.TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => {
                    Brep brep = (Brep)g;
                    int[] componentIds = new int[brep.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    for (int seed = 0; seed < brep.Faces.Count; seed++) {
                        componentIds[seed] switch {
                            -1 => (Queue<int> queue = new([seed,]), componentIds[seed] = componentCount, queue) switch {
                                (Queue<int> q, int _, Queue<int> _) => (Action)(() => {
                                    while (q.Count > 0) {
                                        int faceIdx = q.Dequeue();
                                        _ = brep.Faces[faceIdx].AdjacentEdges()
                                            .SelectMany(edgeIdx => brep.Edges[edgeIdx].AdjacentFaces())
                                            .Where(adjFace => componentIds[adjFace] == -1)
                                            .Select(adjFace => (componentIds[adjFace] = componentCount, q.Enqueue(adjFace), 0).Item3)
                                            .ToArray();
                                    }
                                    componentCount++;
                                })(),
                            },
                            _ => 0,
                        };
                    }
                    IReadOnlyList<IReadOnlyList<int>>[] components = Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),])
                        .ToArray();
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.ConnectivityData(
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

            [(typeof(Mesh), Topology.TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => {
                    Mesh mesh = (Mesh)g;
                    int[] componentIds = new int[mesh.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    for (int seed = 0; seed < mesh.Faces.Count; seed++) {
                        componentIds[seed] switch {
                            -1 => (Queue<int> queue = new([seed,]), componentIds[seed] = componentCount, queue) switch {
                                (Queue<int> q, int _, Queue<int> _) => (Action)(() => {
                                    while (q.Count > 0) {
                                        int faceIdx = q.Dequeue();
                                        int[] verts = mesh.Faces[faceIdx].IsQuad ? [mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C, mesh.Faces[faceIdx].D,] : [mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C,];
                                        _ = verts
                                            .SelectMany(v => mesh.TopologyVertices.ConnectedFaces(v))
                                            .Where(adjFace => componentIds[adjFace] == -1)
                                            .Select(adjFace => (componentIds[adjFace] = componentCount, q.Enqueue(adjFace), 0).Item3)
                                            .ToArray();
                                    }
                                    componentCount++;
                                })(),
                            },
                            _ => 0,
                        };
                    }
                    IReadOnlyList<IReadOnlyList<int>>[] components = Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),])
                        .ToArray();
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => {
                            BoundingBox bbox = BoundingBox.Empty;
                            _ = c.Select(fIdx => (bbox.Union(mesh.Vertices[mesh.Faces[fIdx].A]), bbox.Union(mesh.Vertices[mesh.Faces[fIdx].B]), bbox.Union(mesh.Vertices[mesh.Faces[fIdx].C]), mesh.Faces[fIdx].IsQuad ? bbox.Union(mesh.Vertices[mesh.Faces[fIdx].D]) : bbox, 0).Item5).ToArray();
                            return bbox;
                        }),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                            .Select(f => {
                                int[] verts = mesh.Faces[f].IsQuad ? [mesh.Faces[f].A, mesh.Faces[f].B, mesh.Faces[f].C, mesh.Faces[f].D,] : [mesh.Faces[f].A, mesh.Faces[f].B, mesh.Faces[f].C,];
                                return (f, (IReadOnlyList<int>)[.. verts.SelectMany(v => mesh.TopologyVertices.ConnectedFaces(v)).Where(adj => adj != f).Distinct(),]);
                            })
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Brep), Topology.TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => {
                    Brep brep = (Brep)g;
                    IReadOnlyList<int> nonManifoldEdgeIndices = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdgeIndices.Select(i => brep.Edges[i].Valence),];
                    int maxValence = valences.Count > 0 ? valences.Max() : 0;
                    bool isOrientable = brep.IsSolid;
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.NonManifoldData(
                        EdgeIndices: nonManifoldEdgeIndices,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: [.. nonManifoldEdgeIndices.Select(i => brep.Edges[i].PointAtStart),],
                        IsManifold: nonManifoldEdgeIndices.Count == 0,
                        IsOrientable: isOrientable,
                        MaxValence: maxValence));
                }),

            [(typeof(Mesh), Topology.TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => {
                    Mesh mesh = (Mesh)g;
                    bool topological = mesh.IsManifold(topologicalTest: true, out bool oriented, out bool connected);
                    IReadOnlyList<int> nonManifoldEdgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> nonManifoldVertexIndices = [.. Enumerable.Range(0, mesh.TopologyVertices.Count)
                        .Where(i => {
                            int[] connectedFaces = mesh.TopologyVertices.ConnectedFaces(i);
                            int[] connectedEdges = mesh.TopologyVertices.ConnectedEdges(i);
                            return connectedFaces.Length > 0 && connectedEdges.Length > 0 && connectedFaces.Length != connectedEdges.Length - 1;
                        }),];
                    IReadOnlyList<int> edgeValences = [.. nonManifoldEdgeIndices.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    int maxValence = edgeValences.Count > 0 ? edgeValences.Max() : 0;
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.NonManifoldData(
                        EdgeIndices: nonManifoldEdgeIndices,
                        VertexIndices: nonManifoldVertexIndices,
                        Valences: edgeValences,
                        Locations: [.. nonManifoldEdgeIndices.Select(i => (Point3d)mesh.TopologyVertices[mesh.TopologyEdges.GetTopologyVertices(i).I]),],
                        IsManifold: topological,
                        IsOrientable: oriented,
                        MaxValence: maxValence));
                }),

            [(typeof(Brep), Topology.TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    Continuity minimumContinuity = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous;
                    IReadOnlyList<(int idx, Topology.EdgeContinuityType type, double measure)> classified = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Select(i => {
                            BrepEdge edge = brep.Edges[i];
                            return edge.Valence switch {
                                1 => (i, Topology.EdgeContinuityType.Boundary, 0.0),
                                > 2 => (i, Topology.EdgeContinuityType.NonManifold, 0.0),
                                2 => edge.EdgeCurve is Curve curve && edge.AdjacentFaces().Length == 2 ?
                                    curve.IsContinuous(minimumContinuity, t: edge.Domain.Mid) switch {
                                        true when minimumContinuity == Continuity.G2_continuous => (i, Topology.EdgeContinuityType.Curvature, 2.0),
                                        true when minimumContinuity == Continuity.G1_continuous => (i, Topology.EdgeContinuityType.Smooth, 1.0),
                                        true => (i, Topology.EdgeContinuityType.Interior, 1.0),
                                        false => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                                    } : (i, Topology.EdgeContinuityType.Sharp, 0.0),
                                _ => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                            };
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.EdgeClassificationData(
                        EdgeIndices: [.. classified.Select(x => x.idx),],
                        Classifications: [.. classified.Select(x => x.type),],
                        ContinuityMeasures: [.. classified.Select(x => x.measure),],
                        GroupedByType: classified
                            .GroupBy(x => x.type)
                            .ToFrozenDictionary(grp => grp.Key, grp => (IReadOnlyList<int>)[.. grp.Select(x => x.idx),]),
                        MinimumContinuity: minimumContinuity));
                }),

            [(typeof(Mesh), Topology.TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double angleThresholdRadians = args.Length > 0 && args[0] is double rad ? rad : ctx.AngleTolerance;
                    IReadOnlyList<(int idx, Topology.EdgeContinuityType type, double measure)> classified = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Select(i => {
                            int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
                            return connectedFaces.Length switch {
                                1 => (i, Topology.EdgeContinuityType.Boundary, 0.0),
                                > 2 => (i, Topology.EdgeContinuityType.NonManifold, 0.0),
                                2 => (Vector3d n1 = mesh.FaceNormals[connectedFaces[0]], Vector3d n2 = mesh.FaceNormals[connectedFaces[1]], double angle = Vector3d.VectorAngle(n1, n2)) switch {
                                    (Vector3d _, Vector3d _, double a) when a > angleThresholdRadians => (i, Topology.EdgeContinuityType.Sharp, a),
                                    (Vector3d _, Vector3d _, double a) => (i, Topology.EdgeContinuityType.Smooth, a),
                                },
                                _ => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                            };
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new Topology.EdgeClassificationData(
                        EdgeIndices: [.. classified.Select(x => x.idx),],
                        Classifications: [.. classified.Select(x => x.type),],
                        ContinuityMeasures: [.. classified.Select(x => x.measure),],
                        GroupedByType: classified
                            .GroupBy(x => x.type)
                            .ToFrozenDictionary(grp => grp.Key, grp => (IReadOnlyList<int>)[.. grp.Select(x => x.idx),]),
                        MinimumContinuity: Continuity.G1_continuous));
                }),

            [(typeof(Brep), Topology.TopologyMode.Adjacency)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex >= 0 && edgeIndex < brep.Edges.Count ?
                        (int[] adjacentFaces = brep.Edges[edgeIndex].AdjacentFaces(), int valence = brep.Edges[edgeIndex].Valence) switch {
                            (int[] faces, int v) when v == 1 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => {
                                    (bool success, Vector3d normal) = brep.Faces[f].NormalAt(brep.Faces[f].Domain(0).Mid, brep.Faces[f].Domain(1).Mid);
                                    return success ? normal : Vector3d.Zero;
                                }),],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: true)),
                            (int[] faces, int v) when v == 2 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => {
                                    (bool success, Vector3d normal) = brep.Faces[f].NormalAt(brep.Faces[f].Domain(0).Mid, brep.Faces[f].Domain(1).Mid);
                                    return success ? normal : Vector3d.Zero;
                                }),],
                                DihedralAngle: faces.Length == 2 ?
                                    (brep.Faces[faces[0]].NormalAt(brep.Faces[faces[0]].Domain(0).Mid, brep.Faces[faces[0]].Domain(1).Mid).Item2 is Vector3d n1,
                                     brep.Faces[faces[1]].NormalAt(brep.Faces[faces[1]].Domain(0).Mid, brep.Faces[faces[1]].Domain(1).Mid).Item2 is Vector3d n2,
                                     Vector3d.VectorAngle(n1, n2)) switch {
                                        (Vector3d _, Vector3d _, double angle) => angle,
                                    } : 0.0,
                                IsManifold: true,
                                IsBoundary: false)),
                            (int[] faces, int _) => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => {
                                    (bool success, Vector3d normal) = brep.Faces[f].NormalAt(brep.Faces[f].Domain(0).Mid, brep.Faces[f].Domain(1).Mid);
                                    return success ? normal : Vector3d.Zero;
                                }),],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: false)),
                        } :
                        ResultFactory.Create<Topology.IResult>(
                            error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {brep.Edges.Count - 1}"));
                }),

            [(typeof(Mesh), Topology.TopologyMode.Adjacency)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int edgeIndex = args.Length > 0 && args[0] is int idx ? idx : 0;
                    return edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count ?
                        (int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(edgeIndex)) switch {
                            int[] faces when faces.Length == 1 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => mesh.FaceNormals[f]),],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: true)),
                            int[] faces when faces.Length == 2 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => mesh.FaceNormals[f]),],
                                DihedralAngle: Vector3d.VectorAngle(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]),
                                IsManifold: true,
                                IsBoundary: false)),
                            int[] faces => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: [.. faces,],
                                FaceNormals: [.. faces.Select(f => mesh.FaceNormals[f]),],
                                DihedralAngle: 0.0,
                                IsManifold: false,
                                IsBoundary: false)),
                        } :
                        ResultFactory.Create<Topology.IResult>(
                            error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"));
                }),
        }.ToFrozenDictionary();
}

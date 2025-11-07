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
                (g, ctx, args) => (Brep brep = (Brep)g, bool order = args.Length > 0 && args[0] is bool b && b) switch {
                    var (b, _) => (IReadOnlyList<int> indices = [.. Enumerable.Range(0, b.Edges.Count).Where(i => b.Edges[i].Valence == 1),], IReadOnlyList<Curve> curves = [.. indices.Select(i => b.Edges[i].DuplicateCurve()),]) switch {
                        var (idx, crv) => ResultFactory.Create(value: (Topology.IResult)new Topology.NakedEdgeData(
                            EdgeCurves: crv,
                            EdgeIndices: idx,
                            Valences: [.. idx.Select(_ => 1),],
                            IsOrdered: order,
                            TotalEdgeCount: b.Edges.Count,
                            TotalLength: crv.Sum(c => c.GetLength()))),
                    },
                }),

            [(typeof(Mesh), Topology.TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh mesh = (Mesh)g, int[] indices = mesh.GetNakedEdges() ?? [], bool order = args.Length > 0 && args[0] is bool b && b) switch {
                    var (m, idx, _) => (IReadOnlyList<Curve> curves = [.. idx.Select(i => new Polyline([.. mesh.TopologyEdges.GetTopologyVertices(i) switch { var (vi, vj) => new[] { (Point3d)m.TopologyVertices[vi], (Point3d)m.TopologyVertices[vj], } }]).ToNurbsCurve()),]) switch {
                        var crv => ResultFactory.Create(value: (Topology.IResult)new Topology.NakedEdgeData(
                            EdgeCurves: crv,
                            EdgeIndices: [.. idx,],
                            Valences: [.. idx.Select(_ => 1),],
                            IsOrdered: order,
                            TotalEdgeCount: m.TopologyEdges.Count,
                            TotalLength: crv.Sum(c => c.GetLength()))),
                    },
                }),

            [(typeof(Brep), Topology.TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep brep = (Brep)g, double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance) switch {
                    var (b, t) => (Curve[] naked = b.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray(), Curve[] joined = Curve.JoinCurves(b.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray(), joinTolerance: t, preserveDirection: false)) switch {
                        var (n, j) => ResultFactory.Create(value: (Topology.IResult)new Topology.BoundaryLoopData(
                            Loops: [.. j,],
                            EdgeIndicesPerLoop: [.. j.Select(_ => (IReadOnlyList<int>)[],)],
                            LoopLengths: [.. j.Select(c => c.GetLength()),],
                            IsClosedPerLoop: [.. j.Select(c => c.IsClosed),],
                            JoinTolerance: t,
                            FailedJoins: n.Length - j.Length)),
                    },
                }),

            [(typeof(Mesh), Topology.TopologyMode.BoundaryLoops)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh mesh = (Mesh)g, int[] indices = mesh.GetNakedEdges() ?? [], double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance) switch {
                    var (m, idx, t) => (Curve[] naked = idx.Select(i => new Polyline([.. m.TopologyEdges.GetTopologyVertices(i) switch { var (vi, vj) => new[] { (Point3d)m.TopologyVertices[vi], (Point3d)m.TopologyVertices[vj], } }]).ToNurbsCurve()).ToArray(), Curve[] joined = Curve.JoinCurves(idx.Select(i => new Polyline([.. m.TopologyEdges.GetTopologyVertices(i) switch { var (vi, vj) => new[] { (Point3d)m.TopologyVertices[vi], (Point3d)m.TopologyVertices[vj], } }]).ToNurbsCurve()).ToArray(), joinTolerance: t, preserveDirection: false)) switch {
                        var (n, j) => ResultFactory.Create(value: (Topology.IResult)new Topology.BoundaryLoopData(
                            Loops: [.. j,],
                            EdgeIndicesPerLoop: [.. j.Select(_ => (IReadOnlyList<int>)[],)],
                            LoopLengths: [.. j.Select(c => c.GetLength()),],
                            IsClosedPerLoop: [.. j.Select(c => c.IsClosed),],
                            JoinTolerance: t,
                            FailedJoins: n.Length - j.Length)),
                    },
                }),

            [(typeof(Brep), Topology.TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => (Brep brep = (Brep)g, int[] ids = new int[((Brep)g).Faces.Count], Action fill = () => Array.Fill(ids, -1), int count = 0) switch {
                    var (b, _, __, _) => (fill(), Enumerable.Range(0, b.Faces.Count).Select(_ => ids[_] == -1 ? (Queue<int> q = new([_,]), ids[_] = count, Action traverse = () => { while (q.Count > 0) { int f = q.Dequeue(); _ = b.Faces[f].AdjacentEdges().SelectMany(e => b.Edges[e].AdjacentFaces()).Where(a => ids[a] == -1).Select(a => (ids[a] = count, q.Enqueue(a), 0).Item3).ToArray(); } }, count++) switch { var (__, ___, ____, c) => (traverse(), c).Item2 } : 0).ToArray()) switch {
                        var (__, comps) => (IReadOnlyList<IReadOnlyList<int>>[] components = Enumerable.Range(0, count).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, b.Faces.Count).Where(f => ids[f] == c),]).ToArray()) switch {
                            var c => ResultFactory.Create(value: (Topology.IResult)new Topology.ConnectivityData(
                                ComponentIndices: c,
                                ComponentSizes: [.. c.Select(x => x.Count),],
                                ComponentBounds: [.. c.Select(x => BoundingBox.Union(x.Select(i => b.Faces[i].GetBoundingBox(accurate: false)))),],
                                TotalComponents: count,
                                IsFullyConnected: count == 1,
                                AdjacencyGraph: Enumerable.Range(0, b.Faces.Count).Select(f => (f, (IReadOnlyList<int>)[.. b.Faces[f].AdjacentEdges().SelectMany(e => b.Edges[e].AdjacentFaces()).Where(a => a != f),])).ToFrozenDictionary(x => x.f, x => x.Item2))),
                        },
                    },
                }),

            [(typeof(Mesh), Topology.TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => (Mesh mesh = (Mesh)g, int[] ids = new int[((Mesh)g).Faces.Count], Action fill = () => Array.Fill(ids, -1), int count = 0) switch {
                    var (m, _, __, _) => (fill(), Enumerable.Range(0, m.Faces.Count).Select(_ => ids[_] == -1 ? (Queue<int> q = new([_,]), ids[_] = count, Action traverse = () => { while (q.Count > 0) { int f = q.Dequeue(); int[] v = m.Faces[f].IsQuad ? [m.Faces[f].A, m.Faces[f].B, m.Faces[f].C, m.Faces[f].D,] : [m.Faces[f].A, m.Faces[f].B, m.Faces[f].C,]; _ = v.SelectMany(x => m.TopologyVertices.ConnectedFaces(x)).Where(a => ids[a] == -1).Select(a => (ids[a] = count, q.Enqueue(a), 0).Item3).ToArray(); } }, count++) switch { var (__, ___, ____, c) => (traverse(), c).Item2 } : 0).ToArray()) switch {
                        var (__, comps) => (IReadOnlyList<IReadOnlyList<int>>[] components = Enumerable.Range(0, count).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, m.Faces.Count).Where(f => ids[f] == c),]).ToArray()) switch {
                            var c => ResultFactory.Create(value: (Topology.IResult)new Topology.ConnectivityData(
                                ComponentIndices: c,
                                ComponentSizes: [.. c.Select(x => x.Count),],
                                ComponentBounds: [.. c.Select(x => (BoundingBox b = BoundingBox.Empty, _ = x.Select(i => (b.Union(m.Vertices[m.Faces[i].A]), b.Union(m.Vertices[m.Faces[i].B]), b.Union(m.Vertices[m.Faces[i].C]), m.Faces[i].IsQuad ? b.Union(m.Vertices[m.Faces[i].D]) : b, 0).Item5).ToArray()) switch { var (bb, __) => bb }),],
                                TotalComponents: count,
                                IsFullyConnected: count == 1,
                                AdjacencyGraph: Enumerable.Range(0, m.Faces.Count).Select(f => (f, (IReadOnlyList<int>)[.. (m.Faces[f].IsQuad ? [m.Faces[f].A, m.Faces[f].B, m.Faces[f].C, m.Faces[f].D,] : [m.Faces[f].A, m.Faces[f].B, m.Faces[f].C,]).SelectMany(v => m.TopologyVertices.ConnectedFaces(v)).Where(a => a != f).Distinct(),])).ToFrozenDictionary(x => x.f, x => x.Item2))),
                        },
                    },
                }),

            [(typeof(Brep), Topology.TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => (Brep brep = (Brep)g, IReadOnlyList<int> indices = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence > 2),], IReadOnlyList<int> valences = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence > 2).Select(i => brep.Edges[i].Valence),]) switch {
                    var (b, idx, val) => ResultFactory.Create(value: (Topology.IResult)new Topology.NonManifoldData(
                        EdgeIndices: idx,
                        VertexIndices: [],
                        Valences: val,
                        Locations: [.. idx.Select(i => b.Edges[i].PointAtStart),],
                        IsManifold: idx.Count == 0,
                        IsOrientable: b.IsSolid,
                        MaxValence: val.Count > 0 ? val.Max() : 0)),
                }),

            [(typeof(Mesh), Topology.TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => (Mesh mesh = (Mesh)g, bool topo = mesh.IsManifold(topologicalTest: true, out bool orient, out bool conn)) switch {
                    var (m, t) => (IReadOnlyList<int> edgeIdx = [.. Enumerable.Range(0, m.TopologyEdges.Count).Where(i => m.TopologyEdges.GetConnectedFaces(i).Length > 2),], IReadOnlyList<int> vertIdx = [.. Enumerable.Range(0, m.TopologyVertices.Count).Where(i => (int[] cf = m.TopologyVertices.ConnectedFaces(i), int[] ce = m.TopologyVertices.ConnectedEdges(i)) switch { var (f, e) => f.Length > 0 && e.Length > 0 && f.Length != e.Length - 1 })], IReadOnlyList<int> valences = [.. edgeIdx.Select(i => m.TopologyEdges.GetConnectedFaces(i).Length),]) switch {
                        var (ei, vi, val) => ResultFactory.Create(value: (Topology.IResult)new Topology.NonManifoldData(
                            EdgeIndices: ei,
                            VertexIndices: vi,
                            Valences: val,
                            Locations: [.. ei.Select(i => (Point3d)m.TopologyVertices[m.TopologyEdges.GetTopologyVertices(i).I]),],
                            IsManifold: t,
                            IsOrientable: orient,
                            MaxValence: val.Count > 0 ? val.Max() : 0)),
                    },
                }),

            [(typeof(Brep), Topology.TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep brep = (Brep)g, Continuity minCont = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous) switch {
                    var (b, mc) => (IReadOnlyList<(int idx, Topology.EdgeContinuityType type, double measure)> classified = [.. Enumerable.Range(0, b.Edges.Count).Select(i => b.Edges[i] switch {
                        BrepEdge e when e.Valence == 1 => (i, Topology.EdgeContinuityType.Boundary, 0.0),
                        BrepEdge e when e.Valence > 2 => (i, Topology.EdgeContinuityType.NonManifold, 0.0),
                        BrepEdge e when e.Valence == 2 && e.EdgeCurve is Curve crv && e.AdjacentFaces().Length == 2 => crv.IsContinuous(mc, t: e.Domain.Mid) switch {
                            true when mc == Continuity.G2_continuous => (i, Topology.EdgeContinuityType.Curvature, 2.0),
                            true when mc == Continuity.G1_continuous => (i, Topology.EdgeContinuityType.Smooth, 1.0),
                            true => (i, Topology.EdgeContinuityType.Interior, 1.0),
                            false => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                        },
                        _ => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                    }),]) switch {
                        var cls => ResultFactory.Create(value: (Topology.IResult)new Topology.EdgeClassificationData(
                            EdgeIndices: [.. cls.Select(x => x.idx),],
                            Classifications: [.. cls.Select(x => x.type),],
                            ContinuityMeasures: [.. cls.Select(x => x.measure),],
                            GroupedByType: cls.GroupBy(x => x.type).ToFrozenDictionary(grp => grp.Key, grp => (IReadOnlyList<int>)[.. grp.Select(x => x.idx),]),
                            MinimumContinuity: mc)),
                    },
                }),

            [(typeof(Mesh), Topology.TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh mesh = (Mesh)g, double angleThresh = args.Length > 0 && args[0] is double rad ? rad : ctx.AngleTolerance) switch {
                    var (m, at) => (IReadOnlyList<(int idx, Topology.EdgeContinuityType type, double measure)> classified = [.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.GetConnectedFaces(i) switch {
                        int[] cf when cf.Length == 1 => (i, Topology.EdgeContinuityType.Boundary, 0.0),
                        int[] cf when cf.Length > 2 => (i, Topology.EdgeContinuityType.NonManifold, 0.0),
                        int[] cf when cf.Length == 2 => (double angle = Vector3d.VectorAngle(m.FaceNormals[cf[0]], m.FaceNormals[cf[1]])) switch {
                            double a when a > at => (i, Topology.EdgeContinuityType.Sharp, a),
                            double a => (i, Topology.EdgeContinuityType.Smooth, a),
                        },
                        _ => (i, Topology.EdgeContinuityType.Sharp, 0.0),
                    }),]) switch {
                        var cls => ResultFactory.Create(value: (Topology.IResult)new Topology.EdgeClassificationData(
                            EdgeIndices: [.. cls.Select(x => x.idx),],
                            Classifications: [.. cls.Select(x => x.type),],
                            ContinuityMeasures: [.. cls.Select(x => x.measure),],
                            GroupedByType: cls.GroupBy(x => x.type).ToFrozenDictionary(grp => grp.Key, grp => (IReadOnlyList<int>)[.. grp.Select(x => x.idx),]),
                            MinimumContinuity: Continuity.G1_continuous)),
                    },
                }),

            [(typeof(Brep), Topology.TopologyMode.Adjacency)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => (Brep brep = (Brep)g, int edgeIdx = args.Length > 0 && args[0] is int idx ? idx : 0) switch {
                    var (b, ei) when ei >= 0 && ei < b.Edges.Count => (int[] faces = b.Edges[ei].AdjacentFaces(), int valence = b.Edges[ei].Valence, Func<int, Vector3d> normal = f => b.Faces[f].NormalAt(b.Faces[f].Domain(0).Mid, b.Faces[f].Domain(1).Mid) switch { var (s, n) => s ? n : Vector3d.Zero }) switch {
                        var (f, v, n) when v == 1 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(n),],
                            DihedralAngle: 0.0,
                            IsManifold: false,
                            IsBoundary: true)),
                        var (f, v, n) when v == 2 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(n),],
                            DihedralAngle: f.Length == 2 ? Vector3d.VectorAngle(n(f[0]), n(f[1])) : 0.0,
                            IsManifold: true,
                            IsBoundary: false)),
                        var (f, _, n) => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(n),],
                            DihedralAngle: 0.0,
                            IsManifold: false,
                            IsBoundary: false)),
                    },
                    var (b, ei) => ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {ei}, Max: {b.Edges.Count - 1}")),
                }),

            [(typeof(Mesh), Topology.TopologyMode.Adjacency)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => (Mesh mesh = (Mesh)g, int edgeIdx = args.Length > 0 && args[0] is int idx ? idx : 0) switch {
                    var (m, ei) when ei >= 0 && ei < m.TopologyEdges.Count => m.TopologyEdges.GetConnectedFaces(ei) switch {
                        int[] f when f.Length == 1 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(x => m.FaceNormals[x]),],
                            DihedralAngle: 0.0,
                            IsManifold: false,
                            IsBoundary: true)),
                        int[] f when f.Length == 2 => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(x => m.FaceNormals[x]),],
                            DihedralAngle: Vector3d.VectorAngle(m.FaceNormals[f[0]], m.FaceNormals[f[1]]),
                            IsManifold: true,
                            IsBoundary: false)),
                        int[] f => ResultFactory.Create(value: (Topology.IResult)new Topology.AdjacencyData(
                            EdgeIndex: ei,
                            AdjacentFaceIndices: [.. f,],
                            FaceNormals: [.. f.Select(x => m.FaceNormals[x]),],
                            DihedralAngle: 0.0,
                            IsManifold: false,
                            IsBoundary: false)),
                    },
                    var (m, ei) => ResultFactory.Create<Topology.IResult>(error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {ei}, Max: {m.TopologyEdges.Count - 1}")),
                }),
        }.ToFrozenDictionary();
}

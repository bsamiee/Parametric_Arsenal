using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;
using static Arsenal.Rhino.Topology.TopologyData;

namespace Arsenal.Rhino.Topology;

/// <summary>Dense topology computation with polymorphic dispatch, zero duplication, and inline algorithmic implementations.</summary>
internal static class TopologyCompute {
    private static readonly IReadOnlyList<int> EmptyIndices = [];

    [Pure]
    internal static Result<NakedEdgeData> ExecuteNakedEdges<T>(
        T input,
        IGeometryContext context,
        bool orderLoops,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<NakedEdgeData>>>)(g => g switch {
                Brep brep => brep.Edges.Count == 0
                    ? ResultFactory.Create(value: (IReadOnlyList<NakedEdgeData>)[new NakedEdgeData([], [], [], orderLoops, 0, 0.0),])
                    : [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),] switch {
                        IReadOnlyList<int> indices => ResultFactory.Create(value: (IReadOnlyList<NakedEdgeData>)[
                            new NakedEdgeData(
                                [.. indices.Select(i => brep.Edges[i].DuplicateCurve()),],
                                indices,
                                [.. indices.Select(_ => 1),],
                                orderLoops,
                                brep.Edges.Count,
                                indices.Sum(i => brep.Edges[i].GetLength())),
                        ]),
                    },
                Mesh mesh => (mesh.GetNakedEdges() ?? []) switch {
                    Polyline[] polylines => ResultFactory.Create(value: (IReadOnlyList<NakedEdgeData>)[
                        new NakedEdgeData(
                            [.. polylines.Select(pl => pl.ToNurbsCurve()),],
                            [.. Enumerable.Range(0, polylines.Length),],
                            [.. Enumerable.Repeat(1, polylines.Length),],
                            orderLoops,
                            mesh.TopologyEdges.Count,
                            polylines.Sum(pl => pl.Length)),
                    ]),
                },
                _ => ResultFactory.Create<IReadOnlyList<NakedEdgeData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, NakedEdgeData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetNakedEdges.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<BoundaryLoopData> ExecuteBoundaryLoops<T>(
        T input,
        IGeometryContext context,
        double? tolerance,
        bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<BoundaryLoopData>>>)(g => (g switch {
                Brep brep => [.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                    .Select(i => brep.Edges[i].DuplicateCurve()),],
                Mesh mesh => [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),],
                _ => [],
            }) switch {
                IReadOnlyList<Curve> nakedCurves => (nakedCurves.Count > 0
                    ? Curve.JoinCurves([.. nakedCurves,], joinTolerance: tol, preserveDirection: false)
                    : []) switch {
                    Curve[] joined => ResultFactory.Create(value: (IReadOnlyList<BoundaryLoopData>)[
                        new BoundaryLoopData(
                            joined,
                            [.. joined.Select(_ => EmptyIndices),],
                            [.. joined.Select(c => c.GetLength()),],
                            [.. joined.Select(c => c.IsClosed),],
                            tol,
                            nakedCurves.Count - joined.Length),
                    ]),
                },
            }),
            config: new OperationConfig<T, BoundaryLoopData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetBoundaryLoops.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);
    }

    [Pure]
    internal static Result<NonManifoldData> ExecuteNonManifold<T>(
        T input,
        IGeometryContext context,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<NonManifoldData>>>)(g => g switch {
                Brep brep => ([.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),], brep.IsSolid) switch {
                    (IReadOnlyList<int> edges, bool orientable) => ResultFactory.Create(value: (IReadOnlyList<NonManifoldData>)[
                        new NonManifoldData(
                            edges,
                            [],
                            [.. edges.Select(i => (int)brep.Edges[i].Valence),],
                            [.. edges.Select(i => brep.Edges[i].PointAtStart),],
                            edges.Count == 0,
                            orientable,
                            edges.Count > 0 ? edges.Select(i => (int)brep.Edges[i].Valence).Max() : 0),
                    ]),
                },
                Mesh mesh => (mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _), [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                    .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),]) switch {
                    (bool _, IReadOnlyList<int> edges) => ResultFactory.Create(value: (IReadOnlyList<NonManifoldData>)[
                        new NonManifoldData(
                            edges,
                            [],
                            [.. edges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),],
                            [.. edges.Select(i => {
                                IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
                                (Point3d p1, Point3d p2) = (mesh.TopologyVertices[verts.I], mesh.TopologyVertices[verts.J]);
                                return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
                            }),],
                            edges.Count == 0,
                            isOriented,
                            edges.Count > 0 ? edges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length).Max() : 0),
                    ]),
                },
                _ => ResultFactory.Create<IReadOnlyList<NonManifoldData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, NonManifoldData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetNonManifold.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<ConnectivityData> ExecuteConnectivity<T>(
        T input,
        IGeometryContext context,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<ConnectivityData>>>)(g => g switch {
                Brep brep => (componentIds: new int[brep.Faces.Count], componentCount: 0) switch {
                    (int[] ids, int count) => (Array.Fill(ids, -1), Enumerable.Range(0, brep.Faces.Count)
                        .Aggregate(count, (c, seed) => ids[seed] != -1 ? c : (
                            new Queue<int>([seed,]).Aggregate(c, (cc, _) => (ids[seed] = cc, cc)) switch {
                                int cid => brep.Faces.SelectMany((_, fIdx) => ids[fIdx] != cid ? [] :
                                    brep.Faces[fIdx].AdjacentEdges()
                                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                        .Where(f => ids[f] == -1)
                                        .Select(f => (ids[f] = cid, f)))
                                    .Aggregate(new Queue<int>(), (q, x) => (q.Enqueue(x.f), q))
                                    .Aggregate(cid, (cc, _) => cc),
                            }) + 1)) switch {
                        int finalCount => [.. Enumerable.Range(0, finalCount)
                            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => ids[f] == c),]),] switch {
                            IReadOnlyList<int>[] components => ResultFactory.Create(value: (IReadOnlyList<ConnectivityData>)[
                                new ConnectivityData(
                                    components,
                                    [.. components.Select(c => c.Count),],
                                    [.. components.Select(c => c.Aggregate(
                                        BoundingBox.Empty,
                                        (union, fIdx) => union.IsValid
                                            ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false))
                                            : brep.Faces[fIdx].GetBoundingBox(accurate: false))),],
                                    finalCount,
                                    finalCount == 1,
                                    Enumerable.Range(0, brep.Faces.Count)
                                        .ToFrozenDictionary(
                                            f => f,
                                            f => (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                                .Where(adj => adj != f),])),
                            ]),
                        },
                    },
                },
                Mesh mesh => (componentIds: new int[mesh.Faces.Count], componentCount: 0) switch {
                    (int[] ids, int count) => (Array.Fill(ids, -1), Enumerable.Range(0, mesh.Faces.Count)
                        .Aggregate(count, (c, seed) => ids[seed] != -1 ? c : (
                            new Queue<int>([seed,]).Aggregate(c, (cc, _) => (ids[seed] = cc, cc)) switch {
                                int cid => mesh.Faces.AdjacentFaces(seed)
                                    .Where(f => f >= 0 && ids[f] == -1)
                                    .Select(f => (ids[f] = cid, f))
                                    .Aggregate(new Queue<int>(), (q, x) => (q.Enqueue(x.f), q))
                                    .Aggregate(cid, (cc, _) => cc),
                            }) + 1)) switch {
                        int finalCount => [.. Enumerable.Range(0, finalCount)
                            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => ids[f] == c),]),] switch {
                            IReadOnlyList<int>[] components => ResultFactory.Create(value: (IReadOnlyList<ConnectivityData>)[
                                new ConnectivityData(
                                    components,
                                    [.. components.Select(c => c.Count),],
                                    [.. components.Select(c => c.Aggregate(
                                        BoundingBox.Empty,
                                        (union, fIdx) => (face: mesh.Faces[fIdx], box: mesh.Faces[fIdx].IsQuad
                                            ? new BoundingBox([mesh.Vertices[mesh.Faces[fIdx].A], mesh.Vertices[mesh.Faces[fIdx].B], mesh.Vertices[mesh.Faces[fIdx].C], mesh.Vertices[mesh.Faces[fIdx].D],])
                                            : new BoundingBox([mesh.Vertices[mesh.Faces[fIdx].A], mesh.Vertices[mesh.Faces[fIdx].B], mesh.Vertices[mesh.Faces[fIdx].C],])) switch {
                                            (MeshFace _, BoundingBox fBox) => union.IsValid ? BoundingBox.Union(union, fBox) : fBox,
                                        })),],
                                    finalCount,
                                    finalCount == 1,
                                    Enumerable.Range(0, mesh.Faces.Count)
                                        .ToFrozenDictionary(
                                            f => f,
                                            f => (IReadOnlyList<int>)[.. mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0),])),
                            ]),
                        },
                    },
                },
                _ => ResultFactory.Create<IReadOnlyList<ConnectivityData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, ConnectivityData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetConnectivity.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<EdgeClassificationData> ExecuteEdgeClassification<T>(
        T input,
        IGeometryContext context,
        Continuity? minimumContinuity = null,
        double? angleThreshold = null,
        bool enableDiagnostics = false) where T : notnull {
        (Continuity minCont, double angleThresh) = (minimumContinuity ?? Continuity.G1_continuous, angleThreshold ?? context.AngleToleranceRadians);
        return UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<EdgeClassificationData>>>)(g => g switch {
                Brep brep => [.. Enumerable.Range(0, brep.Edges.Count)
                    .Select(i => (brep.Edges[i].Valence, brep.Edges[i].EdgeCurve, brep.Edges[i]) switch {
                        (EdgeAdjacency.Naked, _, BrepEdge edge) => (IEdgeClassification)new BoundaryEdge(i, edge.GetLength()),
                        (EdgeAdjacency.NonManifold, _, BrepEdge edge) => new NonManifoldEdge(i, edge.GetLength()),
                        (EdgeAdjacency.Interior, Curve crv, BrepEdge edge) when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) ||
                            crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) =>
                            new CurvatureEdge(i, edge.GetLength()),
                        (EdgeAdjacency.Interior, Curve crv, BrepEdge edge) when edge.IsSmoothManifoldEdge(angleToleranceRadians: angleThresh) ||
                            crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) ||
                            crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) =>
                            new SmoothEdge(i, edge.GetLength()),
                        (EdgeAdjacency.Interior, _, BrepEdge edge) when minCont < Continuity.G1_continuous =>
                            new InteriorEdge(i, edge.GetLength()),
                        (_, _, BrepEdge edge) => new SharpEdge(i, edge.GetLength()),
                    }),] switch {
                    IReadOnlyList<IEdgeClassification> classifications => ResultFactory.Create(value: (IReadOnlyList<EdgeClassificationData>)[
                        new EdgeClassificationData(
                            classifications,
                            classifications.GroupBy(c => c.GetType(), c => c.EdgeIndex).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]),
                            minCont),
                    ]),
                },
                Mesh mesh => (curvThresh: angleThresh * 0.1, [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                    .Select(i => mesh.TopologyEdges.GetConnectedFaces(i) switch {
                        int[] faces when faces.Length == 1 => (IEdgeClassification)new BoundaryEdge(
                            i,
                            mesh.TopologyEdges.GetTopologyVertices(i) switch {
                                IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]),
                            }),
                        int[] faces when faces.Length > 2 => new NonManifoldEdge(
                            i,
                            mesh.TopologyEdges.GetTopologyVertices(i) switch {
                                IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]),
                            }),
                        int[] faces when faces.Length == 2 => ((Vector3d)mesh.FaceNormals[faces[0]], (Vector3d)mesh.FaceNormals[faces[1]]) switch {
                            (Vector3d n1, Vector3d n2) => (n1.IsValid && n2.IsValid ? Vector3d.VectorAngle(n1, n2) : Math.PI, mesh.TopologyEdges.GetTopologyVertices(i)) switch {
                                (double angle, IndexPair verts) when Math.Abs(angle) < angleThresh * 0.1 => new CurvatureEdge(
                                    i,
                                    mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J])),
                                (double angle, IndexPair verts) when Math.Abs(angle) < angleThresh => new SmoothEdge(
                                    i,
                                    mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J])),
                                (double _, IndexPair verts) => new SharpEdge(
                                    i,
                                    mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J])),
                            },
                        },
                        _ => new SharpEdge(
                            i,
                            mesh.TopologyEdges.GetTopologyVertices(i) switch {
                                IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]),
                            }),
                    }),]) switch {
                    (double _, IReadOnlyList<IEdgeClassification> classifications) => ResultFactory.Create(value: (IReadOnlyList<EdgeClassificationData>)[
                        new EdgeClassificationData(
                            classifications,
                            classifications.GroupBy(c => c.GetType(), c => c.EdgeIndex).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]),
                            Continuity.C0_continuous),
                    ]),
                },
                _ => ResultFactory.Create<IReadOnlyList<EdgeClassificationData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, EdgeClassificationData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.ClassifyEdges.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);
    }

    [Pure]
    internal static Result<AdjacencyData> ExecuteAdjacency<T>(
        T input,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<AdjacencyData>>>)(g => g switch {
                Brep brep when edgeIndex >= 0 && edgeIndex < brep.Edges.Count =>
                    ([.. brep.Edges[edgeIndex].AdjacentFaces(),], [.. brep.Edges[edgeIndex].AdjacentFaces()
                        .Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)),]) switch {
                        (int[] faces, Vector3d[] normals) => ResultFactory.Create(value: (IReadOnlyList<AdjacencyData>)[
                            new AdjacencyData(
                                edgeIndex,
                                faces,
                                normals,
                                normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0,
                                brep.Edges[edgeIndex].Valence == EdgeAdjacency.Interior,
                                brep.Edges[edgeIndex].Valence == EdgeAdjacency.Naked),
                        ]),
                    },
                Brep brep => ResultFactory.Create<IReadOnlyList<AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {brep.Edges.Count - 1}"))),
                Mesh mesh when edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count =>
                    (mesh.TopologyEdges.GetConnectedFaces(edgeIndex), [.. mesh.TopologyEdges.GetConnectedFaces(edgeIndex)
                        .Select(i => (Vector3d)mesh.FaceNormals[i]),]) switch {
                        (int[] faces, Vector3d[] normals) => ResultFactory.Create(value: (IReadOnlyList<AdjacencyData>)[
                            new AdjacencyData(
                                edgeIndex,
                                faces,
                                normals,
                                normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0,
                                faces.Length == 2,
                                faces.Length == 1),
                        ]),
                    },
                Mesh mesh => ResultFactory.Create<IReadOnlyList<AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))),
                _ => ResultFactory.Create<IReadOnlyList<AdjacencyData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, AdjacencyData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetAdjacency.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);
}

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

namespace Arsenal.Rhino.Topology;

/// <summary>Internal topology computation algorithms with dense polymorphic dispatch and zero-duplication design.</summary>
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
                Brep brep => ComputeNakedEdges(
                    edgeCount: brep.Edges.Count,
                    getNakedIndices: () => [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),],
                    getCurves: indices => indices.Select(i => brep.Edges[i].DuplicateCurve()).ToArray(),
                    getLength: indices => indices.Sum(i => brep.Edges[i].GetLength()),
                    orderLoops: orderLoops),
                Mesh mesh => ComputeNakedEdges(
                    edgeCount: mesh.TopologyEdges.Count,
                    getNakedIndices: () => (mesh.GetNakedEdges() ?? []) switch {
                        Polyline[] polylines => [.. Enumerable.Range(0, polylines.Length),],
                    },
                    getCurves: indices => (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()).ToArray(),
                    getLength: indices => (mesh.GetNakedEdges() ?? []).Sum(pl => pl.Length),
                    orderLoops: orderLoops),
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
            operation: (Func<T, Result<IReadOnlyList<BoundaryLoopData>>>)(g => g switch {
                Brep brep => ComputeBoundaryLoops(
                    nakedCurves: [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                        .Select(i => brep.Edges[i].DuplicateCurve()),],
                    joinTolerance: tol),
                Mesh mesh => ComputeBoundaryLoops(
                    nakedCurves: [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),],
                    joinTolerance: tol),
                _ => ResultFactory.Create<IReadOnlyList<BoundaryLoopData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
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
                Brep brep => ComputeNonManifold(
                    getNonManifoldEdges: () => [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),],
                    getValences: indices => [.. indices.Select(i => (int)brep.Edges[i].Valence),],
                    getLocations: indices => [.. indices.Select(i => brep.Edges[i].PointAtStart),],
                    isOrientable: brep.IsSolid),
                Mesh mesh => mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _) switch {
                    bool _ => ComputeNonManifold(
                        getNonManifoldEdges: () => [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                            .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),],
                        getValences: indices => [.. indices.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),],
                        getLocations: indices => [.. indices.Select(i => {
                            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
                            (Point3d p1, Point3d p2) = (mesh.TopologyVertices[verts.I], mesh.TopologyVertices[verts.J]);
                            return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
                        }),],
                        isOrientable: isOriented),
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
                Brep brep => ComputeBrepConnectivity(brep: brep),
                Mesh mesh => ComputeMeshConnectivity(mesh: mesh),
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
        Continuity minCont = minimumContinuity ?? Continuity.G1_continuous;
        double angleThresh = angleThreshold ?? context.AngleToleranceRadians;
        return UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<EdgeClassificationData>>>)(g => g switch {
                Brep brep => ComputeBrepEdgeClassification(brep: brep, minContinuity: minCont, angleThreshold: angleThresh),
                Mesh mesh => ComputeMeshEdgeClassification(mesh: mesh, angleThreshold: angleThresh),
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
                    ComputeBrepAdjacency(brep: brep, edgeIndex: edgeIndex),
                Brep brep => ResultFactory.Create<IReadOnlyList<AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(
                        string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {brep.Edges.Count - 1}"))),
                Mesh mesh when edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count =>
                    ComputeMeshAdjacency(mesh: mesh, edgeIndex: edgeIndex),
                Mesh mesh => ResultFactory.Create<IReadOnlyList<AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(
                        string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))),
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

    [Pure]
    private static Result<IReadOnlyList<NakedEdgeData>> ComputeNakedEdges(
        int edgeCount,
        Func<IReadOnlyList<int>> getNakedIndices,
        Func<IReadOnlyList<int>, Curve[]> getCurves,
        Func<IReadOnlyList<int>, double> getLength,
        bool orderLoops) =>
        edgeCount switch {
            0 => ResultFactory.Create(value: (IReadOnlyList<NakedEdgeData>)[
                new NakedEdgeData(
                    EdgeCurves: [],
                    EdgeIndices: [],
                    Valences: [],
                    IsOrdered: orderLoops,
                    TotalEdgeCount: 0,
                    TotalLength: 0.0),
            ]),
            _ => getNakedIndices() switch {
                IReadOnlyList<int> indices => ResultFactory.Create(value: (IReadOnlyList<NakedEdgeData>)[
                    new NakedEdgeData(
                        EdgeCurves: getCurves(indices),
                        EdgeIndices: indices,
                        Valences: [.. indices.Select(_ => 1),],
                        IsOrdered: orderLoops,
                        TotalEdgeCount: edgeCount,
                        TotalLength: getLength(indices)),
                ]),
            },
        };

    [Pure]
    private static Result<IReadOnlyList<BoundaryLoopData>> ComputeBoundaryLoops(IReadOnlyList<Curve> nakedCurves, double joinTolerance) {
        Curve[] joined = nakedCurves.Count > 0
            ? Curve.JoinCurves([.. nakedCurves,], joinTolerance: joinTolerance, preserveDirection: false)
            : [];

        return ResultFactory.Create(value: (IReadOnlyList<BoundaryLoopData>)[
            new BoundaryLoopData(
                Loops: joined,
                EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),],
                LoopLengths: [.. joined.Select(c => c.GetLength()),],
                IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                JoinTolerance: joinTolerance,
                FailedJoins: nakedCurves.Count - joined.Length),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<NonManifoldData>> ComputeNonManifold(
        Func<IReadOnlyList<int>> getNonManifoldEdges,
        Func<IReadOnlyList<int>, IReadOnlyList<int>> getValences,
        Func<IReadOnlyList<int>, IReadOnlyList<Point3d>> getLocations,
        bool isOrientable) =>
        getNonManifoldEdges() switch {
            IReadOnlyList<int> edges => getValences(edges) switch {
                IReadOnlyList<int> valences => ResultFactory.Create(value: (IReadOnlyList<NonManifoldData>)[
                    new NonManifoldData(
                        EdgeIndices: edges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: getLocations(edges),
                        IsManifold: edges.Count == 0,
                        IsOrientable: isOrientable,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0),
                ]),
            },
        };

    [Pure]
    private static Result<IReadOnlyList<ConnectivityData>> ComputeBrepConnectivity(Brep brep) {
        int[] componentIds = new int[brep.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;

        for (int seed = 0; seed < brep.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseBrepComponent(brep: brep, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }

        IReadOnlyList<int>[] components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];

        return ResultFactory.Create(value: (IReadOnlyList<ConnectivityData>)[
            new ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: [.. components.Select(c => c.Aggregate(
                    BoundingBox.Empty,
                    (union, fIdx) => union.IsValid
                        ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false))
                        : brep.Faces[fIdx].GetBoundingBox(accurate: false))),],
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                        .Where(adj => adj != f),]))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseBrepComponent(Brep brep, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new([seed,]);
        componentIds[seed] = componentId;
        while (queue.Count > 0) {
            int faceIdx = queue.Dequeue();
            foreach (int edgeIdx in brep.Faces[faceIdx].AdjacentEdges()) {
                foreach (int adjFace in brep.Edges[edgeIdx].AdjacentFaces().Where(f => componentIds[f] == -1)) {
                    componentIds[adjFace] = componentId;
                    queue.Enqueue(adjFace);
                }
            }
        }
        return componentId;
    }

    [Pure]
    private static Result<IReadOnlyList<ConnectivityData>> ComputeMeshConnectivity(Mesh mesh) {
        int[] componentIds = new int[mesh.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;

        for (int seed = 0; seed < mesh.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseMeshComponent(mesh: mesh, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }

        IReadOnlyList<int>[] components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),];

        return ResultFactory.Create(value: (IReadOnlyList<ConnectivityData>)[
            new ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: [.. components.Select(c => c.Aggregate(
                    BoundingBox.Empty,
                    (union, fIdx) => {
                        MeshFace face = mesh.Faces[fIdx];
                        BoundingBox fBox = face.IsQuad
                            ? new([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],])
                            : new([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],]);
                        return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
                    })),],
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)[.. mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0),]))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseMeshComponent(Mesh mesh, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new([seed,]);
        componentIds[seed] = componentId;
        while (queue.Count > 0) {
            int faceIdx = queue.Dequeue();
            foreach (int adjFace in mesh.Faces.AdjacentFaces(faceIdx).Where(f => f >= 0 && componentIds[f] == -1)) {
                componentIds[adjFace] = componentId;
                queue.Enqueue(adjFace);
            }
        }
        return componentId;
    }

    [Pure]
    private static Result<IReadOnlyList<EdgeClassificationData>> ComputeBrepEdgeClassification(
        Brep brep,
        Continuity minContinuity,
        double angleThreshold) {
        IReadOnlyList<IEdgeClassification> classifications = [.. Enumerable.Range(0, brep.Edges.Count)
            .Select(i => ClassifyBrepEdge(edge: brep.Edges[i], minContinuity: minContinuity, angleThreshold: angleThreshold)),];

        return ResultFactory.Create(value: (IReadOnlyList<EdgeClassificationData>)[
            new EdgeClassificationData(
                Classifications: classifications,
                GroupedByType: classifications
                    .GroupBy(c => c.GetType(), c => c.EdgeIndex)
                    .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]),
                MinimumContinuity: minContinuity),
        ]);
    }

    [Pure]
    private static IEdgeClassification ClassifyBrepEdge(BrepEdge edge, Continuity minContinuity, double angleThreshold) =>
        (edge.Valence, edge.EdgeCurve) switch {
            (EdgeAdjacency.Naked, _) => new BoundaryEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
            (EdgeAdjacency.NonManifold, _) => new NonManifoldEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
            (EdgeAdjacency.Interior, Curve crv) when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) ||
                crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) =>
                new CurvatureEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
            (EdgeAdjacency.Interior, Curve crv) when edge.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) ||
                crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) ||
                crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) =>
                new SmoothEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
            (EdgeAdjacency.Interior, _) when minContinuity < Continuity.G1_continuous =>
                new InteriorEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
            _ => new SharpEdge(EdgeIndex: edge.EdgeIndex, Measure: edge.GetLength()),
        };

    [Pure]
    private static Result<IReadOnlyList<EdgeClassificationData>> ComputeMeshEdgeClassification(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * 0.1;
        IReadOnlyList<IEdgeClassification> classifications = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
            .Select(i => mesh.TopologyEdges.GetConnectedFaces(i) switch {
                int[] faces when faces.Length == 1 => (IEdgeClassification)new BoundaryEdge(
                    EdgeIndex: i,
                    Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
                int[] faces when faces.Length > 2 => new NonManifoldEdge(
                    EdgeIndex: i,
                    Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
                int[] faces when faces.Length == 2 => ComputeDihedralAngle(mesh: mesh, faceIdx1: faces[0], faceIdx2: faces[1]) switch {
                    double angle when Math.Abs(angle) < curvatureThreshold => new CurvatureEdge(
                        EdgeIndex: i,
                        Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
                    double angle when Math.Abs(angle) < angleThreshold => new SmoothEdge(
                        EdgeIndex: i,
                        Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
                    _ => new SharpEdge(
                        EdgeIndex: i,
                        Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
                },
                _ => new SharpEdge(
                    EdgeIndex: i,
                    Measure: ComputeEdgeLength(mesh: mesh, edgeIndex: i)),
            }),];

        return ResultFactory.Create(value: (IReadOnlyList<EdgeClassificationData>)[
            new EdgeClassificationData(
                Classifications: classifications,
                GroupedByType: classifications
                    .GroupBy(c => c.GetType(), c => c.EdgeIndex)
                    .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]),
                MinimumContinuity: Continuity.C0_continuous),
        ]);
    }

    [Pure]
    private static double ComputeEdgeLength(Mesh mesh, int edgeIndex) {
        IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(edgeIndex);
        return mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]);
    }

    [Pure]
    private static double ComputeDihedralAngle(Mesh mesh, int faceIdx1, int faceIdx2) {
        Vector3d n1 = mesh.FaceNormals[faceIdx1];
        Vector3d n2 = mesh.FaceNormals[faceIdx2];
        return n1.IsValid && n2.IsValid ? Vector3d.VectorAngle(n1, n2) : Math.PI;
    }

    [Pure]
    private static Result<IReadOnlyList<AdjacencyData>> ComputeBrepAdjacency(Brep brep, int edgeIndex) {
        BrepEdge edge = brep.Edges[edgeIndex];
        int[] adjFaces = [.. edge.AdjacentFaces(),];
        Vector3d[] normals = [.. adjFaces.Select(i => {
            BrepFace face = brep.Faces[i];
            return face.NormalAt(face.Domain(0).Mid, face.Domain(1).Mid);
        }),];
        double dihedralAngle = normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;

        return ResultFactory.Create(value: (IReadOnlyList<AdjacencyData>)[
            new AdjacencyData(
                EdgeIndex: edgeIndex,
                AdjacentFaceIndices: adjFaces,
                FaceNormals: normals,
                DihedralAngle: dihedralAngle,
                IsManifold: edge.Valence == EdgeAdjacency.Interior,
                IsBoundary: edge.Valence == EdgeAdjacency.Naked),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<AdjacencyData>> ComputeMeshAdjacency(Mesh mesh, int edgeIndex) {
        int[] adjFaces = mesh.TopologyEdges.GetConnectedFaces(edgeIndex);
        Vector3d[] normals = [.. adjFaces.Select(i => (Vector3d)mesh.FaceNormals[i]),];
        double dihedralAngle = normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;

        return ResultFactory.Create(value: (IReadOnlyList<AdjacencyData>)[
            new AdjacencyData(
                EdgeIndex: edgeIndex,
                AdjacentFaceIndices: adjFaces,
                FaceNormals: normals,
                DihedralAngle: dihedralAngle,
                IsManifold: adjFaces.Length == 2,
                IsBoundary: adjFaces.Length == 1),
        ]);
    }
}

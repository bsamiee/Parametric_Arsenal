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

/// <summary>Internal topology computation algorithms with FrozenDictionary-based type dispatch.</summary>
internal static class TopologyCore {
    private static readonly IReadOnlyList<int> EmptyIndices = [];

    [Pure]
    private static Result<TResult> Execute<T, TResult>(T input, IGeometryContext context, TopologyConfig.OpType opType, bool enableDiagnostics, Func<T, Result<IReadOnlyList<TResult>>> operation) where T : notnull =>
        TopologyConfig.OperationMeta.TryGetValue((input.GetType(), opType), out (V ValidationMode, string OpName) meta)
            ? UnifiedOperation.Apply(input: input, operation: operation, config: new OperationConfig<T, TResult> { Context = context, ValidationMode = meta.ValidationMode, OperationName = meta.OpName, EnableDiagnostics = enableDiagnostics }).Map(results => results[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}, Operation: {opType}"));

    [Pure]
    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(T input, IGeometryContext context, bool orderLoops, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NakedEdges, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => brep.Edges.Count switch {
                    0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [], EdgeIndices: [], Valences: [], IsOrdered: orderLoops, TotalEdgeCount: 0, TotalLength: 0.0),]),
                    _ => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).ToArray() switch {
                        int[] nakedIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),], EdgeIndices: [.. nakedIndices,], Valences: [.. Enumerable.Repeat(1, nakedIndices.Length),], IsOrdered: orderLoops, TotalEdgeCount: brep.Edges.Count, TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())),]),
                    },
                },
                Mesh mesh => (mesh.GetNakedEdges() ?? []) switch {
                    Polyline[] nakedPolylines => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),], EdgeIndices: [.. Enumerable.Range(0, nakedPolylines.Length),], Valences: [.. Enumerable.Repeat(1, nakedPolylines.Length),], IsOrdered: orderLoops, TotalEdgeCount: mesh.TopologyEdges.Count, TotalLength: nakedPolylines.Sum(pl => pl.Length)),]),
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: TopologyConfig.OpType.BoundaryLoops, enableDiagnostics: enableDiagnostics,
            operation: g => (g, GetNakedCurves(g)) switch {
                (_, Curve[] naked) when naked.Length == 0 => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0),]),
                (_, Curve[] naked) => Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) switch {
                    Curve[] joined => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [.. joined,], EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),], LoopLengths: [.. joined.Select(c => c.GetLength()),], IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),], JoinTolerance: tol, FailedJoins: naked.Length - joined.Length),]),
                },
            });

        static Curve[] GetNakedCurves(object geometry) => geometry switch {
            Brep brep => [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()),],
            Mesh mesh => [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),],
            _ => [],
        };
    }

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NonManifold, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    int[] nm = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => (int)brep.Edges[i].Valence),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(EdgeIndices: [.. nm,], VertexIndices: [], Valences: vals, Locations: [.. nm.Select(i => brep.Edges[i].PointAtStart),], IsManifold: nm.Length == 0, IsOrientable: brep.IsSolid, MaxValence: vals.Count > 0 ? vals.Max() : 0),]);
                }))(),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    bool manifold = mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _);
                    int[] nm = [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(EdgeIndices: [.. nm,], VertexIndices: [], Valences: vals, Locations: [.. nm.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch { IndexPair v => new Point3d((mesh.TopologyVertices[v.I].X + mesh.TopologyVertices[v.J].X) / 2.0, (mesh.TopologyVertices[v.I].Y + mesh.TopologyVertices[v.J].Y) / 2.0, (mesh.TopologyVertices[v.I].Z + mesh.TopologyVertices[v.J].Z) / 2.0) }),], IsManifold: manifold, IsOrientable: oriented, MaxValence: vals.Count > 0 ? vals.Max() : 0),]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Connectivity, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ComputeConnectivity(_: brep, faceCount: brep.Faces.Count, getAdjacent: fIdx => brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()), getBounds: fIdx => brep.Faces[fIdx].GetBoundingBox(accurate: false), getAdjacentForGraph: fIdx => [.. brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()).Where(adj => adj != fIdx),]),
                Mesh mesh => ComputeConnectivity(_: mesh, faceCount: mesh.Faces.Count, getAdjacent: fIdx => mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0), getBounds: fIdx => mesh.Faces[fIdx] switch { MeshFace face => face.IsQuad ? new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],]) : new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],]) }, getAdjacentForGraph: fIdx => [.. mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0),]),
                _ => ResultFactory.Create<IReadOnlyList<Topology.ConnectivityData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(T input, IGeometryContext context, Continuity? minimumContinuity = null, double? angleThreshold = null, bool enableDiagnostics = false) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.EdgeClassification, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ClassifyBrepEdges(brep: brep, minContinuity: minimumContinuity ?? Continuity.G1_continuous, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                Mesh mesh => ClassifyMeshEdges(mesh: mesh, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(T input, IGeometryContext context, int edgeIndex, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Adjacency, enableDiagnostics: enableDiagnostics,
            operation: g => (g, edgeIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => ((Func<Result<IReadOnlyList<Topology.AdjacencyData>>>)(() => {
                    BrepEdge e = brep.Edges[idx];
                    IReadOnlyList<int> af = [.. e.AdjacentFaces(),];
                    IReadOnlyList<Vector3d> norms = [.. af.Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: af, FaceNormals: norms, DihedralAngle: norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, IsManifold: e.Valence == EdgeAdjacency.Interior, IsBoundary: e.Valence == EdgeAdjacency.Naked),]);
                }))(),
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyEdges.Count => ((Func<Result<IReadOnlyList<Topology.AdjacencyData>>>)(() => {
                    int[] af = mesh.TopologyEdges.GetConnectedFaces(idx);
                    IReadOnlyList<Vector3d> norms = [.. af.Select(i => mesh.FaceNormals[i]),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: [.. af,], FaceNormals: norms, DihedralAngle: norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, IsManifold: af.Length == 2, IsBoundary: af.Length == 1),]);
                }))(),
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ComputeConnectivity<TGeometry>(
        TGeometry _,
        int faceCount,
        Func<int, IEnumerable<int>> getAdjacent,
        Func<int, BoundingBox> getBounds,
        Func<int, IReadOnlyList<int>> getAdjacentForGraph) {
        int[] componentIds = new int[faceCount];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < faceCount; seed++) {
            componentCount = componentIds[seed] != -1 ? componentCount : ((Func<int>)(() => {
                Queue<int> queue = new([seed,]);
                componentIds[seed] = componentCount;
                while (queue.Count > 0) {
                    int faceIdx = queue.Dequeue();
                    foreach (int adjFace in getAdjacent(faceIdx).Where(f => componentIds[f] == -1)) {
                        componentIds[adjFace] = componentCount;
                        queue.Enqueue(adjFace);
                    }
                }
                return componentCount;
            }))() + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, faceCount).Where(f => componentIds[f] == c),]),];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => union.IsValid ? BoundingBox.Union(union, getBounds(fIdx)) : getBounds(fIdx))),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(ComponentIndices: components, ComponentSizes: [.. components.Select(c => c.Count),], ComponentBounds: bounds, TotalComponents: componentCount, IsFullyConnected: componentCount == 1, AdjacencyGraph: Enumerable.Range(0, faceCount).Select(f => (f, getAdjacentForGraph(f))).ToFrozenDictionary(x => x.f, x => x.Item2)),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ClassifyBrepEdges(Brep brep, Continuity minContinuity, double angleThreshold) {
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => brep.Edges[i].Valence switch {
            EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
            EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
            EdgeAdjacency.Interior => brep.Edges[i].EdgeCurve switch {
                Curve crv when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Curvature,
                Curve crv when brep.Edges[i].IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) || crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Smooth,
                _ when minContinuity >= Continuity.G1_continuous => Topology.EdgeContinuityType.Sharp,
                _ => Topology.EdgeContinuityType.Interior,
            },
            _ => Topology.EdgeContinuityType.Sharp,
        }),];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => brep.Edges[i].GetLength()),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(x => x.type, x => x.idx).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: minContinuity),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ClassifyMeshEdges(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * TopologyConfig.CurvatureThresholdRatio;
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetConnectedFaces(i) switch {
            int[] cf when cf.Length == 1 => Topology.EdgeContinuityType.Boundary,
            int[] cf when cf.Length > 2 => Topology.EdgeContinuityType.NonManifold,
            int[] cf when cf.Length == 2 => (mesh.FaceNormals[cf[0]].IsValid && mesh.FaceNormals[cf[1]].IsValid ? Vector3d.VectorAngle(mesh.FaceNormals[cf[0]], mesh.FaceNormals[cf[1]]) : Math.PI) switch {
                double angle when Math.Abs(angle) < curvatureThreshold => Topology.EdgeContinuityType.Curvature,
                double angle when Math.Abs(angle) < angleThreshold => Topology.EdgeContinuityType.Smooth,
                _ => Topology.EdgeContinuityType.Sharp,
            },
            _ => Topology.EdgeContinuityType.Sharp,
        }),];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch { IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]) }),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(x => x.type, x => x.idx).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: Continuity.C0_continuous),]);
    }
}

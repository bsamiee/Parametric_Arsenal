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

/// <summary>Topology execution with FrozenDictionary dispatch and unified result type.</summary>
internal static class TopologyCore {
    private static readonly IReadOnlyList<int> EmptyIndices = [];

    /// <summary>Primary dispatch method using FrozenDictionary registry.</summary>
    [Pure]
    internal static Result<Topology.TopologyResult> Execute<T>(
        T input,
        IGeometryContext context,
        TopologyConfig.OpType opType,
        bool enableDiagnostics,
        dynamic parameters) where T : notnull =>
        TopologyConfig.OperationRegistry.TryGetValue((input.GetType(), opType), out (V ValidationMode, string OpName, Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> Execute) config)
            ? UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<Topology.TopologyResult>>>)(item => ResultFactory.Create(value: (IReadOnlyList<Topology.TopologyResult>)[config.Execute(item!, context, parameters),])),
                config: new OperationConfig<T, Topology.TopologyResult> {
                    Context = context,
                    ValidationMode = config.ValidationMode,
                    OperationName = config.OpName,
                    EnableDiagnostics = enableDiagnostics,
                }).Map(results => results[0])
            : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}, Operation: {opType}"));

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteNakedEdges<T>() where T : GeometryBase =>
        (input, _, parameters) => ((T)input, (bool)parameters.orderLoops) switch {
            (Brep { Edges.Count: 0 } brep, bool ordered) => ResultFactory.Create(value: new Topology.TopologyResult(
                Type: Topology.ResultType.NakedEdges,
                EdgeCurves: [],
                EdgeIndices: [],
                Valences: [],
                IsOrdered: ordered,
                TotalEdgeCount: 0,
                TotalLength: 0.0)),
            (Brep brep, bool ordered) => Enumerable.Range(0, brep.Edges.Count)
                .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                .Select(i => (Index: i, Curve: brep.Edges[i].DuplicateCurve(), Length: brep.Edges[i].GetLength()))
                .ToArray() is (int Index, Curve Curve, double Length)[] edges
                    ? ResultFactory.Create(value: new Topology.TopologyResult(
                        Type: Topology.ResultType.NakedEdges,
                        EdgeCurves: [.. edges.Select(e => e.Curve),],
                        EdgeIndices: [.. edges.Select(e => e.Index),],
                        Valences: [.. edges.Select(_ => 1),],
                        IsOrdered: ordered,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: edges.Sum(e => e.Length)))
                    : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            (Mesh mesh, bool ordered) => Enumerable.Range(0, mesh.TopologyEdges.Count)
                .Select(i => (Index: i, Faces: mesh.TopologyEdges.GetConnectedFaces(i), Vertices: mesh.TopologyEdges.GetTopologyVertices(i)))
                .Where(t => t.Faces.Length == 1)
                .Select(t => (t.Index, Curve: (Curve)new LineCurve(mesh.TopologyVertices[t.Vertices.I], mesh.TopologyVertices[t.Vertices.J]), Length: mesh.TopologyVertices[t.Vertices.I].DistanceTo(mesh.TopologyVertices[t.Vertices.J])))
                .ToArray() is (int Index, Curve Curve, double Length)[] edges
                    ? ResultFactory.Create(value: new Topology.TopologyResult(
                        Type: Topology.ResultType.NakedEdges,
                        EdgeCurves: [.. edges.Select(e => e.Curve),],
                        EdgeIndices: [.. edges.Select(e => e.Index),],
                        Valences: [.. edges.Select(_ => 1),],
                        IsOrdered: ordered,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: edges.Sum(e => e.Length)))
                    : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteBoundaryLoops<T>() where T : GeometryBase =>
        (input, context, parameters) => {
            double tol = (double?)parameters.tolerance ?? context.AbsoluteTolerance;
            Curve[] naked = ((T)input) switch {
                Brep brep => [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()),],
                Mesh mesh => [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),],
                _ => [],
            };
            return naked.Length == 0
                ? ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.BoundaryLoops,
                    Loops: [],
                    EdgeIndicesPerLoop: [],
                    LoopLengths: [],
                    IsClosedPerLoop: [],
                    JoinTolerance: tol,
                    FailedJoins: 0))
                : Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) is Curve[] joined
                    ? ResultFactory.Create(value: new Topology.TopologyResult(
                        Type: Topology.ResultType.BoundaryLoops,
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: naked.Length - joined.Length))
                    : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis);
        };

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteNonManifold<T>() where T : GeometryBase =>
        (input, _, __) => ((T)input) switch {
            Brep brep => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold).ToArray() is int[] nm
                ? ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.NonManifold,
                    EdgeIndices: nm,
                    VertexIndices: [],
                    Valences: [.. nm.Select(i => (int)brep.Edges[i].Valence),],
                    Locations: [.. nm.Select(i => brep.Edges[i].PointAtStart),],
                    IsManifold: nm.Length == 0,
                    IsOrientable: brep.IsSolid,
                    MaxValence: nm.Length > 0 ? nm.Max(i => (int)brep.Edges[i].Valence) : 0))
                : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            Mesh mesh => Enumerable.Range(0, mesh.TopologyEdges.Count)
                .Select(i => (Index: i, Faces: mesh.TopologyEdges.GetConnectedFaces(i), Vertices: mesh.TopologyEdges.GetTopologyVertices(i)))
                .Where(t => t.Faces.Length > 2)
                .ToArray() is (int Index, int[] Faces, IndexPair Vertices)[] nm && mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _) is bool isManifold
                    ? ResultFactory.Create(value: new Topology.TopologyResult(
                        Type: Topology.ResultType.NonManifold,
                        EdgeIndices: [.. nm.Select(t => t.Index),],
                        VertexIndices: [],
                        Valences: [.. nm.Select(t => t.Faces.Length),],
                        Locations: [.. nm.Select(t => new Point3d((mesh.TopologyVertices[t.Vertices.I].X + mesh.TopologyVertices[t.Vertices.J].X) / 2.0, (mesh.TopologyVertices[t.Vertices.I].Y + mesh.TopologyVertices[t.Vertices.J].Y) / 2.0, (mesh.TopologyVertices[t.Vertices.I].Z + mesh.TopologyVertices[t.Vertices.J].Z) / 2.0)),],
                        IsManifold: isManifold && nm.Length == 0,
                        IsOrientable: oriented,
                        MaxValence: nm.Length > 0 ? nm.Max(t => t.Faces.Length) : 0))
                    : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteConnectivity<T>() where T : GeometryBase =>
        (input, _, __) => ((T)input) switch {
            Brep brep => ComputeConnectivity(
                faceCount: brep.Faces.Count,
                getAdjacent: fIdx => brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()),
                getBounds: fIdx => brep.Faces[fIdx].GetBoundingBox(accurate: false),
                getAdjacentForGraph: fIdx => [.. brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()).Where(adj => adj != fIdx),]),
            Mesh mesh => ComputeConnectivity(
                faceCount: mesh.Faces.Count,
                getAdjacent: fIdx => mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0),
                getBounds: fIdx => mesh.Faces[fIdx] is MeshFace face && face.IsQuad
                    ? new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],])
                    : new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],]),
                getAdjacentForGraph: fIdx => [.. mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0),]),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    private static Result<Topology.TopologyResult> ComputeConnectivity(
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
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => {
            BoundingBox fBox = getBounds(fIdx);
            return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
        })),
        ];
        return ResultFactory.Create(value: new Topology.TopologyResult(
            Type: Topology.ResultType.Connectivity,
            ComponentIndices: components,
            ComponentSizes: [.. components.Select(c => c.Count),],
            ComponentBounds: bounds,
            TotalComponents: componentCount,
            IsFullyConnected: componentCount == 1,
            AdjacencyGraph: Enumerable.Range(0, faceCount).ToFrozenDictionary(keySelector: i => i, elementSelector: getAdjacentForGraph)));
    }

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteEdgeClassification<T>() where T : GeometryBase =>
        (input, context, parameters) => ((T)input) switch {
            Brep brep => ClassifyBrepEdges(
                brep: brep,
                minContinuity: (Continuity?)parameters.minimumContinuity ?? Continuity.G1_continuous,
                angleThreshold: (double?)parameters.angleThreshold ?? context.AngleToleranceRadians),
            Mesh mesh => ClassifyMeshEdges(
                mesh: mesh,
                angleThreshold: (double?)parameters.angleThreshold ?? context.AngleToleranceRadians),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    private static Result<Topology.TopologyResult> ClassifyBrepEdges(Brep brep, Continuity minContinuity, double angleThreshold) {
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
        }),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => brep.Edges[i].GetLength()),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(x => x.type, x => x.idx).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: new Topology.TopologyResult(
            Type: Topology.ResultType.EdgeClassification,
            EdgeIndices: edgeIndices,
            Classifications: classifications,
            ContinuityMeasures: measures,
            GroupedByType: grouped,
            MinimumContinuity: minContinuity));
    }

    [Pure]
    private static Result<Topology.TopologyResult> ClassifyMeshEdges(Mesh mesh, double angleThreshold) {
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
        }),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) is IndexPair verts ? mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]) : 0.0),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(x => x.type, x => x.idx).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: new Topology.TopologyResult(
            Type: Topology.ResultType.EdgeClassification,
            EdgeIndices: edgeIndices,
            Classifications: classifications,
            ContinuityMeasures: measures,
            GroupedByType: grouped,
            MinimumContinuity: Continuity.C0_continuous));
    }

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteAdjacency<T>() where T : GeometryBase =>
        (input, _, parameters) => (((T)input), (int)parameters.edgeIndex) switch {
            (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => (brep.Edges[idx], brep.Edges[idx].AdjacentFaces().ToArray()) switch {
                (BrepEdge e, int[] af) when af.Length == 2 => ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.Adjacency,
                    EdgeIndex: idx,
                    AdjacentFaceIndices: af,
                    FaceNormals: [brep.Faces[af[0]].NormalAt(brep.Faces[af[0]].Domain(0).Mid, brep.Faces[af[0]].Domain(1).Mid), brep.Faces[af[1]].NormalAt(brep.Faces[af[1]].Domain(0).Mid, brep.Faces[af[1]].Domain(1).Mid),],
                    DihedralAngle: Vector3d.VectorAngle(brep.Faces[af[0]].NormalAt(brep.Faces[af[0]].Domain(0).Mid, brep.Faces[af[0]].Domain(1).Mid), brep.Faces[af[1]].NormalAt(brep.Faces[af[1]].Domain(0).Mid, brep.Faces[af[1]].Domain(1).Mid)),
                    IsManifold: e.Valence == EdgeAdjacency.Interior,
                    IsBoundary: e.Valence == EdgeAdjacency.Naked)),
                (BrepEdge e, int[] af) => ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.Adjacency,
                    EdgeIndex: idx,
                    AdjacentFaceIndices: af,
                    FaceNormals: [.. af.Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)),],
                    DihedralAngle: 0.0,
                    IsManifold: e.Valence == EdgeAdjacency.Interior,
                    IsBoundary: e.Valence == EdgeAdjacency.Naked)),
            },
            (Brep brep, int idx) => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
            (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyEdges.Count => mesh.TopologyEdges.GetConnectedFaces(idx) switch {
                int[] { Length: 2 } af => ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.Adjacency,
                    EdgeIndex: idx,
                    AdjacentFaceIndices: af,
                    FaceNormals: [mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]],],
                    DihedralAngle: Vector3d.VectorAngle(mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]]),
                    IsManifold: true,
                    IsBoundary: false)),
                int[] af => ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.Adjacency,
                    EdgeIndex: idx,
                    AdjacentFaceIndices: af,
                    FaceNormals: [.. af.Select(i => mesh.FaceNormals[i]),],
                    DihedralAngle: 0.0,
                    IsManifold: af.Length == 2,
                    IsBoundary: af.Length == 1)),
            },
            (Mesh mesh, int idx) => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteVertexData<T>() where T : GeometryBase =>
        (input, _, parameters) => (((T)input), (int)parameters.vertexIndex) switch {
            (Brep brep, int idx) when idx >= 0 && idx < brep.Vertices.Count => brep.Vertices[idx].EdgeIndices().ToArray() is int[] edgeIndices
                ? ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.VertexData,
                    VertexIndex: idx,
                    Location: brep.Vertices[idx].Location,
                    ConnectedEdgeIndices: edgeIndices,
                    ConnectedFaceIndices: [],
                    Valence: edgeIndices.Length,
                    IsBoundary: edgeIndices.Any(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),
                    IsManifold: edgeIndices.All(i => brep.Edges[i].Valence == EdgeAdjacency.Interior)))
                : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            (Brep brep, int idx) => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Vertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
            (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyVertices.Count => (new Point3d(mesh.TopologyVertices[idx]), mesh.TopologyVertices.ConnectedFaces(idx).ToArray(), mesh.TopologyVertices.ConnectedTopologyVertices(idx).ToArray(), Enumerable.Range(0, mesh.TopologyEdges.Count).Where(e => mesh.TopologyEdges.GetTopologyVertices(e) is IndexPair verts && (verts.I == idx || verts.J == idx)).ToArray()) is (Point3d location, int[] connectedFaces, int[] connectedVerts, int[] connectedEdges)
                ? ResultFactory.Create(value: new Topology.TopologyResult(
                    Type: Topology.ResultType.VertexData,
                    VertexIndex: idx,
                    Location: location,
                    ConnectedEdgeIndices: connectedEdges,
                    ConnectedFaceIndices: connectedFaces,
                    Valence: connectedVerts.Length,
                    IsBoundary: connectedEdges.Any(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 1),
                    IsManifold: connectedEdges.All(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 2)))
                : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            (Mesh mesh, int idx) => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyVertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };

    [Pure]
    internal static Func<object, IGeometryContext, dynamic, Result<Topology.TopologyResult>> ExecuteNgonTopology<T>() where T : GeometryBase =>
        (input, _, __) => ((T)input) switch {
            Mesh mesh => Enumerable.Range(0, mesh.Ngons.Count)
                .Select(i => (
                    b: (IReadOnlyList<int>)[.. mesh.Ngons.GetNgonBoundary([i,]) ?? [],],
                    f: (IReadOnlyList<int>)[.. Array.ConvertAll(mesh.Ngons[i].FaceIndexList() ?? [], x => unchecked((int)x)),],
                    c: mesh.Ngons.GetNgonCenter(i) is Point3d pt && pt.IsValid ? pt : Point3d.Origin))
                .ToArray() is (IReadOnlyList<int> b, IReadOnlyList<int> f, Point3d c)[] data
                    ? data.Length == 0
                        ? ResultFactory.Create(value: new Topology.TopologyResult(
                            Type: Topology.ResultType.NgonTopology,
                            NgonIndices: [],
                            FaceIndicesPerNgon: [],
                            BoundaryEdgesPerNgon: [],
                            NgonCenters: [],
                            EdgeCountPerNgon: [],
                            TotalNgons: 0,
                            TotalFaces: mesh.Faces.Count))
                        : ResultFactory.Create(value: new Topology.TopologyResult(
                            Type: Topology.ResultType.NgonTopology,
                            NgonIndices: [.. Enumerable.Range(0, data.Length),],
                            FaceIndicesPerNgon: [.. data.Select(d => d.f),],
                            BoundaryEdgesPerNgon: [.. data.Select(d => d.b),],
                            NgonCenters: [.. data.Select(d => d.c),],
                            EdgeCountPerNgon: [.. data.Select(d => d.b.Count),],
                            TotalNgons: data.Length,
                            TotalFaces: mesh.Faces.Count))
                    : ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
        };
}

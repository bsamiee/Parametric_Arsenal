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

/// <summary>Topology analysis execution with BFS connectivity, edge classification, and adjacency queries.</summary>
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
                Brep brep => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).ToArray() switch {
                    int[] nakedIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),], EdgeIndices: [.. nakedIndices,], Valences: [.. Enumerable.Repeat(1, nakedIndices.Length),], IsOrdered: orderLoops, TotalEdgeCount: brep.Edges.Count, TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())),]),
                },
                Mesh mesh => Enumerable.Range(0, mesh.TopologyEdges.Count).Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length == 1).ToArray() switch {
                    int[] nakedIndices => nakedIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch {
                        IndexPair verts => ((Point3d)mesh.TopologyVertices[verts.I], (Point3d)mesh.TopologyVertices[verts.J]),
                    }).ToArray() switch {
                        (Point3d, Point3d)[] pts => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. pts.Select(p => (Curve)new LineCurve(p.Item1, p.Item2)),], EdgeIndices: [.. nakedIndices,], Valences: [.. Enumerable.Repeat(1, nakedIndices.Length),], IsOrdered: orderLoops, TotalEdgeCount: mesh.TopologyEdges.Count, TotalLength: pts.Sum(p => p.Item1.DistanceTo(p.Item2))),]),
                    },
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: TopologyConfig.OpType.BoundaryLoops, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()).ToArray() switch {
                    Curve[] naked when naked.Length == 0 => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0),]),
                    Curve[] naked => Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) switch {
                        Curve[] joined => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [.. joined,], EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),], LoopLengths: [.. joined.Select(c => c.GetLength()),], IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),], JoinTolerance: tol, FailedJoins: naked.Length - joined.Length),]),
                    },
                },
                Mesh mesh => ((mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()).ToArray()) switch {
                    Curve[] naked when naked.Length == 0 => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0),]),
                    Curve[] naked => Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) switch {
                        Curve[] joined => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [.. joined,], EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),], LoopLengths: [.. joined.Select(c => c.GetLength()),], IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),], JoinTolerance: tol, FailedJoins: naked.Length - joined.Length),]),
                    },
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.BoundaryLoopData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });
    }

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NonManifold, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold).ToArray() switch {
                    int[] nm => nm.Select(i => (int)brep.Edges[i].Valence).ToArray() switch {
                        int[] vals => ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(EdgeIndices: nm, VertexIndices: [], Valences: [.. vals,], Locations: [.. nm.Select(i => brep.Edges[i].PointAtStart),], IsManifold: nm.Length == 0, IsOrientable: brep.IsSolid, MaxValence: vals.Length > 0 ? vals.Max() : 0),]),
                    },
                },
                Mesh mesh => (mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _), Enumerable.Range(0, mesh.TopologyEdges.Count).Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2).ToArray()) switch {
                    (bool manifold, int[] nm) => nm.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length).ToArray() switch {
                        int[] vals => ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(EdgeIndices: nm, VertexIndices: [], Valences: [.. vals,], Locations: [.. nm.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch { IndexPair v => new Point3d((mesh.TopologyVertices[v.I].X + mesh.TopologyVertices[v.J].X) * 0.5, (mesh.TopologyVertices[v.I].Y + mesh.TopologyVertices[v.J].Y) * 0.5, (mesh.TopologyVertices[v.I].Z + mesh.TopologyVertices[v.J].Z) * 0.5) }),], IsManifold: manifold, IsOrientable: oriented, MaxValence: vals.Length > 0 ? vals.Max() : 0),]),
                    },
                },
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
                (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => (brep.Edges[idx].AdjacentFaces().ToArray(), brep.Edges[idx].Valence) switch {
                    (int[] af, EdgeAdjacency val) => af.Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)).ToArray() switch {
                        Vector3d[] norms => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: [.. af,], FaceNormals: [.. norms,], DihedralAngle: norms.Length == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, IsManifold: val == EdgeAdjacency.Interior, IsBoundary: val == EdgeAdjacency.Naked),]),
                    },
                },
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyEdges.Count => mesh.TopologyEdges.GetConnectedFaces(idx) switch {
                    int[] af => af.Select(i => (Vector3d)mesh.FaceNormals[i]).ToArray() switch {
                        Vector3d[] norms => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: [.. af,], FaceNormals: [.. norms,], DihedralAngle: norms.Length == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, IsManifold: af.Length == 2, IsBoundary: af.Length == 1),]),
                    },
                },
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
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => {
            BoundingBox fBox = getBounds(fIdx);
            return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
        })),
        ];
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
        }),
        ];
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
        }),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch { IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]) }),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(x => x.type, x => x.idx).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: Continuity.C0_continuous),]);
    }

    [Pure]
    internal static Result<Topology.VertexData> ExecuteVertexData<T>(T input, IGeometryContext context, int vertexIndex, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.VertexData, enableDiagnostics: enableDiagnostics,
            operation: g => (g, vertexIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Vertices.Count => brep.Vertices[idx].EdgeIndices().ToArray() switch {
                    int[] edgeIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[new Topology.VertexData(VertexIndex: idx, Location: brep.Vertices[idx].Location, ConnectedEdgeIndices: [.. edgeIndices,], ConnectedFaceIndices: [], Valence: edgeIndices.Length, IsBoundary: edgeIndices.Any(i => brep.Edges[i].Valence == EdgeAdjacency.Naked), IsManifold: edgeIndices.All(i => brep.Edges[i].Valence == EdgeAdjacency.Interior)),]),
                },
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Vertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyVertices.Count => Enumerable.Range(0, mesh.TopologyEdges.Count).Where(e => mesh.TopologyEdges.GetTopologyVertices(e) switch { IndexPair verts => verts.I == idx || verts.J == idx }).ToArray() switch {
                    int[] connectedEdges => ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[new Topology.VertexData(VertexIndex: idx, Location: mesh.TopologyVertices[idx], ConnectedEdgeIndices: [.. connectedEdges,], ConnectedFaceIndices: [.. mesh.TopologyVertices.ConnectedFaces(idx),], Valence: mesh.TopologyVertices.ConnectedTopologyVertices(idx).Length, IsBoundary: connectedEdges.Any(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 1), IsManifold: connectedEdges.All(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 2)),]),
                },
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyVertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.NgonTopologyData> ExecuteNgonTopology<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NgonTopology, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Mesh mesh when mesh.Ngons.Count == 0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData(NgonIndices: [], FaceIndicesPerNgon: [], BoundaryEdgesPerNgon: [], NgonCenters: [], EdgeCountPerNgon: [], TotalNgons: 0, TotalFaces: mesh.Faces.Count),]),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NgonTopologyData>>>)(() => {
                    IReadOnlyList<int> ngonIndices = [.. Enumerable.Range(0, mesh.Ngons.Count),];
#pragma warning disable IDE0004 // Cast is necessary for type safety
                    IReadOnlyList<IReadOnlyList<int>> faceIndices = [.. ngonIndices.Select(i => {
                        MeshNgon ngon = mesh.Ngons[i];
                        return (IReadOnlyList<int>)[.. Enumerable.Range(0, (int)ngon.FaceCount).Select(f => (int)ngon.FaceIndexList()[f]),];
                    }),];
#pragma warning restore IDE0004
                    IReadOnlyList<IReadOnlyList<int>> boundaryEdges = [.. ngonIndices.Select(i => (IReadOnlyList<int>)[.. mesh.Ngons.GetNgonBoundary([i]) ?? [],]),];
                    IReadOnlyList<Point3d> centers = [.. ngonIndices.Select(i => mesh.Ngons.GetNgonCenter(i) switch { Point3d pt when pt.IsValid => pt, _ => Point3d.Origin }),];
                    IReadOnlyList<int> edgeCounts = [.. boundaryEdges.Select(static edges => edges.Count),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData(NgonIndices: ngonIndices, FaceIndicesPerNgon: faceIndices, BoundaryEdgesPerNgon: boundaryEdges, NgonCenters: centers, EdgeCountPerNgon: edgeCounts, TotalNgons: mesh.Ngons.Count, TotalFaces: mesh.Faces.Count),]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NgonTopologyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });
}

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

/// <summary>Topology execution via BFS connectivity and edge classification.</summary>
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
                Brep { Edges.Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [], EdgeIndices: [], Valences: [], IsOrdered: orderLoops, TotalEdgeCount: 0, TotalLength: 0.0),]),
                Brep brep => Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                    .Select(i => (Index: i, Curve: brep.Edges[i].DuplicateCurve(), Length: brep.Edges[i].GetLength()))
                    .ToArray() switch {
                        (int Index, Curve Curve, double Length)[] edges => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. edges.Select(e => e.Curve),], EdgeIndices: [.. edges.Select(e => e.Index),], Valences: [.. edges.Select(_ => 1),], IsOrdered: orderLoops, TotalEdgeCount: brep.Edges.Count, TotalLength: edges.Sum(e => e.Length)),]),
                    },
                Mesh mesh => Enumerable.Range(0, mesh.TopologyEdges.Count)
                    .Select(i => (Index: i, Faces: mesh.TopologyEdges.GetConnectedFaces(i), Vertices: mesh.TopologyEdges.GetTopologyVertices(i)))
                    .Where(t => t.Faces.Length == 1)
                    // LineCurve ownership transfers to NakedEdgeData; consumer must dispose
                    .Select(t => (t.Index, Curve: (Curve)new LineCurve(mesh.TopologyVertices[t.Vertices.I], mesh.TopologyVertices[t.Vertices.J]), Length: mesh.TopologyVertices[t.Vertices.I].DistanceTo(mesh.TopologyVertices[t.Vertices.J])))
                    .ToArray() switch {
                        (int Index, Curve Curve, double Length)[] edges => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [.. edges.Select(e => e.Curve),], EdgeIndices: [.. edges.Select(e => e.Index),], Valences: [.. edges.Select(_ => 1),], IsOrdered: orderLoops, TotalEdgeCount: mesh.TopologyEdges.Count, TotalLength: edges.Sum(e => e.Length)),]),
                    },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: TopologyConfig.OpType.BoundaryLoops, enableDiagnostics: enableDiagnostics,
            operation: g => GetNakedCurves(geometry: g) switch {
                [] => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0),]),
                Curve[] naked => Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) switch {
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
                Brep brep => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold).ToArray() switch {
                    int[] nm => ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(EdgeIndices: nm, VertexIndices: [], Valences: [.. nm.Select(i => (int)brep.Edges[i].Valence),], Locations: [.. nm.Select(i => brep.Edges[i].PointAtStart),], IsManifold: nm.Length == 0, IsOrientable: brep.IsSolid, MaxValence: nm.Length > 0 ? nm.Max(i => (int)brep.Edges[i].Valence) : 0),]),
                },
                Mesh mesh => Enumerable.Range(0, mesh.TopologyEdges.Count)
                    .Select(i => (Index: i, Faces: mesh.TopologyEdges.GetConnectedFaces(i), Vertices: mesh.TopologyEdges.GetTopologyVertices(i)))
                    .Where(t => t.Faces.Length > 2)
                    .ToArray() switch {
                        (int Index, int[] Faces, IndexPair Vertices)[] nm =>
                            mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _) is bool isManifold
                                ? ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                                    new Topology.NonManifoldData(
                                        EdgeIndices: [.. nm.Select(t => t.Index),],
                                        VertexIndices: [],
                                        Valences: [.. nm.Select(t => t.Faces.Length),],
                                        Locations: [.. nm.Select(t => new Point3d(
                                            (mesh.TopologyVertices[t.Vertices.I].X + mesh.TopologyVertices[t.Vertices.J].X) / 2.0,
                                            (mesh.TopologyVertices[t.Vertices.I].Y + mesh.TopologyVertices[t.Vertices.J].Y) / 2.0,
                                            (mesh.TopologyVertices[t.Vertices.I].Z + mesh.TopologyVertices[t.Vertices.J].Z) / 2.0)),
                                        ],
                                        IsManifold: isManifold && nm.Length == 0,
                                        IsOrientable: oriented,
                                        MaxValence: nm.Length > 0 ? nm.Max(t => t.Faces.Length) : 0),
                                ])
                                : ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
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
                (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => (brep.Edges[idx], brep.Edges[idx].AdjacentFaces().ToArray()) switch {
                    (BrepEdge e, int[] af) when af.Length == 2 => ((Func<Point3d, Result<IReadOnlyList<Topology.AdjacencyData>>>)(edgeMid => {
                        Vector3d n0 = brep.Faces[af[0]].ClosestPoint(edgeMid, out double u0, out double v0) ? brep.Faces[af[0]].NormalAt(u0, v0) : Vector3d.Unset;
                        Vector3d n1 = brep.Faces[af[1]].ClosestPoint(edgeMid, out double u1, out double v1) ? brep.Faces[af[1]].NormalAt(u1, v1) : Vector3d.Unset;
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(
                            EdgeIndex: idx,
                            AdjacentFaceIndices: af,
                            FaceNormals: [n0, n1,],
                            DihedralAngle: n0.IsValid && n1.IsValid ? Vector3d.VectorAngle(n0, n1) : 0.0,
                            IsManifold: e.Valence == EdgeAdjacency.Interior,
                            IsBoundary: e.Valence == EdgeAdjacency.Naked),
                        ]);
                    }))(e.PointAt(e.Domain.Mid)),
                    (BrepEdge e, int[] af) => ((Func<Point3d, Result<IReadOnlyList<Topology.AdjacencyData>>>)(edgeMid =>
                        ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(
                            EdgeIndex: idx,
                            AdjacentFaceIndices: af,
                            FaceNormals: [.. af.Select(i => brep.Faces[i].ClosestPoint(edgeMid, out double u, out double v) ? brep.Faces[i].NormalAt(u, v) : Vector3d.Unset),],
                            DihedralAngle: 0.0,
                            IsManifold: e.Valence == EdgeAdjacency.Interior,
                            IsBoundary: e.Valence == EdgeAdjacency.Naked),
                        ])
                    ))(e.PointAt(e.Domain.Mid)),
                },
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyEdges.Count => mesh.TopologyEdges.GetConnectedFaces(idx) switch {
                    int[] { Length: 2 } af => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: af, FaceNormals: [mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]],], DihedralAngle: Vector3d.VectorAngle(mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]]), IsManifold: true, IsBoundary: false),]),
                    int[] af => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(EdgeIndex: idx, AdjacentFaceIndices: af, FaceNormals: [.. af.Select(i => mesh.FaceNormals[i]),], DihedralAngle: 0.0, IsManifold: af.Length == 2, IsBoundary: af.Length == 1),]),
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
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(ComponentIndices: components, ComponentSizes: [.. components.Select(c => c.Count),], ComponentBounds: bounds, TotalComponents: componentCount, IsFullyConnected: componentCount == 1, AdjacencyGraph: Enumerable.Range(0, faceCount).ToFrozenDictionary(keySelector: i => i, elementSelector: getAdjacentForGraph)),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ClassifyBrepEdges(Brep brep, Continuity minContinuity, double angleThreshold) {
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => brep.Edges[i].Valence switch {
            EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
            EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
            EdgeAdjacency.Interior => brep.Edges[i] switch {
                BrepEdge e when e.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) && e.EdgeCurve is Curve crv && (crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid)) => Topology.EdgeContinuityType.Curvature,
                BrepEdge e when e.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) && minContinuity < Continuity.G2_continuous => Topology.EdgeContinuityType.Smooth,
                BrepEdge e when e.EdgeCurve is Curve crv && (crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid)) && minContinuity < Continuity.G2_continuous => Topology.EdgeContinuityType.Smooth,
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
                    int[] edgeIndices => ((Func<Result<IReadOnlyList<Topology.VertexData>>>)(() => {
                        IReadOnlyList<int> faceIndices = (IReadOnlyList<int>)[.. new HashSet<int>(
                            edgeIndices
                                .SelectMany(edgeIdx => brep.Edges[edgeIdx].AdjacentFaces())
                                .Where(faceIdx => faceIdx >= 0)
                        ),];
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[new Topology.VertexData(VertexIndex: idx, Location: brep.Vertices[idx].Location, ConnectedEdgeIndices: edgeIndices, ConnectedFaceIndices: faceIndices, Valence: edgeIndices.Length, IsBoundary: edgeIndices.Any(i => brep.Edges[i].Valence == EdgeAdjacency.Naked), IsManifold: edgeIndices.All(i => brep.Edges[i].Valence == EdgeAdjacency.Interior)),]);
                    }))(),
                },
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Vertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyVertices.Count => (new Point3d(mesh.TopologyVertices[idx]), mesh.TopologyVertices.ConnectedFaces(idx).ToArray(), mesh.TopologyVertices.ConnectedTopologyVertices(idx).ToArray(), Enumerable.Range(0, mesh.TopologyEdges.Count).Where(e => mesh.TopologyEdges.GetTopologyVertices(e) switch { IndexPair verts => verts.I == idx || verts.J == idx }).ToArray()) switch {
                    (Point3d location, int[] connectedFaces, int[] connectedVerts, int[] connectedEdges) => ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[new Topology.VertexData(VertexIndex: idx, Location: location, ConnectedEdgeIndices: connectedEdges, ConnectedFaceIndices: connectedFaces, Valence: connectedVerts.Length, IsBoundary: connectedEdges.Any(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 1), IsManifold: connectedEdges.All(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 2)),]),
                },
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyVertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.NgonTopologyData> ExecuteNgonTopology<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NgonTopology, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Mesh mesh => ((Func<(IReadOnlyList<int> Boundaries, IReadOnlyList<int> Faces, Point3d Center, int EdgeCount)[], Result<IReadOnlyList<Topology.NgonTopologyData>>>)(
                    data => data.Length == 0
                        ? ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData([], [], [], [], [], 0, mesh.Faces.Count),])
                        : ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData(
                            NgonIndices: [.. Enumerable.Range(0, data.Length),],
                            FaceIndicesPerNgon: [.. data.Select(d => d.Faces),],
                            BoundaryEdgesPerNgon: [.. data.Select(d => d.Boundaries),],
                            NgonCenters: [.. data.Select(d => d.Center),],
                            EdgeCountPerNgon: [.. data.Select(d => d.EdgeCount),],
                            TotalNgons: data.Length,
                            TotalFaces: mesh.Faces.Count),
                        ])))([.. Enumerable.Range(0, mesh.Ngons.Count).Select(index => ((Func<(IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)>)(() => {
                        using Ngon? ngon = mesh.Ngons.GetNgon(index);
                        uint[]? faceList = ngon?.FaceIndexList();
                        int[]? boundaryEdges = ngon?.BoundaryEdgeIndexList();
                        Point3d center = mesh.Ngons.GetNgonCenter(index);
                        IReadOnlyList<int> faces = (IReadOnlyList<int>)[.. (faceList ?? []).Select(face => unchecked((int)face)),];
                        IReadOnlyList<int> boundaries = (IReadOnlyList<int>)[.. (boundaryEdges ?? []).Select(edge => unchecked((int)edge)),];
                        return (
                            Boundaries: boundaries,
                            Faces: faces,
                            Center: center.IsValid ? center : Point3d.Origin,
                            EdgeCount: boundaries.Count);
                    }))() ),
                        ]),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NgonTopologyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });
}

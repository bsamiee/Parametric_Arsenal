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

/// <summary>Topology execution via breadth-first search and edge classification algorithms.</summary>
[Pure]
internal static class TopologyCore {
    private static Result<TResult> Execute<T, TResult>(T input, IGeometryContext context, TopologyConfig.OpType opType, Func<T, Result<IReadOnlyList<TResult>>> operation) where T : notnull =>
        TopologyConfig.OperationMeta.TryGetValue((input.GetType(), opType), out (V ValidationMode, string OpName) meta)
            ? UnifiedOperation.Apply(input: input, operation: operation, config: new OperationConfig<T, TResult> { Context = context, ValidationMode = meta.ValidationMode, OperationName = meta.OpName, EnableDiagnostics = false }).Map(results => results[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}, Operation: {opType}"));

    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(T input, IGeometryContext context, bool orderLoops) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NakedEdges,
            operation: g => g switch {
                Brep { Edges.Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData(EdgeCurves: [], EdgeIndices: [], Valences: [], IsOrdered: orderLoops, TotalEdgeCount: 0, TotalLength: 0.0),]),
                Brep brep => brep.DuplicateNakedEdgeCurves(nakedOuter: true, nakedInner: true) switch {
                    null => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(EdgeCurves: [], EdgeIndices: [], Valences: [], IsOrdered: orderLoops, TotalEdgeCount: brep.Edges.Count, TotalLength: 0.0),
                    ]),
                    Curve[] nakedCurves => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: nakedCurves,
                            EdgeIndices: [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),],
                            Valences: [.. nakedCurves.Select(static _ => 1),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: brep.Edges.Count,
                            TotalLength: nakedCurves.Sum(static c => c.GetLength())),
                    ]),
                },
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NakedEdgeData>>>)(() => {
                    (int Index, Curve Curve, double Length)[] edges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length == 1)
                        .Select(i => {
                            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
                            Point3d p1 = mesh.TopologyVertices[verts.I];
                            Point3d p2 = mesh.TopologyVertices[verts.J];
                            return (i, new LineCurve(p1, p2), p1.DistanceTo(p2));
                        }),
                    ];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: [.. edges.Select(e => e.Curve),],
                            EdgeIndices: [.. edges.Select(e => e.Index),],
                            Valences: [.. edges.Select(_ => 1),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: edges.Sum(e => e.Length)),
                    ]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: TopologyConfig.OpType.BoundaryLoops,
            operation: g => ((Curve[])((object)g switch {
                Brep brep => [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()),],
                Mesh mesh => [.. (mesh.GetNakedEdges() is Polyline[] naked ? naked : []).Select(pl => pl.ToNurbsCurve()),],
                _ => [],
            })) switch {
                [] => ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0),]),
                Curve[] nakedCurves => ((Func<Result<IReadOnlyList<Topology.BoundaryLoopData>>>)(() => {
                    try {
                        Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false) is Curve[] j ? j : [];
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData(
                            Loops: [.. joined,],
                            EdgeIndicesPerLoop: [.. joined.Select(static _ => (IReadOnlyList<int>)[]),],
                            LoopLengths: [.. joined.Select(static c => c.GetLength()),],
                            IsClosedPerLoop: [.. joined.Select(static c => c.IsClosed),],
                            JoinTolerance: tol,
                            FailedJoins: nakedCurves.Length - joined.Length),
                        ]);
                    } finally {
                        foreach (Curve sourceCurve in nakedCurves) {
                            sourceCurve.Dispose();
                        }
                    }
                }))(),
            });
    }

    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NonManifold,
            operation: g => g switch {
                Brep brep => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    int[] nmEdges = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),
                    ];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                        new Topology.NonManifoldData(
                            EdgeIndices: nmEdges,
                            VertexIndices: [],
                            Valences: [.. nmEdges.Select(i => (int)brep.Edges[i].Valence),],
                            Locations: [.. nmEdges.Select(i => brep.Edges[i].PointAtStart),],
                            IsManifold: nmEdges.Length == 0,
                            IsOrientable: brep.IsSolid,
                            MaxValence: nmEdges.Length > 0 ? nmEdges.Max(i => (int)brep.Edges[i].Valence) : 0),
                    ]);
                }))(),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    (int Index, int FaceCount, Point3d Location)[] nmEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Select(i => (Faces: mesh.TopologyEdges.GetConnectedFaces(i), Index: i))
                        .Where(t => t.Faces.Length > 2)
                        .Select(t => {
                            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(t.Index);
                            return (t.Index, t.Faces.Length, Point3d.Add(mesh.TopologyVertices[verts.I], mesh.TopologyVertices[verts.J]) / 2.0);
                        }),
                    ];
                    return mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _) is bool isManifold
                        ? ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                            new Topology.NonManifoldData(
                                EdgeIndices: [.. nmEdges.Select(t => t.Index),],
                                VertexIndices: [],
                                Valences: [.. nmEdges.Select(t => t.FaceCount),],
                                Locations: [.. nmEdges.Select(t => t.Location),],
                                IsManifold: isManifold && nmEdges.Length == 0,
                                IsOrientable: oriented,
                                MaxValence: nmEdges.Length > 0 ? nmEdges.Max(t => t.FaceCount) : 0),
                        ])
                        : ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}"));
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Connectivity,
            operation: g => g switch {
                Brep brep => ComputeConnectivity(
                    _: brep,
                    faceCount: brep.Faces.Count,
                    getAdjacent: fIdx => brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()),
                    getBounds: fIdx => brep.Faces[fIdx].GetBoundingBox(accurate: false),
                    getAdjacentForGraph: fIdx => [.. brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()).Where(adj => adj != fIdx),]),
                Mesh mesh => ComputeConnectivity(
                    _: mesh,
                    faceCount: mesh.Faces.Count,
                    getAdjacent: fIdx => mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0),
                    getBounds: fIdx => mesh.Faces[fIdx] switch {
                        MeshFace f when f.IsQuad => new BoundingBox([mesh.Vertices[f.A], mesh.Vertices[f.B], mesh.Vertices[f.C], mesh.Vertices[f.D],]),
                        MeshFace f => new BoundingBox([mesh.Vertices[f.A], mesh.Vertices[f.B], mesh.Vertices[f.C],]),
                    },
                    getAdjacentForGraph: fIdx => [.. mesh.Faces.AdjacentFaces(fIdx).Where(adj => adj >= 0),]),
                _ => ResultFactory.Create<IReadOnlyList<Topology.ConnectivityData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    internal static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(T input, IGeometryContext context, Continuity? minimumContinuity = null, double? angleThreshold = null) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.EdgeClassification,
            operation: g => g switch {
                Brep brep => ClassifyBrepEdges(brep: brep, minContinuity: minimumContinuity ?? Continuity.G1_continuous, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                Mesh mesh => ClassifyMeshEdges(mesh: mesh, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(T input, IGeometryContext context, int edgeIndex) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Adjacency,
            operation: g => (g, edgeIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => ((Func<Result<IReadOnlyList<Topology.AdjacencyData>>>)(() => {
                    BrepEdge edge = brep.Edges[idx];
                    int[] adjacentFaces = [.. edge.AdjacentFaces(),];
                    Point3d edgeMid = edge.PointAt(edge.Domain.Mid);
                    Vector3d[] normals = [.. adjacentFaces.Select(i =>
                        brep.Faces[i].ClosestPoint(edgeMid, out double u, out double v)
                            ? brep.Faces[i].NormalAt(u, v)
                            : Vector3d.Unset),
                    ];
                    double angle = normals.Length == 2 && normals[0].IsValid && normals[1].IsValid
                        ? Vector3d.VectorAngle(normals[0], normals[1])
                        : 0.0;
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
                        new Topology.AdjacencyData(
                            EdgeIndex: idx,
                            AdjacentFaceIndices: adjacentFaces,
                            FaceNormals: normals,
                            DihedralAngle: angle,
                            IsManifold: edge.Valence == EdgeAdjacency.Interior,
                            IsBoundary: edge.Valence == EdgeAdjacency.Naked),
                    ]);
                }))(),
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyEdges.Count => ((Func<Result<IReadOnlyList<Topology.AdjacencyData>>>)(() => {
                    bool normalsValid = mesh.FaceNormals.Count == mesh.Faces.Count
                        || mesh.FaceNormals.ComputeFaceNormals();
                    _ = mesh.FaceNormals.UnitizeFaceNormals();
                    return !normalsValid
                        ? ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                            error: E.Geometry.InvalidGeometry.WithContext("Failed to compute mesh face normals"),
                        )
                        : mesh.TopologyEdges.GetConnectedFaces(idx) switch {
                            int[] { Length: 2 } af => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
                                new Topology.AdjacencyData(
                                    EdgeIndex: idx,
                                    AdjacentFaceIndices: af,
                                    FaceNormals: [mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]],],
                                    DihedralAngle: Vector3d.VectorAngle(mesh.FaceNormals[af[0]], mesh.FaceNormals[af[1]]),
                                    IsManifold: true,
                                    IsBoundary: false),
                            ]),
                            int[] af => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
                                new Topology.AdjacencyData(
                                    EdgeIndex: idx,
                                    AdjacentFaceIndices: af,
                                    FaceNormals: [.. af.Select(i => mesh.FaceNormals[i]),],
                                    DihedralAngle: 0.0,
                                    IsManifold: af.Length == 2,
                                    IsBoundary: af.Length == 1),
                            ]),
                        };
                }))(),
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

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
            componentCount = componentIds[seed] != -1
                ? componentCount
                : ((Func<int>)(() => {
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
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => getBounds(fIdx) switch {
            BoundingBox fBox when union.IsValid => BoundingBox.Union(union, fBox),
            BoundingBox fBox => fBox,
        })),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(
            ComponentIndices: components,
            ComponentSizes: [.. components.Select(static c => c.Count),],
            ComponentBounds: bounds,
            TotalComponents: componentCount,
            IsFullyConnected: componentCount == 1,
            AdjacencyGraph: Enumerable.Range(0, faceCount).ToFrozenDictionary(keySelector: i => i, elementSelector: getAdjacentForGraph)),
        ]);
    }

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
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(static x => x.type, static x => x.idx).ToFrozenDictionary(static g => g.Key, static g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: minContinuity),]);
    }

    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ClassifyMeshEdges(Mesh mesh, double angleThreshold) {
        _ = mesh.FaceNormals.Count == mesh.Faces.Count
            || mesh.FaceNormals.ComputeFaceNormals();
        _ = mesh.FaceNormals.UnitizeFaceNormals();
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
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(static x => x.type, static x => x.idx).ToFrozenDictionary(static g => g.Key, static g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: Continuity.C0_continuous),]);
    }

    internal static Result<Topology.VertexData> ExecuteVertexData<T>(T input, IGeometryContext context, int vertexIndex) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.VertexData,
            operation: g => (g, vertexIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Vertices.Count => ((Func<Result<IReadOnlyList<Topology.VertexData>>>)(() => {
                    int[] edgeIndices = [.. brep.Vertices[idx].EdgeIndices(),];
                    int[] faceIndices = [.. new HashSet<int>(
                        edgeIndices
                            .SelectMany(edgeIdx => brep.Edges[edgeIdx].AdjacentFaces())
                            .Where(faceIdx => faceIdx >= 0)),
                    ];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[
                        new Topology.VertexData(
                            VertexIndex: idx,
                            Location: brep.Vertices[idx].Location,
                            ConnectedEdgeIndices: edgeIndices,
                            ConnectedFaceIndices: faceIndices,
                            Valence: edgeIndices.Length,
                            IsBoundary: edgeIndices.Any(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),
                            IsManifold: edgeIndices.All(i => brep.Edges[i].Valence == EdgeAdjacency.Interior)),
                    ]);
                }))(),
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Vertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyVertices.Count => ((Func<Result<IReadOnlyList<Topology.VertexData>>>)(() => {
                    int[] connectedEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Where(e => mesh.TopologyEdges.GetTopologyVertices(e) is IndexPair v && (v.I == idx || v.J == idx)),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[new Topology.VertexData(
                        VertexIndex: idx,
                        Location: new Point3d(mesh.TopologyVertices[idx]),
                        ConnectedEdgeIndices: connectedEdges,
                        ConnectedFaceIndices: mesh.TopologyVertices.ConnectedFaces(idx),
                        Valence: mesh.TopologyVertices.ConnectedTopologyVertices(idx).Length,
                        IsBoundary: connectedEdges.Any(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 1),
                        IsManifold: connectedEdges.All(e => mesh.TopologyEdges.GetConnectedFaces(e).Length == 2)),
                    ]);
                }))(),
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyVertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    internal static Result<Topology.NgonTopologyData> ExecuteNgonTopology<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NgonTopology,
            operation: g => g switch {
                Mesh mesh => ((Func<(IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)[]>)(() => {
                    (IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)[] data = new (IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)[mesh.Ngons.Count];
                    for (int index = 0; index < mesh.Ngons.Count; index++) {
                        MeshNgon ngon = mesh.Ngons.GetNgon(index);
                        uint[]? faceList = ngon.FaceIndexList();
                        uint[]? boundaryVerts = ngon.BoundaryVertexIndexList();
                        Point3d center = mesh.Ngons.GetNgonCenter(index);
                        IReadOnlyList<int> faces = [.. (faceList is uint[] fl ? fl : []).Select(face => unchecked((int)face)),];
                        IReadOnlyList<int> boundaries = [.. (boundaryVerts is uint[] bv ? bv : []).Select(vert => unchecked((int)vert)),];
                        data[index] = (boundaries, faces, center.IsValid ? center : Point3d.Origin, boundaries.Count);
                    }
                    return data;
                }))() switch {
                    (IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)[] { Length: 0 } => ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData([], [], [], [], [], 0, mesh.Faces.Count),]),
                    (IReadOnlyList<int>, IReadOnlyList<int>, Point3d, int)[] data => ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData(
                        NgonIndices: [.. Enumerable.Range(0, data.Length),],
                        FaceIndicesPerNgon: [.. data.Select(d => d.Item2),],
                        BoundaryEdgesPerNgon: [.. data.Select(d => d.Item1),],
                        NgonCenters: [.. data.Select(d => d.Item3),],
                        EdgeCountPerNgon: [.. data.Select(d => d.Item4),],
                        TotalNgons: data.Length,
                        TotalFaces: mesh.Faces.Count),
                    ]),
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NgonTopologyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });
}

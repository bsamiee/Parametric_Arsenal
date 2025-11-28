using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology execution via breadth-first search and edge classification algorithms.</summary>
[Pure] internal static class TopologyCore {
    internal static Result<Topology.TopologyResult> ExecuteQuery<T>(T input, Topology.QueryOperation operation, IGeometryContext context) where T : notnull =>
        operation switch {
            Topology.ConnectivityQuery => ExecuteConnectivity(input: input, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Connectivity(d)),
            Topology.NonManifoldQuery => ExecuteNonManifold(input: input, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.NonManifold(d)),
            Topology.NgonQuery => ExecuteNgonTopology(input: input, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Ngon(d)),
            Topology.AdjacencyQuery q => ExecuteAdjacency(input: input, context: context, edgeIndex: q.EdgeIndex).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Adjacency(d)),
            Topology.VertexQuery q => ExecuteVertexData(input: input, context: context, vertexIndex: q.VertexIndex).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Vertex(d)),
            Topology.NakedEdgesQuery q => ExecuteNakedEdges(input: input, context: context, orderLoops: q.OrderLoops).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.NakedEdges(d)),
            Topology.BoundaryLoopsQuery q => ExecuteBoundaryLoops(input: input, context: context, tolerance: q.Tolerance).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.BoundaryLoops(d)),
            Topology.EdgeClassificationQuery q => ExecuteEdgeClassification(input: input, context: context, minimumContinuity: q.MinimumContinuity, angleThreshold: q.AngleThreshold).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.EdgeClassification(d)),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown QueryOperation: {operation.GetType().Name}")),
        };

    internal static Result<Topology.TopologyResult> ExecuteBrepOperation(Brep brep, Topology.BrepOperation operation, IGeometryContext context) =>
        operation switch {
            Topology.DiagnoseOperation => ExecuteDiagnose(input: brep, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Diagnosis(d)),
            Topology.ExtractFeaturesOperation => ExecuteExtractFeatures(input: brep, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Features(d)),
            Topology.HealOperation h => ExecuteHeal(input: brep, strategies: h.Strategies, context: context).Map(static d => (Topology.TopologyResult)new Topology.TopologyResult.Healing(d)),
            _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown BrepOperation: {operation.GetType().Name}")),
        };

    private static Result<Topology.TopologyDiagnosis> ExecuteDiagnose(Brep input, IGeometryContext context) =>
        TopologyConfig.DiagnosticOps.TryGetValue(TopologyConfig.OpType.Diagnose, out TopologyConfig.DiagnosticMetadata? diagMeta)
            ? input.IsValidTopology(out string topologyLog)
                ? ResultFactory.Create(value: input)
                    .Validate(args: [context, diagMeta!.ValidationMode,])
                    .Bind(validBrep => TopologyCompute.Diagnose(validBrep: validBrep, context: context, nearMissMultiplier: diagMeta!.NearMissMultiplier, maxEdgeThreshold: diagMeta!.MaxEdgeThreshold))
                : ResultFactory.Create<Topology.TopologyDiagnosis>(error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {topologyLog}"))
            : ResultFactory.Create<Topology.TopologyDiagnosis>(error: E.Geometry.UnsupportedAnalysis.WithContext("Operation: Diagnose"));

    private static Result<Topology.TopologicalFeatures> ExecuteExtractFeatures(Brep input, IGeometryContext context) =>
        TopologyConfig.FeaturesOps.TryGetValue(TopologyConfig.OpType.ExtractFeatures, out TopologyConfig.FeaturesMetadata? featuresMeta)
            ? input.IsValidTopology(out string topologyLog)
                ? ResultFactory.Create(value: input)
                    .Validate(args: [context, featuresMeta!.ValidationMode,])
                    .Bind(validBrep => TopologyCompute.ExtractFeatures(validBrep: validBrep, context: context))
                : ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid for feature extraction: {topologyLog}"))
            : ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Geometry.UnsupportedAnalysis.WithContext("Operation: ExtractFeatures"));

    private static Result<Topology.HealingResult> ExecuteHeal(Brep input, IReadOnlyList<Topology.Strategy> strategies, IGeometryContext context) =>
        TopologyConfig.HealingOps.TryGetValue(TopologyConfig.OpType.Heal, out TopologyConfig.HealingMetadata? healMeta)
            ? input.IsValidTopology(out string topologyLog)
                ? ResultFactory.Create(value: input)
                    .Validate(args: [context, healMeta!.ValidationMode,])
                    .Bind(validBrep => TopologyCompute.Heal(validBrep: validBrep, strategies: strategies, context: context, maxTargetedJoinIterations: healMeta!.MaxTargetedJoinIterations))
                : ResultFactory.Create<Topology.HealingResult>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid before healing: {topologyLog}"))
            : ResultFactory.Create<Topology.HealingResult>(error: E.Geometry.UnsupportedAnalysis.WithContext("Operation: Heal"));

    private static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Connectivity,
            operation: g => g switch {
                Brep brep => TopologyCompute.ComputeConnectivity(
                    _: brep,
                    faceCount: brep.Faces.Count,
                    getAdjacent: fIdx => brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()),
                    getBounds: fIdx => brep.Faces[fIdx].GetBoundingBox(accurate: false),
                    getAdjacentForGraph: fIdx => [.. brep.Faces[fIdx].AdjacentEdges().SelectMany(eIdx => brep.Edges[eIdx].AdjacentFaces()).Where(adj => adj != fIdx),]),
                Mesh mesh => TopologyCompute.ComputeConnectivity(
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

    private static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: TopologyConfig.OpType.BoundaryLoops,
            operation: g => {
                Curve[] nakedCurves = (object)g switch {
                    Brep brep => [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()),],
                    Mesh mesh => [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),],
                    _ => [],
                };
                Topology.BoundaryLoopData result = nakedCurves.Length == 0
                    ? new Topology.BoundaryLoopData(Loops: [], EdgeIndicesPerLoop: [], LoopLengths: [], IsClosedPerLoop: [], JoinTolerance: tol, FailedJoins: 0)
                    : ((Func<Topology.BoundaryLoopData>)(() => {
                        Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false) ?? [];
                        Array.ForEach(nakedCurves, c => c?.Dispose());
                        return new Topology.BoundaryLoopData(
                            Loops: joined,
                            EdgeIndicesPerLoop: [.. joined.Select(static _ => (IReadOnlyList<int>)[]),],
                            LoopLengths: [.. joined.Select(static c => c.GetLength()),],
                            IsClosedPerLoop: [.. joined.Select(static c => c.IsClosed),],
                            JoinTolerance: tol,
                            FailedJoins: nakedCurves.Length - joined.Length);
                    }))();
                return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[result,]);
            });
    }

    private static Result<Topology.NgonTopologyData> ExecuteNgonTopology<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NgonTopology,
            operation: g => g switch {
                Mesh mesh when mesh.Ngons.Count == 0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData([], [], [], [], [], 0, mesh.Faces.Count),]),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NgonTopologyData>>>)(() => {
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
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NgonTopologyData>)[new Topology.NgonTopologyData(
                        NgonIndices: [.. Enumerable.Range(0, data.Length),],
                        FaceIndicesPerNgon: [.. data.Select(d => d.Item2),],
                        BoundaryEdgesPerNgon: [.. data.Select(d => d.Item1),],
                        NgonCenters: [.. data.Select(d => d.Item3),],
                        EdgeCountPerNgon: [.. data.Select(d => d.Item4),],
                        TotalNgons: data.Length,
                        TotalFaces: mesh.Faces.Count),
                    ]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NgonTopologyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    private static Result<Topology.VertexData> ExecuteVertexData<T>(T input, IGeometryContext context, int vertexIndex) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.VertexData,
            operation: g => (g, vertexIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Vertices.Count => ((Func<Result<IReadOnlyList<Topology.VertexData>>>)(() => {
                    int[] edgeIndices = [.. brep.Vertices[idx].EdgeIndices(),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[
                        new Topology.VertexData(
                            VertexIndex: idx,
                            Location: brep.Vertices[idx].Location,
                            ConnectedEdgeIndices: edgeIndices,
                            ConnectedFaceIndices: [.. new HashSet<int>(edgeIndices.SelectMany(edgeIdx => brep.Edges[edgeIdx].AdjacentFaces()).Where(faceIdx => faceIdx >= 0)),],
                            Valence: edgeIndices.Length,
                            IsBoundary: edgeIndices.Any(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),
                            IsManifold: edgeIndices.All(i => brep.Edges[i].Valence == EdgeAdjacency.Interior)),
                    ]);
                }))(),
                (Brep brep, int idx) => ResultFactory.Create<IReadOnlyList<Topology.VertexData>>(error: E.Geometry.InvalidVertexIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"VertexIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Vertices.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, int idx) when idx >= 0 && idx < mesh.TopologyVertices.Count => ((Func<Result<IReadOnlyList<Topology.VertexData>>>)(() => {
                    int[] connectedEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Where(e => mesh.TopologyEdges.GetTopologyVertices(e) is IndexPair v && (v.I == idx || v.J == idx)),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.VertexData>)[
                        new Topology.VertexData(
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

    private static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(T input, IGeometryContext context, bool orderLoops) where T : notnull =>
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
                            (Point3d p1, Point3d p2) = (mesh.TopologyVertices[verts.I], mesh.TopologyVertices[verts.J]);
                            return (i, new LineCurve(p1, p2), p1.DistanceTo(p2));
                        }),
                    ];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: [.. edges.Select(static e => e.Curve),],
                            EdgeIndices: [.. edges.Select(static e => e.Index),],
                            Valences: [.. edges.Select(static _ => 1),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: edges.Sum(static e => e.Length)),
                    ]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    private static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.NonManifold,
            operation: g => g switch {
                Brep brep => ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                    ((Func<Topology.NonManifoldData>)(() => {
                        int[] nmEdges = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),];
                        return new Topology.NonManifoldData(
                            EdgeIndices: nmEdges,
                            VertexIndices: [],
                            Valences: [.. nmEdges.Select(i => (int)brep.Edges[i].Valence),],
                            Locations: [.. nmEdges.Select(i => brep.Edges[i].PointAtStart),],
                            IsManifold: nmEdges.Length == 0,
                            IsOrientable: brep.IsSolid,
                            MaxValence: nmEdges.Length > 0 ? nmEdges.Max(i => (int)brep.Edges[i].Valence) : 0);
                    }))(),
                ]),
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

    private static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(T input, IGeometryContext context, Continuity? minimumContinuity = null, double? angleThreshold = null) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.EdgeClassification,
            operation: g => g switch {
                Brep brep => ((Func<Result<IReadOnlyList<Topology.EdgeClassificationData>>>)(() => {
                    Continuity minContinuity = minimumContinuity ?? Continuity.G1_continuous;
                    double threshold = angleThreshold ?? context.AngleToleranceRadians;
                    IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
                    IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => brep.Edges[i].Valence switch {
                        EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
                        EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
                        EdgeAdjacency.Interior => brep.Edges[i] switch {
                            BrepEdge e when e.IsSmoothManifoldEdge(angleToleranceRadians: threshold) && e.EdgeCurve is Curve crv && (crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid)) => Topology.EdgeContinuityType.Curvature,
                            BrepEdge e when e.IsSmoothManifoldEdge(angleToleranceRadians: threshold) && minContinuity < Continuity.G2_continuous => Topology.EdgeContinuityType.Smooth,
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
                }))(),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.EdgeClassificationData>>>)(() => {
                    bool computed = TopologyCompute.EnsureMeshNormals(mesh);
                    double threshold = angleThreshold ?? context.AngleToleranceRadians;
                    double curvatureThreshold = threshold * TopologyConfig.CurvatureThresholdRatio;
                    IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
                    IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetConnectedFaces(i) switch {
                        int[] cf when cf.Length == 1 => Topology.EdgeContinuityType.Boundary,
                        int[] cf when cf.Length > 2 => Topology.EdgeContinuityType.NonManifold,
                        int[] cf when cf.Length == 2 && computed && mesh.FaceNormals.Count > Math.Max(cf[0], cf[1]) => Vector3d.VectorAngle(mesh.FaceNormals[cf[0]], mesh.FaceNormals[cf[1]]) switch {
                            double angle when Math.Abs(angle) < curvatureThreshold => Topology.EdgeContinuityType.Curvature,
                            double angle when Math.Abs(angle) < threshold => Topology.EdgeContinuityType.Smooth,
                            _ => Topology.EdgeContinuityType.Sharp,
                        },
                        _ => Topology.EdgeContinuityType.Sharp,
                    }),
                    ];
                    IReadOnlyList<double> measures = [.. edgeIndices.Select(i => mesh.TopologyEdges.GetTopologyVertices(i) switch { IndexPair verts => mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]) }),];
                    FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices.Select((idx, pos) => (idx, type: classifications[pos])).GroupBy(static x => x.type, static x => x.idx).ToFrozenDictionary(static g => g.Key, static g => (IReadOnlyList<int>)[.. g,]);
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[new Topology.EdgeClassificationData(EdgeIndices: edgeIndices, Classifications: classifications, ContinuityMeasures: measures, GroupedByType: grouped, MinimumContinuity: Continuity.C0_continuous),]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    private static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(T input, IGeometryContext context, int edgeIndex) where T : notnull =>
        Execute(input: input, context: context, opType: TopologyConfig.OpType.Adjacency,
            operation: g => (g, edgeIndex) switch {
                (Brep brep, int idx) when idx >= 0 && idx < brep.Edges.Count => ((Func<Result<IReadOnlyList<Topology.AdjacencyData>>>)(() => {
                    BrepEdge edge = brep.Edges[idx];
                    int[] adjacentFaces = [.. edge.AdjacentFaces(),];
                    Point3d edgeMid = edge.PointAt(edge.Domain.Mid);
                    Vector3d[] normals = [.. adjacentFaces.Select(i => brep.Faces[i].ClosestPoint(edgeMid, out double u, out double v) ? brep.Faces[i].NormalAt(u, v) : Vector3d.Unset),];
                    double angle = normals.Length == 2 && normals[0].IsValid && normals[1].IsValid ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;
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
                    bool computed = TopologyCompute.EnsureMeshNormals(mesh);
                    int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(idx);
                    bool hasValidNormals = computed && connectedFaces.Length == 2 && mesh.FaceNormals.Count > Math.Max(connectedFaces[0], connectedFaces[1]);
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
                        new Topology.AdjacencyData(
                            EdgeIndex: idx,
                            AdjacentFaceIndices: connectedFaces,
                            FaceNormals: hasValidNormals
                                ? [mesh.FaceNormals[connectedFaces[0]], mesh.FaceNormals[connectedFaces[1]],]
                                : computed && connectedFaces.Length > 0 && mesh.FaceNormals.Count > connectedFaces.Max()
                                    ? [.. connectedFaces.Select(i => mesh.FaceNormals[i]),]
                                    : [.. connectedFaces.Select(_ => Vector3d.Unset),],
                            DihedralAngle: hasValidNormals
                                ? Vector3d.VectorAngle(mesh.FaceNormals[connectedFaces[0]], mesh.FaceNormals[connectedFaces[1]])
                                : 0.0,
                            IsManifold: connectedFaces.Length == 2,
                            IsBoundary: connectedFaces.Length == 1),
                    ]);
                }))(),
                (Mesh mesh, int idx) => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {idx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    private static Result<TResult> Execute<T, TResult>(T input, IGeometryContext context, TopologyConfig.OpType opType, Func<T, Result<IReadOnlyList<TResult>>> operation) where T : notnull =>
        TopologyConfig.OperationMeta.TryGetValue((input.GetType(), opType), out TopologyConfig.OperationMetadata? meta)
            ? UnifiedOperation.Apply(input: input, operation: operation, config: new OperationConfig<T, TResult> { Context = context, ValidationMode = meta!.ValidationMode, OperationName = meta!.OpName, EnableDiagnostics = false }).Map(results => results[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}, Operation: {opType}"));
}

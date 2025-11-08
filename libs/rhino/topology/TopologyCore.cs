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

    private enum OperationType { NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2, Connectivity = 3, EdgeClassification = 4, Adjacency = 5 }

    private static readonly FrozenDictionary<(Type, OperationType), (V ValidationMode, string OpName)> _operationMeta =
        new Dictionary<(Type, OperationType), (V, string)> {
            [(typeof(Brep), OperationType.NakedEdges)] = (V.Standard | V.Topology, "Topology.GetNakedEdges.Brep"),
            [(typeof(Mesh), OperationType.NakedEdges)] = (V.Standard | V.MeshSpecific, "Topology.GetNakedEdges.Mesh"),
            [(typeof(Brep), OperationType.BoundaryLoops)] = (V.Standard | V.Topology, "Topology.GetBoundaryLoops.Brep"),
            [(typeof(Mesh), OperationType.BoundaryLoops)] = (V.Standard | V.MeshSpecific, "Topology.GetBoundaryLoops.Mesh"),
            [(typeof(Brep), OperationType.NonManifold)] = (V.Standard | V.Topology, "Topology.GetNonManifold.Brep"),
            [(typeof(Mesh), OperationType.NonManifold)] = (V.Standard | V.MeshSpecific, "Topology.GetNonManifold.Mesh"),
            [(typeof(Brep), OperationType.Connectivity)] = (V.Standard | V.Topology, "Topology.GetConnectivity.Brep"),
            [(typeof(Mesh), OperationType.Connectivity)] = (V.Standard | V.MeshSpecific, "Topology.GetConnectivity.Mesh"),
            [(typeof(Brep), OperationType.EdgeClassification)] = (V.Standard | V.Topology, "Topology.ClassifyEdges.Brep"),
            [(typeof(Mesh), OperationType.EdgeClassification)] = (V.Standard | V.MeshSpecific, "Topology.ClassifyEdges.Mesh"),
            [(typeof(Brep), OperationType.Adjacency)] = (V.Standard | V.Topology, "Topology.GetAdjacency.Brep"),
            [(typeof(Mesh), OperationType.Adjacency)] = (V.Standard | V.MeshSpecific, "Topology.GetAdjacency.Mesh"),
        }.ToFrozenDictionary();

    [Pure]
    private static Result<TResult> Execute<T, TResult>(T input, IGeometryContext context, OperationType opType, bool enableDiagnostics, Func<T, Result<IReadOnlyList<TResult>>> operation) where T : notnull =>
        _operationMeta.TryGetValue((input.GetType(), opType), out (V ValidationMode, string OpName) meta)
            ? UnifiedOperation.Apply(input: input, operation: operation, config: new OperationConfig<T, TResult> { Context = context, ValidationMode = meta.ValidationMode, OperationName = meta.OpName, EnableDiagnostics = enableDiagnostics }).Map(results => results[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}, Operation: {opType}"));

    [Pure]
    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(T input, IGeometryContext context, bool orderLoops, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.NakedEdges, enableDiagnostics: enableDiagnostics,
            operation: g => TopologyConfig.EdgeAccessors.TryGetValue(g!.GetType(), out Func<object, TopologyConfig.EdgeAccessor>? accessorFactory)
                ? accessorFactory(g) switch {
                    TopologyConfig.EdgeAccessor accessor => accessor.GetNakedEdges() switch {
                        IReadOnlyList<(Curve Curve, int Index)> naked => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                            new Topology.NakedEdgeData(
                                EdgeCurves: [.. naked.Select(x => x.Curve),],
                                EdgeIndices: [.. naked.Select(x => x.Index),],
                                Valences: [.. Enumerable.Repeat(1, naked.Count),],
                                IsOrdered: orderLoops,
                                TotalEdgeCount: accessor.EdgeCount,
                                TotalLength: accessor.GetTotalLength()),
                        ]),
                    },
                }
                : ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")));

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: OperationType.BoundaryLoops, enableDiagnostics: enableDiagnostics,
            operation: g => TopologyConfig.EdgeAccessors.TryGetValue(g!.GetType(), out Func<object, TopologyConfig.EdgeAccessor>? accessorFactory)
                ? accessorFactory(g) switch {
                    TopologyConfig.EdgeAccessor accessor => ((Func<Result<IReadOnlyList<Topology.BoundaryLoopData>>>)(() => {
                        Curve[] naked = [.. accessor.GetNakedEdges().Select(x => x.Curve),];
                        Curve[] joined = naked.Length > 0 ? Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) : [];
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[
                            new Topology.BoundaryLoopData(
                                Loops: [.. joined,],
                                EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),],
                                LoopLengths: [.. joined.Select(c => c.GetLength()),],
                                IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                                JoinTolerance: tol,
                                FailedJoins: naked.Length - joined.Length),
                        ]);
                    }))(),
                }
                : ResultFactory.Create<IReadOnlyList<Topology.BoundaryLoopData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")));
    }

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.NonManifold, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    IReadOnlyList<int> nm = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => (int)brep.Edges[i].Valence),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                        new Topology.NonManifoldData(
                            EdgeIndices: nm,
                            VertexIndices: [],
                            Valences: vals,
                            Locations: [.. nm.Select(i => brep.Edges[i].PointAtStart),],
                            IsManifold: nm.Count == 0,
                            IsOrientable: brep.IsSolid,
                            MaxValence: vals.Count > 0 ? vals.Max() : 0),
                    ]);
                }))(),
                Mesh mesh => ((Func<Result<IReadOnlyList<Topology.NonManifoldData>>>)(() => {
                    bool manifold = mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _);
                    IReadOnlyList<int> nm = [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
                        new Topology.NonManifoldData(
                            EdgeIndices: nm,
                            VertexIndices: [],
                            Valences: vals,
                            Locations: [.. nm.Select(i => {
                                IndexPair v = mesh.TopologyEdges.GetTopologyVertices(i);
                                Point3d p1 = mesh.TopologyVertices[v.I];
                                Point3d p2 = mesh.TopologyVertices[v.J];
                                return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
                            }),],
                            IsManifold: manifold,
                            IsOrientable: oriented,
                            MaxValence: vals.Count > 0 ? vals.Max() : 0),
                    ]);
                }))(),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.Connectivity, enableDiagnostics: enableDiagnostics,
            operation: g => TopologyConfig.TopologyAccessors.TryGetValue(g!.GetType(), out Func<object, TopologyConfig.TopologyAccessor>? accessorFactory)
                ? ExecuteConnectivityBFS(accessor: accessorFactory(g))
                : ResultFactory.Create<IReadOnlyList<Topology.ConnectivityData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")));

    [Pure]
    internal static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(T input, IGeometryContext context, Continuity? minimumContinuity = null, double? angleThreshold = null, bool enableDiagnostics = false) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.EdgeClassification, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ExecuteBrepEdgeClassification(brep: brep, minContinuity: minimumContinuity ?? Continuity.G1_continuous, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                Mesh mesh => ExecuteMeshEdgeClassification(mesh: mesh, angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(T input, IGeometryContext context, int edgeIndex, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.Adjacency, enableDiagnostics: enableDiagnostics,
            operation: g => TopologyConfig.AdjacencyAccessors.TryGetValue(g!.GetType(), out Func<object, TopologyConfig.AdjacencyAccessor>? accessorFactory)
                ? accessorFactory(g) switch {
                    TopologyConfig.AdjacencyAccessor accessor when accessor.IsValidEdgeIndex(edgeIndex) => accessor.GetEdgeAdjacency(edgeIndex) switch {
                        (IReadOnlyList<int> af, IReadOnlyList<Vector3d> norms, EdgeAdjacency valence) => ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
                            new Topology.AdjacencyData(
                                EdgeIndex: edgeIndex,
                                AdjacentFaceIndices: af,
                                FaceNormals: norms,
                                DihedralAngle: norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0,
                                IsManifold: valence == EdgeAdjacency.Interior,
                                IsBoundary: valence == EdgeAdjacency.Naked),
                        ]),
                    },
                    TopologyConfig.AdjacencyAccessor accessor => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex.ToString(CultureInfo.InvariantCulture)}"))),
                }
                : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")));

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteConnectivityBFS(TopologyConfig.TopologyAccessor accessor) {
        int[] componentIds = new int[accessor.FaceCount];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < accessor.FaceCount; seed++) {
            componentCount = componentIds[seed] != -1 ? componentCount : ((Func<int>)(() => {
                Queue<int> queue = new([seed,]);
                componentIds[seed] = componentCount;
                while (queue.Count > 0) {
                    int faceIdx = queue.Dequeue();
                    foreach (int adjFace in accessor.GetAdjacentFaces(faceIdx).Where(f => componentIds[f] == -1)) {
                        componentIds[adjFace] = componentCount;
                        queue.Enqueue(adjFace);
                    }
                }
                return componentCount;
            }))() + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, accessor.FaceCount).Where(f => componentIds[f] == c),]),];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => union.IsValid ? BoundingBox.Union(union, accessor.GetFaceBoundingBox(fIdx)) : accessor.GetFaceBoundingBox(fIdx))),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(ComponentIndices: components, ComponentSizes: [.. components.Select(c => c.Count),], ComponentBounds: bounds, TotalComponents: componentCount, IsFullyConnected: componentCount == 1, AdjacencyGraph: Enumerable.Range(0, accessor.FaceCount).Select(f => (f, (IReadOnlyList<int>)[.. accessor.GetAdjacentFaces(f).Where(adj => adj != f),])).ToFrozenDictionary(x => x.f, x => x.Item2)),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteBrepEdgeClassification(Brep brep, Continuity minContinuity, double angleThreshold) {
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
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteMeshEdgeClassification(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * 0.1;
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

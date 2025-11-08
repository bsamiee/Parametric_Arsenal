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
            operation: g => g switch {
                Brep brep => brep.Edges.Count switch {
                    0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData([], [], [], orderLoops, 0, 0.0),]),
                    _ => Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).ToArray() switch {
                        int[] nakedIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData([.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),], [.. nakedIndices,], [.. Enumerable.Repeat(1, nakedIndices.Length),], orderLoops, brep.Edges.Count, nakedIndices.Sum(i => brep.Edges[i].GetLength())),]),
                    },
                },
                Mesh mesh => (mesh.GetNakedEdges() ?? []) switch {
                    Polyline[] nakedPolylines => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[new Topology.NakedEdgeData([.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),], [.. Enumerable.Range(0, nakedPolylines.Length),], [.. Enumerable.Repeat(1, nakedPolylines.Length),], orderLoops, mesh.TopologyEdges.Count, nakedPolylines.Sum(pl => pl.Length)),]),
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return Execute(input: input, context: context, opType: OperationType.BoundaryLoops, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ResultFactory.Create(value: brep).Bind(b => {
                    Curve[] naked = [.. Enumerable.Range(0, b.Edges.Count).Where(i => b.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => b.Edges[i].DuplicateCurve()),];
                    Curve[] joined = naked.Length > 0 ? Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) : [];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData([.. joined,], [.. joined.Select(_ => EmptyIndices),], [.. joined.Select(c => c.GetLength()),], [.. joined.Select(c => c.IsClosed),], tol, naked.Length - joined.Length),]);
                }),
                Mesh mesh => ResultFactory.Create(value: mesh).Bind(m => {
                    Curve[] naked = [.. (m.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),];
                    Curve[] joined = naked.Length > 0 ? Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) : [];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData([.. joined,], [.. joined.Select(_ => EmptyIndices),], [.. joined.Select(c => c.GetLength()),], [.. joined.Select(c => c.IsClosed),], tol, naked.Length - joined.Length),]);
                }),
                _ => ResultFactory.Create<IReadOnlyList<Topology.BoundaryLoopData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });
    }

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.NonManifold, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ResultFactory.Create(value: brep).Bind(b => {
                    IReadOnlyList<int> nm = [.. Enumerable.Range(0, b.Edges.Count).Where(i => b.Edges[i].Valence == EdgeAdjacency.NonManifold),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => (int)b.Edges[i].Valence),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(nm, [], vals, [.. nm.Select(i => b.Edges[i].PointAtStart),], nm.Count == 0, b.IsSolid, vals.Count > 0 ? vals.Max() : 0),]);
                }),
                Mesh mesh => ResultFactory.Create(value: mesh).Bind(m => {
                    bool manifold = m.IsManifold(topologicalTest: true, out bool oriented, out bool _);
                    IReadOnlyList<int> nm = [.. Enumerable.Range(0, m.TopologyEdges.Count).Where(i => m.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> vals = [.. nm.Select(i => m.TopologyEdges.GetConnectedFaces(i).Length),];
                    return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(nm, [], vals, [.. nm.Select(i => {
                        IndexPair v = m.TopologyEdges.GetTopologyVertices(i);
                        Point3d p1 = m.TopologyVertices[v.I];
                        Point3d p2 = m.TopologyVertices[v.J];
                        return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
                    }),], manifold, oriented, vals.Count > 0 ? vals.Max() : 0),]);
                }),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context, bool enableDiagnostics) where T : notnull =>
        Execute(input: input, context: context, opType: OperationType.Connectivity, enableDiagnostics: enableDiagnostics,
            operation: g => g switch {
                Brep brep => ExecuteBrepConnectivity(brep: brep),
                Mesh mesh => ExecuteMeshConnectivity(mesh: mesh),
                _ => ResultFactory.Create<IReadOnlyList<Topology.ConnectivityData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

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
            operation: g => g switch {
                Brep brep => edgeIndex >= 0 && edgeIndex < brep.Edges.Count
                    ? ResultFactory.Create(value: brep).Bind(b => {
                        BrepEdge e = b.Edges[edgeIndex];
                        IReadOnlyList<int> af = [.. e.AdjacentFaces(),];
                        IReadOnlyList<Vector3d> norms = [.. af.Select(i => b.Faces[i].NormalAt(b.Faces[i].Domain(0).Mid, b.Faces[i].Domain(1).Mid)),];
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(edgeIndex, af, norms, norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, e.Valence == EdgeAdjacency.Interior, e.Valence == EdgeAdjacency.Naked),]);
                    })
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                Mesh mesh => edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count
                    ? ResultFactory.Create(value: mesh).Bind(m => {
                        int[] af = m.TopologyEdges.GetConnectedFaces(edgeIndex);
                        IReadOnlyList<Vector3d> norms = [.. af.Select(i => m.FaceNormals[i]),];
                        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(edgeIndex, [.. af,], norms, norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, af.Length == 2, af.Length == 1),]);
                    })
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            });

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteBrepConnectivity(Brep brep) {
        int[] componentIds = new int[brep.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < brep.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1 ? componentCount : ((Func<int>)(() => {
                Queue<int> queue = new([seed,]);
                componentIds[seed] = componentCount;
                while (queue.Count > 0) {
                    int faceIdx = queue.Dequeue();
                    foreach (int edgeIdx in brep.Faces[faceIdx].AdjacentEdges()) {
                        foreach (int adjFace in brep.Edges[edgeIdx].AdjacentFaces().Where(f => componentIds[f] == -1)) {
                            componentIds[adjFace] = componentCount;
                            queue.Enqueue(adjFace);
                        }
                    }
                }
                return componentCount;
            }))() + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => union.IsValid ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false)) : brep.Faces[fIdx].GetBoundingBox(accurate: false))),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(ComponentIndices: components, ComponentSizes: [.. components.Select(c => c.Count),], ComponentBounds: bounds, TotalComponents: componentCount, IsFullyConnected: componentCount == 1, AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count).Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges().SelectMany(e => brep.Edges[e].AdjacentFaces()).Where(adj => adj != f),])).ToFrozenDictionary(x => x.f, x => x.Item2)),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteMeshConnectivity(Mesh mesh) {
        int[] componentIds = new int[mesh.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < mesh.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1 ? componentCount : ((Func<int>)(() => {
                Queue<int> queue = new([seed,]);
                componentIds[seed] = componentCount;
                while (queue.Count > 0) {
                    int faceIdx = queue.Dequeue();
                    foreach (int adjFace in mesh.Faces.AdjacentFaces(faceIdx).Where(f => f >= 0 && componentIds[f] == -1)) {
                        componentIds[adjFace] = componentCount;
                        queue.Enqueue(adjFace);
                    }
                }
                return componentCount;
            }))() + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => {
            MeshFace face = mesh.Faces[fIdx];
            BoundingBox fBox = face.IsQuad ? new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],]) : new BoundingBox([mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],]);
            return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
        })),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(ComponentIndices: components, ComponentSizes: [.. components.Select(c => c.Count),], ComponentBounds: bounds, TotalComponents: componentCount, IsFullyConnected: componentCount == 1, AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count).Select(f => (f, (IReadOnlyList<int>)[.. mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0),])).ToFrozenDictionary(x => x.f, x => x.Item2)),]);
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

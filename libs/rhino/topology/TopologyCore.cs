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

    /// <summary>Operation registry mapping operation types to their implementation dispatchers.</summary>
    private static readonly FrozenDictionary<Type, Func<object, object, IGeometryContext, Result<object>>> Operations =
        new Dictionary<Type, Func<object, object, IGeometryContext, Result<object>>> {
            [typeof(Topology.NakedEdgeData)] = (input, config, _) => ((input, config) switch {
                (Brep brep, ValueTuple<bool>(var orderLoops)) => ComputeNakedEdges(brep, orderLoops),
                (Mesh mesh, ValueTuple<bool>(var orderLoops)) => ComputeNakedEdges(mesh, orderLoops),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            }).Map(r => (object)r),
            [typeof(Topology.BoundaryLoopData)] = (input, config, _) => ((input, config) switch {
                (Brep brep, ValueTuple<double>(var tol)) => ExecuteBrepBoundaryLoops(brep, tol),
                (Mesh mesh, ValueTuple<double>(var tol)) => ExecuteMeshBoundaryLoops(mesh, tol),
                _ => ResultFactory.Create<IReadOnlyList<Topology.BoundaryLoopData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            }).Map(r => (object)r),
            [typeof(Topology.NonManifoldData)] = (input, _, __) => input switch {
                Brep brep => ExecuteBrepNonManifold(brep).Map(r => (object)r),
                Mesh mesh => ExecuteMeshNonManifold(mesh).Map(r => (object)r),
                _ => ResultFactory.Create<object>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            },
            [typeof(Topology.ConnectivityData)] = (input, _, __) => input switch {
                Brep brep => ExecuteBrepConnectivity(brep).Map(r => (object)r),
                Mesh mesh => ExecuteMeshConnectivity(mesh).Map(r => (object)r),
                _ => ResultFactory.Create<object>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            },
            [typeof(Topology.EdgeClassificationData)] = (input, config, _) => ((input, config) switch {
                (Brep brep, ValueTuple<Continuity, double>(var minCont, var angleThresh)) => ExecuteBrepEdgeClassification(brep, minCont, angleThresh),
                (Mesh mesh, ValueTuple<Continuity, double>(_, var angleThresh)) => ExecuteMeshEdgeClassification(mesh, angleThresh),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            }).Map(r => (object)r),
            [typeof(Topology.AdjacencyData)] = (input, config, _) => ((input, config) switch {
                (Brep brep, ValueTuple<int>(var edgeIdx)) => edgeIdx >= 0 && edgeIdx < brep.Edges.Count
                    ? ExecuteBrepAdjacency(brep, edgeIdx)
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIdx.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                (Mesh mesh, ValueTuple<int>(var edgeIdx)) => edgeIdx >= 0 && edgeIdx < mesh.TopologyEdges.Count
                    ? ExecuteMeshAdjacency(mesh, edgeIdx)
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIdx.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {input.GetType().Name}")),
            }).Map(r => (object)r),
        }.ToFrozenDictionary();

    /// <summary>Unified execution engine for all topology operations using type-based dispatch.</summary>
    [Pure]
    internal static Result<TResult> Execute<T, TResult>(
        T input,
        object? config,
        IGeometryContext context) where T : notnull where TResult : Topology.IResult =>
        Operations.TryGetValue(typeof(TResult), out Func<object, object, IGeometryContext, Result<object>>? dispatcher)
            ? ResultFactory.Create(value: input)
                .Validate(args: TopologyConfig.ValidationModes.TryGetValue(input.GetType(), out V vm) && vm != V.None
                    ? [context, vm,]
                    : null)
                .Bind(_ => dispatcher(input, config ?? new object(), context))
                .Map(r => ((IReadOnlyList<TResult>)r)[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"ResultType: {typeof(TResult).Name}"));

    [Pure]
    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(T input, IGeometryContext context, bool orderLoops, bool _) where T : notnull =>
        Execute<T, Topology.NakedEdgeData>(input, ValueTuple.Create(orderLoops), context);

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(T input, IGeometryContext context, double? tolerance, bool _) where T : notnull =>
        Execute<T, Topology.BoundaryLoopData>(input, ValueTuple.Create(tolerance ?? context.AbsoluteTolerance), context);

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(T input, IGeometryContext context, bool _) where T : notnull =>
        Execute<T, Topology.NonManifoldData>(input, config: null, context);

    [Pure]
    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(T input, IGeometryContext context, bool _) where T : notnull =>
        Execute<T, Topology.ConnectivityData>(input, config: null, context);

    [Pure]
    internal static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(T input, IGeometryContext context, Continuity? minimumContinuity = null, double? angleThreshold = null, bool _ = false) where T : notnull =>
        Execute<T, Topology.EdgeClassificationData>(input, (minimumContinuity ?? Continuity.G1_continuous, angleThreshold ?? context.AngleToleranceRadians), context);

    [Pure]
    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(T input, IGeometryContext context, int edgeIndex, bool _) where T : notnull =>
        Execute<T, Topology.AdjacencyData>(input, ValueTuple.Create(edgeIndex), context);

    [Pure]
    private static Result<IReadOnlyList<Topology.NakedEdgeData>> ComputeNakedEdges(Brep brep, bool orderLoops) =>
        brep.Edges.Count switch {
            0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                new Topology.NakedEdgeData(
                    EdgeCurves: [],
                    EdgeIndices: [],
                    Valences: [],
                    IsOrdered: orderLoops,
                    TotalEdgeCount: 0,
                    TotalLength: 0.0),
            ]),
            _ => Enumerable.Range(0, brep.Edges.Count)
                .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                .ToArray() switch {
                    int[] nakedIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),],
                            EdgeIndices: [.. nakedIndices,],
                            Valences: [.. Enumerable.Repeat(1, nakedIndices.Length),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: brep.Edges.Count,
                            TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())),
                    ]),
                },
        };

    [Pure]
    private static Result<IReadOnlyList<Topology.NakedEdgeData>> ComputeNakedEdges(Mesh mesh, bool orderLoops) =>
        (mesh.GetNakedEdges() ?? []) switch {
            Polyline[] nakedPolylines => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                new Topology.NakedEdgeData(
                    EdgeCurves: [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),],
                    EdgeIndices: [.. Enumerable.Range(0, nakedPolylines.Length),],
                    Valences: [.. Enumerable.Repeat(1, nakedPolylines.Length),],
                    IsOrdered: orderLoops,
                    TotalEdgeCount: mesh.TopologyEdges.Count,
                    TotalLength: nakedPolylines.Sum(pl => pl.Length)),
            ]),
        };

    [Pure]
    private static Result<IReadOnlyList<Topology.BoundaryLoopData>> ExecuteBrepBoundaryLoops(Brep brep, double tol) {
        Curve[] naked = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked).Select(i => brep.Edges[i].DuplicateCurve()),];
        Curve[] joined = naked.Length > 0 ? Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) : [];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData([.. joined,], [.. joined.Select(_ => EmptyIndices),], [.. joined.Select(c => c.GetLength()),], [.. joined.Select(c => c.IsClosed),], tol, naked.Length - joined.Length),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.BoundaryLoopData>> ExecuteMeshBoundaryLoops(Mesh mesh, double tol) {
        Curve[] naked = [.. (mesh.GetNakedEdges() ?? []).Select(pl => pl.ToNurbsCurve()),];
        Curve[] joined = naked.Length > 0 ? Curve.JoinCurves(naked, joinTolerance: tol, preserveDirection: false) : [];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[new Topology.BoundaryLoopData([.. joined,], [.. joined.Select(_ => EmptyIndices),], [.. joined.Select(c => c.GetLength()),], [.. joined.Select(c => c.IsClosed),], tol, naked.Length - joined.Length),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ExecuteBrepNonManifold(Brep brep) {
        IReadOnlyList<int> nm = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),];
        IReadOnlyList<int> vals = [.. nm.Select(i => (int)brep.Edges[i].Valence),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(nm, [], vals, [.. nm.Select(i => brep.Edges[i].PointAtStart),], nm.Count == 0, brep.IsSolid, vals.Count > 0 ? vals.Max() : 0),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ExecuteMeshNonManifold(Mesh mesh) {
        bool manifold = mesh.IsManifold(topologicalTest: true, out bool oriented, out bool _);
        IReadOnlyList<int> nm = [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
        IReadOnlyList<int> vals = [.. nm.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[new Topology.NonManifoldData(nm, [], vals, [.. nm.Select(i => {
            IndexPair v = mesh.TopologyEdges.GetTopologyVertices(i);
            Point3d p1 = mesh.TopologyVertices[v.I];
            Point3d p2 = mesh.TopologyVertices[v.J];
            return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
        }),], manifold, oriented, vals.Count > 0 ? vals.Max() : 0),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteBrepConnectivity(Brep brep) {
        int[] componentIds = new int[brep.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < brep.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseBrepComponent(brep: brep, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),
        ];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(
            BoundingBox.Empty,
            (union, fIdx) => union.IsValid
                ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false))
                : brep.Faces[fIdx].GetBoundingBox(accurate: false))),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: bounds,
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                        .Where(adj => adj != f),
                    ]))
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
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteMeshConnectivity(Mesh mesh) {
        int[] componentIds = new int[mesh.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < mesh.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseMeshComponent(mesh: mesh, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),
        ];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(
            BoundingBox.Empty,
            (union, fIdx) => {
                MeshFace face = mesh.Faces[fIdx];
                BoundingBox fBox = face.IsQuad
                    ? new BoundingBox([
                        mesh.Vertices[face.A],
                        mesh.Vertices[face.B],
                        mesh.Vertices[face.C],
                        mesh.Vertices[face.D],
                    ])
                    : new BoundingBox([
                        mesh.Vertices[face.A],
                        mesh.Vertices[face.B],
                        mesh.Vertices[face.C],
                    ]);
                return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
            })),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: bounds,
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
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteBrepEdgeClassification(Brep brep, Continuity minContinuity, double angleThreshold) {
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => ClassifyBrepEdge(
            edge: brep.Edges[i],
            _: brep,
            minContinuity: minContinuity,
            angleThreshold: angleThreshold)),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => brep.Edges[i].GetLength()),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
            .Select((idx, pos) => (idx, type: classifications[pos]))
            .GroupBy(x => x.type, x => x.idx)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: grouped,
                MinimumContinuity: minContinuity),
        ]);
    }

    [Pure]
    private static Topology.EdgeContinuityType ClassifyBrepEdge(BrepEdge edge, Brep _, Continuity minContinuity, double angleThreshold) =>
        edge.Valence switch {
            EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
            EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
            EdgeAdjacency.Interior => edge.EdgeCurve switch {
                Curve crv when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Curvature,
                Curve crv when edge.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) || crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Smooth,
                _ when minContinuity >= Continuity.G1_continuous => Topology.EdgeContinuityType.Sharp,
                _ => Topology.EdgeContinuityType.Interior,
            },
            _ => Topology.EdgeContinuityType.Sharp,
        };

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteMeshEdgeClassification(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * 0.1;
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => {
            int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
            return connectedFaces.Length switch {
                1 => Topology.EdgeContinuityType.Boundary,
                > 2 => Topology.EdgeContinuityType.NonManifold,
                2 => ((Func<double>)(() => {
                    Vector3d n1 = mesh.FaceNormals[connectedFaces[0]];
                    Vector3d n2 = mesh.FaceNormals[connectedFaces[1]];
                    return n1.IsValid && n2.IsValid ? Vector3d.VectorAngle(n1, n2) : Math.PI;
                }))() switch {
                    double angle when Math.Abs(angle) < curvatureThreshold => Topology.EdgeContinuityType.Curvature,
                    double angle when Math.Abs(angle) < angleThreshold => Topology.EdgeContinuityType.Smooth,
                    _ => Topology.EdgeContinuityType.Sharp,
                },
                _ => Topology.EdgeContinuityType.Sharp,
            };
        }),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => {
            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
            return mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]);
        }),
        ];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
            .Select((idx, pos) => (idx, type: classifications[pos]))
            .GroupBy(x => x.type, x => x.idx)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: grouped,
                MinimumContinuity: Continuity.C0_continuous),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ExecuteBrepAdjacency(Brep brep, int edgeIndex) {
        BrepEdge e = brep.Edges[edgeIndex];
        IReadOnlyList<int> af = [.. e.AdjacentFaces(),];
        IReadOnlyList<Vector3d> norms = [.. af.Select(i => brep.Faces[i].NormalAt(brep.Faces[i].Domain(0).Mid, brep.Faces[i].Domain(1).Mid)),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(edgeIndex, af, norms, norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, e.Valence == EdgeAdjacency.Interior, e.Valence == EdgeAdjacency.Naked),]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ExecuteMeshAdjacency(Mesh mesh, int edgeIndex) {
        int[] af = mesh.TopologyEdges.GetConnectedFaces(edgeIndex);
        IReadOnlyList<Vector3d> norms = [.. af.Select(i => mesh.FaceNormals[i]),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[new Topology.AdjacencyData(edgeIndex, [.. af,], norms, norms.Count == 2 ? Vector3d.VectorAngle(norms[0], norms[1]) : 0.0, af.Length == 2, af.Length == 1),]);
    }
}

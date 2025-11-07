using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology computation engine with FrozenDictionary dispatch and inline strategy execution.</summary>
internal static class TopologyCompute {
    internal static readonly FrozenDictionary<(System.Type, TopologyMode), (V Mode, System.Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> StrategyConfig =
        new System.Collections.Generic.Dictionary<(System.Type, TopologyMode), (V, System.Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
            [(typeof(Brep), TopologyMode.NakedEdges)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    (int idx, Curve curve)[] nakedEdges = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == 1)
                        .Select(i => (i, brep.Edges[i].DuplicateCurve())),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: [.. nakedEdges.Select(e => e.curve),],
                        EdgeIndices: [.. nakedEdges.Select(e => e.idx),],
                        Valences: [.. nakedEdges.Select(_ => 1),],
                        IsOrdered: args is [bool order, ..] && order,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: nakedEdges.Sum(e => e.curve.GetLength())));
                }),

            [(typeof(Mesh), TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    (int idx, Curve curve)[] nakedEdges = [.. (mesh.GetNakedEdges() ?? []).Select(i => {
                        (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                        return (i, new Polyline([
                            (Point3d)mesh.TopologyVertices[vi],
                            (Point3d)mesh.TopologyVertices[vj],
                        ]).ToNurbsCurve());
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: [.. nakedEdges.Select(e => e.curve),],
                        EdgeIndices: [.. nakedEdges.Select(e => e.idx),],
                        Valences: [.. nakedEdges.Select(_ => 1),],
                        IsOrdered: args is [bool order, ..] && order,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: nakedEdges.Sum(e => e.curve.GetLength())));
                }),

            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args is [double tolerance, ..] ? tolerance : ctx.AbsoluteTolerance;
                    Curve[] nakedCurves = [.. brep.Edges
                        .Where(e => e.Valence == 1)
                        .Select(e => e.DuplicateCurve()),];
                    Curve[] joined = Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false);
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(_ => (IReadOnlyList<int>)[],)],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
                }),

            [(typeof(Brep), TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => {
                    Brep brep = (Brep)g;
                    int[] componentIds = new int[brep.Faces.Count];
                    System.Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    for (int seed = 0; seed < brep.Faces.Count; seed++) {
                        componentCount = componentIds[seed] switch {
                            -1 => (() => {
                                System.Collections.Generic.Queue<int> queue = new([seed,]);
                                componentIds[seed] = componentCount;
                                while (queue.Count > 0) {
                                    int faceIdx = queue.Dequeue();
                                    foreach (int adjFace in brep.Faces[faceIdx].AdjacentEdges()
                                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                        .Where(f => componentIds[f] == -1)) {
                                        componentIds[adjFace] = componentCount;
                                        queue.Enqueue(adjFace);
                                    }
                                }
                                return componentCount + 1;
                            })(),
                            _ => componentCount,
                        };
                    }
                    IReadOnlyList<IReadOnlyList<int>>[] components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => brep.Faces[i].GetBoundingBox(accurate: false)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: [.. Enumerable.Range(0, brep.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                .Where(adj => adj != f),])),].ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => {
                    Brep brep = (Brep)g;
                    (int idx, int valence, Point3d location)[] nonManifold = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence > 2)
                        .Select(i => (i, brep.Edges[i].Valence, brep.Edges[i].PointAt(0.5))),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: [.. nonManifold.Select(e => e.idx),],
                        VertexIndices: [],
                        Valences: [.. nonManifold.Select(e => e.valence),],
                        Locations: [.. nonManifold.Select(e => e.location),],
                        IsManifold: nonManifold.Length == 0,
                        IsOrientable: brep.IsSolid,
                        MaxValence: nonManifold.Length > 0 ? nonManifold.Max(e => e.valence) : 0));
                }),

            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => {
                    Mesh mesh = (Mesh)g;
                    bool isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _);
                    (int idx, int valence, Point3d location)[] nonManifold = isManifold
                        ? []
                        : [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                            .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length != 2)
                            .Select(i => {
                                (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                                Point3d pi = (Point3d)mesh.TopologyVertices[vi];
                                Point3d pj = (Point3d)mesh.TopologyVertices[vj];
                                return (i, mesh.TopologyEdges.GetConnectedFaces(i).Length, 0.5 * (pi + pj));
                            }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: [.. nonManifold.Select(e => e.idx),],
                        VertexIndices: [],
                        Valences: [.. nonManifold.Select(e => e.valence),],
                        Locations: [.. nonManifold.Select(e => e.location),],
                        IsManifold: isManifold,
                        IsOrientable: isOriented,
                        MaxValence: nonManifold.Length > 0 ? nonManifold.Max(e => e.valence) : 0));
                }),

            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    Continuity minContinuity = args is [Continuity c, ..] ? c : Continuity.G1_continuous;
                    (int idx, EdgeContinuityType classification, double measure)[] edges = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Select(i => {
                            BrepEdge edge = brep.Edges[i];
                            EdgeContinuityType classification = edge.Valence switch {
                                1 => EdgeContinuityType.Boundary,
                                > 2 => EdgeContinuityType.NonManifold,
                                2 => edge.EdgeCurve.IsContinuous(minContinuity)
                                    ? minContinuity >= Continuity.G2_continuous ? EdgeContinuityType.Curvature
                                    : minContinuity >= Continuity.G1_continuous ? EdgeContinuityType.Smooth
                                    : EdgeContinuityType.Interior
                                    : EdgeContinuityType.Sharp,
                                _ => EdgeContinuityType.Sharp,
                            };
                            double measure = edge.Valence == 2 && edge.AdjacentFaces() is { Length: 2 } faces
                                ? System.Math.Acos(System.Math.Clamp(
                                    brep.Faces[faces[0]].NormalAt(0.5, 0.5) * brep.Faces[faces[1]].NormalAt(0.5, 0.5),
                                    -1.0, 1.0))
                                : 0.0;
                            return (i, classification, measure);
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: [.. edges.Select(e => e.idx),],
                        Classifications: [.. edges.Select(e => e.classification),],
                        ContinuityMeasures: [.. edges.Select(e => e.measure),],
                        GroupedByType: [.. Enumerable.Range(0, 6)
                            .Select(type => ((EdgeContinuityType)type, (IReadOnlyList<int>)[.. edges
                                .Where(e => (int)e.classification == type)
                                .Select(e => e.idx),])),]
                            .ToFrozenDictionary(x => x.Item1, x => x.Item2),
                        MinimumContinuity: minContinuity));
                }),
        }.ToFrozenDictionary();
}

/// <summary>Naked edge analysis result containing edge curves and indices.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"NakedEdges: {EdgeCurves.Count}/{TotalEdgeCount} | L={TotalLength:F3} | Ordered={IsOrdered}");
}

/// <summary>Boundary loop analysis result with closed loop curves.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record BoundaryLoopData(
    IReadOnlyList<Curve> Loops,
    IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
    IReadOnlyList<double> LoopLengths,
    IReadOnlyList<bool> IsClosedPerLoop,
    double JoinTolerance,
    int FailedJoins) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"BoundaryLoops: {Loops.Count} | FailedJoins={FailedJoins} | Tol={JoinTolerance:E2}");
}

/// <summary>Non-manifold topology analysis result with diagnostic data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NonManifoldData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> VertexIndices,
    IReadOnlyList<int> Valences,
    IReadOnlyList<Point3d> Locations,
    bool IsManifold,
    bool IsOrientable,
    int MaxValence) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={EdgeIndices.Count} | Verts={VertexIndices.Count} | MaxVal={MaxValence}");
}

/// <summary>Connected component analysis result with adjacency graph data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {TotalComponents} components | Largest={ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
}

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
                    bool orderLoops = args.Length > 0 && args[0] is bool b && b;
                    IReadOnlyList<int> nakedIndices = [.. System.Linq.Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == 1),];
                    IReadOnlyList<Curve> curves = [.. nakedIndices
                        .Select(i => brep.Edges[i].DuplicateCurve()),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: nakedIndices,
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: orderLoops,
                        TotalEdgeCount: brep.Edges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Mesh), TopologyMode.NakedEdges)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                    IReadOnlyList<Curve> curves = [.. nakedIndices.Select(i => {
                        (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                        return new Polyline([
                            (Point3d)mesh.TopologyVertices[vi],
                            (Point3d)mesh.TopologyVertices[vj],
                        ]).ToNurbsCurve();
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                        EdgeCurves: curves,
                        EdgeIndices: [.. nakedIndices,],
                        Valences: [.. nakedIndices.Select(_ => 1),],
                        IsOrdered: args.Length > 0 && args[0] is bool b && b,
                        TotalEdgeCount: mesh.TopologyEdges.Count,
                        TotalLength: curves.Sum(c => c.GetLength())));
                }),

            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
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
                        componentCount = componentIds[seed] != -1 ? componentCount : (() => {
                            System.Collections.Generic.Queue<int> queue = new([seed,]);
                            componentIds[seed] = componentCount;
                            while (queue.Count > 0) {
                                int faceIdx = queue.Dequeue();
                                foreach (int edgeIdx in brep.Faces[faceIdx].AdjacentEdges()) {
                                    foreach (int adjFace in brep.Edges[edgeIdx].AdjacentFaces()) {
                                        componentIds[adjFace] = componentIds[adjFace] == -1 ? (() => {
                                            queue.Enqueue(adjFace);
                                            return componentCount;
                                        })() : componentIds[adjFace];
                                    }
                                }
                            }
                            return componentCount + 1;
                        })();
                    }
                    IReadOnlyList<IReadOnlyList<int>>[] components = [.. System.Linq.Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. System.Linq.Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: [.. components.Select(c => BoundingBox.Union(c.Select(i => brep.Faces[i].GetBoundingBox(accurate: false)))),],
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: [.. System.Linq.Enumerable.Range(0, brep.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                .Where(adj => adj != f),])),].ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, ctx, _) => {
                    Brep brep = (Brep)g;
                    IReadOnlyList<int> nonManifoldEdgeIndices = [.. System.Linq.Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdgeIndices
                        .Select(i => brep.Edges[i].Valence),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdgeIndices
                        .Select(i => brep.Edges[i].PointAt(0.5)),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdgeIndices,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: nonManifoldEdgeIndices.Count == 0,
                        IsOrientable: brep.IsSolid,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),

            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, _) => {
                    Mesh mesh = (Mesh)g;
                    bool isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool isConnected);
                    IReadOnlyList<int> nonManifoldEdgeIndices = isManifold
                        ? []
                        : [.. System.Linq.Enumerable.Range(0, mesh.TopologyEdges.Count)
                            .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length != 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdgeIndices
                        .Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdgeIndices
                        .Select(i => {
                            (int vi, int vj) = mesh.TopologyEdges.GetTopologyVertices(i);
                            Point3d pi = (Point3d)mesh.TopologyVertices[vi];
                            Point3d pj = (Point3d)mesh.TopologyVertices[vj];
                            return pi + 0.5 * (pj - pi);
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdgeIndices,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: isManifold,
                        IsOrientable: isOriented,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),

            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    Continuity minContinuity = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous;
                    IReadOnlyList<int> edgeIndices = [.. System.Linq.Enumerable.Range(0, brep.Edges.Count),];
                    IReadOnlyList<EdgeContinuityType> classifications = [.. edgeIndices.Select(i => {
                        BrepEdge edge = brep.Edges[i];
                        return edge.Valence switch {
                            1 => EdgeContinuityType.Boundary,
                            > 2 => EdgeContinuityType.NonManifold,
                            2 => edge.EdgeCurve.IsContinuous(minContinuity) switch {
                                true => minContinuity >= Continuity.G2_continuous ? EdgeContinuityType.Curvature :
                                        minContinuity >= Continuity.G1_continuous ? EdgeContinuityType.Smooth :
                                        EdgeContinuityType.Interior,
                                false => EdgeContinuityType.Sharp,
                            },
                            _ => EdgeContinuityType.Sharp,
                        };
                    }),];
                    IReadOnlyList<double> measures = [.. edgeIndices.Select(i => {
                        BrepEdge edge = brep.Edges[i];
                        return edge.Valence == 2 && edge.AdjacentFaces().Length == 2 ? (() => {
                            int[] faces = edge.AdjacentFaces();
                            Vector3d n1 = brep.Faces[faces[0]].NormalAt(0.5, 0.5);
                            Vector3d n2 = brep.Faces[faces[1]].NormalAt(0.5, 0.5);
                            return System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, n1 * n2)));
                        })() : 0.0;
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: edgeIndices,
                        Classifications: classifications,
                        ContinuityMeasures: measures,
                        GroupedByType: [.. System.Linq.Enumerable.Range(0, 6)
                            .Select(type => ((EdgeContinuityType)type, (IReadOnlyList<int>)[.. edgeIndices
                                .Where(i => (int)classifications[i] == type),])),]
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

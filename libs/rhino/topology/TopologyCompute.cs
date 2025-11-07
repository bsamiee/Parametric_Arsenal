using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Naked edge analysis result containing edge curves and indices with topology metadata.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Result records grouped semantically in TopologyCompute.cs")]
public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"NakedEdges: {EdgeCurves.Count}/{TotalEdgeCount} | L={TotalLength:F3} | Ordered={IsOrdered}");
}

/// <summary>Boundary loop analysis result with closed loop curves and join diagnostics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Result records grouped semantically in TopologyCompute.cs")]
public sealed record BoundaryLoopData(
    IReadOnlyList<Curve> Loops,
    IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
    IReadOnlyList<double> LoopLengths,
    IReadOnlyList<bool> IsClosedPerLoop,
    double JoinTolerance,
    int FailedJoins) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"BoundaryLoops: {Loops.Count} | FailedJoins={FailedJoins} | Tol={JoinTolerance:E2}");
}

/// <summary>Non-manifold topology analysis result with diagnostic data for irregular vertices and edges.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Result records grouped semantically in TopologyCompute.cs")]
public sealed record NonManifoldData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> VertexIndices,
    IReadOnlyList<int> Valences,
    IReadOnlyList<Point3d> Locations,
    bool IsManifold,
    bool IsOrientable,
    int MaxValence) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={EdgeIndices.Count} | Verts={VertexIndices.Count} | MaxVal={MaxValence}");
}

/// <summary>Connected component analysis result with adjacency graph and spatial bounds.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Result records grouped semantically in TopologyCompute.cs")]
public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {TotalComponents} components | Largest={ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type with geometric measures.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Result records grouped semantically in TopologyCompute.cs")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0009:Member access should be qualified", Justification = "this. qualification not required in records")]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
}

/// <summary>Internal topology computation algorithms with FrozenDictionary-based strategy dispatch.</summary>
internal static class TopologyCompute {
    /// <summary>Strategy configuration mapping (Type, TopologyMode) pairs to validation and computation functions.</summary>
    internal static readonly FrozenDictionary<(Type, TopologyMode), (V Mode, Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> StrategyConfig =
        new Dictionary<(Type, TopologyMode), (V, Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
            [(typeof(Brep), TopologyMode.NakedEdges)] = (
                V.Standard | V.Topology,
                (g, _, args) => {
                    Brep brep = (Brep)g;
                    bool orderLoops = args.Length > 0 && args[0] is bool b && b;
                    IReadOnlyList<int> nakedIndices = [.. Enumerable.Range(0, brep.Edges.Count).Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked),];
                    IReadOnlyList<Curve> curves = [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),];
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
                (g, _, args) => {
                    Mesh mesh = (Mesh)g;
                    Polyline[] nakedPolylines = mesh.GetNakedEdges() ?? [];
                    Curve[] nakedCurves = [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),];
                    return nakedCurves.Length switch {
                        0 => ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                            EdgeCurves: [],
                            EdgeIndices: [],
                            Valences: [],
                            IsOrdered: args.Length > 0 && args[0] is bool b && b,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: 0.0)),
                        _ => ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                            EdgeCurves: [.. nakedCurves,],
                            EdgeIndices: [.. Enumerable.Range(0, nakedCurves.Length),],
                            Valences: [.. Enumerable.Range(0, nakedCurves.Length).Select(_ => 1),],
                            IsOrdered: args.Length > 0 && args[0] is bool b2 && b2,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: nakedCurves.Sum(c => c.GetLength()))),
                    };
                }),

            [(typeof(Brep), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.Topology,
                (g, ctx, args) => {
                    Brep brep = (Brep)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    Curve[] nakedCurves = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                        .Select(i => brep.Edges[i].DuplicateCurve()),];
                    Curve[] joined = nakedCurves.Length > 0
                        ? Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false)
                        : [];
#pragma warning disable IDE0301 // Collection initialization syntax not valid in lambda expression
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(static _ => (IReadOnlyList<int>)Array.Empty<int>()),],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
#pragma warning restore IDE0301
                }),

            [(typeof(Mesh), TopologyMode.BoundaryLoops)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double tol = args.Length > 0 && args[0] is double d ? d : ctx.AbsoluteTolerance;
                    Polyline[] nakedPolylines = mesh.GetNakedEdges() ?? [];
                    Curve[] nakedCurves = [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),];
                    Curve[] joined = nakedCurves.Length > 0
                        ? Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false)
                        : [];
#pragma warning disable IDE0301 // Collection initialization syntax not valid in lambda expression
                    return ResultFactory.Create(value: (Topology.IResult)new BoundaryLoopData(
                        Loops: [.. joined,],
                        EdgeIndicesPerLoop: [.. joined.Select(static _ => (IReadOnlyList<int>)Array.Empty<int>()),],
                        LoopLengths: [.. joined.Select(c => c.GetLength()),],
                        IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                        JoinTolerance: tol,
                        FailedJoins: nakedCurves.Length - joined.Length));
#pragma warning restore IDE0301
                }),

            [(typeof(Brep), TopologyMode.NonManifold)] = (
                V.Standard | V.Topology,
                (g, _, _) => {
                    Brep brep = (Brep)g;
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),];
                    IReadOnlyList<int> valences = [.. Enumerable.Range(0, nonManifoldEdges.Count).Select(_ => 3),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdges
                        .Select(i => brep.Edges[i].PointAtStart),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: nonManifoldEdges.Count == 0,
                        IsOrientable: brep.IsSolid,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),

            [(typeof(Mesh), TopologyMode.NonManifold)] = (
                V.Standard | V.MeshSpecific,
                (g, _, _) => {
                    Mesh mesh = (Mesh)g;
                    bool isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool hasBoundary);
                    IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                        .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),];
                    IReadOnlyList<int> valences = [.. nonManifoldEdges
                        .Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
                    IReadOnlyList<Point3d> locations = [.. nonManifoldEdges
                        .Select(i => {
                            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
                            Point3d p1 = mesh.TopologyVertices[verts.I];
                            Point3d p2 = mesh.TopologyVertices[verts.J];
                            return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
                        }),];
                    return ResultFactory.Create(value: (Topology.IResult)new NonManifoldData(
                        EdgeIndices: nonManifoldEdges,
                        VertexIndices: [],
                        Valences: valences,
                        Locations: locations,
                        IsManifold: isManifold,
                        IsOrientable: isOriented,
                        MaxValence: valences.Count > 0 ? valences.Max() : 0));
                }),

            [(typeof(Brep), TopologyMode.Connectivity)] = (
                V.Standard | V.Topology,
                (g, _, _) => {
                    Brep brep = (Brep)g;
                    int[] componentIds = new int[brep.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    for (int seed = 0; seed < brep.Faces.Count; seed++) {
                        if (componentIds[seed] != -1) {
                            continue;
                        }
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = componentCount;
                        while (queue.Count > 0) {
                            int faceIdx = queue.Dequeue();
                            foreach (int edgeIdx in brep.Faces[faceIdx].AdjacentEdges()) {
                                foreach (int adjFace in brep.Edges[edgeIdx].AdjacentFaces()) {
                                    if (componentIds[adjFace] == -1) {
                                        componentIds[adjFace] = componentCount;
                                        queue.Enqueue(adjFace);
                                    }
                                }
                            }
                        }
                        componentCount++;
                    }
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),];
                    IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => {
                        BoundingBox union = BoundingBox.Empty;
                        foreach (int fIdx in c) {
                            BoundingBox fBox = brep.Faces[fIdx].GetBoundingBox(accurate: false);
                            union = union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
                        }
                        return union;
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: bounds,
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                                .SelectMany(e => brep.Edges[e].AdjacentFaces())
                                .Where(adj => adj != f),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Mesh), TopologyMode.Connectivity)] = (
                V.Standard | V.MeshSpecific,
                (g, _, _) => {
                    Mesh mesh = (Mesh)g;
                    int[] componentIds = new int[mesh.Faces.Count];
                    Array.Fill(componentIds, -1);
                    int componentCount = 0;
                    for (int seed = 0; seed < mesh.Faces.Count; seed++) {
                        if (componentIds[seed] != -1) {
                            continue;
                        }
                        Queue<int> queue = new([seed,]);
                        componentIds[seed] = componentCount;
                        while (queue.Count > 0) {
                            int faceIdx = queue.Dequeue();
                            foreach (int adjFace in mesh.Faces.AdjacentFaces(faceIdx)) {
                                if (adjFace >= 0 && componentIds[adjFace] == -1) {
                                    componentIds[adjFace] = componentCount;
                                    queue.Enqueue(adjFace);
                                }
                            }
                        }
                        componentCount++;
                    }
                    IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
                        .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),];
                    IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => {
                        BoundingBox union = BoundingBox.Empty;
                        foreach (int fIdx in c) {
                            Point3d p1 = mesh.Vertices[mesh.Faces[fIdx].A];
                            Point3d p2 = mesh.Vertices[mesh.Faces[fIdx].B];
                            Point3d p3 = mesh.Vertices[mesh.Faces[fIdx].C];
                            BoundingBox fBox = new(p1, p2);
                            fBox.Union(p3);
                            union = union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
                        }
                        return union;
                    }),];
                    return ResultFactory.Create(value: (Topology.IResult)new ConnectivityData(
                        ComponentIndices: components,
                        ComponentSizes: [.. components.Select(c => c.Count),],
                        ComponentBounds: bounds,
                        TotalComponents: componentCount,
                        IsFullyConnected: componentCount == 1,
                        AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                            .Select(f => (f, (IReadOnlyList<int>)[.. mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0),]))
                            .ToFrozenDictionary(x => x.f, x => x.Item2)));
                }),

            [(typeof(Brep), TopologyMode.EdgeClassification)] = (
                V.Standard | V.Topology,
                (g, _, args) => {
                    Brep brep = (Brep)g;
                    Continuity minContinuity = args.Length > 0 && args[0] is Continuity c ? c : Continuity.G1_continuous;
                    IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
                    IReadOnlyList<EdgeContinuityType> classifications = [.. edgeIndices.Select(i => {
                        BrepEdge edge = brep.Edges[i];
                        return edge.Valence switch {
                            EdgeAdjacency.Naked => EdgeContinuityType.Boundary,
                            EdgeAdjacency.NonManifold => EdgeContinuityType.NonManifold,
                            EdgeAdjacency.Interior => edge.IsSmoothManifoldEdge(angleToleranceRadians: 0.01) switch {
                                true => EdgeContinuityType.Smooth,
                                false => EdgeContinuityType.Sharp,
                            },
                            _ => EdgeContinuityType.Sharp,
                        };
                    }),];
                    IReadOnlyList<double> measures = [.. edgeIndices.Select(i => brep.Edges[i].GetLength()),];
                    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
                        .Select((idx, pos) => (idx, type: classifications[pos]))
                        .GroupBy(x => x.type, x => x.idx)
                        .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: edgeIndices,
                        Classifications: classifications,
                        ContinuityMeasures: measures,
                        GroupedByType: grouped,
                        MinimumContinuity: minContinuity));
                }),

            [(typeof(Mesh), TopologyMode.EdgeClassification)] = (
                V.Standard | V.MeshSpecific,
                (g, ctx, args) => {
                    Mesh mesh = (Mesh)g;
                    double angleThreshold = args.Length > 0 && args[0] is double a ? a : ctx.AngleToleranceRadians;
                    IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
                    IReadOnlyList<EdgeContinuityType> classifications = [.. edgeIndices.Select(i => {
                        int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
                        return connectedFaces.Length switch {
                            1 => EdgeContinuityType.Boundary,
                            > 2 => EdgeContinuityType.NonManifold,
                            2 => CalculateDihedralAngle(mesh, connectedFaces[0], connectedFaces[1]) switch {
                                double angle when Math.Abs(angle) < angleThreshold => EdgeContinuityType.Smooth,
                                _ => EdgeContinuityType.Sharp,
                            },
                            _ => EdgeContinuityType.Sharp,
                        };
                    }),];
                    IReadOnlyList<double> measures = [.. edgeIndices.Select(i => {
                        IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
                        return mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]);
                    }),];
                    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
                        .Select((idx, pos) => (idx, type: classifications[pos]))
                        .GroupBy(x => x.type, x => x.idx)
                        .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
                    return ResultFactory.Create(value: (Topology.IResult)new EdgeClassificationData(
                        EdgeIndices: edgeIndices,
                        Classifications: classifications,
                        ContinuityMeasures: measures,
                        GroupedByType: grouped,
                        MinimumContinuity: Continuity.C0_continuous));
                }),
        }.ToFrozenDictionary();

    /// <summary>Calculates dihedral angle between two adjacent mesh faces using face normals.</summary>
    [Pure]
    private static double CalculateDihedralAngle(Mesh mesh, int faceIdx1, int faceIdx2) {
        Vector3d n1 = mesh.FaceNormals[faceIdx1];
        Vector3d n2 = mesh.FaceNormals[faceIdx2];
        return n1.IsValid && n2.IsValid
            ? Vector3d.VectorAngle(n1, n2)
            : Math.PI;
    }
}

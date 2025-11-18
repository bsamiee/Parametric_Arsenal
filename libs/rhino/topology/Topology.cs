using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology analysis via type-based dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker for polymorphic dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult;

    /// <summary>Edge continuity classification: G0/G1/G2, interior, boundary, non-manifold.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct EdgeContinuityType(byte Value) {
        /// <summary>G0 sharp edge with discontinuous tangent.</summary>
        public static readonly EdgeContinuityType Sharp = new(0);
        /// <summary>G1 smooth edge with continuous tangent.</summary>
        public static readonly EdgeContinuityType Smooth = new(1);
        /// <summary>G2 edge with continuous curvature.</summary>
        public static readonly EdgeContinuityType Curvature = new(2);
        /// <summary>Interior manifold edge with valence 2.</summary>
        public static readonly EdgeContinuityType Interior = new(3);
        /// <summary>Boundary naked edge with valence 1.</summary>
        public static readonly EdgeContinuityType Boundary = new(4);
        /// <summary>Non-manifold edge with valence greater than 2.</summary>
        public static readonly EdgeContinuityType NonManifold = new(5);
    }

    /// <summary>Naked edge analysis: boundary curves, indices, valences, and metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(
        IReadOnlyList<Curve> EdgeCurves,
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> Valences,
        bool IsOrdered,
        int TotalEdgeCount,
        double TotalLength) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Boundary loops from joined naked edges with closure and join diagnostics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(
        IReadOnlyList<Curve> Loops,
        IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
        IReadOnlyList<double> LoopLengths,
        IReadOnlyList<bool> IsClosedPerLoop,
        double JoinTolerance,
        int FailedJoins) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
    }

    /// <summary>Non-manifold detection with edge/vertex indices, valences, and locations.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NonManifoldData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> VertexIndices,
        IReadOnlyList<int> Valences,
        IReadOnlyList<Point3d> Locations,
        bool IsManifold,
        bool IsOrientable,
        int MaxValence) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Connected components via BFS with adjacency graph and bounds.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(
        IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
        IReadOnlyList<int> ComponentSizes,
        IReadOnlyList<BoundingBox> ComponentBounds,
        int TotalComponents,
        bool IsFullyConnected,
        FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Edge continuity classification with measures grouped by type.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<EdgeContinuityType> Classifications,
        IReadOnlyList<double> ContinuityMeasures,
        FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
        Continuity MinimumContinuity) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
    }

    /// <summary>Edge-face adjacency with normals, dihedral angle, and manifold status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(
        int EdgeIndex,
        IReadOnlyList<int> AdjacentFaceIndices,
        IReadOnlyList<Vector3d> FaceNormals,
        double DihedralAngle,
        bool IsManifold,
        bool IsBoundary) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsBoundary
            ? string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: Boundary (valence=1)")
            : this.IsManifold
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={RhinoMath.ToDegrees(this.DihedralAngle):F1}°")
                : string.Create(CultureInfo.InvariantCulture, $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})");
    }

    /// <summary>Vertex topology with connected edges/faces, valence, and manifold status.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record VertexData(
        int VertexIndex,
        Point3d Location,
        IReadOnlyList<int> ConnectedEdgeIndices,
        IReadOnlyList<int> ConnectedFaceIndices,
        int Valence,
        bool IsBoundary,
        bool IsManifold) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Vertex[{this.VertexIndex}]: Valence={this.Valence} | {(this.IsBoundary ? "Boundary" : "Interior")} | {(this.IsManifold ? "Manifold" : "NonManifold")}");
    }

    /// <summary>Ngon analysis: face membership, boundary edges, and centroids.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NgonTopologyData(
        IReadOnlyList<int> NgonIndices,
        IReadOnlyList<IReadOnlyList<int>> FaceIndicesPerNgon,
        IReadOnlyList<IReadOnlyList<int>> BoundaryEdgesPerNgon,
        IReadOnlyList<Point3d> NgonCenters,
        IReadOnlyList<int> EdgeCountPerNgon,
        int TotalNgons,
        int TotalFaces) : IResult {
        [Pure]
        private string DebuggerDisplay => this.TotalNgons == 0
            ? "NgonTopology: No ngons detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NgonTopology: {this.TotalNgons} ngons | {this.TotalFaces} faces | AvgValence={this.EdgeCountPerNgon.Average():F1}");
    }

    /// <summary>Extract naked boundary edges with optional loop ordering.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NakedEdgeData> GetNakedEdges<T>(
        T geometry,
        IGeometryContext context,
        bool orderLoops = false) where T : notnull =>
        TopologyCore.ExecuteNakedEdges(input: geometry, context: context, orderLoops: orderLoops);

    /// <summary>Join naked edges into closed boundary loops with tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BoundaryLoopData> GetBoundaryLoops<T>(
        T geometry,
        IGeometryContext context,
        double? tolerance = null) where T : notnull =>
        TopologyCore.ExecuteBoundaryLoops(input: geometry, context: context, tolerance: tolerance);

    /// <summary>Detect non-manifold vertices and edges with valences.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NonManifoldData> GetNonManifoldData<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNonManifold(input: geometry, context: context);

    /// <summary>Compute connected components via BFS with adjacency graph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ConnectivityData> GetConnectivity<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteConnectivity(input: geometry, context: context);

    /// <summary>Classify edges by G0/G1/G2 continuity with angle threshold.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<EdgeClassificationData> ClassifyEdges<T>(
        T geometry,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        double? angleThreshold = null) where T : notnull =>
        TopologyCore.ExecuteEdgeClassification(input: geometry, context: context, minimumContinuity: minimumContinuity, angleThreshold: angleThreshold);

    /// <summary>Get adjacent faces and dihedral angle for edge index.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AdjacencyData> GetAdjacency<T>(
        T geometry,
        IGeometryContext context,
        int edgeIndex) where T : notnull =>
        TopologyCore.ExecuteAdjacency(input: geometry, context: context, edgeIndex: edgeIndex);

    /// <summary>Get vertex topology with connected edges, faces, and valence.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VertexData> GetVertexData<T>(
        T geometry,
        IGeometryContext context,
        int vertexIndex) where T : notnull =>
        TopologyCore.ExecuteVertexData(input: geometry, context: context, vertexIndex: vertexIndex);

    /// <summary>Extract ngon topology with face membership and boundaries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NgonTopologyData> GetNgonTopology<T>(
        T geometry,
        IGeometryContext context) where T : notnull =>
        TopologyCore.ExecuteNgonTopology(input: geometry, context: context);

    /// <summary>Diagnose topology with edge gaps, near-misses, and typed repair strategy suggestions.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TopologyHealingStrategy.Diagnosis> DiagnoseTopology(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.Diagnose(brep: brep, context: context);

    /// <summary>Progressive healing with ordered strategy list and automatic rollback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Brep Healed, TopologyHealingStrategy Strategy, bool Success)> HealTopology(
        Brep brep,
        IReadOnlyList<TopologyHealingStrategy> strategies,
        IGeometryContext context) =>
        TopologyCompute.Heal(brep: brep, strategies: strategies, context: context);

    /// <summary>Extract genus, holes, handles, and solid classification via Euler.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractTopologicalFeatures(
        Brep brep,
        IGeometryContext context) =>
        TopologyCompute.ExtractFeatures(brep: brep, context);

    /// <summary>Topology diagnosis, progressive healing, and topological feature extraction.</summary>
    [Pure]
    private static class TopologyCompute {
        internal static Result<TopologyHealingStrategy.Diagnosis> Diagnose(
            Brep brep,
            IGeometryContext context) =>
            !brep.IsValidTopology(out string topologyLog)
                ? ResultFactory.Create<TopologyHealingStrategy.Diagnosis>(
                    error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {topologyLog}"))
                : ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
                    .Bind(validBrep => ((Func<Result<TopologyHealingStrategy.Diagnosis>>)(() => {
                        (int Index, Point3d Start, Point3d End)[] nakedEdges = [.. Enumerable.Range(0, validBrep.Edges.Count)
                            .Where(i => validBrep.Edges[i].Valence == EdgeAdjacency.Naked && validBrep.Edges[i].EdgeCurve is not null)
                            .Select(i => (Index: i, Start: validBrep.Edges[i].PointAtStart, End: validBrep.Edges[i].PointAtEnd)),
                        ];

                        double[] gaps = nakedEdges.Length is > 0 and < TopologyConfig.MaxEdgesForNearMissAnalysis
                            ? [.. (from e1 in nakedEdges
                                   from e2 in nakedEdges
                                   where e1.Index != e2.Index
                                   from dist in new[] { e1.Start.DistanceTo(e2.Start), e1.Start.DistanceTo(e2.End), e1.End.DistanceTo(e2.Start), e1.End.DistanceTo(e2.End), }
                                   where dist > context.AbsoluteTolerance && dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier
                                   select dist),
                            ]
                            : [];

                        int nakedEdgeCount = nakedEdges.Length;
                        int nonManifoldEdgeCount = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.NonManifold);

                        (int EdgeA, int EdgeB, double Distance)[] nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
                            ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
                                   from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                                   let edgeI = validBrep.Edges[nakedEdges[i].Index]
                                   let edgeJ = validBrep.Edges[nakedEdges[j].Index]
                                   let dist = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue
                                   where dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier && dist > context.AbsoluteTolerance
                                   select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, Distance: dist)),
                            ]
                            : [];

                        TopologyHealingStrategy[] strategies = (nakedEdgeCount, nonManifoldEdgeCount, nearMisses.Length) switch {
                            ( > 0, > 0, > 0) => [new TopologyHealingStrategy.ConservativeRepair(), new TopologyHealingStrategy.ModerateJoin(), new TopologyHealingStrategy.AggressiveJoin(), new TopologyHealingStrategy.CombinedRepairAndJoin(),],
                            ( > 0, > 0, _) => [new TopologyHealingStrategy.ConservativeRepair(), new TopologyHealingStrategy.ModerateJoin(), new TopologyHealingStrategy.AggressiveJoin(),],
                            ( > 0, _, > 0) => [new TopologyHealingStrategy.ConservativeRepair(), new TopologyHealingStrategy.ModerateJoin(), new TopologyHealingStrategy.CombinedRepairAndJoin(),],
                            (_, > 0, > 0) => [new TopologyHealingStrategy.AggressiveJoin(), new TopologyHealingStrategy.CombinedRepairAndJoin(),],
                            ( > 0, _, _) => [new TopologyHealingStrategy.ConservativeRepair(), new TopologyHealingStrategy.ModerateJoin(),],
                            (_, > 0, _) => [new TopologyHealingStrategy.AggressiveJoin(),],
                            (_, _, > 0) => [new TopologyHealingStrategy.CombinedRepairAndJoin(),],
                            _ => [],
                        };

                        return ResultFactory.Create(value: new TopologyHealingStrategy.Diagnosis(EdgeGaps: gaps, NearMisses: nearMisses, SuggestedStrategies: strategies));
                    }))());

        internal static Result<(Brep Healed, TopologyHealingStrategy Strategy, bool Success)> Heal(
            Brep brep,
            IReadOnlyList<TopologyHealingStrategy> strategies,
            IGeometryContext context) =>
            !brep.IsValidTopology(out string _)
                ? ResultFactory.Create<(Brep, TopologyHealingStrategy, bool)>(error: E.Topology.DiagnosisFailed.WithContext("Topology invalid before healing"))
                : ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.Topology,])
                    .Bind(validBrep => ((Func<Result<(Brep, TopologyHealingStrategy, bool)>>)(() => {
                        int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                        Brep? bestHealed = null;
                        TopologyHealingStrategy? bestStrategy = null;
                        int bestNakedEdges = int.MaxValue;

                        for (int index = 0; index < strategies.Count; index++) {
                            TopologyHealingStrategy strategy = strategies[index];
                            Brep copy = validBrep.DuplicateBrep();
                            bool success = strategy switch {
                                TopologyHealingStrategy.ConservativeRepair => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
                                TopologyHealingStrategy.ModerateJoin => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                                TopologyHealingStrategy.AggressiveJoin => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
                                TopologyHealingStrategy.CombinedRepairAndJoin => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance) && copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                                TopologyHealingStrategy.TargetedJoin => ((Func<bool>)(() => {
                                    double threshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                                    bool joinedAny = false;
                                    for (int iteration = 0; iteration < TopologyConfig.MaxEdgesForNearMissAnalysis; iteration++) {
                                        int[] nakedEdgeIndices = [.. Enumerable.Range(0, copy.Edges.Count).Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked),];
                                        bool joinedThisPass = nakedEdgeIndices.Length != 0 && (from i in Enumerable.Range(0, nakedEdgeIndices.Length)
                                                                                               from j in Enumerable.Range(i + 1, nakedEdgeIndices.Length - i - 1)
                                                                                               let idxA = nakedEdgeIndices[i]
                                                                                               let idxB = nakedEdgeIndices[j]
                                                                                               where idxA < copy.Edges.Count && idxB < copy.Edges.Count
                                                                                               let eA = copy.Edges[idxA]
                                                                                               let eB = copy.Edges[idxB]
                                                                                               where eA.Valence == EdgeAdjacency.Naked && eB.Valence == EdgeAdjacency.Naked
                                                                                               let minDist = Math.Min(
                                                                                                   Math.Min(eA.PointAtStart.DistanceTo(eB.PointAtStart), eA.PointAtStart.DistanceTo(eB.PointAtEnd)),
                                                                                                   Math.Min(eA.PointAtEnd.DistanceTo(eB.PointAtStart), eA.PointAtEnd.DistanceTo(eB.PointAtEnd)))
                                                                                               where minDist < threshold && copy.JoinEdges(edgeIndex0: idxA, edgeIndex1: idxB, joinTolerance: threshold, compact: false)
                                                                                               select true).Any();
                                        joinedAny = joinedAny || joinedThisPass;
                                        _ = joinedThisPass || ((Func<bool>)(() => { iteration = TopologyConfig.MaxEdgesForNearMissAnalysis; return false; }))();
                                    }
                                    copy.Compact();
                                    return joinedAny;
                                }))(),
                                TopologyHealingStrategy.ComponentJoin => ((Func<bool>)(() => {
                                    Brep[] components = copy.GetConnectedComponents() ?? [];
                                    return components.Length > 1 && Brep.JoinBreps(brepsToJoin: components, tolerance: context.AbsoluteTolerance) switch {
                                        null or { Length: 0 } => false,
                                        Brep[] { Length: 1 } joined => ((Func<bool>)(() => { copy.Dispose(); copy = joined[0]; return true; }))(),
                                        Brep[] joined => ((Func<bool>)(() => { Array.ForEach(joined, b => b.Dispose()); return false; }))(),
                                    };
                                }))(),
                                _ => false,
                            };
                            (bool isValid, int nakedEdges) = success && copy.IsValidTopology(out string _)
                                ? (true, copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked))
                                : (false, int.MaxValue);
                            bool isImprovement = isValid && nakedEdges < originalNakedEdges && nakedEdges < bestNakedEdges;

                            Brep? toDispose = isImprovement ? bestHealed : copy;
                            toDispose?.Dispose();
                            (bestHealed, bestStrategy, bestNakedEdges) = isImprovement
                                ? (copy, strategy, nakedEdges)
                                : (bestHealed, bestStrategy, bestNakedEdges);
                        }

                        return bestHealed is Brep healed && bestStrategy is not null
                            ? ResultFactory.Create(value: (healed, bestStrategy, bestNakedEdges < originalNakedEdges))
                            : ResultFactory.Create<(Brep, TopologyHealingStrategy, bool)>(error: E.Topology.HealingFailed.WithContext($"All {strategies.Count.ToString(CultureInfo.InvariantCulture)} strategies failed"));
                    }))());

        internal static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractFeatures(
            Brep brep,
            IGeometryContext context) =>
            !brep.IsValidTopology(out string _)
                ? ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.DiagnosisFailed.WithContext("Topology invalid for feature extraction"))
                : ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.Topology | V.MassProperties,])
                    .Map<(int V, int E, int F, bool Solid, (int LoopIndex, bool IsHole)[] Loops)>(validBrep => (
                        V: validBrep.Vertices.Count,
                        E: validBrep.Edges.Count,
                        F: validBrep.Faces.Count,
                        Solid: validBrep.IsSolid && validBrep.IsManifold,
                        Loops: [.. validBrep.Loops.Select((l, i) => {
                            using Curve? loopCurve = l.To3dCurve();
                            return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (loopCurve?.GetLength() ?? 0.0) > Math.Max(context.AbsoluteTolerance, TopologyConfig.MinLoopLength));
                        }),
                        ]))
                    .Bind(data => data switch {
                        (int v, int e, int f, bool solid, (int LoopIndex, bool IsHole)[] loops) when v > 0 && e > 0 && f > 0 => (solid, Numerator: e - v - f + 2) switch {
                            (true, int numerator) when numerator >= 0 && (numerator & 1) == 0 => ResultFactory.Create(value: (Genus: numerator / 2, Loops: loops, IsSolid: solid, HandleCount: numerator / 2)),
                            (true, _) => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed.WithContext("Euler characteristic invalid for solid brep")),
                            (false, _) => ResultFactory.Create(value: (Genus: 0, Loops: loops, IsSolid: solid, HandleCount: 0)),
                        },
                        _ => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
                    });
    }
}

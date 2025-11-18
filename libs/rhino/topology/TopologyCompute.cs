using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology diagnosis, progressive healing, and topological feature extraction.</summary>
[Pure]
internal static class TopologyCompute {
    internal static Result<Topology.TopologyDiagnosis> Diagnose(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.TopologyDiagnosis>(
                error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
                .Bind(validBrep => ((Func<Result<Topology.TopologyDiagnosis>>)(() => {
                    (int Index, Point3d Start, Point3d End)[] nakedEdges = [.. Enumerable.Range(0, validBrep.Edges.Count)
                        .Where(i => validBrep.Edges[i].Valence == EdgeAdjacency.Naked && validBrep.Edges[i].EdgeCurve is not null)
                        .Select(i => (Index: i, Start: validBrep.Edges[i].PointAtStart, End: validBrep.Edges[i].PointAtEnd)),
                    ];

                    IReadOnlyList<double> gaps = nakedEdges.Length is > 0 and < TopologyConfig.MaxEdgesForNearMissAnalysis
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

                    IReadOnlyList<(int EdgeA, int EdgeB, double Distance)> nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
                        ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
                               from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                               let edgeI = validBrep.Edges[nakedEdges[i].Index]
                               let edgeJ = validBrep.Edges[nakedEdges[j].Index]
                               let dist = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue
                               where dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier && dist > context.AbsoluteTolerance
                               select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, Distance: dist)),
                        ]
                        : [];

                    IReadOnlyList<Topology.Strategy> repairs = (nakedEdgeCount, nonManifoldEdgeCount, nearMisses.Count) switch {
                        ( > 0, > 0, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
                        ( > 0, > 0, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin,],
                        ( > 0, _, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.Combined,],
                        (_, > 0, > 0) => [Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
                        ( > 0, _, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin,],
                        (_, > 0, _) => [Topology.Strategy.AggressiveJoin,],
                        (_, _, > 0) => [Topology.Strategy.Combined,],
                        _ => [],
                    };

                    return ResultFactory.Create(value: new Topology.TopologyDiagnosis(EdgeGaps: gaps, NearMisses: nearMisses, SuggestedStrategies: repairs));
                }))());

    internal static Result<Topology.HealingResult> Heal(
        Brep brep,
        IReadOnlyList<Topology.Strategy> strategies,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.HealingResult>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid before healing: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology,])
                .Bind(validBrep => ((Func<Result<Topology.HealingResult>>)(() => {
                    int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                    Brep? bestHealed = null;
                    Topology.Strategy? bestStrategy = null;
                    int bestNakedEdges = originalNakedEdges;

                    foreach (Topology.Strategy currentStrategy in strategies) {
                        Brep copy = validBrep.DuplicateBrep();
                        bool success = currentStrategy switch {
                            Topology.ConservativeRepairStrategy => copy.Repair(TopologyConfig.ConservativeRepairMultiplier * context.AbsoluteTolerance),
                            Topology.ModerateJoinStrategy => copy.JoinNakedEdges(TopologyConfig.ModerateJoinMultiplier * context.AbsoluteTolerance) > 0,
                            Topology.AggressiveJoinStrategy => copy.JoinNakedEdges(TopologyConfig.AggressiveJoinMultiplier * context.AbsoluteTolerance) > 0,
                            Topology.CombinedStrategy => copy.Repair(TopologyConfig.ConservativeRepairMultiplier * context.AbsoluteTolerance) && copy.JoinNakedEdges(TopologyConfig.ModerateJoinMultiplier * context.AbsoluteTolerance) > 0,
                            Topology.TargetedJoinStrategy => ((Func<bool>)(() => {
                                double threshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                                bool joinedAny = false;
                                const int maxIterations = TopologyConfig.MaxEdgesForNearMissAnalysis;
                                for (int iteration = 0; iteration < maxIterations; iteration++) {
                                    int[] nakedEdgeIndices = [.. Enumerable.Range(0, copy.Edges.Count).Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked),];
                                    bool joinedThisPass = false;
                                    foreach (int i in Enumerable.Range(0, nakedEdgeIndices.Length)) {
                                        foreach (int j in Enumerable.Range(i + 1, nakedEdgeIndices.Length - i - 1)) {
                                            int idxA = nakedEdgeIndices[i];
                                            int idxB = nakedEdgeIndices[j];
                                            bool validIndices = idxA < copy.Edges.Count && idxB < copy.Edges.Count;
                                            BrepEdge? eA = validIndices ? copy.Edges[idxA] : null;
                                            BrepEdge? eB = validIndices ? copy.Edges[idxB] : null;
                                            bool bothNaked = validIndices
                                                && eA is not null && eB is not null
                                                && eA.Valence == EdgeAdjacency.Naked
                                                && eB.Valence == EdgeAdjacency.Naked;
                                            double minDist = bothNaked
                                                ? Math.Min(
                                                    Math.Min(eA!.PointAtStart.DistanceTo(eB!.PointAtStart), eA.PointAtStart.DistanceTo(eB.PointAtEnd)),
                                                    Math.Min(eA.PointAtEnd.DistanceTo(eB.PointAtStart), eA.PointAtEnd.DistanceTo(eB.PointAtEnd)))
                                                : double.MaxValue;
                                            bool shouldJoin = bothNaked && minDist < threshold && copy.JoinEdges(edgeIndex0: idxA, edgeIndex1: idxB, joinTolerance: threshold, compact: false);
                                            joinedThisPass = joinedThisPass || shouldJoin;
                                        }
                                    }
                                    joinedAny = joinedAny || joinedThisPass;
                                    bool shouldContinue = joinedThisPass;
                                    if (!shouldContinue) { break; }
                                }
                                copy.Compact();
                                return joinedAny;
                            }))(),
                            Topology.ComponentJoinStrategy => ((Func<bool>)(() => {
                                Brep[] components = copy.GetConnectedComponents() ?? [];
                                return components.Length > 1 && Brep.JoinBreps(brepsToJoin: components, tolerance: context.AbsoluteTolerance) switch {
                                    null or { Length: 0 } => false,
                                    Brep[] { Length: 1 } joined => ((Func<bool>)(() => { copy.Dispose(); copy = joined[0]; return true; }))(),
                                    Brep[] joined => ((Func<bool>)(() => { Array.ForEach(joined, b => b.Dispose()); return false; }))(),
                                };
                            }))(),
                            _ => false,
                        };
                        string validationLog = string.Empty;
                        (bool isValid, int nakedEdges) = success && copy.IsValidTopology(out validationLog)
                            ? (true, copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked))
                            : ((Func<(bool, int)>)(() => { System.Diagnostics.Debug.WriteLine($"Strategy {currentStrategy.GetType().Name} failed validation: {validationLog}"); return (false, int.MaxValue); }))();
                        bool isImprovement = isValid && nakedEdges < originalNakedEdges && nakedEdges < bestNakedEdges;

                        Brep? toDispose = isImprovement ? bestHealed : copy;
                        toDispose?.Dispose();
                        (bestHealed, bestStrategy, bestNakedEdges) = isImprovement
                            ? (copy, currentStrategy, nakedEdges)
                            : (bestHealed, bestStrategy, bestNakedEdges);
                    }

                    return bestHealed is Brep healed && bestStrategy is Topology.Strategy strategy
                        ? ResultFactory.Create(value: new Topology.HealingResult(Healed: healed, AppliedStrategy: strategy, Success: bestNakedEdges < originalNakedEdges))
                        : ResultFactory.Create<Topology.HealingResult>(error: E.Topology.HealingFailed.WithContext($"All {strategies.Count.ToString(CultureInfo.InvariantCulture)} strategies failed"));
                }))());

    internal static Result<Topology.TopologicalFeatures> ExtractFeatures(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid for feature extraction: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.MassProperties,])
                .Map<(int V, int E, int F, bool Solid, IReadOnlyList<(int LoopIndex, bool IsHole)> Loops)>(validBrep => (
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
                    (int v, int e, int f, bool solid, IReadOnlyList<(int LoopIndex, bool IsHole)> loops) when v > 0 && e > 0 && f > 0 => (solid, Numerator: e - v - f + 2) switch {
                        (true, int numerator) when numerator >= 0 && (numerator & 1) == 0 => ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: numerator / 2, Loops: loops, IsSolid: solid, HandleCount: numerator / 2)),
                        (true, _) => ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Euler characteristic invalid for solid brep")),
                        (false, _) => ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: 0, Loops: loops, IsSolid: solid, HandleCount: 0)),
                    },
                    _ => ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
                });
}

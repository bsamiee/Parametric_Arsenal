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
    internal static Result<Topology.TopologicalFeatures> ExtractFeatures(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid for feature extraction: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.MassProperties,])
                .Bind(validBrep => (validBrep.Vertices.Count, validBrep.Edges.Count, validBrep.Faces.Count) switch {
                    (int v, int e, int f) when v > 0 && e > 0 && f > 0 => ((Func<Result<Topology.TopologicalFeatures>>)(() => {
                        bool isSolid = validBrep.IsSolid && validBrep.IsManifold;
                        int numerator = e - v - f + 2;
                        (int, bool)[] loops = [.. validBrep.Loops.Select((l, i) => {
                            using Curve? loopCurve = l.To3dCurve();
                            return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (loopCurve?.GetLength() ?? 0.0) > context.AbsoluteTolerance);
                        }),
                        ];
                        return (isSolid, numerator, loops) switch {
                            (true, int num, (int, bool)[] lps) when num >= 0 && (num & 1) == 0 => ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: num / 2, Loops: lps, IsSolid: true, HandleCount: num / 2)),
                            (true, _, _) => ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Euler characteristic invalid for solid brep")),
                            (false, _, (int, bool)[] lps) => ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: 0, Loops: lps, IsSolid: false, HandleCount: 0)),
                        };
                    }))(),
                    _ => ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
                });

    internal static Result<Topology.TopologyDiagnosis> Diagnose(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.TopologyDiagnosis>(
                error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
                .Map(validBrep => {
                    double nearMissThreshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                    (int Index, Point3d Start, Point3d End)[] nakedEdges = [.. Enumerable.Range(0, validBrep.Edges.Count)
                        .Where(i => validBrep.Edges[i].Valence == EdgeAdjacency.Naked && validBrep.Edges[i].EdgeCurve is not null)
                        .Select(i => (Index: i, Start: validBrep.Edges[i].PointAtStart, End: validBrep.Edges[i].PointAtEnd)),
                    ];

                    IReadOnlyList<double> gaps = nakedEdges.Length is > 0 and < TopologyConfig.MaxEdgesForNearMissAnalysis
                        ? [.. (from e1 in nakedEdges
                               from e2 in nakedEdges
                               where e1.Index != e2.Index
                               from dist in new[] { e1.Start.DistanceTo(e2.Start), e1.Start.DistanceTo(e2.End), e1.End.DistanceTo(e2.Start), e1.End.DistanceTo(e2.End), }
                               where dist > context.AbsoluteTolerance && dist < nearMissThreshold
                               select dist),
                        ]
                        : [];

                    int nonManifoldEdgeCount = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.NonManifold);
                    IReadOnlyList<(int EdgeA, int EdgeB, double Distance)> nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
                        ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
                               from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                               let edgeI = validBrep.Edges[nakedEdges[i].Index]
                               let edgeJ = validBrep.Edges[nakedEdges[j].Index]
                               let dist = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue
                               where dist < nearMissThreshold && dist > context.AbsoluteTolerance
                               select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, Distance: dist)),
                        ]
                        : [];

                    IReadOnlyList<Topology.Strategy> repairs = (nakedEdges.Length, nonManifoldEdgeCount, nearMisses.Count) switch {
                        ( > 0, > 0, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
                        ( > 0, > 0, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin,],
                        ( > 0, _, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.Combined,],
                        (_, > 0, > 0) => [Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
                        ( > 0, _, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin,],
                        (_, > 0, _) => [Topology.Strategy.AggressiveJoin,],
                        (_, _, > 0) => [Topology.Strategy.Combined,],
                        _ => [],
                    };

                    return new Topology.TopologyDiagnosis(EdgeGaps: gaps, NearMisses: nearMisses, SuggestedStrategies: repairs);
                });

    internal static Result<Topology.HealingResult> Heal(
        Brep brep,
        IReadOnlyList<Topology.Strategy> strategies,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<Topology.HealingResult>(error: E.Topology.DiagnosisFailed.WithContext($"Topology invalid before healing: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology,])
                .Bind(validBrep => {
                    double nearMissThreshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                    int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                    Topology.Strategy bestStrategy = strategies.Count > 0 ? strategies[0] : Topology.Strategy.ConservativeRepair;
                    Brep bestHealed = validBrep.DuplicateBrep();
                    int bestNakedEdges = originalNakedEdges;

                    foreach (Topology.Strategy currentStrategy in strategies) {
                        Brep copy = validBrep.DuplicateBrep();
                        double toleranceMultiplier = TopologyConfig.StrategyMetadata.TryGetValue(currentStrategy.GetType(), out TopologyConfig.HealingStrategyMetadata? meta) ? meta.ToleranceMultiplier : 1.0;
                        bool success = currentStrategy switch {
                            Topology.ConservativeRepairStrategy => copy.Repair(toleranceMultiplier * context.AbsoluteTolerance),
                            Topology.ModerateJoinStrategy => copy.JoinNakedEdges(toleranceMultiplier * context.AbsoluteTolerance) > 0,
                            Topology.AggressiveJoinStrategy => copy.JoinNakedEdges(toleranceMultiplier * context.AbsoluteTolerance) > 0,
                            Topology.CombinedStrategy => ((Func<bool>)(() => {
                                double repairTolerance = TopologyConfig.StrategyMetadata.TryGetValue(typeof(Topology.ConservativeRepairStrategy), out TopologyConfig.HealingStrategyMetadata? conservativeMeta) ? conservativeMeta.ToleranceMultiplier * context.AbsoluteTolerance : 0.1 * context.AbsoluteTolerance;
                                double joinTolerance = TopologyConfig.StrategyMetadata.TryGetValue(typeof(Topology.ModerateJoinStrategy), out TopologyConfig.HealingStrategyMetadata? moderateMeta) ? moderateMeta.ToleranceMultiplier * context.AbsoluteTolerance : context.AbsoluteTolerance;
                                return copy.Repair(repairTolerance) && copy.JoinNakedEdges(joinTolerance) > 0;
                            }))(),
                            Topology.TargetedJoinStrategy => ((Func<bool>)(() => {
                                bool joinedAny = false;
                                for (int iteration = 0; iteration < TopologyConfig.MaxEdgesForNearMissAnalysis; iteration++) {
                                    int[] nakedEdgeIndices = [.. Enumerable.Range(0, copy.Edges.Count).Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked),];
                                    bool joinedThisPass = false;
                                    for (int i = 0; i < nakedEdgeIndices.Length; i++) {
                                        for (int j = i + 1; j < nakedEdgeIndices.Length; j++) {
                                            (int idxA, int idxB) = (nakedEdgeIndices[i], nakedEdgeIndices[j]);
                                            (bool validIndices, BrepEdge? eA, BrepEdge? eB) = idxA < copy.Edges.Count && idxB < copy.Edges.Count
                                                ? (true, copy.Edges[idxA], copy.Edges[idxB])
                                                : (false, null, null);
                                            bool bothNaked = validIndices && eA is not null && eB is not null && eA.Valence == EdgeAdjacency.Naked && eB.Valence == EdgeAdjacency.Naked;
                                            double minDist = bothNaked
                                                ? Math.Min(
                                                    Math.Min(eA!.PointAtStart.DistanceTo(eB!.PointAtStart), eA.PointAtStart.DistanceTo(eB.PointAtEnd)),
                                                    Math.Min(eA.PointAtEnd.DistanceTo(eB.PointAtStart), eA.PointAtEnd.DistanceTo(eB.PointAtEnd))
                                                )
                                                : double.MaxValue;
                                            joinedThisPass = (bothNaked && minDist < nearMissThreshold && copy.JoinEdges(edgeIndex0: idxA, edgeIndex1: idxB, joinTolerance: nearMissThreshold, compact: false)) || joinedThisPass;
                                        }
                                    }
                                    joinedAny = joinedAny || joinedThisPass;
                                    if (!joinedThisPass) { break; }
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
                        bool isImprovement = isValid && nakedEdges < bestNakedEdges;

                        Brep? toDispose = isImprovement ? bestHealed : copy;
                        toDispose?.Dispose();
                        (bestHealed, bestStrategy, bestNakedEdges) = isImprovement
                            ? (copy, currentStrategy, nakedEdges)
                            : (bestHealed, bestStrategy, bestNakedEdges);
                    }

                    bool healedSuccessfully = bestNakedEdges < originalNakedEdges || originalNakedEdges == 0;
                    return healedSuccessfully
                        ? ResultFactory.Create(value: new Topology.HealingResult(Healed: bestHealed, AppliedStrategy: bestStrategy, Success: true))
                        : ((Func<Result<Topology.HealingResult>>)(() => { bestHealed.Dispose(); return ResultFactory.Create<Topology.HealingResult>(error: E.Topology.HealingFailed.WithContext($"All {strategies.Count.ToString(CultureInfo.InvariantCulture)} strategies failed")); }))();
                });
}

using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology diagnosis, progressive healing, and topological feature extraction.</summary>
[Pure]
internal static class TopologyCompute {
    internal static Result<(double[] EdgeGaps, (int EdgeA, int EdgeB, double Distance)[] NearMisses, byte[] SuggestedRepairs)> Diagnose(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string topologyLog)
            ? ResultFactory.Create<(double[], (int, int, double)[], byte[])>(
                error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {topologyLog}"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
                .Bind(validBrep => ((Func<Result<(double[], (int, int, double)[], byte[])>>)(() => {
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

                    byte[] repairs = (nakedEdgeCount, nonManifoldEdgeCount, nearMisses.Length) switch {
                        ( > 0, > 0, > 0) => [TopologyConfig.StrategyConservativeRepair, TopologyConfig.StrategyModerateJoin, TopologyConfig.StrategyAggressiveJoin, TopologyConfig.StrategyCombined,],
                        ( > 0, > 0, _) => [TopologyConfig.StrategyConservativeRepair, TopologyConfig.StrategyModerateJoin, TopologyConfig.StrategyAggressiveJoin,],
                        ( > 0, _, > 0) => [TopologyConfig.StrategyConservativeRepair, TopologyConfig.StrategyModerateJoin, TopologyConfig.StrategyCombined,],
                        (_, > 0, > 0) => [TopologyConfig.StrategyAggressiveJoin, TopologyConfig.StrategyCombined,],
                        ( > 0, _, _) => [TopologyConfig.StrategyConservativeRepair, TopologyConfig.StrategyModerateJoin,],
                        (_, > 0, _) => [TopologyConfig.StrategyAggressiveJoin,],
                        (_, _, > 0) => [TopologyConfig.StrategyCombined,],
                        _ => [],
                    };

                    return ResultFactory.Create<(double[], (int, int, double)[], byte[])>(value: (gaps, nearMisses, repairs));
                }))());

    internal static Result<(Brep Healed, byte Strategy, bool Success)> Heal(
        Brep brep,
        byte maxStrategy,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string _)
            ? ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.DiagnosisFailed.WithContext("Topology invalid before healing"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology,])
                .Bind(validBrep => ((Func<Result<(Brep, byte, bool)>>)(() => {
                    int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                    int strategyCount = RhinoMath.Clamp(maxStrategy + 1, 0, TopologyConfig.MaxHealingStrategies);
                    Brep? bestHealed = null;
                    byte bestStrategy = 0;
                    int bestNakedEdges = int.MaxValue;

                    for (int index = 0; index < strategyCount; index++) {
                        byte currentStrategy = (byte)index;
                        Brep copy = validBrep.DuplicateBrep();
                        bool success = currentStrategy switch {
                            TopologyConfig.StrategyConservativeRepair => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
                            TopologyConfig.StrategyModerateJoin => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                            TopologyConfig.StrategyAggressiveJoin => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
                            TopologyConfig.StrategyCombined => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance) && copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                            TopologyConfig.StrategyTargetedJoin => ((Func<bool>)(() => {
                                double threshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                                bool joinedAny = false;
                                for (int iteration = 0; iteration < TopologyConfig.MaxEdgesForNearMissAnalysis; iteration++) {
                                    int[] nakedEdgeIndices = [.. Enumerable.Range(0, copy.Edges.Count).Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked),];
                                    bool joinedThisPass = nakedEdgeIndices.Length == 0
                                        ? false
                                        : (from i in Enumerable.Range(0, nakedEdgeIndices.Length)
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
                                    _ = joinedThisPass ? true : ((Func<bool>)(() => { iteration = TopologyConfig.MaxEdgesForNearMissAnalysis; return false; }))();
                                }
                                copy.Compact();
                                return joinedAny;
                            }))(),
                            _ => ((Func<bool>)(() => {
                                Brep[] components = copy.GetConnectedComponents() ?? [];
                                return components.Length > 1 && Brep.JoinBreps(brepsToJoin: components, tolerance: context.AbsoluteTolerance) switch {
                                    null or { Length: 0 } => false,
                                    Brep[] { Length: 1 } joined => ((Func<bool>)(() => { copy.Dispose(); copy = joined[0]; return true; }))(),
                                    Brep[] joined => ((Func<bool>)(() => { Array.ForEach(joined, b => b.Dispose()); return false; }))(),
                                };
                            }))(),
                        };
                        (bool isValid, int nakedEdges) = success && copy.IsValidTopology(out string _)
                            ? (true, copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked))
                            : (false, int.MaxValue);
                        bool isImprovement = isValid && nakedEdges < originalNakedEdges && nakedEdges < bestNakedEdges;

                        Brep? toDispose = isImprovement ? bestHealed : copy;
                        toDispose?.Dispose();
                        (bestHealed, bestStrategy, bestNakedEdges) = isImprovement
                            ? (copy, currentStrategy, nakedEdges)
                            : (bestHealed, bestStrategy, bestNakedEdges);
                    }

                    return bestHealed is Brep healed
                        ? ResultFactory.Create<(Brep, byte, bool)>(value: (healed, bestStrategy, bestNakedEdges < originalNakedEdges))
                        : ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.HealingFailed.WithContext($"All {strategyCount.ToString(CultureInfo.InvariantCulture)} strategies failed"));
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

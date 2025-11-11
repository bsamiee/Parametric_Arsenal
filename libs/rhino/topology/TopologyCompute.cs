using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology diagnosis, healing, feature extraction algorithms.</summary>
internal static class TopologyCompute {
    [Pure]
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
                        ? [.. nakedEdges.SelectMany(e1 => new[] {
                                nakedEdges
                                    .Where(e2 => e2.Index != e1.Index)
                                    .Select(e2 => e1.Start.DistanceTo(e2.Start))
                                    .Concat(nakedEdges
                                        .Where(e2 => e2.Index != e1.Index)
                                        .Select(e2 => e1.Start.DistanceTo(e2.End)))
                                    .Where(dist => dist > context.AbsoluteTolerance && dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier)
                                    .DefaultIfEmpty(double.MaxValue)
                                    .Min(),
                                nakedEdges
                                    .Where(e2 => e2.Index != e1.Index)
                                    .Select(e2 => e1.End.DistanceTo(e2.Start))
                                    .Concat(nakedEdges
                                        .Where(e2 => e2.Index != e1.Index)
                                        .Select(e2 => e1.End.DistanceTo(e2.End)))
                                    .Where(dist => dist > context.AbsoluteTolerance && dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier)
                                    .DefaultIfEmpty(double.MaxValue)
                                    .Min(),
                            })
                            .Where(gap => gap < double.MaxValue),
                        ]
                        : [];

                    int nakedEdgeCount = nakedEdges.Length;
                    int nonManifoldEdgeCount = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.NonManifold);

                    (int EdgeA, int EdgeB, double Distance)[] nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
                        ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
                               from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                               let edgeI = validBrep.Edges[nakedEdges[i].Index]
                               let edgeJ = validBrep.Edges[nakedEdges[j].Index]
                               let result = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB)
                               ? (Success: true, Distance: ptA.DistanceTo(ptB))
                               : (Success: false, Distance: double.MaxValue)
                               where result.Success && result.Distance < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier && result.Distance > context.AbsoluteTolerance
                               select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, result.Distance)),
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

    [Pure]
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
                    int strategyCount = Math.Min(maxStrategy + 1, 4);
                    Brep? bestHealed = null;
                    byte bestStrategy = 0;
                    int bestNakedEdges = int.MaxValue;

                    for (int index = 0; index < strategyCount; index++) {
                        byte strategy = (byte)index;
                        Brep copy = validBrep.DuplicateBrep();
                        bool success = strategy switch {
                            0 => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
                            1 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                            2 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
                            _ => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance) && copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                        };
                        (bool IsValid, int NakedEdges) evaluation = success && copy.IsValidTopology(out string _)
                            ? (true, copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked))
                            : (false, int.MaxValue);
                        bool isImprovement = evaluation.IsValid && evaluation.NakedEdges < originalNakedEdges && evaluation.NakedEdges < bestNakedEdges;

                        (bestHealed, bestStrategy, bestNakedEdges) = (isImprovement, bestHealed) switch {
                            (true, Brep? prev) => ((Func<(Brep?, byte, int)>)(() => {
                                prev?.Dispose();
                                return (copy, strategy, evaluation.NakedEdges);
                            }))(),
                            (_, Brep? prev) => ((Func<(Brep?, byte, int)>)(() => {
                                copy.Dispose();
                                return (prev, bestStrategy, bestNakedEdges);
                            }))(),
                        };
                    }

                    return bestHealed is Brep healed
                        ? ResultFactory.Create<(Brep, byte, bool)>(value: (healed, bestStrategy, bestNakedEdges < originalNakedEdges))
                        : ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.HealingFailed.WithContext($"All {strategyCount.ToString(CultureInfo.InvariantCulture)} strategies failed"));
                }))());

    [Pure]
    internal static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractFeatures(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValidTopology(out string _)
            ? ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.DiagnosisFailed.WithContext("Topology invalid for feature extraction"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology | V.MassProperties,])
                .Map(validBrep => (validBrep.Vertices.Count, validBrep.Edges.Count, validBrep.Faces.Count, validBrep.IsSolid && validBrep.IsManifold, validBrep.Loops.Select((l, i) => ((Func<(int LoopIndex, bool IsHole)>)(() => { using Curve? c = l.To3dCurve(); return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (c?.GetLength() ?? 0.0) > context.AbsoluteTolerance); }))()).ToArray()))
                .Bind(data => data switch {
                    (int v, int e, int f, bool solid, (int LoopIndex, bool IsHole)[] loops) when v > 0 && e > 0 && f > 0 && solid && (e - v - f + 2) / 2 is int genus && genus >= 0 =>
                        ResultFactory.Create(value: (Genus: genus, Loops: loops, solid, HandleCount: genus)),
                    (int v, int e, int f, bool solid, (int LoopIndex, bool IsHole)[] loops) when v > 0 && e > 0 && f > 0 =>
                        ResultFactory.Create(value: (Genus: 0, Loops: loops, solid, HandleCount: 0)),
                    _ => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
                });
}

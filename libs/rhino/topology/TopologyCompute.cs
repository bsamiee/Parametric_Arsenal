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
    internal static Result<Topology.TopologyDiagnosis> Diagnose(
        Brep brep,
        IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
            .Bind(validBrep => ((Func<Result<Topology.TopologyDiagnosis>>)(() => {
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

                IReadOnlyList<Topology.HealingStrategy> repairs = (nakedEdgeCount, nonManifoldEdgeCount, nearMisses.Length) switch {
                    ( > 0, > 0, > 0) => [new Topology.HealingStrategy.ConservativeRepair(), new Topology.HealingStrategy.ModerateJoin(), new Topology.HealingStrategy.AggressiveJoin(), new Topology.HealingStrategy.Combined(),],
                    ( > 0, > 0, _) => [new Topology.HealingStrategy.ConservativeRepair(), new Topology.HealingStrategy.ModerateJoin(), new Topology.HealingStrategy.AggressiveJoin(),],
                    ( > 0, _, > 0) => [new Topology.HealingStrategy.ConservativeRepair(), new Topology.HealingStrategy.ModerateJoin(), new Topology.HealingStrategy.Combined(),],
                    (_, > 0, > 0) => [new Topology.HealingStrategy.AggressiveJoin(), new Topology.HealingStrategy.Combined(),],
                    ( > 0, _, _) => [new Topology.HealingStrategy.ConservativeRepair(), new Topology.HealingStrategy.ModerateJoin(),],
                    (_, > 0, _) => [new Topology.HealingStrategy.AggressiveJoin(),],
                    (_, _, > 0) => [new Topology.HealingStrategy.Combined(),],
                    _ => [],
                };

                return ResultFactory.Create(value: new Topology.TopologyDiagnosis(
                    EdgeGaps: gaps,
                    NearMisses: nearMisses,
                    SuggestedRepairs: repairs));
            }))());

    internal static Result<Topology.HealingResult> Heal(
        Brep brep,
        IReadOnlyList<Topology.HealingStrategy> strategies,
        IGeometryContext context) =>
        strategies.Count == 0
            ? ResultFactory.Create<Topology.HealingResult>(error: E.Topology.HealingFailed.WithContext("No healing strategies provided"))
            : ResultFactory.Create(value: brep)
                .Validate(args: [context, V.Standard | V.Topology,])
                .Bind(validBrep => ((Func<Result<Topology.HealingResult>>)(() => {
                    int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                    (Brep? Healed, Topology.HealingStrategy Strategy, int NakedEdges) best = strategies.Aggregate(
                        seed: ((Brep?)null, strategies[0], int.MaxValue),
                        func: (acc, strategy) => {
                            double toleranceMultiplier = TopologyConfig.HealingToleranceMultipliers.GetValueOrDefault(strategy.GetType(), 1.0);
                            Brep copy = validBrep.DuplicateBrep();
                            bool success = strategy switch {
                                Topology.HealingStrategy.ConservativeRepair => copy.Repair(toleranceMultiplier * context.AbsoluteTolerance),
                                Topology.HealingStrategy.ModerateJoin => copy.JoinNakedEdges(toleranceMultiplier * context.AbsoluteTolerance) > 0,
                                Topology.HealingStrategy.AggressiveJoin => copy.JoinNakedEdges(toleranceMultiplier * context.AbsoluteTolerance) > 0,
                                Topology.HealingStrategy.Combined => copy.Repair(TopologyConfig.HealingToleranceMultipliers[typeof(Topology.HealingStrategy.ConservativeRepair)] * context.AbsoluteTolerance) && copy.JoinNakedEdges(toleranceMultiplier * context.AbsoluteTolerance) > 0,
                                Topology.HealingStrategy.TargetedJoin => ((Func<bool>)(() => {
                                    double threshold = context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier;
                                    bool joinedAny = Enumerable.Range(0, TopologyConfig.MaxEdgesForNearMissAnalysis)
                                        .TakeWhile(_ => Enumerable.Range(0, copy.Edges.Count)
                                            .Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked)
                                            .ToArray() is int[] naked && naked.Length > 0 &&
                                            (from i in Enumerable.Range(0, naked.Length)
                                             from j in Enumerable.Range(i + 1, naked.Length - i - 1)
                                             let eA = copy.Edges[naked[i]]
                                             let eB = copy.Edges[naked[j]]
                                             where eA.Valence == EdgeAdjacency.Naked && eB.Valence == EdgeAdjacency.Naked
                                             let minDist = Math.Min(
                                                 Math.Min(eA.PointAtStart.DistanceTo(eB.PointAtStart), eA.PointAtStart.DistanceTo(eB.PointAtEnd)),
                                                 Math.Min(eA.PointAtEnd.DistanceTo(eB.PointAtStart), eA.PointAtEnd.DistanceTo(eB.PointAtEnd)))
                                             where minDist < threshold && copy.JoinEdges(edgeIndex0: naked[i], edgeIndex1: naked[j], joinTolerance: threshold, compact: false)
                                             select true).Any())
                                        .Any();
                                    copy.Compact();
                                    return joinedAny;
                                }))(),
                                Topology.HealingStrategy.ComponentJoin => ((Func<bool>)(() => {
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
                            return isValid && nakedEdges < originalNakedEdges && nakedEdges < acc.Item3
                                ? (acc.Item1?.Dispose(), (copy, strategy, nakedEdges)).Item2
                                : (copy.Dispose(), acc).Item2;
                        });

                    return best.Healed is Brep healed
                        ? ResultFactory.Create(value: new Topology.HealingResult(
                            Healed: healed,
                            AppliedStrategy: best.Strategy,
                            Success: best.NakedEdges < originalNakedEdges))
                        : ResultFactory.Create<Topology.HealingResult>(error: E.Topology.HealingFailed.WithContext($"All {strategies.Count.ToString(CultureInfo.InvariantCulture)} strategies failed"));
                }))());

    internal static Result<Topology.TopologyFeatures> ExtractFeatures(
        Brep brep,
        IGeometryContext context) =>
        ResultFactory.Create(value: brep)
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
                    (true, int numerator) when numerator >= 0 && (numerator & 1) == 0 => ResultFactory.Create(value: new Topology.TopologyFeatures(
                        Genus: numerator / 2,
                        Loops: loops,
                        IsSolid: solid,
                        HandleCount: numerator / 2)),
                    (true, _) => ResultFactory.Create<Topology.TopologyFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Euler characteristic invalid for solid brep")),
                    (false, _) => ResultFactory.Create(value: new Topology.TopologyFeatures(
                        Genus: 0,
                        Loops: loops,
                        IsSolid: solid,
                        HandleCount: 0)),
                },
                _ => ResultFactory.Create<Topology.TopologyFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
            });
}

using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology diagnosis, healing, and feature extraction algorithms.</summary>
internal static class TopologyCompute {
    [Pure]
    internal static Result<(double[] EdgeGaps, (int EdgeA, int EdgeB, double Distance)[] NearMisses, byte[] SuggestedRepairs)> Diagnose(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValid
            ? ResultFactory.Create<(double[], (int, int, double)[], byte[])>(error: E.Validation.GeometryInvalid)
            : !brep.IsValidTopology(out string log)
                ? ResultFactory.Create<(double[], (int, int, double)[], byte[])>(
                    error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {log}"))
                : ((Func<Result<(double[], (int, int, double)[], byte[])>>)(() => {
                double[] gaps = [.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked && brep.Edges[i].EdgeCurve is not null)
                    .Select(i => brep.Edges[i].PointAtStart.DistanceTo(brep.Edges[i].PointAtEnd)),
                ];

                int nakedEdgeCount = brep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
                int nonManifoldEdgeCount = brep.Edges.Count(e => e.Valence == EdgeAdjacency.NonManifold);

                (int EdgeA, int EdgeB, double Distance)[] nearMisses = brep.Edges.Count < 100
                    ? [.. (from i in Enumerable.Range(0, brep.Edges.Count)
                          from j in Enumerable.Range(i + 1, brep.Edges.Count - i - 1)
                          where brep.Edges[i].EdgeCurve is not null && brep.Edges[j].EdgeCurve is not null
                          let result = brep.Edges[i].EdgeCurve.ClosestPoints(brep.Edges[j].EdgeCurve, out Point3d ptA, out Point3d ptB)
                              ? (Success: true, Distance: ptA.DistanceTo(ptB))
                              : (Success: false, Distance: double.MaxValue)
                          where result.Success && result.Distance < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier && result.Distance > context.AbsoluteTolerance
                          select (EdgeA: i, EdgeB: j, result.Distance)),]
                    : [];

                byte[] repairs = (nakedEdgeCount, nonManifoldEdgeCount, nearMisses.Length) switch {
                    (> 0, > 0, > 0) => [0, 1, 2, 3,],
                    (> 0, > 0, _) => [0, 1, 2,],
                    (> 0, _, > 0) => [0, 1, 3,],
                    (_, > 0, > 0) => [2, 3,],
                    (> 0, _, _) => [0, 1,],
                    (_, > 0, _) => [2,],
                    (_, _, > 0) => [3,],
                    _ => [0,],
                };

                return ResultFactory.Create<(double[], (int, int, double)[], byte[])>(value: (gaps, nearMisses, repairs));
            }))();

    [Pure]
    internal static Result<(Brep Healed, byte Strategy, bool Success)> Heal(
        Brep brep,
        byte maxStrategy,
        IGeometryContext context) =>
        !brep.IsValid
            ? ResultFactory.Create<(Brep, byte, bool)>(error: E.Validation.GeometryInvalid)
            : ((Func<Result<(Brep, byte, bool)>>)(() => {
            static Brep? DisposeReturningNull(Brep b) { b.Dispose(); return null; }
            int originalNakedEdges = brep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);

            (byte Strategy, Brep? Healed, int NakedEdges)[] attempts = [.. Enumerable.Range(0, Math.Min(maxStrategy + 1, 4))
                .Select(s => {
                    byte strategy = (byte)s;
                    Brep copy = brep.DuplicateBrep();
                    bool success = strategy switch {
                        0 => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
                        1 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                        2 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
                        _ => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance) && copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                    };
                    bool isValid = success && copy.IsValidTopology(out string _);
                    int nakedEdges = isValid ? copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked) : int.MaxValue;
                    Brep? result = isValid && nakedEdges < originalNakedEdges ? copy : DisposeReturningNull(copy);
                    return (Strategy: strategy, Healed: result, NakedEdges: nakedEdges);
                }),
            ];

            (byte strategy, Brep? healed, int nakedEdges) = attempts.Where(a => a.Healed is not null).OrderBy(a => a.NakedEdges).FirstOrDefault();
            return healed is not null
                ? ResultFactory.Create<(Brep, byte, bool)>(value: (healed, strategy, nakedEdges < originalNakedEdges))
                : ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.HealingFailed.WithContext($"All {attempts.Length} strategies failed"));
        }))();

    [Pure]
    internal static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractFeatures(
        Brep brep,
        IGeometryContext context) =>
        !brep.IsValid
            ? ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Validation.GeometryInvalid)
            : (brep.Vertices.Count, brep.Edges.Count, brep.Faces.Count, brep.IsSolid && brep.IsManifold, brep.Loops.Select((l, i) => ((Func<(int LoopIndex, bool IsHole)>)(() => { using Curve? c = l.To3dCurve(); return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (c?.GetLength() ?? 0.0) > context.AbsoluteTolerance); }))()).ToArray()) switch {
                (int v, int e, int f, bool solid, (int LoopIndex, bool IsHole)[] loops) when v > 0 && e > 0 && f > 0 && solid && (e - v - f + 2) / 2 is int genus && genus >= 0 =>
                    ResultFactory.Create(value: (Genus: genus, Loops: loops, solid, HandleCount: genus)),
                (int v, int e, int f, bool solid, (int LoopIndex, bool IsHole)[] loops) when v > 0 && e > 0 && f > 0 =>
                    ResultFactory.Create(value: (Genus: 0, Loops: loops, solid, HandleCount: 0)),
                _ => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed.WithContext("Invalid vertex/edge/face counts")),
            };
}

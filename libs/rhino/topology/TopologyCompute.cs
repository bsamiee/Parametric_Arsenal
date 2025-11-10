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
        !brep.IsValidTopology(out string log)
            ? ResultFactory.Create<(double[], (int, int, double)[], byte[])>(
                error: E.Topology.DiagnosisFailed.WithContext($"Topology validation failed: {log}"))
            : ((Func<Result<(double[], (int, int, double)[], byte[])>>)(() => {
                double[] gaps = [.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                    .Select(i => brep.Edges[i].PointAtStart.DistanceTo(brep.Edges[i].PointAtEnd)),
                ];
                (int, int, double)[] nearMisses = [.. (from i in Enumerable.Range(0, brep.Edges.Count)
                                                       from j in Enumerable.Range(i + 1, brep.Edges.Count - i - 1)
                                                       let result = brep.Edges[i].EdgeCurve.ClosestPoints(brep.Edges[j].EdgeCurve, out Point3d ptA, out Point3d ptB)
                                                           ? (Success: true, Distance: ptA.DistanceTo(ptB))
                                                           : (Success: false, Distance: double.MaxValue)
                                                       where result.Success && result.Distance < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier
                                                       select (i, j, result.Distance)),
                ];
                byte[] repairs = [0, 1, 2, 3,];
                return ResultFactory.Create(value: (gaps, nearMisses, repairs));
            }))();

    [Pure]
    internal static Result<(Brep Healed, byte Strategy, bool Success)> Heal(
        Brep brep,
        byte maxStrategy,
        IGeometryContext context) =>
        ((Func<Result<(Brep, byte, bool)>>)(() => {
            static Brep? DisposeReturningNull(Brep b) { b.Dispose(); return null; }
            (byte Strategy, Brep? Healed)[] attempts = [.. Enumerable.Range(0, Math.Min(maxStrategy + 1, 3))
                .Select(s => {
                    byte strategy = (byte)s;
                    Brep copy = brep.DuplicateBrep();
                    bool success = strategy switch {
                        0 => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
                        1 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
                        _ => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
                    };
                    bool isValid = success && copy.IsValidTopology(out string _);
                    Brep? result = isValid ? copy : DisposeReturningNull(copy);
                    return (Strategy: strategy, Healed: result);
                }),
            ];
            (byte strat, Brep? healed) = attempts.FirstOrDefault(a => a.Healed is not null);
            return healed is not null
                ? ResultFactory.Create(value: (Healed: healed, Strategy: strat, Success: true))
                : ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.HealingFailed);
        }))();

    [Pure]
    internal static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractFeatures(
        Brep brep,
        IGeometryContext _) {
        (int LoopIndex, bool IsHole)[] ExtractLoops() => [.. brep.Loops
            .Select((l, i) => {
                using Curve? curve = l.To3dCurve();
                return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (curve?.GetLength() ?? 0.0) > TopologyConfig.MinLoopLength);
            }),];

        return (v: brep.Vertices.Count, e: brep.Edges.Count, f: brep.Faces.Count, solid: brep.IsSolid) switch {
            (int vCount, int eCount, int fCount, bool isSolid) when vCount > 0 && eCount > 0 && fCount > 0 && isSolid =>
                ResultFactory.Create(value: (
                    Genus: (eCount - vCount - fCount + 2) / 2,
                    Loops: ExtractLoops(),
                    isSolid,
                    HandleCount: (eCount - vCount - fCount + 2) / 2)),
            (int vCount, int eCount, int fCount, bool isSolid) when vCount > 0 && eCount > 0 && fCount > 0 =>
                ResultFactory.Create(value: (
                    Genus: 0,
                    Loops: ExtractLoops(),
                    isSolid,
                    HandleCount: 0)),
            _ => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed),
        };
    }
}

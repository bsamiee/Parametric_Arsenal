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
        !brep.IsValidTopology(out string _)
            ? ResultFactory.Create<(double[], (int, int, double)[], byte[])>(error: E.Topology.DiagnosisFailed)
            : ((Func<Result<(double[], (int, int, double)[], byte[])>>)(() => {
                double[] gaps = [.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                    .Select(i => Math.Min(
                        brep.Edges[i].PointAtStart.DistanceTo(brep.Edges[i].PointAtEnd),
                        TopologyConfig.EdgeGapTolerance * 2.0)),
                ];
                (int, int, double)[] nearMisses = [.. (from i in Enumerable.Range(0, brep.Edges.Count)
                                                       from j in Enumerable.Range(i + 1, brep.Edges.Count - i - 1)
                                                       let dist = brep.Edges[i].EdgeCurve.ClosestPoints(brep.Edges[j].EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue
                                                       where dist < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier
                                                       select (i, j, dist)),
                ];
                byte[] repairs = [0, 1, 2, 3,];
                return ResultFactory.Create(value: (gaps, nearMisses, repairs));
            }))();

    [Pure]
    internal static Result<(Brep Healed, byte Strategy, bool Success)> Heal(
        Brep brep,
        byte maxStrategy,
        IGeometryContext context) =>
        Enumerable.Range(0, Math.Min(maxStrategy + 1, 3))
            .Select(s => (Strategy: (byte)s, Brep: brep.DuplicateBrep()))
            .Select(t => (t.Strategy, Healed: t.Strategy switch {
                0 => t.Brep.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance) ? t.Brep : null,
                1 => t.Brep.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0 ? t.Brep : null,
                _ => t.Brep.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0 ? t.Brep : null,
            }))
            .FirstOrDefault(r => r.Healed?.IsValidTopology(out string _) == true) switch {
                (byte strat, Brep healed) when healed is not null =>
                    ResultFactory.Create(value: (Healed: healed, Strategy: strat, Success: true)),
                _ => ResultFactory.Create<(Brep, byte, bool)>(error: E.Topology.HealingFailed),
            };

    [Pure]
    internal static Result<(int Genus, (int LoopIndex, bool IsHole)[] Loops, bool IsSolid, int HandleCount)> ExtractFeatures(
        Brep brep,
        IGeometryContext _) =>
        (v: brep.Vertices.Count, e: brep.Edges.Count, f: brep.Faces.Count) switch {
            (int vCount, int eCount, int fCount) when vCount > 0 && eCount > 0 && fCount > 0 =>
                ResultFactory.Create(value: (
                    Genus: (vCount - eCount + fCount) / 2,
                    Loops: brep.Loops
                        .Select((l, i) => {
                            using Curve? curve = l.To3dCurve();
                            return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (curve?.GetLength() ?? 0.0) > TopologyConfig.MinLoopLength);
                        })
                        .ToArray(),
                    brep.IsSolid,
                    HandleCount: brep.IsSolid ? (vCount - eCount + fCount) / 2 : 0)),
            _ => ResultFactory.Create<(int, (int, bool)[], bool, int)>(error: E.Topology.FeatureExtractionFailed),
        };
}

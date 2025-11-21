using System.Collections.Frozen;
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
        Brep validBrep,
        IGeometryContext context,
        double minLoopLength) =>
        (validBrep.Vertices.Count, validBrep.Edges.Count, validBrep.Faces.Count) switch {
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
        };

    internal static Result<Topology.TopologyDiagnosis> Diagnose(
        Brep validBrep,
        IGeometryContext context,
        double nearMissMultiplier,
        int maxEdgeThreshold) {
        double tolerance = context.AbsoluteTolerance;
        double nearMissThreshold = tolerance * nearMissMultiplier;
        (int Index, Point3d Start, Point3d End)[] nakedEdges = [.. Enumerable.Range(0, validBrep.Edges.Count)
            .Where(i => validBrep.Edges[i].Valence == EdgeAdjacency.Naked && validBrep.Edges[i].EdgeCurve is not null)
            .Select(i => (Index: i, Start: validBrep.Edges[i].PointAtStart, End: validBrep.Edges[i].PointAtEnd)),
        ];

        IReadOnlyList<double> gaps = nakedEdges.Length > 1 && nakedEdges.Length <= maxEdgeThreshold
            ? [.. (from i in Enumerable.Range(0, nakedEdges.Length - 1)
                   from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                   let edgeI = validBrep.Edges[nakedEdges[i].Index].EdgeCurve
                   let edgeJ = validBrep.Edges[nakedEdges[j].Index].EdgeCurve
                   let distance = edgeI is Curve ci && edgeJ is Curve cj && ci.ClosestPoints(cj, out Point3d ptA, out Point3d ptB)
                       ? ptA.DistanceTo(ptB)
                       : double.MaxValue
                   where distance > tolerance && distance < nearMissThreshold
                   select distance),
            ]
            : [];

        int nonManifoldEdgeCount = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.NonManifold);
        IReadOnlyList<(int EdgeA, int EdgeB, double Distance)> nearMisses = nakedEdges.Length > 1 && nakedEdges.Length <= maxEdgeThreshold
            ? [.. (from i in Enumerable.Range(0, nakedEdges.Length - 1)
                   from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
                   let edgeI = validBrep.Edges[nakedEdges[i].Index]
                   let edgeJ = validBrep.Edges[nakedEdges[j].Index]
                   let dist = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue
                   where dist < nearMissThreshold && dist > tolerance
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

        return ResultFactory.Create(value: new Topology.TopologyDiagnosis(EdgeGaps: gaps, NearMisses: nearMisses, SuggestedStrategies: repairs));
    }

    internal static Result<Topology.HealingResult> Heal(
        Brep validBrep,
        IReadOnlyList<Topology.Strategy> strategies,
        IGeometryContext context,
        int maxTargetedJoinIterations) {
        int originalNakedEdges = validBrep.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
        Topology.Strategy bestStrategy = strategies.Count > 0 ? strategies[0] : Topology.Strategy.ConservativeRepair;
        Brep bestHealed = validBrep.DuplicateBrep();
        int bestNakedEdges = originalNakedEdges;
        double baseTolerance = context.AbsoluteTolerance;
        double conservativeMultiplier = TopologyConfig.StrategyToleranceMultipliers.TryGetValue(typeof(Topology.ConservativeRepairStrategy), out HealingStrategyMetadata conservMeta) ? conservMeta.ToleranceMultiplier : 0.1;
        double moderateMultiplier = TopologyConfig.StrategyToleranceMultipliers.TryGetValue(typeof(Topology.ModerateJoinStrategy), out HealingStrategyMetadata modMeta) ? modMeta.ToleranceMultiplier : 1.0;
        double conservativeTolerance = conservativeMultiplier * baseTolerance;
        double moderateTolerance = moderateMultiplier * baseTolerance;

        foreach (Topology.Strategy currentStrategy in strategies) {
            Brep copy = validBrep.DuplicateBrep();
            double toleranceMultiplier = TopologyConfig.StrategyToleranceMultipliers.TryGetValue(currentStrategy.GetType(), out HealingStrategyMetadata meta) ? meta.ToleranceMultiplier : 1.0;
            double strategyTolerance = toleranceMultiplier * baseTolerance;
            bool success = currentStrategy switch {
                Topology.ConservativeRepairStrategy => copy.Repair(strategyTolerance),
                Topology.ModerateJoinStrategy => copy.JoinNakedEdges(strategyTolerance) > 0,
                Topology.AggressiveJoinStrategy => copy.JoinNakedEdges(strategyTolerance) > 0,
                Topology.CombinedStrategy => copy.Repair(conservativeTolerance) && copy.JoinNakedEdges(moderateTolerance) > 0,
                Topology.TargetedJoinStrategy => ((Func<bool>)(() => {
                    double threshold = strategyTolerance;
                    bool joinedAny = false;
                    for (int iteration = 0; iteration < maxTargetedJoinIterations; iteration++) {
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
                                joinedThisPass = (bothNaked && minDist < threshold && copy.JoinEdges(edgeIndex0: idxA, edgeIndex1: idxB, joinTolerance: threshold, compact: false)) || joinedThisPass;
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
                    return components.Length > 1 && Brep.JoinBreps(brepsToJoin: components, tolerance: baseTolerance) switch {
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
    }

    internal static Result<IReadOnlyList<Topology.ConnectivityData>> ComputeConnectivity<TGeometry>(
        TGeometry _,
        int faceCount,
        Func<int, IEnumerable<int>> getAdjacent,
        Func<int, BoundingBox> getBounds,
        Func<int, IReadOnlyList<int>> getAdjacentForGraph) {
        int[] componentIds = new int[faceCount];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < faceCount; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : ((Func<int>)(() => {
                    Queue<int> queue = new([seed,]);
                    componentIds[seed] = componentCount;
                    while (queue.Count > 0) {
                        int faceIdx = queue.Dequeue();
                        foreach (int adjFace in getAdjacent(faceIdx).Where(f => componentIds[f] == -1)) {
                            componentIds[adjFace] = componentCount;
                            queue.Enqueue(adjFace);
                        }
                    }
                    return componentCount;
                }))() + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount).Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, faceCount).Where(f => componentIds[f] == c),]),];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(BoundingBox.Empty, (union, fIdx) => getBounds(fIdx) switch {
            BoundingBox fBox when union.IsValid => BoundingBox.Union(union, fBox),
            BoundingBox fBox => fBox,
        })),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[new Topology.ConnectivityData(
            ComponentIndices: components,
            ComponentSizes: [.. components.Select(static c => c.Count),],
            ComponentBounds: bounds,
            TotalComponents: componentCount,
            IsFullyConnected: componentCount == 1,
            AdjacencyGraph: Enumerable.Range(0, faceCount).ToFrozenDictionary(keySelector: i => i, elementSelector: getAdjacentForGraph)),
        ]);
    }

    internal static bool EnsureMeshNormals(Mesh mesh) =>
        mesh.FaceNormals.Count == mesh.Faces.Count
            ? mesh.FaceNormals.UnitizeFaceNormals()
            : mesh.FaceNormals.ComputeFaceNormals() && mesh.FaceNormals.UnitizeFaceNormals();
}

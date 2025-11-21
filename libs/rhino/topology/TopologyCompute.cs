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
        IGeometryContext context) =>
        (validBrep.Vertices.Count, validBrep.Edges.Count, validBrep.Faces.Count) switch {
            (int v, int e, int f) when v > 0 && e > 0 && f > 0 => ((Func<Result<Topology.TopologicalFeatures>>)(() => {
                bool isSolid = validBrep.IsSolid && validBrep.IsManifold;
                int eulerCharacteristic = e - v - f + 2;
                (int, bool)[] loops = [.. validBrep.Loops.Select((l, i) => {
                    using Curve? loopCurve = l.To3dCurve();
                    return (LoopIndex: i, IsHole: l.LoopType == BrepLoopType.Inner && (loopCurve?.GetLength() ?? 0.0) > context.AbsoluteTolerance);
                }),
                ];
                return isSolid && eulerCharacteristic >= 0 && (eulerCharacteristic & 1) == 0
                    ? ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: eulerCharacteristic / 2, Loops: loops, IsSolid: true, HandleCount: eulerCharacteristic / 2))
                    : isSolid
                        ? ResultFactory.Create<Topology.TopologicalFeatures>(error: E.Topology.FeatureExtractionFailed.WithContext("Euler characteristic invalid for solid brep"))
                        : ResultFactory.Create(value: new Topology.TopologicalFeatures(Genus: 0, Loops: loops, IsSolid: false, HandleCount: 0));
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
        (int nonManifoldCount, (int Index, BrepEdge Edge)[] nakedEdgeData) = ((Func<(int, (int, BrepEdge)[])>)(() => {
            int nmCount = 0;
            List<(int, BrepEdge)> naked = [];
            for (int i = 0; i < validBrep.Edges.Count; i++) {
                BrepEdge edge = validBrep.Edges[i];
                EdgeAdjacency valence = edge.Valence;
                nmCount = valence == EdgeAdjacency.NonManifold ? nmCount + 1 : nmCount;
                if (valence == EdgeAdjacency.Naked && edge.EdgeCurve is not null) {
                    naked.Add((i, edge));
                }
            }
            return (nmCount, [.. naked,]);
        }))();

        (int EdgeA, int EdgeB, double Distance)[] computedPairs = nakedEdgeData.Length > 1 && nakedEdgeData.Length <= maxEdgeThreshold
            ? ((Func<(int, int, double)[]>)(() => {
                List<(int, int, double)> pairs = [];
                for (int i = 0; i < nakedEdgeData.Length - 1; i++) {
                    (int idxA, BrepEdge edgeA) = nakedEdgeData[i];
                    Curve? curveA = edgeA.EdgeCurve;
                    if (curveA is null) { continue; }
                    for (int j = i + 1; j < nakedEdgeData.Length; j++) {
                        (int idxB, BrepEdge edgeB) = nakedEdgeData[j];
                        Curve? curveB = edgeB.EdgeCurve;
                        if (curveB is not null && curveA.ClosestPoints(curveB, out Point3d ptA, out Point3d ptB)) {
                            double distance = ptA.DistanceTo(ptB);
                            if (distance > tolerance && distance < nearMissThreshold) {
                                pairs.Add((idxA, idxB, distance));
                            }
                        }
                    }
                }
                return [.. pairs,];
            }))()
            : [];

        IReadOnlyList<Topology.Strategy> repairs = (nakedEdgeData.Length, nonManifoldCount, computedPairs.Length) switch {
            ( > 0, > 0, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
            ( > 0, > 0, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.AggressiveJoin,],
            ( > 0, _, > 0) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin, Topology.Strategy.Combined,],
            (_, > 0, > 0) => [Topology.Strategy.AggressiveJoin, Topology.Strategy.Combined,],
            ( > 0, _, _) => [Topology.Strategy.ConservativeRepair, Topology.Strategy.ModerateJoin,],
            (_, > 0, _) => [Topology.Strategy.AggressiveJoin,],
            (_, _, > 0) => [Topology.Strategy.Combined,],
            _ => [],
        };

        return ResultFactory.Create(value: new Topology.TopologyDiagnosis(
            EdgeGaps: [.. computedPairs.Select(p => p.Distance),],
            NearMisses: computedPairs,
            SuggestedStrategies: repairs));
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

        foreach (Topology.Strategy currentStrategy in strategies) {
            Brep copy = validBrep.DuplicateBrep();
            double toleranceMultiplier = TopologyConfig.StrategyToleranceMultipliers.TryGetValue(currentStrategy.GetType(), out double mult) ? mult : 1.0;
            double strategyTolerance = toleranceMultiplier * baseTolerance;
            bool success = currentStrategy switch {
                Topology.ConservativeRepairStrategy => copy.Repair(strategyTolerance),
                Topology.ModerateJoinStrategy => copy.JoinNakedEdges(strategyTolerance) > 0,
                Topology.AggressiveJoinStrategy => copy.JoinNakedEdges(strategyTolerance) > 0,
                Topology.CombinedStrategy => ((Func<bool>)(() => {
                    double conservativeTolerance = (TopologyConfig.StrategyToleranceMultipliers.TryGetValue(typeof(Topology.ConservativeRepairStrategy), out double cMult) ? cMult : 0.1) * baseTolerance;
                    double moderateTolerance = (TopologyConfig.StrategyToleranceMultipliers.TryGetValue(typeof(Topology.ModerateJoinStrategy), out double mMult) ? mMult : 1.0) * baseTolerance;
                    return copy.Repair(conservativeTolerance) && copy.JoinNakedEdges(moderateTolerance) > 0;
                }))(),
                Topology.TargetedJoinStrategy => ((Func<bool>)(() => {
                    bool joinedAny = false;
                    for (int iteration = 0; iteration < maxTargetedJoinIterations; iteration++) {
                        int[] nakedEdgeIndices = [.. Enumerable.Range(0, copy.Edges.Count).Where(i => copy.Edges[i].Valence == EdgeAdjacency.Naked),];
                        bool joinedThisPass = false;
                        for (int i = 0; i < nakedEdgeIndices.Length - 1; i++) {
                            int idxA = nakedEdgeIndices[i];
                            if (idxA >= copy.Edges.Count || copy.Edges[idxA].Valence != EdgeAdjacency.Naked) { continue; }
                            BrepEdge edgeA = copy.Edges[idxA];
                            Curve? curveA = edgeA.EdgeCurve;
                            if (curveA is null) { continue; }
                            for (int j = i + 1; j < nakedEdgeIndices.Length; j++) {
                                int idxB = nakedEdgeIndices[j];
                                if (idxB >= copy.Edges.Count || copy.Edges[idxB].Valence != EdgeAdjacency.Naked) { continue; }
                                BrepEdge edgeB = copy.Edges[idxB];
                                Curve? curveB = edgeB.EdgeCurve;
                                if (curveB is not null && curveA.ClosestPoints(curveB, out Point3d ptA, out Point3d ptB)) {
                                    double distance = ptA.DistanceTo(ptB);
                                    if (distance < strategyTolerance && copy.JoinEdges(edgeIndex0: idxA, edgeIndex1: idxB, joinTolerance: strategyTolerance, compact: false)) {
                                        joinedThisPass = true;
                                    }
                                }
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
                    if (components.Length <= 1) { return false; }
                    Brep[]? joined = Brep.JoinBreps(brepsToJoin: components, tolerance: strategyTolerance);
                    if (joined is null || joined.Length == 0) { return false; }
                    if (joined.Length == 1) {
                        copy.Dispose();
                        copy = joined[0];
                        return true;
                    }
                    Array.ForEach(joined, b => b?.Dispose());
                    return false;
                }))(),
                _ => false,
            };
            bool isValid = success && copy.IsValidTopology(out string validationLog);
            int nakedEdges = isValid ? copy.Edges.Count(e => e.Valence == EdgeAdjacency.Naked) : int.MaxValue;
            if (!isValid) {
                System.Diagnostics.Debug.WriteLine($"Strategy {currentStrategy.GetType().Name} failed validation: {validationLog}");
            }
            bool isImprovement = isValid && nakedEdges < bestNakedEdges;

            Brep? toDispose = isImprovement ? bestHealed : copy;
            toDispose?.Dispose();
            (bestHealed, bestStrategy, bestNakedEdges) = isImprovement
                ? (copy, currentStrategy, nakedEdges)
                : (bestHealed, bestStrategy, bestNakedEdges);
        }

        bool healedSuccessfully = bestNakedEdges < originalNakedEdges || originalNakedEdges == 0;
        if (!healedSuccessfully) {
            bestHealed.Dispose();
            return ResultFactory.Create<Topology.HealingResult>(error: E.Topology.HealingFailed.WithContext($"All {strategies.Count.ToString(CultureInfo.InvariantCulture)} strategies failed"));
        }
        return ResultFactory.Create(value: new Topology.HealingResult(Healed: bestHealed, AppliedStrategy: bestStrategy, Success: true));
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
        Queue<int> queue = new();
        for (int seed = 0; seed < faceCount; seed++) {
            if (componentIds[seed] != -1) { continue; }
            queue.Clear();
            queue.Enqueue(seed);
            componentIds[seed] = componentCount;
            while (queue.Count > 0) {
                int faceIdx = queue.Dequeue();
                foreach (int adjFace in getAdjacent(faceIdx).Where(f => componentIds[f] == -1)) {
                    componentIds[adjFace] = componentCount;
                    queue.Enqueue(adjFace);
                }
            }
            componentCount++;
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

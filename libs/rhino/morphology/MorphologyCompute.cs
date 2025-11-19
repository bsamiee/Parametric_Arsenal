using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology algorithm implementations.</summary>
internal static class MorphologyCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<GeometryBase> CageDeform(
        GeometryBase geometry,
        GeometryBase _,
        Point3d[] originalControlPoints,
        Point3d[] deformedControlPoints,
        IGeometryContext __) =>
        originalControlPoints.Length != deformedControlPoints.Length
            ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageControlPointMismatch.WithContext($"Original: {originalControlPoints.Length}, Deformed: {deformedControlPoints.Length}"))
            : originalControlPoints.Length < MorphologyConfig.MinCageControlPoints
                ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.InsufficientCagePoints.WithContext($"Count: {originalControlPoints.Length}, Required: {MorphologyConfig.MinCageControlPoints}"))
                : ((Func<Result<GeometryBase>>)(() => {
                    BoundingBox cageBounds = new(originalControlPoints);
                    return !RhinoMath.IsValidDouble(cageBounds.Volume) || cageBounds.Volume <= RhinoMath.ZeroTolerance
                        ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Cage bounding box has zero volume"))
                        : geometry switch {
                            Mesh m => (GeometryBase?)m.DuplicateMesh(),
                            Brep b => (GeometryBase?)b.DuplicateBrep(),
                            _ => null,
                        } is not GeometryBase deformed
                            ? ResultFactory.Create<GeometryBase>(error: E.Geometry.InvalidGeometryType.WithContext($"Type: {geometry.GetType().Name}"))
                            : ApplyCageDeformation(deformed, cageBounds, deformedControlPoints)
                                ? ResultFactory.Create(value: deformed)
                                : ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Failed to apply deformation to geometry"));
                }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ApplyCageDeformation(GeometryBase geometry, BoundingBox cageBounds, Point3d[] deformedControlPoints) {
        Vector3d span = cageBounds.Max - cageBounds.Min;
        Vector3d ComputeTrilinear(double u, double v, double w) =>
            ((1 - u) * (1 - v) * (1 - w) * (deformedControlPoints[0] - Point3d.Origin))
            + (u * (1 - v) * (1 - w) * (deformedControlPoints[1] - Point3d.Origin))
            + ((1 - u) * v * (1 - w) * (deformedControlPoints[2] - Point3d.Origin))
            + (u * v * (1 - w) * (deformedControlPoints[3] - Point3d.Origin))
            + ((1 - u) * (1 - v) * w * (deformedControlPoints[4] - Point3d.Origin))
            + (u * (1 - v) * w * (deformedControlPoints[5] - Point3d.Origin))
            + ((1 - u) * v * w * (deformedControlPoints[6] - Point3d.Origin))
            + (u * v * w * (deformedControlPoints[7] - Point3d.Origin));

        return geometry switch {
            Mesh m => ((Func<bool>)(() => {
                for (int i = 0; i < m.Vertices.Count; i++) {
                    Vector3d local = (Point3d)m.Vertices[i] - cageBounds.Min;
                    (double u, double v, double w) = (
                        RhinoMath.Clamp(span.X > RhinoMath.ZeroTolerance ? local.X / span.X : 0.0, 0.0, 1.0),
                        RhinoMath.Clamp(span.Y > RhinoMath.ZeroTolerance ? local.Y / span.Y : 0.0, 0.0, 1.0),
                        RhinoMath.Clamp(span.Z > RhinoMath.ZeroTolerance ? local.Z / span.Z : 0.0, 0.0, 1.0));
                    Point3d deformed = Point3d.Origin + ComputeTrilinear(u, v, w);
                    m.Vertices[i] = new Point3f((float)deformed.X, (float)deformed.Y, (float)deformed.Z);
                }
                return m.Normals.ComputeNormals() && m.Compact();
            }))(),
            Brep b => ((Func<bool>)(() => {
                for (int i = 0; i < b.Vertices.Count; i++) {
                    Vector3d local = b.Vertices[i].Location - cageBounds.Min;
                    (double u, double v, double w) = (
                        RhinoMath.Clamp(span.X > RhinoMath.ZeroTolerance ? local.X / span.X : 0.0, 0.0, 1.0),
                        RhinoMath.Clamp(span.Y > RhinoMath.ZeroTolerance ? local.Y / span.Y : 0.0, 0.0, 1.0),
                        RhinoMath.Clamp(span.Z > RhinoMath.ZeroTolerance ? local.Z / span.Z : 0.0, 0.0, 1.0));
                    b.Vertices[i].Location = Point3d.Origin + ComputeTrilinear(u, v, w);
                }
                return b.IsValid;
            }))(),
            _ => false,
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> SubdivideIterative(
        Mesh mesh,
        byte algorithm,
        int levels,
        IGeometryContext context) =>
        levels <= 0
            ? ResultFactory.Create<Mesh>(error: E.Geometry.InvalidCount.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Levels: {levels}")))
            : levels > MorphologyConfig.MaxSubdivisionLevels
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.SubdivisionLevelExceeded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Max: {MorphologyConfig.MaxSubdivisionLevels}")))
                : Enumerable.Range(0, levels).Aggregate(
                    ResultFactory.Create(value: mesh.DuplicateMesh()),
                    (result, level) => result.Bind(current => {
                        Mesh? next = algorithm switch {
                            MorphologyConfig.OpSubdivideCatmullClark => current.DuplicateMesh(),
                            MorphologyConfig.OpSubdivideLoop => SubdivideLoop(current),
                            MorphologyConfig.OpSubdivideButterfly => SubdivideButterfly(current),
                            _ => null,
                        };
                        bool valid = next?.IsValid is true && ValidateMeshQuality(next, context).IsSuccess;
                        if (level > 0) {
                            current.Dispose();
                        }
                        if (!valid) {
                            next?.Dispose();
                            return ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.SubdivisionFailed.WithContext(
                                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Level: {level}, Algorithm: {algorithm}")));
                        }
                        return ResultFactory.Create(value: next);
                    }));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideLoop(Mesh mesh) =>
        mesh.Faces.TriangleCount != mesh.Faces.Count
            ? null
            : ((Func<Mesh?>)(() => {
                int vertCount = mesh.Vertices.Count;
                Point3d[] originalVerts = new Point3d[vertCount];
                for (int i = 0; i < vertCount; i++) {
                    originalVerts[i] = mesh.Vertices[i];
                }

                Point3d[] newVerts = new Point3d[vertCount];
                for (int i = 0; i < vertCount; i++) {
                    int topologyIndex = mesh.TopologyVertices.TopologyVertexIndex(i);
                    int[] neighbors = topologyIndex >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(topologyIndex) : [];
                    int valence = neighbors.Length;
                    double beta = valence switch {
                        3 => MorphologyConfig.LoopBetaValence3,
                        6 => MorphologyConfig.LoopBetaValence6,
                        > 2 => (1.0 / valence) * (MorphologyConfig.LoopCenterWeight - Math.Pow(MorphologyConfig.LoopNeighborBase + (MorphologyConfig.LoopCosineMultiplier * Math.Cos(RhinoMath.TwoPI / valence)), 2.0)),
                        _ => 0.0,
                    };
                    Point3d sum = Point3d.Origin;
                    for (int j = 0; j < neighbors.Length; j++) {
                        sum += originalVerts[mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0]];
                    }
                    newVerts[i] = ((1.0 - (valence * beta)) * originalVerts[i]) + (beta * sum);
                }

                Mesh subdivided = new();
                for (int i = 0; i < vertCount; i++) {
                    _ = subdivided.Vertices.Add(newVerts[i]);
                }

                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    (int a, int b, int c) = (mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C);
                    (int, int)[] edges = [(Math.Min(a, b), Math.Max(a, b)), (Math.Min(b, c), Math.Max(b, c)), (Math.Min(c, a), Math.Max(c, a)),];
                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        if (edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)) {
                            midIndices[e] = existingMidIdx;
                        } else {
                            Vector3d edgeSum = (originalVerts[edges[e].Item1] - Point3d.Origin) + (originalVerts[edges[e].Item2] - Point3d.Origin);
                            Vector3d faceSum = (originalVerts[a] - Point3d.Origin) + (originalVerts[b] - Point3d.Origin) + (originalVerts[c] - Point3d.Origin);
                            Vector3d oppositeSum = faceSum - edgeSum;
                            Point3d midpoint = Point3d.Origin + (MorphologyConfig.LoopEdgeMidpointWeight * edgeSum) + (MorphologyConfig.LoopEdgeOppositeWeight * oppositeSum);
                            midIndices[e] = subdivided.Vertices.Add(midpoint);
                            edgeMidpoints[edges[e]] = midIndices[e];
                        }
                    }
                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }
                return subdivided.Normals.ComputeNormals() && subdivided.Compact() ? subdivided : null;
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideButterfly(Mesh mesh) =>
        mesh.Faces.TriangleCount != mesh.Faces.Count
            ? null
            : ((Func<Mesh?>)(() => {
                int vertCount = mesh.Vertices.Count;
                Point3d[] originalVerts = new Point3d[vertCount];
                for (int i = 0; i < vertCount; i++) {
                    originalVerts[i] = mesh.Vertices[i];
                }

                Mesh subdivided = new();
                for (int i = 0; i < vertCount; i++) {
                    _ = subdivided.Vertices.Add(originalVerts[i]);
                }

                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    (int a, int b, int c) = (mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C);
                    (int, int)[] edges = [(Math.Min(a, b), Math.Max(a, b)), (Math.Min(b, c), Math.Max(b, c)), (Math.Min(c, a), Math.Max(c, a)),];
                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        if (edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)) {
                            midIndices[e] = existingMidIdx;
                        } else {
                            (int v1, int v2) = (edges[e].Item1, edges[e].Item2);
                            Point3d mid = MorphologyConfig.ButterflyMidpointWeight * (originalVerts[v1] + originalVerts[v2]);
                            int t1 = mesh.TopologyVertices.TopologyVertexIndex(v1);
                            int t2 = mesh.TopologyVertices.TopologyVertexIndex(v2);
                            int[] v1Neighbors = t1 >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(t1) : [];
                            int[] v2Neighbors = t2 >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(t2) : [];
                            (int opposite1, int opposite2) = v1Neighbors.Length >= 4 && v2Neighbors.Length >= 4 ? FindButterflyOpposites(mesh, v1, v2) : (-1, -1);

                            Point3d midpoint = mid;
                            if (opposite1 >= 0 && opposite2 >= 0) {
                                Vector3d oppositeContrib = MorphologyConfig.ButterflyOppositeWeight * ((originalVerts[opposite1] - Point3d.Origin) + (originalVerts[opposite2] - Point3d.Origin));
                                Vector3d wingContrib = Vector3d.Zero;
                                int wingCount = 0;
                                for (int i = 0; i < v1Neighbors.Length && wingCount < 2; i++) {
                                    int n = v1Neighbors[i];
                                    if (n != default && n != v2 && n != opposite1 && n != opposite2) {
                                        wingContrib -= MorphologyConfig.ButterflyWingWeight * (originalVerts[mesh.TopologyVertices.MeshVertexIndices(n)[0]] - Point3d.Origin);
                                        wingCount++;
                                    }
                                }
                                for (int i = 0; i < v2Neighbors.Length && wingCount < 4; i++) {
                                    int n = v2Neighbors[i];
                                    if (n != default && n != v1 && n != opposite1 && n != opposite2) {
                                        wingContrib -= MorphologyConfig.ButterflyWingWeight * (originalVerts[mesh.TopologyVertices.MeshVertexIndices(n)[0]] - Point3d.Origin);
                                        wingCount++;
                                    }
                                }
                                midpoint = Point3d.Origin + (mid - Point3d.Origin) + oppositeContrib + wingContrib;
                            }

                            midIndices[e] = subdivided.Vertices.Add(midpoint);
                            edgeMidpoints[edges[e]] = midIndices[e];
                        }
                    }
                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }
                return subdivided.Normals.ComputeNormals() && subdivided.Compact() ? subdivided : null;
            }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int, int) FindButterflyOpposites(Mesh mesh, int v1, int v2) =>
        Enumerable.Range(0, mesh.Faces.Count).Aggregate(
            (-1, -1),
            (state, fi) => {
                int[] faceVerts = [mesh.Faces[fi].A, mesh.Faces[fi].B, mesh.Faces[fi].C,];
                return Array.IndexOf(faceVerts, v1) >= 0 && Array.IndexOf(faceVerts, v2) >= 0
                    ? (faceVerts.First(v => v != v1 && v != v2) is int opp
                        ? (state.Item1 < 0 ? opp : state.Item1,
                           state.Item1 >= 0 && opp != state.Item1 ? opp : state.Item2)
                        : state)
                    : state;
            });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> SmoothWithConvergence(
        Mesh mesh,
        int maxIterations,
        bool lockBoundary,
        Func<Mesh, Point3d[], IGeometryContext, Point3d[]> updateFunc,
        IGeometryContext context) =>
        maxIterations is <= 0 or > MorphologyConfig.MaxSmoothingIterations
            ? ResultFactory.Create<Mesh>(error: E.Geometry.InvalidCount.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Iterations: {maxIterations}")))
            : ((Func<Result<Mesh>>)(() => {
                Mesh smoothed = mesh.DuplicateMesh();
                Point3d[] positions = ArrayPool<Point3d>.Shared.Rent(smoothed.Vertices.Count);
                Point3d[] prevPositions = ArrayPool<Point3d>.Shared.Rent(smoothed.Vertices.Count);

                try {
                    for (int i = 0; i < smoothed.Vertices.Count; i++) {
                        positions[i] = smoothed.Vertices[i];
                        prevPositions[i] = positions[i];
                    }

                    _ = smoothed.Normals.ComputeNormals();
                    int iterPerformed = 0;
                    bool converged = false;
                    double threshold = context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;

                    for (int iter = 0; iter < maxIterations && !converged; iter++) {
                        Point3d[] updated = updateFunc(smoothed, positions, context);

                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            int topologyIndex = mesh.TopologyVertices.TopologyVertexIndex(i);
                            bool isBoundary = lockBoundary && (topologyIndex < 0 || mesh.TopologyVertices.ConnectedFaces(topologyIndex).Length < 2);
                            positions[i] = isBoundary ? positions[i] : updated[i];
                            _ = smoothed.Vertices.SetVertex(i, positions[i]);
                        }

                        iterPerformed++;

                        double rmsDisp = iter > 0
                            ? ((Func<double>)(() => {
                                double sumSq = 0.0;
                                int count = smoothed.Vertices.Count;
                                for (int i = 0; i < count; i++) {
                                    double dist = positions[i].DistanceTo(prevPositions[i]);
                                    sumSq += dist * dist;
                                }
                                return Math.Sqrt(sumSq / count);
                            }))()
                            : double.MaxValue;
                        converged = rmsDisp < threshold;

                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            prevPositions[i] = positions[i];
                        }

                        _ = smoothed.Normals.ComputeNormals();
                    }

                    _ = smoothed.Compact();
                    return converged || iterPerformed == maxIterations
                        ? ResultFactory.Create(value: smoothed)
                        : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.SmoothingConvergenceFailed);
                } finally {
                    ArrayPool<Point3d>.Shared.Return(positions, clearArray: true);
                    ArrayPool<Point3d>.Shared.Return(prevPositions, clearArray: true);
                }
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> OffsetMesh(
        Mesh mesh,
        double distance,
        bool bothSides,
        IGeometryContext __) =>
        Math.Abs(distance) switch {
            double abs when !RhinoMath.IsValidDouble(distance) || abs < MorphologyConfig.MinOffsetDistance =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.OffsetDistanceInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Distance: {distance:F6}"))),
            double abs when abs > MorphologyConfig.MaxOffsetDistance =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.OffsetDistanceInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Max: {MorphologyConfig.MaxOffsetDistance}"))),
            _ => ((Func<Result<Mesh>>)(() => {
                Mesh? offset = mesh.Offset(distance: distance, solidify: bothSides);
                return offset?.IsValid is true
                    ? ResultFactory.Create(value: offset)
                    : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshOffsetFailed.WithContext(offset is null ? "Offset operation returned null" : "Generated offset mesh is invalid"));
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ReduceMesh(
        Mesh mesh,
        int targetFaceCount,
        bool _,
        double accuracy,
        IGeometryContext __) =>
        (targetFaceCount, accuracy) switch {
            ( < MorphologyConfig.MinReductionFaceCount, _) =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetFaceCount}, Min: {MorphologyConfig.MinReductionFaceCount}"))),
            (int target, _) when target >= mesh.Faces.Count =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {target} >= Current: {mesh.Faces.Count}"))),
            (_, double acc) when !RhinoMath.IsValidDouble(acc) || acc < MorphologyConfig.MinReductionAccuracy || acc > MorphologyConfig.MaxReductionAccuracy =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionAccuracyInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Accuracy: {acc:F3}"))),
            _ => ((Func<Result<Mesh>>)(() => {
                Mesh reduced = mesh.DuplicateMesh();
                bool success = reduced.Reduce(
                    desiredPolygonCount: targetFaceCount,
                    allowDistortion: accuracy < MorphologyConfig.DefaultReductionAccuracy,
                    accuracy: (int)(RhinoMath.Clamp(accuracy, MorphologyConfig.MinReductionAccuracy, MorphologyConfig.MaxReductionAccuracy) * MorphologyConfig.ReductionAccuracyScale),
                    normalizeSize: false,
                    cancelToken: CancellationToken.None,
                    progress: null,
                    problemDescription: out string _,
                    threaded: false);
                return success && reduced.IsValid && reduced.Faces.Count <= targetFaceCount * MorphologyConfig.ReductionTargetTolerance
                    ? ResultFactory.Create(value: reduced)
                    : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(
                        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Achieved: {reduced.Faces.Count}, Target: {targetFaceCount}")));
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Mesh Remeshed, int IterationsPerformed)> RemeshIsotropic(
        Mesh mesh,
        double targetEdgeLength,
        int maxIterations,
        bool preserveFeatures,
        IGeometryContext context) =>
        ((Func<Result<(Mesh, int)>>)(() => {
            BoundingBox bounds = mesh.GetBoundingBox(accurate: false);
            double diagLength = bounds.Diagonal.Length;
            (double minEdge, double maxEdge) = (context.AbsoluteTolerance * MorphologyConfig.RemeshMinEdgeLengthFactor, diagLength * MorphologyConfig.RemeshMaxEdgeLengthFactor);
            return (!RhinoMath.IsValidDouble(targetEdgeLength) || targetEdgeLength < minEdge || targetEdgeLength > maxEdge, maxIterations) switch {
                (true, _) => ResultFactory.Create<(Mesh, int)>(error: E.Geometry.Morphology.RemeshTargetEdgeLengthInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetEdgeLength:F6}, Range: [{minEdge:F6}, {maxEdge:F6}]"))),
                (_, <= 0) or (_, > MorphologyConfig.MaxRemeshIterations) => ResultFactory.Create<(Mesh, int)>(error: E.Geometry.Morphology.RemeshIterationLimitExceeded.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxIters: {maxIterations}"))),
                _ => ((Func<Result<(Mesh, int)>>)(() => {
                    Mesh remeshed = mesh.DuplicateMesh();
                    double splitThreshold = targetEdgeLength * MorphologyConfig.RemeshSplitThresholdFactor;
                    double collapseThreshold = targetEdgeLength / MorphologyConfig.RemeshSplitThresholdFactor;
                    int iterationsPerformed = 0;
                    bool converged = false;

                    while (iterationsPerformed < maxIterations && !converged) {
                        iterationsPerformed++;
                        bool modified = false;
                        MeshTopologyEdgeList edges = remeshed.TopologyEdges;
                        int edgeCount = edges.Count;
                        for (int edgeIndex = edgeCount - 1; edgeIndex >= 0; edgeIndex--) {
                            Line edge = edges.EdgeLine(edgeIndex);
                            double length = edge.Length;
                            if (!RhinoMath.IsValidDouble(length) || length <= RhinoMath.ZeroTolerance) {
                                continue;
                            }
                            bool boundaryEdge = preserveFeatures && edges.GetConnectedFaces(edgeIndex).Length < 2;
                            if (boundaryEdge) {
                                continue;
                            }
                            if (length > splitThreshold && edges.SplitEdge(edgeIndex, MorphologyConfig.EdgeMidpointParameter)) {
                                modified = true;
                                continue;
                            }
                            if (length < collapseThreshold && edges.CollapseEdge(edgeIndex)) {
                                modified = true;
                            }
                        }

                        _ = remeshed.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true);
                        _ = remeshed.Vertices.CullUnused();
                        _ = remeshed.Faces.CullDegenerateFaces();

                        bool[] boundaryMask = preserveFeatures ? remeshed.GetNakedEdgePointStatus() : [];
                        int vertexCount = remeshed.Vertices.Count;
                        Point3d[] positions = new Point3d[vertexCount];
                        for (int i = 0; i < vertexCount; i++) {
                            positions[i] = remeshed.Vertices[i];
                        }
                        Point3d[] relaxed = MorphologyCore.LaplacianUpdate(remeshed, positions, useCotangent: false);
                        for (int i = 0; i < vertexCount; i++) {
                            if (preserveFeatures && boundaryMask.Length > i && boundaryMask[i]) {
                                continue;
                            }
                            _ = remeshed.Vertices.SetVertex(i, relaxed[i]);
                        }

                        _ = remeshed.Normals.ComputeNormals();
                        _ = remeshed.Compact();

                        int totalEdges = remeshed.TopologyEdges.Count;
                        (double sum, double sumSq) = (0.0, 0.0);
                        for (int ei = 0; ei < totalEdges; ei++) {
                            double len = remeshed.TopologyEdges.EdgeLine(ei).Length;
                            sum += len;
                            sumSq += len * len;
                        }
                        double meanEdge = totalEdges > 0 ? sum / totalEdges : 0.0;
                        double variance = totalEdges > 0 ? (sumSq / totalEdges) - (meanEdge * meanEdge) : 0.0;
                        double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
                        double allowedDelta = targetEdgeLength * MorphologyConfig.RemeshConvergenceThreshold;
                        bool lengthConverged = Math.Abs(meanEdge - targetEdgeLength) <= allowedDelta;
                        bool uniformityConverged = meanEdge > RhinoMath.ZeroTolerance && (stdDev / meanEdge) <= MorphologyConfig.RemeshUniformityWeight;
                        converged = lengthConverged && uniformityConverged;
                        if (!modified && !converged) {
                            break;
                        }
                    }

                    return remeshed.IsValid
                        ? ResultFactory.Create(value: (remeshed, iterationsPerformed))
                        : ResultFactory.Create<(Mesh, int)>(error: E.Geometry.Morphology.RemeshingFailed);
                }))(),
            };
        }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ValidateMeshQuality(Mesh mesh, IGeometryContext context) {
        (double[] _, double[] aspectRatios, double[] minAngles) = MorphologyCore.ComputeMeshMetrics(mesh, context);
        int aspectCount = aspectRatios.Length;
        int angleCount = minAngles.Length;
        if (aspectCount == 0 || angleCount == 0) {
            return ResultFactory.Create(value: mesh);
        }

        (double maxAspect, double minAngle) = (aspectRatios.Max(), minAngles.Min());
        return maxAspect > MorphologyConfig.AspectRatioThreshold
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxAspect: {maxAspect:F2}")))
            : minAngle < MorphologyConfig.MinAngleRadiansThreshold
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MinAngle: {RhinoMath.ToDegrees(minAngle):F1}°")))
                : ResultFactory.Create(value: mesh);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> RepairMesh(
        Mesh mesh,
        byte flags,
        double weldTolerance,
        IGeometryContext _) =>
        (weldTolerance, mesh.DuplicateMesh()) switch {
            ( < MorphologyConfig.MinWeldTolerance, _) or ( > MorphologyConfig.MaxWeldTolerance, _) =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.WeldToleranceInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Tolerance: {weldTolerance:E2}, Range: [{MorphologyConfig.MinWeldTolerance:E2}, {MorphologyConfig.MaxWeldTolerance:E2}]"))),
            (_, null) or (_, { IsValid: false }) =>
                ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshRepairFailed.WithContext("Mesh duplication failed")),
            (double tol, Mesh repaired) => ((Func<Result<Mesh>>)(() => {
                MorphologyConfig.MorphologyOperationMetadata?[] repairMetas = [
                    (flags & MorphologyConfig.RepairFillHoles) != 0 ? MorphologyConfig.Operations.Values.FirstOrDefault(m => m.RepairFlags == MorphologyConfig.RepairFillHoles) : null,
                    (flags & MorphologyConfig.RepairUnifyNormals) != 0 ? MorphologyConfig.Operations.Values.FirstOrDefault(m => m.RepairFlags == MorphologyConfig.RepairUnifyNormals) : null,
                    (flags & MorphologyConfig.RepairCullDegenerateFaces) != 0 ? MorphologyConfig.Operations.Values.FirstOrDefault(m => m.RepairFlags == MorphologyConfig.RepairCullDegenerateFaces) : null,
                    (flags & MorphologyConfig.RepairCompact) != 0 ? MorphologyConfig.Operations.Values.FirstOrDefault(m => m.RepairFlags == MorphologyConfig.RepairCompact) : null,
                    (flags & MorphologyConfig.RepairWeld) != 0 ? MorphologyConfig.Operations.Values.FirstOrDefault(m => m.RepairFlags == MorphologyConfig.RepairWeld) : null,
                ];
                for (int i = 0; i < repairMetas.Length; i++) {
                    MorphologyConfig.MorphologyOperationMetadata? meta = repairMetas[i];
                    bool _ = meta?.RepairAction is not null && meta.RepairAction(repaired, tol);
                }
                return repaired.Normals.ComputeNormals()
                    ? ResultFactory.Create(value: repaired)
                    : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshRepairFailed.WithContext("Normal recomputation failed"));
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh[]> SeparateMeshComponents(Mesh mesh, IGeometryContext _) =>
        mesh switch { { DisjointMeshCount: <= 0 } => ResultFactory.Create<Mesh[]>(error: E.Geometry.Morphology.MeshRepairFailed.WithContext("Disjoint mesh count invalid")),
            Mesh m => ((Func<Result<Mesh[]>>)(() => {
                Mesh[] components = m.SplitDisjointPieces();
                return (components is not { Length: > 0 })
                    ? ResultFactory.Create<Mesh[]>(error: E.Geometry.Morphology.MeshRepairFailed.WithContext("Component separation failed"))
                    : ResultFactory.Create(value: components);
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> WeldMeshVertices(
        Mesh mesh,
        double tolerance,
        bool weldNormals,
        IGeometryContext _) =>
        (tolerance < MorphologyConfig.MinWeldTolerance || tolerance > MorphologyConfig.MaxWeldTolerance) switch {
            true => ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshWeldFailed.WithContext(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Tolerance: {tolerance:E2}, Range: [{MorphologyConfig.MinWeldTolerance:E2}, {MorphologyConfig.MaxWeldTolerance:E2}]"))),
            false => ((Func<Result<Mesh>>)(() => {
                Mesh welded = mesh.DuplicateMesh();
                return !welded.IsValid
                    ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshWeldFailed.WithContext("Mesh duplication failed"))
                    : ((Func<Result<Mesh>>)(() => {
                        bool combined = welded.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true);
                        return weldNormals && !welded.Normals.ComputeNormals()
                            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshWeldFailed.WithContext("Normal recomputation failed"))
                            : ResultFactory.Create(value: welded);
                    }))();
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> BrepToMesh(
        Brep brep,
        MeshingParameters? meshParams,
        bool joinMeshes,
        IGeometryContext __) =>
        meshParams is null
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshingParametersInvalid.WithContext("Parameters cannot be null"))
            : ((Func<Result<Mesh>>)(() => {
                Mesh[]? meshes = Mesh.CreateFromBrep(brep: brep, meshingParameters: meshParams);
                return (meshes is null || meshes.Length == 0) switch {
                    true => ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.BrepToMeshFailed.WithContext(
                        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Brep face count: {brep.Faces.Count}"))),
                    false => ((Func<Result<Mesh>>)(() => {
                        Mesh result = joinMeshes && meshes.Length > 1
                            ? ((Func<Mesh>)(() => {
                                Mesh joined = new();
                                joined.Append(meshes);
                                return joined;
                            }))()
                            : meshes[0];
                        _ = result.Vertices.CombineIdentical(ignoreNormals: true, ignoreAdditional: true);
                        _ = result.Faces.CullDegenerateFaces();
                        _ = result.Normals.ComputeNormals();
                        _ = result.Compact();
                        return result.IsValid
                            ? ResultFactory.Create(value: result)
                            : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.BrepToMeshFailed.WithContext("Result mesh invalid"));
                    }))(),
                };
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ThickenMesh(
        Mesh mesh,
        double thickness,
        bool solidify,
        Vector3d direction,
        IGeometryContext _) =>
        (!RhinoMath.IsValidDouble(thickness) || Math.Abs(thickness) < MorphologyConfig.MinThickenDistance || Math.Abs(thickness) > MorphologyConfig.MaxThickenDistance) switch {
            true => ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshThickenFailed.WithContext(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Thickness: {thickness:F6}, Range: [{MorphologyConfig.MinThickenDistance:F6}, {MorphologyConfig.MaxThickenDistance:F6}]"))),
            false => ((Func<Result<Mesh>>)(() => {
                Mesh? thickened = mesh.Offset(distance: thickness, solidify: solidify, direction: direction, wallFacesOut: out List<int>? _);
                return thickened?.IsValid is true
                    ? ResultFactory.Create(value: thickened)
                    : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshThickenFailed.WithContext(thickened is null ? "Offset operation returned null" : "Generated mesh is invalid"));
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> UnwrapMesh(
        Mesh mesh,
        byte unwrapMethod,
        IGeometryContext _) =>
        unwrapMethod switch {
            0 or 1 => ((Func<Result<Mesh>>)(() => {
                Mesh unwrapped = mesh.DuplicateMesh();
                using MeshUnwrapper unwrapper = new(unwrapped);
                bool success = unwrapper.Unwrap(method: (MeshUnwrapMethod)unwrapMethod);
                return success && unwrapped.TextureCoordinates.Count > 0
                    ? ResultFactory.Create(value: unwrapped)
                    : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshUnwrapFailed.WithContext(
                        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Method: {(unwrapMethod == 0 ? "AngleBased" : "ConformalEnergyMinimization")}, Success: {success}, UVCount: {unwrapped.TextureCoordinates.Count}")));
            }))(),
            _ => ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshUnwrapFailed.WithContext(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Invalid unwrap method: {unwrapMethod}"))),
        };
}

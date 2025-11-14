using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology algorithm implementations with convergence tracking.</summary>
internal static class MorphologyCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<GeometryBase> CageDeform(
        GeometryBase geometry,
        GeometryBase _,
        Point3d[] originalControlPoints,
        Point3d[] deformedControlPoints,
        IGeometryContext __) =>
        originalControlPoints.Length != deformedControlPoints.Length
            ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageControlPointMismatch.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Original: {originalControlPoints.Length}, Deformed: {deformedControlPoints.Length}")))
            : originalControlPoints.Length < MorphologyConfig.MinCageControlPoints
                ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.InsufficientCagePoints.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Count: {originalControlPoints.Length}, Required: {MorphologyConfig.MinCageControlPoints}")))
                : ((Func<Result<GeometryBase>>)(() => {
                    BoundingBox cageBounds = new(originalControlPoints);
                    return !RhinoMath.IsValidDouble(cageBounds.Volume) || cageBounds.Volume <= RhinoMath.ZeroTolerance
                        ? ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Cage bounding box has zero volume"))
                        : ((Func<Result<GeometryBase>>)(() => {
                            GeometryBase? deformed = geometry switch {
                                Mesh m => m.DuplicateMesh(),
                                Brep b => b.DuplicateBrep(),
                                _ => null,
                            };
                            return deformed is null
                                ? ResultFactory.Create<GeometryBase>(error: E.Geometry.InvalidGeometryType.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Type: {geometry.GetType().Name}")))
                                : ((Func<Result<GeometryBase>>)(() => {
                                    Point3d[] vertices = deformed switch {
                                        Mesh m => [.. Enumerable.Range(0, m.Vertices.Count).Select(i => (Point3d)m.Vertices[i]),],
                                        Brep b => [.. b.Vertices.Select(v => v.Location),],
                                        _ => [],
                                    };
                                    Point3d[] deformedVerts = new Point3d[vertices.Length];
                                    for (int i = 0; i < vertices.Length; i++) {
                                        Point3d localCoord = ComputeLocalCoordinates(vertices[i], originalControlPoints, cageBounds);
                                        deformedVerts[i] = RhinoMath.IsValidDouble(localCoord.X) && RhinoMath.IsValidDouble(localCoord.Y) && RhinoMath.IsValidDouble(localCoord.Z)
                                            ? InterpolateDeformedPosition(localCoord, deformedControlPoints)
                                            : vertices[i];
                                    }
                                    bool success = deformed switch {
                                        Mesh meshDeformed => ((Func<bool>)(() => {
                                            for (int i = 0; i < deformedVerts.Length; i++) {
                                                meshDeformed.Vertices[i] = new Point3f((float)deformedVerts[i].X, (float)deformedVerts[i].Y, (float)deformedVerts[i].Z);
                                            }
                                            return meshDeformed.Normals.ComputeNormals() && meshDeformed.Compact();
                                        }))(),
                                        Brep brepDeformed => ((Func<bool>)(() => {
                                            for (int i = 0; i < Math.Min(deformedVerts.Length, brepDeformed.Vertices.Count); i++) {
                                                brepDeformed.Vertices[i].Location = deformedVerts[i];
                                            }
                                            return brepDeformed.IsValid;
                                        }))(),
                                        _ => false,
                                    };
                                    return success
                                        ? ResultFactory.Create(value: deformed)
                                        : ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Failed to apply deformation to geometry"));
                                }))();
                        }))();
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ComputeLocalCoordinates(Point3d point, Point3d[] _, BoundingBox bounds) {
        Vector3d localVec = point - bounds.Min;
        Vector3d spanVec = bounds.Max - bounds.Min;
        return new Point3d(
            RhinoMath.Clamp(spanVec.X > RhinoMath.ZeroTolerance ? localVec.X / spanVec.X : 0.0, 0.0, 1.0),
            RhinoMath.Clamp(spanVec.Y > RhinoMath.ZeroTolerance ? localVec.Y / spanVec.Y : 0.0, 0.0, 1.0),
            RhinoMath.Clamp(spanVec.Z > RhinoMath.ZeroTolerance ? localVec.Z / spanVec.Z : 0.0, 0.0, 1.0));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d InterpolateDeformedPosition(Point3d localCoord, Point3d[] deformedControlPoints) {
        double u = localCoord.X;
        double v = localCoord.Y;
        double w = localCoord.Z;
        double u1 = 1.0 - u;
        double v1 = 1.0 - v;
        double w1 = 1.0 - w;

        int minPoints = Math.Min(deformedControlPoints.Length, MorphologyConfig.MinCageControlPoints);
        return minPoints >= MorphologyConfig.MinCageControlPoints
            ? Point3d.Origin +
                (u1 * v1 * w1 * (deformedControlPoints[0] - Point3d.Origin)) +
                (u * v1 * w1 * (deformedControlPoints[1] - Point3d.Origin)) +
                (u1 * v * w1 * (deformedControlPoints[2] - Point3d.Origin)) +
                (u * v * w1 * (deformedControlPoints[3] - Point3d.Origin)) +
                (u1 * v1 * w * (deformedControlPoints[4] - Point3d.Origin)) +
                (u * v1 * w * (deformedControlPoints[5] - Point3d.Origin)) +
                (u1 * v * w * (deformedControlPoints[6] - Point3d.Origin)) +
                (u * v * w * (deformedControlPoints[7] - Point3d.Origin))
            : Point3d.Origin;
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
                : ((Func<Result<Mesh>>)(() => {
                    Mesh current = mesh.DuplicateMesh();
                    Mesh result = current;
                    int failedLevel = -1;
                    for (int level = 0; level < levels && failedLevel < 0; level++) {
                        Mesh? next = algorithm switch {
                            MorphologyConfig.OpSubdivideCatmullClark => current?.DuplicateMesh(),
                            MorphologyConfig.OpSubdivideLoop => current is not null ? SubdivideLoop(current) : null,
                            MorphologyConfig.OpSubdivideButterfly => current is not null ? SubdivideButterfly(current) : null,
                            _ => null,
                        };
                        bool valid = next?.IsValid is true && ValidateMeshQuality(next, context).IsSuccess;
                        _ = !valid ? ((Func<int>)(() => { next?.Dispose(); return 0; }))() : 0;
                        failedLevel = !valid ? level : failedLevel;
                        _ = valid && level > 0 && level < levels - 1 && current is not null && next is not null ? ((Func<int>)(() => { current.Dispose(); return 0; }))() : 0;
                        current = valid && next is not null ? next : current!;
                        result = valid && next is not null ? next : result;
                    }
                    return failedLevel is 0
                        ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.SubdivisionFailed.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Level: {failedLevel}, Algorithm: {algorithm}")))
                        : ResultFactory.Create(value: result);
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideLoop(Mesh mesh) =>
        !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
            ? null
            : ((Func<Mesh>)(() => {
                Mesh subdivided = new();
                Point3d[] originalVerts = [.. Enumerable.Range(0, mesh.Vertices.Count).Select(i => (Point3d)mesh.Vertices[i]),];
                Point3d[] newVerts = new Point3d[originalVerts.Length];
                for (int i = 0; i < originalVerts.Length; i++) {
                    int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
                    int valence = neighbors.Length;
                    double beta = valence is 3
                        ? MorphologyConfig.LoopBetaValence3
                        : valence is 6
                            ? MorphologyConfig.LoopBetaValence6
                            : valence > 2
                                ? (((1.0 / valence) * (MorphologyConfig.LoopCenterWeight - Math.Pow((MorphologyConfig.LoopNeighborBase + ((MorphologyConfig.LoopCosineMultiplier * Math.Cos(RhinoMath.TwoPI / valence)))), 2.0))))
                                : 0.0;

                    Point3d sum = Point3d.Origin;
                    for (int j = 0; j < neighbors.Length; j++) {
                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                        sum += originalVerts[meshVertIdx];
                    }
                    newVerts[i] = ((1.0 - (valence * beta)) * originalVerts[i]) + (beta * sum);
                }

                for (int i = 0; i < newVerts.Length; i++) {
                    _ = subdivided.Vertices.Add(newVerts[i]);
                }

                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    int a = mesh.Faces[faceIdx].A;
                    int b = mesh.Faces[faceIdx].B;
                    int c = mesh.Faces[faceIdx].C;

                    (int, int)[] edges = [
                        (Math.Min(a, b), Math.Max(a, b)),
                        (Math.Min(b, c), Math.Max(b, c)),
                        (Math.Min(c, a), Math.Max(c, a)),
                    ];

                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        midIndices[e] = edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)
                            ? existingMidIdx
                            : ((Func<int>)(() => {
                                Vector3d v1 = originalVerts[edges[e].Item1] - Point3d.Origin;
                                Vector3d v2 = originalVerts[edges[e].Item2] - Point3d.Origin;
                                Vector3d va = originalVerts[a] - Point3d.Origin;
                                Vector3d vb = originalVerts[b] - Point3d.Origin;
                                Vector3d vc = originalVerts[c] - Point3d.Origin;
                                Point3d mid = Point3d.Origin + (MorphologyConfig.LoopEdgeMidpointWeight * (v1 + v2)) + (MorphologyConfig.LoopEdgeOppositeWeight * (va + vb + vc - v1 - v2));
                                int newMidIdx = subdivided.Vertices.Add(mid);
                                edgeMidpoints[edges[e]] = newMidIdx;
                                return newMidIdx;
                            }))();
                    }

                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }

                _ = subdivided.Normals.ComputeNormals();
                _ = subdivided.Compact();
                return subdivided;
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideButterfly(Mesh mesh) =>
        !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
            ? null
            : ((Func<Mesh>)(() => {
                Mesh subdivided = new();
                Point3d[] originalVerts = [.. Enumerable.Range(0, mesh.Vertices.Count).Select(i => (Point3d)mesh.Vertices[i]),];

                for (int i = 0; i < originalVerts.Length; i++) {
                    _ = subdivided.Vertices.Add(originalVerts[i]);
                }

                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    int a = mesh.Faces[faceIdx].A;
                    int b = mesh.Faces[faceIdx].B;
                    int c = mesh.Faces[faceIdx].C;

                    (int, int)[] edges = [
                        (Math.Min(a, b), Math.Max(a, b)),
                        (Math.Min(b, c), Math.Max(b, c)),
                        (Math.Min(c, a), Math.Max(c, a)),
                    ];

                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        midIndices[e] = edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)
                            ? existingMidIdx
                            : ((Func<int>)(() => {
                                int v1 = edges[e].Item1;
                                int v2 = edges[e].Item2;
                                Point3d mid = MorphologyConfig.ButterflyMidpointWeight * (originalVerts[v1] + originalVerts[v2]);
                                int[] v1Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v1);
                                int[] v2Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v2);
                                bool hasRegularStencil = v1Neighbors.Length >= 4 && v2Neighbors.Length >= 4;
                                (int opposite1, int opposite2) = hasRegularStencil
                                    ? ((Func<(int, int)>)(() => {
                                        int opp1 = -1;
                                        int opp2 = -1;
                                        for (int fi = 0; fi < mesh.Faces.Count; fi++) {
                                            int[] faceVerts = [mesh.Faces[fi].A, mesh.Faces[fi].B, mesh.Faces[fi].C,];
                                            bool hasV1 = Array.IndexOf(faceVerts, v1) >= 0;
                                            bool hasV2 = Array.IndexOf(faceVerts, v2) >= 0;
                                            int opp = hasV1 && hasV2 ? faceVerts.First(v => v != v1 && v != v2) : -1;
                                            opp1 = opp >= 0 && opp1 < 0 ? opp : opp1;
                                            opp2 = opp >= 0 && opp1 >= 0 && opp != opp1 ? opp : opp2;
                                        }
                                        return (opp1, opp2);
                                    }))()
                                    : (-1, -1);
                                mid = opposite1 >= 0 && opposite2 >= 0
                                    ? ((Func<Point3d>)(() => {
                                        Vector3d vmid = mid - Point3d.Origin;
                                        Vector3d vopp1 = originalVerts[opposite1] - Point3d.Origin;
                                        Vector3d vopp2 = originalVerts[opposite2] - Point3d.Origin;
                                        Point3d adjusted = Point3d.Origin + vmid + (MorphologyConfig.ButterflyOppositeWeight * (vopp1 + vopp2));
                                        int[] wing1 = [.. v1Neighbors.Where(n => n != v2 && n != opposite1 && n != opposite2).Take(2),];
                                        int[] wing2 = [.. v2Neighbors.Where(n => n != v1 && n != opposite1 && n != opposite2).Take(2),];
                                        Vector3d vadjusted = adjusted - Point3d.Origin;
                                        for (int w = 0; w < wing1.Length && w < 2; w++) {
                                            int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(wing1[w])[0];
                                            vadjusted -= MorphologyConfig.ButterflyWingWeight * (originalVerts[meshVertIdx] - Point3d.Origin);
                                        }
                                        for (int w = 0; w < wing2.Length && w < 2; w++) {
                                            int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(wing2[w])[0];
                                            vadjusted -= MorphologyConfig.ButterflyWingWeight * (originalVerts[meshVertIdx] - Point3d.Origin);
                                        }
                                        return Point3d.Origin + vadjusted;
                                    }))()
                                    : mid;
                                int newMidIdx = subdivided.Vertices.Add(mid);
                                edgeMidpoints[edges[e]] = newMidIdx;
                                return newMidIdx;
                            }))();
                    }

                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }

                _ = subdivided.Normals.ComputeNormals();
                _ = subdivided.Compact();
                return subdivided;
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> SmoothWithConvergence(
        Mesh mesh,
        int maxIterations,
        bool lockBoundary,
        Func<Mesh, Point3d[], IGeometryContext, Point3d[]> updateFunc,
        IGeometryContext context) =>
        maxIterations is <= 0 or > 1000
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

                    int iterPerformed = 0;
                    bool converged = false;
                    double threshold = context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;

                    for (int iter = 0; iter < maxIterations && !converged; iter++) {
                        Point3d[] updated = updateFunc(smoothed, positions, context);

                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            bool isBoundary = lockBoundary && (mesh.TopologyVertices.ConnectedFaces(i).Length < 2);
                            positions[i] = isBoundary ? positions[i] : updated[i];
                            _ = smoothed.Vertices.SetVertex(i, positions[i]);
                        }

                        _ = smoothed.Normals.ComputeNormals();
                        iterPerformed++;

                        double[] distances = [.. Enumerable.Range(0, smoothed.Vertices.Count)
                            .Select(i => positions[i].DistanceTo(prevPositions[i])),
                        ];
                        double[] distSquares = [.. distances.Select(static d => d * d),];
                        double rmsDisp = iter > 0
                            ? Math.Sqrt(distSquares.Average())
                            : double.MaxValue;

                        converged = rmsDisp < threshold;

                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            prevPositions[i] = positions[i];
                        }
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
        bool _,
        IGeometryContext __) =>
        !RhinoMath.IsValidDouble(distance) || Math.Abs(distance) < MorphologyConfig.MinOffsetDistance
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.OffsetDistanceInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Distance: {distance:F6}")))
            : Math.Abs(distance) > MorphologyConfig.MaxOffsetDistance
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.OffsetDistanceInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Max: {MorphologyConfig.MaxOffsetDistance}")))
                : ((Func<Result<Mesh>>)(() => {
                    Mesh? offset = mesh.Offset(distance);
                    return offset?.IsValid is true
                        ? ResultFactory.Create(value: offset)
                        : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshOffsetFailed.WithContext(offset is null ? "Offset operation returned null" : "Generated offset mesh is invalid"));
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ReduceMesh(
        Mesh mesh,
        int targetFaceCount,
        bool _,
        double accuracy,
        IGeometryContext __) =>
        targetFaceCount < MorphologyConfig.MinReductionFaceCount
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetFaceCount}, Min: {MorphologyConfig.MinReductionFaceCount}")))
            : targetFaceCount >= mesh.Faces.Count
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetFaceCount} >= Current: {mesh.Faces.Count}")))
                : !RhinoMath.IsValidDouble(accuracy) || accuracy < MorphologyConfig.MinReductionAccuracy || accuracy > MorphologyConfig.MaxReductionAccuracy
                    ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionAccuracyInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Accuracy: {accuracy:F3}")))
                    : ((Func<Result<Mesh>>)(() => {
                        Mesh reduced = mesh.DuplicateMesh();
                        bool success = reduced.Reduce(
                            desiredPolygonCount: targetFaceCount,
                            allowDistortion: accuracy < MorphologyConfig.DefaultReductionAccuracy,
                            accuracy: (int)(RhinoMath.Clamp(accuracy, MorphologyConfig.MinReductionAccuracy, MorphologyConfig.MaxReductionAccuracy) * 10),
                            normalizeSize: false,
                            cancelToken: System.Threading.CancellationToken.None,
                            progress: null,
                            problemDescription: out string _,
                            threaded: false);
                        return success && reduced.IsValid && reduced.Faces.Count <= targetFaceCount * 1.1
                            ? ResultFactory.Create(value: reduced)
                            : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Achieved: {reduced.Faces.Count}, Target: {targetFaceCount}")));
                    }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> RemeshIsotropic(
        Mesh mesh,
        double targetEdgeLength,
        int maxIterations,
        bool _,
        IGeometryContext context) {
        BoundingBox bounds = mesh.GetBoundingBox(accurate: false);
        double diagLength = bounds.Diagonal.Length;
        double minEdge = context.AbsoluteTolerance * MorphologyConfig.RemeshMinEdgeLengthFactor;
        double maxEdge = diagLength * MorphologyConfig.RemeshMaxEdgeLengthFactor;

        return !RhinoMath.IsValidDouble(targetEdgeLength) || targetEdgeLength < minEdge || targetEdgeLength > maxEdge
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshTargetEdgeLengthInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetEdgeLength:F6}, Range: [{minEdge:F6}, {maxEdge:F6}]")))
            : maxIterations is <= 0 or > 100
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshIterationLimitExceeded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxIters: {maxIterations}")))
                : ((Func<Result<Mesh>>)(() => {
                    Mesh remeshed = mesh.DuplicateMesh();
                    double splitThreshold = targetEdgeLength * MorphologyConfig.RemeshSplitThresholdFactor;

                    for (int iter = 0; iter < maxIterations; iter++) {
                        bool changed = false;

                        for (int i = remeshed.TopologyEdges.Count - 1; i >= 0; i--) {
                            Line edge = remeshed.TopologyEdges.EdgeLine(i);
                            changed = edge.Length > splitThreshold
                                ? ((Func<bool>)(() => {
                                    Point3d mid = edge.PointAt(0.5);
                                    int midIdx = remeshed.Vertices.Add(mid);
                                    return midIdx >= 0;
                                }))() || changed
                                : changed;
                        }

                        changed = false;

                        Point3d[] positions = [.. Enumerable.Range(0, remeshed.Vertices.Count).Select(i => (Point3d)remeshed.Vertices[i]),];
                        Point3d[] smoothed = LaplacianUpdate(remeshed, positions, useCotangent: false);
                        for (int i = 0; i < smoothed.Length; i++) {
                            _ = remeshed.Vertices.SetVertex(i, smoothed[i]);
                        }

                        _ = remeshed.Normals.ComputeNormals();
                        _ = remeshed.Compact();

                        double[] edgeLengths = [.. Enumerable.Range(0, remeshed.TopologyEdges.Count)
                            .Select(i => remeshed.TopologyEdges.EdgeLine(i).Length),
                        ];
                        double meanEdge = edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0;
                        bool converged = Math.Abs(meanEdge - targetEdgeLength) < (targetEdgeLength * 0.1);
                        changed = !converged && changed;
                    }

                    return remeshed.IsValid
                        ? ResultFactory.Create(value: remeshed)
                        : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshingFailed);
                }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            updated[i] = neighbors.Length is 0
                ? positions[i]
                : ((Func<Point3d>)(() => {
                    Point3d sum = Point3d.Origin;
                    double weightSum = 0.0;
                    for (int j = 0; j < neighbors.Length; j++) {
                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                        Point3d neighborPos = positions[meshVertIdx];
                        double weight = useCotangent
                            ? MorphologyConfig.UniformLaplacianWeight / Math.Max(positions[i].DistanceTo(neighborPos), RhinoMath.ZeroTolerance)
                            : MorphologyConfig.UniformLaplacianWeight;
                        sum += (weight * neighborPos);
                        weightSum += weight;
                    }
                    return weightSum > RhinoMath.ZeroTolerance ? sum / weightSum : positions[i];
                }))();
        }

        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ValidateMeshQuality(Mesh mesh, IGeometryContext context) {
        double[] aspectRatios = new double[mesh.Faces.Count];
        double[] minAngles = new double[mesh.Faces.Count];

        for (int i = 0; i < mesh.Faces.Count; i++) {
            Point3d a = mesh.Vertices[mesh.Faces[i].A];
            Point3d b = mesh.Vertices[mesh.Faces[i].B];
            Point3d c = mesh.Vertices[mesh.Faces[i].C];

            double ab = a.DistanceTo(b);
            double bc = b.DistanceTo(c);
            double ca = c.DistanceTo(a);

            double maxEdge = Math.Max(Math.Max(ab, bc), ca);
            double minEdge = Math.Min(Math.Min(ab, bc), ca);
            aspectRatios[i] = minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue;

            Vector3d vAB = b - a;
            Vector3d vCA = a - c;
            Vector3d vBC = c - b;

            double angleA = Vector3d.VectorAngle(vAB, -vCA);
            double angleB = Vector3d.VectorAngle(vBC, -vAB);
            double angleC = Vector3d.VectorAngle(vCA, -vBC);

            minAngles[i] = Math.Min(Math.Min(angleA, angleB), angleC);
        }

        double maxAspect = aspectRatios.Max();
        double minAngle = minAngles.Min();

        return maxAspect > MorphologyConfig.AspectRatioThreshold
            ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxAspect: {maxAspect:F2}")))
            : minAngle < MorphologyConfig.MinAngleRadiansThreshold
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MinAngle: {RhinoMath.ToDegrees(minAngle):F1}°")))
                : ResultFactory.Create(value: mesh);
    }
}

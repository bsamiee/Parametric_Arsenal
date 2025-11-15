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
                    if (!RhinoMath.IsValidDouble(cageBounds.Volume) || cageBounds.Volume <= RhinoMath.ZeroTolerance) {
                        return ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Cage bounding box has zero volume"));
                    }
                    GeometryBase? deformed = geometry switch {
                        Mesh m => m.DuplicateMesh(),
                        Brep b => b.DuplicateBrep(),
                        _ => null,
                    };
                    if (deformed is null) {
                        return ResultFactory.Create<GeometryBase>(error: E.Geometry.InvalidGeometryType.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Type: {geometry.GetType().Name}")));
                    }
                    Point3d[] vertices = deformed switch {
                        Mesh m => [.. Enumerable.Range(0, m.Vertices.Count).Select(i => (Point3d)m.Vertices[i]),],
                        Brep b => [.. b.Vertices.Select(static v => v.Location),],
                        _ => [],
                    };
                    Vector3d spanVec = cageBounds.Max - cageBounds.Min;
                    Point3d[] deformedVerts = new Point3d[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++) {
                        Vector3d localVec = vertices[i] - cageBounds.Min;
                        (double u, double v, double w) = (
                            RhinoMath.Clamp(spanVec.X > RhinoMath.ZeroTolerance ? localVec.X / spanVec.X : 0.0, 0.0, 1.0),
                            RhinoMath.Clamp(spanVec.Y > RhinoMath.ZeroTolerance ? localVec.Y / spanVec.Y : 0.0, 0.0, 1.0),
                            RhinoMath.Clamp(spanVec.Z > RhinoMath.ZeroTolerance ? localVec.Z / spanVec.Z : 0.0, 0.0, 1.0));
                        (double u1, double v1, double w1) = (1.0 - u, 1.0 - v, 1.0 - w);
                        deformedVerts[i] = Point3d.Origin +
                            (u1 * v1 * w1 * (deformedControlPoints[0] - Point3d.Origin)) +
                            (u * v1 * w1 * (deformedControlPoints[1] - Point3d.Origin)) +
                            (u1 * v * w1 * (deformedControlPoints[2] - Point3d.Origin)) +
                            (u * v * w1 * (deformedControlPoints[3] - Point3d.Origin)) +
                            (u1 * v1 * w * (deformedControlPoints[4] - Point3d.Origin)) +
                            (u * v1 * w * (deformedControlPoints[5] - Point3d.Origin)) +
                            (u1 * v * w * (deformedControlPoints[6] - Point3d.Origin)) +
                            (u * v * w * (deformedControlPoints[7] - Point3d.Origin));
                    }
                    return (deformed switch {
                        Mesh meshDeformed => ApplyMeshDeformation(meshDeformed, deformedVerts),
                        Brep brepDeformed => ApplyBrepDeformation(brepDeformed, deformedVerts),
                        _ => false,
                    })
                        ? ResultFactory.Create(value: deformed)
                        : ResultFactory.Create<GeometryBase>(error: E.Geometry.Morphology.CageDeformFailed.WithContext("Failed to apply deformation to geometry"));
                }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ApplyMeshDeformation(Mesh mesh, Point3d[] deformedVerts) {
        for (int i = 0; i < deformedVerts.Length; i++) {
            mesh.Vertices[i] = new Point3f((float)deformedVerts[i].X, (float)deformedVerts[i].Y, (float)deformedVerts[i].Z);
        }
        return mesh.Normals.ComputeNormals() && mesh.Compact();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ApplyBrepDeformation(Brep brep, Point3d[] deformedVerts) {
        for (int i = 0; i < Math.Min(deformedVerts.Length, brep.Vertices.Count); i++) {
            brep.Vertices[i].Location = deformedVerts[i];
        }
        return brep.IsValid;
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
                        _ = level > 0 ? ((Func<int>)(() => { current.Dispose(); return 0; }))() : 0;
                        return !valid
                            ? (level is 0
                                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.SubdivisionFailed.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Level: {level}, Algorithm: {algorithm}")))
                                : result)
                            : ResultFactory.Create(value: next!);
                    }));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideLoop(Mesh mesh) {
        if (!mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)) {
            return null;
        }

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
                        ? (1.0 / valence) * (MorphologyConfig.LoopCenterWeight - Math.Pow(MorphologyConfig.LoopNeighborBase + (MorphologyConfig.LoopCosineMultiplier * Math.Cos(RhinoMath.TwoPI / valence)), 2.0))
                        : 0.0;

            Point3d sum = neighbors.Aggregate(
                Point3d.Origin,
                (acc, neighborIdx) => {
                    int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0];
                    return acc + originalVerts[meshVertIdx];
                });
            newVerts[i] = ((1.0 - (valence * beta)) * originalVerts[i]) + (beta * sum);
        }

        for (int i = 0; i < newVerts.Length; i++) {
            _ = subdivided.Vertices.Add(newVerts[i]);
        }

        Dictionary<(int, int), int> edgeMidpoints = [];
        for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
            (int a, int b, int c) = (mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C);
            (int, int)[] edges = [
                (Math.Min(a, b), Math.Max(a, b)),
                (Math.Min(b, c), Math.Max(b, c)),
                (Math.Min(c, a), Math.Max(c, a)),
            ];

            int[] midIndices = new int[3];
            for (int e = 0; e < 3; e++) {
                if (edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)) {
                    midIndices[e] = existingMidIdx;
                } else {
                    Vector3d v1 = originalVerts[edges[e].Item1] - Point3d.Origin;
                    Vector3d v2 = originalVerts[edges[e].Item2] - Point3d.Origin;
                    Vector3d va = originalVerts[a] - Point3d.Origin;
                    Vector3d vb = originalVerts[b] - Point3d.Origin;
                    Vector3d vc = originalVerts[c] - Point3d.Origin;
                    Point3d midpoint = Point3d.Origin + (MorphologyConfig.LoopEdgeMidpointWeight * (v1 + v2)) + (MorphologyConfig.LoopEdgeOppositeWeight * (va + vb + vc - v1 - v2));
                    int newMidIdx = subdivided.Vertices.Add(midpoint);
                    edgeMidpoints[edges[e]] = newMidIdx;
                    midIndices[e] = newMidIdx;
                }
            }

            _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
            _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
            _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
            _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
        }

        _ = subdivided.Normals.ComputeNormals();
        _ = subdivided.Compact();
        return subdivided;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Mesh? SubdivideButterfly(Mesh mesh) {
        if (!mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)) {
            return null;
        }

        Mesh subdivided = new();
        Point3d[] originalVerts = [.. Enumerable.Range(0, mesh.Vertices.Count).Select(i => (Point3d)mesh.Vertices[i]),];

        for (int i = 0; i < originalVerts.Length; i++) {
            _ = subdivided.Vertices.Add(originalVerts[i]);
        }

        Dictionary<(int, int), int> edgeMidpoints = [];
        for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
            (int a, int b, int c) = (mesh.Faces[faceIdx].A, mesh.Faces[faceIdx].B, mesh.Faces[faceIdx].C);
            (int, int)[] edges = [
                (Math.Min(a, b), Math.Max(a, b)),
                (Math.Min(b, c), Math.Max(b, c)),
                (Math.Min(c, a), Math.Max(c, a)),
            ];

            int[] midIndices = new int[3];
            for (int e = 0; e < 3; e++) {
                if (edgeMidpoints.TryGetValue(edges[e], out int existingMidIdx)) {
                    midIndices[e] = existingMidIdx;
                } else {
                    int v1 = edges[e].Item1;
                    int v2 = edges[e].Item2;
                    Point3d mid = MorphologyConfig.ButterflyMidpointWeight * (originalVerts[v1] + originalVerts[v2]);
                    int[] v1Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v1);
                    int[] v2Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v2);
                    (int opposite1, int opposite2) = v1Neighbors.Length >= 4 && v2Neighbors.Length >= 4
                        ? FindButterflyOpposites(mesh, v1, v2)
                        : (-1, -1);

                    Point3d midpoint = mid;
                    if (opposite1 >= 0 && opposite2 >= 0) {
                        int[] wings = [
                            .. v1Neighbors.Where(n => n != default && n != v2 && n != opposite1 && n != opposite2).Take(2),
                            .. v2Neighbors.Where(n => n != default && n != v1 && n != opposite1 && n != opposite2).Take(2),
                        ];
                        Vector3d adjusted = (mid - Point3d.Origin) + (MorphologyConfig.ButterflyOppositeWeight * ((originalVerts[opposite1] - Point3d.Origin) + (originalVerts[opposite2] - Point3d.Origin)));
                        Vector3d wingAdjustment = wings.Aggregate(
                            Vector3d.Zero,
                            (acc, w) => acc - (MorphologyConfig.ButterflyWingWeight * (originalVerts[mesh.TopologyVertices.MeshVertexIndices(w)[0]] - Point3d.Origin)));
                        midpoint = Point3d.Origin + adjusted + wingAdjustment;
                    }

                    int newMidIdx = subdivided.Vertices.Add(midpoint);
                    edgeMidpoints[edges[e]] = newMidIdx;
                    midIndices[e] = newMidIdx;
                }
            }

            _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
            _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
            _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
            _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
        }

        _ = subdivided.Normals.ComputeNormals();
        _ = subdivided.Compact();
        return subdivided;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int, int) FindButterflyOpposites(Mesh mesh, int v1, int v2) =>
        Enumerable.Range(0, mesh.Faces.Count).Aggregate(
            (-1, -1),
            (state, fi) => {
                int[] faceVerts = [mesh.Faces[fi].A, mesh.Faces[fi].B, mesh.Faces[fi].C,];
                (bool hasV1, bool hasV2) = (Array.IndexOf(faceVerts, v1) >= 0, Array.IndexOf(faceVerts, v2) >= 0);
                int opp = hasV1 && hasV2 ? faceVerts.First(v => v != v1 && v != v2) : -1;
                return (
                    opp >= 0 && state.Item1 < 0 ? opp : state.Item1,
                    opp >= 0 && state.Item1 >= 0 && opp != state.Item1 ? opp : state.Item2);
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

                        double rmsDisp = iter > 0
                            ? Math.Sqrt(Enumerable.Range(0, smoothed.Vertices.Count).Average(i => {
                                double dist = positions[i].DistanceTo(prevPositions[i]);
                                return dist * dist;
                            }))
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
                            accuracy: (int)(RhinoMath.Clamp(accuracy, MorphologyConfig.MinReductionAccuracy, MorphologyConfig.MaxReductionAccuracy) * MorphologyConfig.ReductionAccuracyScale),
                            normalizeSize: false,
                            cancelToken: CancellationToken.None,
                            progress: null,
                            problemDescription: out string _,
                            threaded: false);
                        return success && reduced.IsValid && reduced.Faces.Count <= targetFaceCount * MorphologyConfig.ReductionTargetTolerance
                            ? ResultFactory.Create(value: reduced)
                            : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.ReductionTargetInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Achieved: {reduced.Faces.Count}, Target: {targetFaceCount}")));
                    }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> RemeshIsotropic(
        Mesh mesh,
        double targetEdgeLength,
        int maxIterations,
        bool _,
        IGeometryContext context) =>
        ((Func<Result<Mesh>>)(() => {
            BoundingBox bounds = mesh.GetBoundingBox(accurate: false);
            double diagLength = bounds.Diagonal.Length;
            double minEdge = context.AbsoluteTolerance * MorphologyConfig.RemeshMinEdgeLengthFactor;
            double maxEdge = diagLength * MorphologyConfig.RemeshMaxEdgeLengthFactor;
            if (!RhinoMath.IsValidDouble(targetEdgeLength) || targetEdgeLength < minEdge || targetEdgeLength > maxEdge) {
                return ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshTargetEdgeLengthInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target: {targetEdgeLength:F6}, Range: [{minEdge:F6}, {maxEdge:F6}]")));
            }
            if (maxIterations is <= 0 or > MorphologyConfig.MaxRemeshIterations) {
                return ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshIterationLimitExceeded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxIters: {maxIterations}")));
            }
            Mesh remeshed = mesh.DuplicateMesh();
            double splitThreshold = targetEdgeLength * MorphologyConfig.RemeshSplitThresholdFactor;
            for (int iter = 0; iter < maxIterations; iter++) {
                for (int i = remeshed.TopologyEdges.Count - 1; i >= 0; i--) {
                    Line edge = remeshed.TopologyEdges.EdgeLine(i);
                    _ = edge.Length > splitThreshold && remeshed.Vertices.Add(edge.PointAt(MorphologyConfig.EdgeMidpointParameter)) >= 0;
                }
                Point3d[] positions = [.. Enumerable.Range(0, remeshed.Vertices.Count).Select(i => (Point3d)remeshed.Vertices[i]),];
                Point3d[] smoothed = MorphologyCore.LaplacianUpdate(remeshed, positions, useCotangent: false);
                for (int i = 0; i < smoothed.Length; i++) {
                    _ = remeshed.Vertices.SetVertex(i, smoothed[i]);
                }
                _ = remeshed.Normals.ComputeNormals();
                _ = remeshed.Compact();
            }
            return remeshed.IsValid
                ? ResultFactory.Create(value: remeshed)
                : ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.RemeshingFailed);
        }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ValidateMeshQuality(Mesh mesh, IGeometryContext context) =>
        ((Func<Result<Mesh>>)(() => {
            double[] aspectRatios = new double[mesh.Faces.Count];
            double[] minAngles = new double[mesh.Faces.Count];
            for (int i = 0; i < mesh.Faces.Count; i++) {
                (Point3d a, Point3d b, Point3d c) = (mesh.Vertices[mesh.Faces[i].A], mesh.Vertices[mesh.Faces[i].B], mesh.Vertices[mesh.Faces[i].C]);
                (double ab, double bc, double ca) = (a.DistanceTo(b), b.DistanceTo(c), c.DistanceTo(a));
                (double maxEdge, double minEdge) = (Math.Max(Math.Max(ab, bc), ca), Math.Min(Math.Min(ab, bc), ca));
                aspectRatios[i] = minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue;
                (Vector3d vAB, Vector3d vCA, Vector3d vBC) = (b - a, a - c, c - b);
                (double angleA, double angleB, double angleC) = (Vector3d.VectorAngle(vAB, -vCA), Vector3d.VectorAngle(vBC, -vAB), Vector3d.VectorAngle(vCA, -vBC));
                minAngles[i] = Math.Min(Math.Min(angleA, angleB), angleC);
            }
            (double maxAspect, double minAngle) = (aspectRatios.Max(), minAngles.Min());
            return maxAspect > MorphologyConfig.AspectRatioThreshold
                ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxAspect: {maxAspect:F2}")))
                : minAngle < MorphologyConfig.MinAngleRadiansThreshold
                    ? ResultFactory.Create<Mesh>(error: E.Geometry.Morphology.MeshQualityDegraded.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MinAngle: {RhinoMath.ToDegrees(minAngle):F1}°")))
                    : ResultFactory.Create(value: mesh);
        }))();
}

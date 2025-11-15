using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation dispatch and executor implementations.</summary>
internal static class MorphologyCore {
    /// <summary>Operation dispatch: (operation ID, type) → executor function.</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type InputType), Func<object, object, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>> OperationDispatch =
        new Dictionary<(byte, Type), Func<object, object, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>> {
            [(MorphologyConfig.OpCageDeform, typeof(Mesh))] = ExecuteCageDeform,
            [(MorphologyConfig.OpCageDeform, typeof(Brep))] = ExecuteCageDeform,
            [(MorphologyConfig.OpSubdivideCatmullClark, typeof(Mesh))] = ExecuteSubdivideCatmullClark,
            [(MorphologyConfig.OpSubdivideLoop, typeof(Mesh))] = ExecuteSubdivideLoop,
            [(MorphologyConfig.OpSubdivideButterfly, typeof(Mesh))] = ExecuteSubdivideButterfly,
            [(MorphologyConfig.OpSmoothLaplacian, typeof(Mesh))] = ExecuteSmoothLaplacian,
            [(MorphologyConfig.OpSmoothTaubin, typeof(Mesh))] = ExecuteSmoothTaubin,
            [(MorphologyConfig.OpEvolveMeanCurvature, typeof(Mesh))] = ExecuteEvolveMeanCurvature,
            [(MorphologyConfig.OpOffset, typeof(Mesh))] = ExecuteOffset,
            [(MorphologyConfig.OpReduce, typeof(Mesh))] = ExecuteReduce,
            [(MorphologyConfig.OpRemesh, typeof(Mesh))] = ExecuteRemesh,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(
        object input,
        object parameters,
        IGeometryContext context) =>
        parameters is not (GeometryBase cage, Point3d[] originalPts, Point3d[] deformedPts)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext("Expected: (GeometryBase cage, Point3d[] original, Point3d[] deformed)"))
            : input is not Mesh and not Brep
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.InvalidGeometryType.WithContext($"Type: {input.GetType().Name}"))
                : ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    GeometryBase inputGeom = (GeometryBase)input;
                    BoundingBox originalBounds = inputGeom.GetBoundingBox(accurate: false);
                    double originalVolume = originalBounds.Volume;

                    Result<GeometryBase> deformResult = MorphologyCompute.CageDeform(inputGeom, cage, originalPts, deformedPts, context);
                    return deformResult.IsSuccess
                        ? ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                            GeometryBase deformed = deformResult.Value;
                            BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
                            double deformedVolume = deformedBounds.Volume;
                            double[] displacements = [.. originalPts.Zip(deformedPts, (o, d) => o.DistanceTo(d)),];
                            double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
                            double meanDisp = displacements.Length > 0 ? displacements.Average() : 0.0;
                            double volumeRatio = RhinoMath.IsValidDouble(originalVolume) && originalVolume > RhinoMath.ZeroTolerance
                                ? deformedVolume / originalVolume
                                : 1.0;

                            return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                                value: [new Morphology.CageDeformResult(deformed, maxDisp, meanDisp, originalBounds, deformedBounds, volumeRatio),]);
                        }))()
                        : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: deformResult.Error);
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideCatmullClark, levels, context)
                    .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.LoopRequiresTriangles.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                    : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideLoop, levels, context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.ButterflyRequiresTriangles.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                    : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideButterfly, levels, context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (int iters, bool lockBound)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, bool lockBoundary)"))
                : MorphologyCompute.SmoothWithConvergence(
                    mesh,
                    iters,
                    lockBound,
                    (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true),
                    context)
                    .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iters, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (int iterations, double lambda, double mu)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, double lambda, double mu)"))
                : mu >= -lambda
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({mu:F4}) must be < -λ ({(-lambda):F4})")))
                    : MorphologyCompute.SmoothWithConvergence(
                        mesh,
                        iterations,
                        lockBoundary: false,
                        (m, pos, _) => {
                            Point3d[] step1 = LaplacianUpdate(m, pos, useCotangent: false);
                            Point3d[] blended1 = [.. Enumerable.Range(0, pos.Length).Select(i => pos[i] + (lambda * (step1[i] - pos[i]))),];
                            Point3d[] step2 = LaplacianUpdate(m, blended1, useCotangent: false);
                            return [.. Enumerable.Range(0, pos.Length).Select(i => blended1[i] + (mu * (step2[i] - blended1[i]))),];
                        },
                        context)
                        .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (double timeStep, int iters)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (double timeStep, int iterations)"))
                : MorphologyCompute.SmoothWithConvergence(
                    mesh,
                    iters,
                    lockBoundary: false,
                    (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, timeStep, _),
                    context)
                    .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, iters, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (double distance, bool bothSides)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (double distance, bool bothSides)"))
                : MorphologyCompute.OffsetMesh(mesh, distance, bothSides, context)
                    .Bind(offset => ComputeOffsetMetrics(mesh, offset, distance, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (int targetFaces, bool preserveBoundary, double accuracy)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int targetFaceCount, bool preserveBoundary, double accuracy)"))
                : MorphologyCompute.ReduceMesh(mesh, targetFaces, preserveBoundary, accuracy, context)
                    .Bind(reduced => ComputeReductionMetrics(mesh, reduced, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (double targetEdge, int maxIters, bool preserveFeats)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (double targetEdgeLength, int maxIterations, bool preserveFeatures)"))
                : MorphologyCompute.RemeshIsotropic(mesh, targetEdge, maxIters, preserveFeats, context)
                    .Bind(remeshed => ComputeRemeshMetrics(remeshed, targetEdge, maxIters, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) {
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
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep, IGeometryContext _) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            updated[i] = neighbors.Length is 0
                ? positions[i]
                : ((Func<Point3d>)(() => {
                    Point3d laplacian = Point3d.Origin;
                    for (int j = 0; j < neighbors.Length; j++) {
                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                        laplacian += positions[meshVertIdx] - positions[i];
                    }
                    laplacian /= neighbors.Length;

                    Vector3d normal = mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis;
                    Vector3d laplacianVec = laplacian - Point3d.Origin;
                    double curvature = normal * laplacianVec;
                    return positions[i] + ((timeStep * curvature) * normal);
                }))();
        }

        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSubdivisionMetrics(
        Mesh original,
        Mesh subdivided,
        IGeometryContext context) {
        double[] edgeLengths = [.. Enumerable.Range(0, subdivided.TopologyEdges.Count)
            .Select(i => subdivided.TopologyEdges.EdgeLine(i).Length),
        ];

        double[] aspectRatios = [.. Enumerable.Range(0, subdivided.Faces.Count)
            .Select(i => {
                Point3d a = subdivided.Vertices[subdivided.Faces[i].A];
                Point3d b = subdivided.Vertices[subdivided.Faces[i].B];
                Point3d c = subdivided.Vertices[subdivided.Faces[i].C];
                double ab = a.DistanceTo(b);
                double bc = b.DistanceTo(c);
                double ca = c.DistanceTo(a);
                double maxEdge = Math.Max(Math.Max(ab, bc), ca);
                double minEdge = Math.Min(Math.Min(ab, bc), ca);
                return minEdge > context.AbsoluteTolerance ? (maxEdge / minEdge) : double.MaxValue;
            }),
        ];

        double[] triangleAngles = [.. Enumerable.Range(0, subdivided.Faces.Count)
            .SelectMany(i => {
                Point3d a = subdivided.Vertices[subdivided.Faces[i].A];
                Point3d b = subdivided.Vertices[subdivided.Faces[i].B];
                Point3d c = subdivided.Vertices[subdivided.Faces[i].C];
                return new[] {
                    Vector3d.VectorAngle(b - a, c - a),
                    Vector3d.VectorAngle(a - b, c - b),
                    Vector3d.VectorAngle(a - c, b - c),
                };
            }),
        ];

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [
                new Morphology.SubdivisionResult(
                    subdivided,
                    original.Faces.Count,
                    subdivided.Faces.Count,
                    edgeLengths.Min(),
                    edgeLengths.Max(),
                    edgeLengths.Average(),
                    aspectRatios.Average(),
                    triangleAngles.Min()),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) {
        double[] displacements = [.. Enumerable.Range(0, Math.Min(original.Vertices.Count, smoothed.Vertices.Count))
            .Select(i => ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i])),
        ];

        double[] squares = [.. displacements.Select(static d => d * d),];
        double rms = displacements.Length > 0 ? Math.Sqrt(squares.Average()) : 0.0;
        double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
        double quality = MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess ? 1.0 : 0.0;
        bool converged = rms < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [
                new Morphology.SmoothingResult(smoothed, iterations, rms, maxDisp, quality, converged),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeOffsetMetrics(
        Mesh original,
        Mesh offset,
        double _,
        IGeometryContext context) {
        Point3d[] originalVerts = [.. Enumerable.Range(0, Math.Min(original.Vertices.Count, offset.Vertices.Count))
            .Select(i => (Point3d)original.Vertices[i]),
        ];
        Point3d[] offsetVerts = [.. Enumerable.Range(0, Math.Min(original.Vertices.Count, offset.Vertices.Count))
            .Select(i => (Point3d)offset.Vertices[i]),
        ];
        double[] displacements = [.. originalVerts.Zip(offsetVerts, (o, off) => o.DistanceTo(off)),];
        double actualDist = displacements.Length > 0 ? displacements.Average() : 0.0;
        bool hasDegen = !MorphologyCompute.ValidateMeshQuality(offset, context).IsSuccess;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.OffsetResult(
                offset,
                actualDist,
                hasDegen,
                original.Vertices.Count,
                offset.Vertices.Count,
                original.Faces.Count,
                offset.Faces.Count),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeReductionMetrics(
        Mesh original,
        Mesh reduced,
        IGeometryContext context) {
        double reductionRatio = original.Faces.Count > 0
            ? (double)reduced.Faces.Count / original.Faces.Count
            : 1.0;

        double[] aspectRatios = [.. Enumerable.Range(0, reduced.Faces.Count)
            .Select(i => {
                Point3d a = reduced.Vertices[reduced.Faces[i].A];
                Point3d b = reduced.Vertices[reduced.Faces[i].B];
                Point3d c = reduced.Vertices[reduced.Faces[i].C];
                double ab = a.DistanceTo(b);
                double bc = b.DistanceTo(c);
                double ca = c.DistanceTo(a);
                double maxEdge = Math.Max(Math.Max(ab, bc), ca);
                double minEdge = Math.Min(Math.Min(ab, bc), ca);
                return minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue;
            }),
        ];

        double[] edgeLengths = [.. Enumerable.Range(0, reduced.TopologyEdges.Count)
            .Select(i => reduced.TopologyEdges.EdgeLine(i).Length),
        ];

        double quality = MorphologyCompute.ValidateMeshQuality(reduced, context).IsSuccess ? 1.0 : 0.0;
        double meanAspect = aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0;
        double minEdge = edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0;
        double maxEdge = edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.ReductionResult(
                reduced,
                original.Faces.Count,
                reduced.Faces.Count,
                reductionRatio,
                quality,
                meanAspect,
                minEdge,
                maxEdge),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRemeshMetrics(
        Mesh remeshed,
        double targetEdge,
        int maxIters,
        IGeometryContext context) {
        double[] edgeLengths = [.. Enumerable.Range(0, remeshed.TopologyEdges.Count)
            .Select(i => remeshed.TopologyEdges.EdgeLine(i).Length),
        ];

        double mean = edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0;
        double[] squares = [.. edgeLengths.Select(e => Math.Pow(e - mean, 2.0)),];
        double variance = edgeLengths.Length > 0
            ? squares.Average()
            : 0.0;
        double stdDev = Math.Sqrt(variance);
        double uniformity = mean > context.AbsoluteTolerance
            ? Math.Exp(-stdDev / mean)
            : 0.0;
        bool converged = Math.Abs(mean - targetEdge) < (targetEdge * MorphologyConfig.RemeshUniformityWeight * 0.1);

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.RemeshResult(
                remeshed,
                targetEdge,
                mean,
                stdDev,
                uniformity,
                maxIters,
                converged,
                0,
                remeshed.Faces.Count),
            ]);
    }
}

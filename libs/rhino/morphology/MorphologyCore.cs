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
                : MorphologyCompute.CageDeform((GeometryBase)input, cage, originalPts, deformedPts, context)
                    .Bind(deformed => {
                        GeometryBase inputGeom = (GeometryBase)input;
                        BoundingBox originalBounds = inputGeom.GetBoundingBox(accurate: false);
                        BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
                        double[] displacements = [.. originalPts.Zip(deformedPts, static (o, d) => o.DistanceTo(d)),];
                        (double maxDisp, double meanDisp) = displacements.Length > 0
                            ? (displacements.Max(), displacements.Average())
                            : (0.0, 0.0);
                        double volumeRatio = RhinoMath.IsValidDouble(originalBounds.Volume) && originalBounds.Volume > RhinoMath.ZeroTolerance
                            ? deformedBounds.Volume / originalBounds.Volume
                            : 1.0;

                        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            value: [new Morphology.CageDeformResult(deformed, maxDisp, meanDisp, originalBounds, deformedBounds, volumeRatio),]);
                    });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(
        object input,
        object parameters,
        IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, MorphologyConfig.OpSubdivideCatmullClark, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(
        object input,
        object parameters,
        IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, MorphologyConfig.OpSubdivideLoop, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(
        object input,
        object parameters,
        IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, MorphologyConfig.OpSubdivideButterfly, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(
        object input,
        object parameters,
        byte algorithm,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : MorphologyConfig.TriangulatedSubdivisionOps.Contains(algorithm) && !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: (algorithm == MorphologyConfig.OpSubdivideLoop ? E.Geometry.Morphology.LoopRequiresTriangles : E.Geometry.Morphology.ButterflyRequiresTriangles)
                            .WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                    : MorphologyCompute.SubdivideIterative(mesh, algorithm, levels, context)
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
                        (m, pos, _) => TaubinUpdate(m, pos, lambda, mu),
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
    private static Point3d[] TaubinUpdate(Mesh mesh, Point3d[] positions, double lambda, double mu) {
        Point3d[] step1 = LaplacianUpdate(mesh, positions, useCotangent: false);
        Point3d[] blended1 = new Point3d[positions.Length];
        for (int i = 0; i < positions.Length; i++) {
            blended1[i] = positions[i] + (lambda * (step1[i] - positions[i]));
        }
        Point3d[] step2 = LaplacianUpdate(mesh, blended1, useCotangent: false);
        Point3d[] result = new Point3d[positions.Length];
        for (int i = 0; i < positions.Length; i++) {
            result[i] = blended1[i] + (mu * (step2[i] - blended1[i]));
        }
        return result;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            if (neighbors.Length is 0) {
                updated[i] = positions[i];
                continue;
            }
            Point3d currentPos = positions[i];
            (Vector3d weightedSum, double weightSum) = (Vector3d.Zero, 0.0);
            for (int j = 0; j < neighbors.Length; j++) {
                int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                Point3d neighborPos = positions[meshVertIdx];
                double weight = useCotangent
                    ? MorphologyConfig.UniformLaplacianWeight / Math.Max(currentPos.DistanceTo(neighborPos), RhinoMath.ZeroTolerance)
                    : MorphologyConfig.UniformLaplacianWeight;
                weightedSum += weight * (Vector3d)neighborPos;
                weightSum += weight;
            }
            updated[i] = weightSum > RhinoMath.ZeroTolerance ? (Point3d)(weightedSum / weightSum) : currentPos;
        }

        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep, IGeometryContext _) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            Point3d currentPos = positions[i];
            Point3d laplacian = neighbors.Length is 0
                ? Point3d.Origin
                : neighbors.Aggregate(
                    Point3d.Origin,
                    (acc, neighborIdx) => {
                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0];
                        return acc + (positions[meshVertIdx] - currentPos);
                    }) / neighbors.Length;
            Vector3d normal = mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis;
            double curvature = normal * (laplacian - Point3d.Origin);
            updated[i] = neighbors.Length is 0 ? currentPos : currentPos + ((timeStep * curvature) * normal);
        }

        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (double[] EdgeLengths, double[] AspectRatios, double[] MinAngles) ComputeMeshMetrics(Mesh mesh, IGeometryContext context) {
        int faceCount = mesh.Faces.Count;
        int edgeCount = mesh.TopologyEdges.Count;
        double[] edges = new double[edgeCount];
        double[] aspects = new double[faceCount];
        double[] angles = new double[faceCount];

        for (int i = 0; i < edgeCount; i++) {
            edges[i] = mesh.TopologyEdges.EdgeLine(i).Length;
        }

        for (int i = 0; i < faceCount; i++) {
            (Point3d a, Point3d b, Point3d c) = (mesh.Vertices[mesh.Faces[i].A], mesh.Vertices[mesh.Faces[i].B], mesh.Vertices[mesh.Faces[i].C]);
            (double ab, double bc, double ca) = (a.DistanceTo(b), b.DistanceTo(c), c.DistanceTo(a));
            (double maxE, double minE) = (Math.Max(Math.Max(ab, bc), ca), Math.Min(Math.Min(ab, bc), ca));
            aspects[i] = minE > context.AbsoluteTolerance ? maxE / minE : double.MaxValue;
            (Vector3d vAB, Vector3d vBC, Vector3d vCA) = (b - a, c - b, a - c);
            angles[i] = Math.Min(Math.Min(
                Vector3d.VectorAngle(vAB, -vCA),
                Vector3d.VectorAngle(vBC, -vAB)),
                Vector3d.VectorAngle(vCA, -vBC));
        }

        return (edges, aspects, angles);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSubdivisionMetrics(
        Mesh original,
        Mesh subdivided,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = ComputeMeshMetrics(subdivided, context);
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
                    minAngles.Min()),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) {
        int vertCount = Math.Min(original.Vertices.Count, smoothed.Vertices.Count);
        (double sumSq, double maxDisp) = (0.0, 0.0);
        for (int i = 0; i < vertCount; i++) {
            double dist = ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i]);
            sumSq += dist * dist;
            maxDisp = Math.Max(maxDisp, dist);
        }
        double rms = vertCount > 0 ? Math.Sqrt(sumSq / vertCount) : 0.0;
        double quality = MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess ? 1.0 : 0.0;
        bool converged = rms < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.SmoothingResult(smoothed, iterations, rms, maxDisp, quality, converged),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeOffsetMetrics(
        Mesh original,
        Mesh offset,
        double _,
        IGeometryContext context) {
        int minCount = Math.Min(original.Vertices.Count, offset.Vertices.Count);
        double actualDist = minCount > 0
            ? Enumerable.Range(0, minCount).Average(i => ((Point3d)original.Vertices[i]).DistanceTo(offset.Vertices[i]))
            : 0.0;
        bool hasDegen = !MorphologyCompute.ValidateMeshQuality(offset, context).IsSuccess;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.OffsetResult(offset, actualDist, hasDegen, original.Vertices.Count, offset.Vertices.Count, original.Faces.Count, offset.Faces.Count),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeReductionMetrics(
        Mesh original,
        Mesh reduced,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] _) = ComputeMeshMetrics(reduced, context);
        double reductionRatio = original.Faces.Count > 0 ? (double)reduced.Faces.Count / original.Faces.Count : 1.0;
        double quality = MorphologyCompute.ValidateMeshQuality(reduced, context).IsSuccess ? 1.0 : 0.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.ReductionResult(
                reduced,
                original.Faces.Count,
                reduced.Faces.Count,
                reductionRatio,
                quality,
                aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRemeshMetrics(
        Mesh remeshed,
        double targetEdge,
        int maxIters,
        IGeometryContext context) {
        int edgeCount = remeshed.TopologyEdges.Count;
        (double sum, double sumSq) = (0.0, 0.0);
        for (int i = 0; i < edgeCount; i++) {
            double len = remeshed.TopologyEdges.EdgeLine(i).Length;
            sum += len;
            sumSq += len * len;
        }
        double mean = edgeCount > 0 ? sum / edgeCount : 0.0;
        double variance = edgeCount > 0 ? (sumSq / edgeCount) - (mean * mean) : 0.0;
        double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
        (double uniformity, bool converged) = (
            mean > context.AbsoluteTolerance ? Math.Exp(-stdDev / mean) : 0.0,
            Math.Abs(mean - targetEdge) < (targetEdge * MorphologyConfig.RemeshUniformityWeight * MorphologyConfig.RemeshConvergenceThreshold));

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.RemeshResult(remeshed, targetEdge, mean, stdDev, uniformity, maxIters, converged, 0, remeshed.Faces.Count),]);
    }
}

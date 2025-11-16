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
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> Execute<TGeom, TParam>(
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
                        BoundingBox originalBounds = ((GeometryBase)input).GetBoundingBox(accurate: false);
                        BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
                        double[] displacements = [.. originalPts.Zip(deformedPts, static (o, d) => o.DistanceTo(d)),];
                        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            value: [new Morphology.CageDeformResult(
                                deformed,
                                displacements.Length > 0 ? displacements.Max() : 0.0,
                                displacements.Length > 0 ? displacements.Average() : 0.0,
                                originalBounds,
                                deformedBounds,
                                RhinoMath.IsValidDouble(originalBounds.Volume) && originalBounds.Volume > RhinoMath.ZeroTolerance
                                    ? deformedBounds.Volume / originalBounds.Volume
                                    : 1.0),
                            ]);
                    });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideCatmullClark);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideLoop);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideButterfly);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(object input, object parameters, IGeometryContext context, byte algorithm) =>
        Execute<Mesh, int>(input, parameters, context, (mesh, levels, ctx) =>
            MorphologyConfig.TriangulatedSubdivisionOps.Contains(algorithm) && !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: (algorithm == MorphologyConfig.OpSubdivideLoop ? E.Geometry.Morphology.LoopRequiresTriangles : E.Geometry.Morphology.ButterflyRequiresTriangles)
                        .WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                : MorphologyCompute.SubdivideIterative(mesh, algorithm, levels, ctx).Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, ctx)));

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
                            Point3d[] blended1 = [.. pos.Select((p, i) => p + (lambda * (step1[i] - p))),];
                            Point3d[] step2 = LaplacianUpdate(m, blended1, useCotangent: false);
                            return [.. blended1.Select((b, i) => b + (mu * (step2[i] - b))),];
                        },
                        context)
                        .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (int, double, double)>(input, parameters, context, (mesh, p, ctx) =>
            p.Item3 >= -p.Item2
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({p.Item3:F4}) must be < -λ ({(-p.Item2):F4})")))
                : MorphologyCompute.SmoothWithConvergence(mesh, p.Item1, lockBoundary: false, (m, pos, _) => TaubinUpdate(m, pos, p.Item2, p.Item3), ctx).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, p.Item1, ctx)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, int)>(input, parameters, context, (mesh, p, ctx) =>
            MorphologyCompute.SmoothWithConvergence(mesh, p.Item2, lockBoundary: false, (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, p.Item1, _), ctx)
                .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, p.Item2, ctx)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, bool)>(input, parameters, context, (mesh, p, ctx) =>
            MorphologyCompute.OffsetMesh(mesh, p.Item1, p.Item2, ctx).Bind(offset => ComputeOffsetMetrics(mesh, offset, p.Item1, ctx)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (int, bool, double)>(input, parameters, context, (mesh, p, ctx) =>
            MorphologyCompute.ReduceMesh(mesh, p.Item1, p.Item2, p.Item3, ctx).Bind(reduced => ComputeReductionMetrics(mesh, reduced, ctx)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, int, bool)>(input, parameters, context, (mesh, p, ctx) =>
            MorphologyCompute.RemeshIsotropic(mesh, p.Item1, p.Item2, p.Item3, ctx).Bind(remeshed => ComputeRemeshMetrics(remeshed, p.Item1, p.Item2, ctx)));

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
    internal static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) =>
        [.. Enumerable.Range(0, positions.Length).Select(i => {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            return neighbors.Length is 0
                ? positions[i]
                : neighbors.Aggregate(
                    (weightedSum: Vector3d.Zero, weightSum: 0.0),
                    (acc, neighborIdx) => {
                        Point3d neighborPos = positions[mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0]];
                        double weight = useCotangent
                            ? MorphologyConfig.UniformLaplacianWeight / Math.Max(positions[i].DistanceTo(neighborPos), RhinoMath.ZeroTolerance)
                            : MorphologyConfig.UniformLaplacianWeight;
                        return (acc.weightedSum + (weight * (Vector3d)neighborPos), acc.weightSum + weight);
                    }) is var (wSum, wTotal) && wTotal > RhinoMath.ZeroTolerance
                        ? (Point3d)(wSum / wTotal)
                        : positions[i];
        }),
        ];

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep, IGeometryContext _) =>
        [.. Enumerable.Range(0, positions.Length).Select(i => {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            return neighbors.Length is 0
                ? positions[i]
                : positions[i] + ((timeStep * (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis) *
                    ((neighbors.Aggregate(Point3d.Origin, (acc, neighborIdx) =>
                        acc + (positions[mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0]] - positions[i])) / neighbors.Length) - Point3d.Origin)) *
                    (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis));
        }),
        ];

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
        double[] edgeLengths = [.. Enumerable.Range(0, subdivided.TopologyEdges.Count).Select(i => subdivided.TopologyEdges.EdgeLine(i).Length),];
        (double aspectRatio, double[] angles)[] metrics = [.. Enumerable.Range(0, subdivided.Faces.Count).Select(i => {
            (Point3d a, Point3d b, Point3d c) = (subdivided.Vertices[subdivided.Faces[i].A], subdivided.Vertices[subdivided.Faces[i].B], subdivided.Vertices[subdivided.Faces[i].C]);
            (double ab, double bc, double ca) = (a.DistanceTo(b), b.DistanceTo(c), c.DistanceTo(a));
            (double maxEdge, double minEdge) = (Math.Max(Math.Max(ab, bc), ca), Math.Min(Math.Min(ab, bc), ca));
            return (
                aspectRatio: minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue,
                angles: new[] {
                    Vector3d.VectorAngle(b - a, c - a),
                    Vector3d.VectorAngle(a - b, c - b),
                    Vector3d.VectorAngle(a - c, b - c),
                }
            );
        }),
        ];
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.SubdivisionResult(
                subdivided,
                original.Faces.Count,
                subdivided.Faces.Count,
                edgeLengths.Min(),
                edgeLengths.Max(),
                edgeLengths.Average(),
                metrics.Average(m => m.aspectRatio),
                metrics.SelectMany(m => m.angles).Min()),
        ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) {
        int vertCount = Math.Min(original.Vertices.Count, smoothed.Vertices.Count);
        (double sumSq, double maxDisp) = Enumerable.Range(0, vertCount).Aggregate(
            (0.0, 0.0),
            (acc, i) => {
                double dist = ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i]);
                return (acc.Item1 + (dist * dist), Math.Max(acc.Item2, dist));
            });
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.SmoothingResult(
                smoothed,
                iterations,
                vertCount > 0 ? Math.Sqrt(sumSq / vertCount) : 0.0,
                maxDisp,
                MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess ? 1.0 : 0.0,
                Math.Sqrt(sumSq / Math.Max(vertCount, 1)) < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier),
        ]);
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

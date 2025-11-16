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
        IGeometryContext context,
        Func<TGeom, TParam, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>> compute,
        Func<object, bool>? geomCheck = null) where TGeom : GeometryBase =>
        input is not TGeom geom || (geomCheck is not null && !geomCheck(input))
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: {typeof(TGeom).Name}"))
            : parameters is not TParam param
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext($"Expected: {typeof(TParam).Name}"))
                : compute(geom, param, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(object input, object parameters, IGeometryContext context) =>
        Execute<GeometryBase, (GeometryBase, Point3d[], Point3d[])>(
            input,
            parameters,
            context,
            (geom, p, ctx) => {
                (GeometryBase cage, Point3d[] originalPts, Point3d[] deformedPts) = p;
                return MorphologyCompute.CageDeform(geom, cage, originalPts, deformedPts, ctx).Bind(deformed => {
                    BoundingBox originalBounds = geom.GetBoundingBox(accurate: false);
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
            },
            geomCheck: g => g is Mesh or Brep);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideCatmullClark);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideLoop);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(object input, object parameters, IGeometryContext context) =>
        ExecuteSubdivision(input, parameters, context, MorphologyConfig.OpSubdivideButterfly);

    /// <summary>Unified subdivision executor for CatmullClark, Loop, and Butterfly algorithms. Validates triangulated mesh requirement for Loop/Butterfly (MorphologyConfig.OpSubdivideLoop/OpSubdivideButterfly).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(object input, object parameters, IGeometryContext context, byte algorithm) =>
        Execute<Mesh, int>(input, parameters, context, (mesh, levels, ctx) =>
            MorphologyConfig.TriangulatedSubdivisionOps.Contains(algorithm) && !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: (algorithm == MorphologyConfig.OpSubdivideLoop ? E.Geometry.Morphology.LoopRequiresTriangles : E.Geometry.Morphology.ButterflyRequiresTriangles)
                        .WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                : MorphologyCompute.SubdivideIterative(mesh, algorithm, levels, ctx).Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, ctx)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (int, bool)>(input, parameters, context, (mesh, p, ctx) => {
            (int iters, bool lockBound) = p;
            return MorphologyCompute.SmoothWithConvergence(mesh, iters, lockBound, (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true), ctx)
                .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iters, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (int, double, double)>(input, parameters, context, (mesh, p, ctx) => {
            (int iterations, double lambda, double mu) = p;
            return mu >= -lambda
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({mu:F4}) must be < -λ ({(-lambda):F4})")))
                : MorphologyCompute.SmoothWithConvergence(mesh, iterations, lockBoundary: false, (m, pos, _) => {
                    Point3d[] step1 = LaplacianUpdate(m, pos, useCotangent: false);
                    Point3d[] blended1 = new Point3d[pos.Length];
                    for (int i = 0; i < pos.Length; i++) {
                        blended1[i] = pos[i] + (lambda * (step1[i] - pos[i]));
                    }
                    Point3d[] step2 = LaplacianUpdate(m, blended1, useCotangent: false);
                    Point3d[] result = new Point3d[pos.Length];
                    for (int i = 0; i < pos.Length; i++) {
                        result[i] = blended1[i] + (mu * (step2[i] - blended1[i]));
                    }
                    return result;
                }, ctx).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iterations, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, int)>(input, parameters, context, (mesh, p, ctx) => {
            (double timeStep, int iters) = p;
            return MorphologyCompute.SmoothWithConvergence(mesh, iters, lockBoundary: false, (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, timeStep, _), ctx)
                .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, iters, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, bool)>(input, parameters, context, (mesh, p, ctx) => {
            (double distance, bool bothSides) = p;
            return MorphologyCompute.OffsetMesh(mesh, distance, bothSides, ctx).Bind(offset => ComputeOffsetMetrics(mesh, offset, distance, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (int, bool, double)>(input, parameters, context, (mesh, p, ctx) => {
            (int targetFaces, bool preserveBoundary, double accuracy) = p;
            return MorphologyCompute.ReduceMesh(mesh, targetFaces, preserveBoundary, accuracy, ctx).Bind(reduced => ComputeReductionMetrics(mesh, reduced, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh(object input, object parameters, IGeometryContext context) =>
        Execute<Mesh, (double, int, bool)>(input, parameters, context, (mesh, p, ctx) => {
            (double targetEdge, int maxIters, bool preserveFeats) = p;
            return MorphologyCompute.RemeshIsotropic(mesh, targetEdge, maxIters, preserveFeats, ctx).Bind(remeshed => ComputeRemeshMetrics(remeshed, targetEdge, maxIters, ctx));
        });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) =>
        [.. Enumerable.Range(0, positions.Length).Select(i => {
            int topologyIndex = mesh.TopologyVertices.TopologyVertexIndex(i);
            int[] neighbors = topologyIndex >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(topologyIndex) : [];
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
            int topologyIndex = mesh.TopologyVertices.TopologyVertexIndex(i);
            int[] neighbors = topologyIndex >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(topologyIndex) : [];
            return neighbors.Length is 0
                ? positions[i]
                : positions[i] + ((timeStep * (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis) *
                    ((neighbors.Aggregate(Point3d.Origin, (acc, neighborIdx) =>
                        acc + (positions[mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0]] - positions[i])) / neighbors.Length) - Point3d.Origin)) *
                    (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis));
        }),
        ];

    /// <summary>Computes mesh quality metrics. Assumes triangulated mesh - only processes first 3 vertices (.A, .B, .C) of each face. Quad faces (.D) are ignored.</summary>
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
                edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0),
            ]);
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

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
    /// <summary>Get executor function for algebraic request type and geometry type.</summary>
    [Pure]
    internal static Func<object, Morphology.Request, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>? GetExecutor(
        Morphology.Request request,
        Type inputType) => (request, inputType) switch {
        (Morphology.CageDeformationRequest, Type t) when t == typeof(Mesh) || t == typeof(Brep) => ExecuteCageDeform,
        (Morphology.CatmullClarkSubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivideCatmullClark,
        (Morphology.LoopSubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivideLoop,
        (Morphology.ButterflySubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivideButterfly,
        (Morphology.LaplacianSmoothing, Type t) when t == typeof(Mesh) => ExecuteSmoothLaplacian,
        (Morphology.TaubinSmoothing, Type t) when t == typeof(Mesh) => ExecuteSmoothTaubin,
        (Morphology.MeanCurvatureEvolution, Type t) when t == typeof(Mesh) => ExecuteEvolveMeanCurvature,
        (Morphology.MeshOffsetRequest, Type t) when t == typeof(Mesh) => ExecuteOffset,
        (Morphology.MeshReductionRequest, Type t) when t == typeof(Mesh) => ExecuteReduce,
        (Morphology.IsotropicRemeshRequest, Type t) when t == typeof(Mesh) => ExecuteRemesh,
        (Morphology.BrepToMeshRequest, Type t) when t == typeof(Brep) => ExecuteBrepToMesh,
        (Morphology.MeshRepairRequest, Type t) when t == typeof(Mesh) => ExecuteMeshRepair,
        (Morphology.MeshThickenRequest, Type t) when t == typeof(Mesh) => ExecuteMeshThicken,
        (Morphology.MeshUnwrapRequest, Type t) when t == typeof(Mesh) => ExecuteMeshUnwrap,
        (Morphology.MeshSeparationRequest, Type t) when t == typeof(Mesh) => ExecuteMeshSeparate,
        (Morphology.MeshWeldRequest, Type t) when t == typeof(Mesh) => ExecuteMeshWeld,
        _ => null,
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (GeometryBase geom, Morphology.CageDeformationRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: GeometryBase"))
            : MorphologyCompute.CageDeform(geom, r.Cage, r.OriginalControlPoints, r.DeformedControlPoints, context).Map(deformed =>
                (geom.GetBoundingBox(accurate: false), deformed.GetBoundingBox(accurate: false),
                 [.. r.OriginalControlPoints.Zip(r.DeformedControlPoints, static (o, d) => o.DistanceTo(d)),]) is var (origBounds, defBounds, disps)
                    ? (IReadOnlyList<Morphology.IMorphologyResult>)[
                        new Morphology.CageDeformResult(
                            deformed,
                            disps.Length > 0 ? disps.Max() : 0.0,
                            disps.Length > 0 ? disps.Average() : 0.0,
                            origBounds,
                            defBounds,
                            RhinoMath.IsValidDouble(origBounds.Volume) && origBounds.Volume > RhinoMath.ZeroTolerance
                                ? defBounds.Volume / origBounds.Volume
                                : 1.0),
                    ]
                    : []);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        ExecuteSubdivision(input, request, context, SubdivisionAlgorithm.CatmullClark);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        ExecuteSubdivision(input, request, context, SubdivisionAlgorithm.Loop);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        ExecuteSubdivision(input, request, context, SubdivisionAlgorithm.Butterfly);

    /// <summary>Internal enum for subdivision algorithm dispatch.</summary>
    internal enum SubdivisionAlgorithm : byte { CatmullClark, Loop, Butterfly, }

    /// <summary>Unified subdivision executor for CatmullClark, Loop, and Butterfly algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(
        object input,
        Morphology.Request request,
        IGeometryContext context,
        SubdivisionAlgorithm algorithm) =>
        (input, request) is not (Mesh mesh, Morphology.SubdivisionRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : algorithm is SubdivisionAlgorithm.Loop or SubdivisionAlgorithm.Butterfly && mesh.Faces.TriangleCount != mesh.Faces.Count
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: (algorithm == SubdivisionAlgorithm.Loop ? E.Geometry.Morphology.LoopRequiresTriangles : E.Geometry.Morphology.ButterflyRequiresTriangles)
                        .WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                : MorphologyCompute.SubdivideIterative(mesh, algorithm, r.Levels, context).Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.LaplacianSmoothing r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.SmoothWithConvergence(mesh, r.Iterations, r.LockBoundary, (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true), context)
                .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, r.Iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.TaubinSmoothing r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : r.Mu >= -r.Lambda
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({r.Mu:F4}) must be < -λ ({(-r.Lambda):F4})")))
                : MorphologyCompute.SmoothWithConvergence(mesh, r.Iterations, lockBoundary: false, (m, pos, _) =>
                    LaplacianUpdate(m, pos, useCotangent: false) is Point3d[] step1 &&
                    pos.Zip(step1, (p, s) => p + (r.Lambda * (s - p))).ToArray() is Point3d[] blended1 &&
                    LaplacianUpdate(m, blended1, useCotangent: false) is Point3d[] step2
                        ? [.. blended1.Zip(step2, (b, s) => b + (r.Mu * (s - b))),]
                        : pos, context).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, r.Iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeanCurvatureEvolution r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.SmoothWithConvergence(mesh, r.Iterations, lockBoundary: false, (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, r.TimeStep), context)
                .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, r.Iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshOffsetRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.OffsetMesh(mesh, r.Distance, r.BothSides, context).Bind(offset => ComputeOffsetMetrics(mesh, offset, r.Distance, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshReductionRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.ReduceMesh(mesh, r.TargetFaceCount, r.PreserveBoundary, r.Accuracy, context).Bind(reduced => ComputeReductionMetrics(mesh, reduced, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.IsotropicRemeshRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.RemeshIsotropic(mesh, r.TargetEdgeLength, r.MaxIterations, r.PreserveFeatures, context)
                .Bind(remeshData => ComputeRemeshMetrics(mesh, remeshData.Remeshed, r.TargetEdgeLength, remeshData.IterationsPerformed, context));

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
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep) =>
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

    /// <summary>Computes mesh quality metrics. Assumes triangulated mesh - only processes first 3 vertices (.A, .B, .C) of each face.</summary>
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
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.SubdivisionResult(
                subdivided,
                original.Faces.Count,
                subdivided.Faces.Count,
                edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0,
                aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                minAngles.Length > 0 ? minAngles.Min() : 0.0),
        ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) =>
        Enumerable.Range(0, Math.Min(original.Vertices.Count, smoothed.Vertices.Count))
            .Aggregate((SumSq: 0.0, MaxDisp: 0.0, Count: 0), (acc, i) => ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i]) is double dist
                ? (acc.SumSq + (dist * dist), Math.Max(acc.MaxDisp, dist), acc.Count + 1)
                : acc) is (double sumSq, double maxDisp, int count) && Math.Sqrt(sumSq / Math.Max(count, 1)) is double rms
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                new Morphology.SmoothingResult(
                    smoothed,
                    iterations,
                    rms,
                    maxDisp,
                    MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess ? 1.0 : 0.0,
                    rms < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier),
            ])
            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeOffsetMetrics(
        Mesh original,
        Mesh offset,
        double _,
        IGeometryContext context) =>
        ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.OffsetResult(
                offset,
                Math.Min(original.Vertices.Count, offset.Vertices.Count) switch {
                    0 => 0.0,
                    int sampleCount => Enumerable.Range(0, sampleCount).Average(i => ((Point3d)original.Vertices[i]).DistanceTo(offset.Vertices[i])),
                },
                !MorphologyCompute.ValidateMeshQuality(offset, context).IsSuccess,
                original.Vertices.Count,
                offset.Vertices.Count,
                original.Faces.Count,
                offset.Faces.Count),
        ]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeReductionMetrics(
        Mesh original,
        Mesh reduced,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] _) = ComputeMeshMetrics(reduced, context);
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.ReductionResult(
                reduced,
                original.Faces.Count,
                reduced.Faces.Count,
                original.Faces.Count > 0 ? (double)reduced.Faces.Count / original.Faces.Count : 1.0,
                MorphologyCompute.ValidateMeshQuality(reduced, context).IsSuccess ? 1.0 : 0.0,
                aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRemeshMetrics(
        Mesh original,
        Mesh remeshed,
        double targetEdge,
        int iterationsPerformed,
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
        double uniformity = mean > context.AbsoluteTolerance ? Math.Exp(-stdDev / Math.Max(mean, RhinoMath.ZeroTolerance)) : 0.0;
        double delta = Math.Abs(mean - targetEdge);
        double allowed = targetEdge * MorphologyConfig.RemeshConvergenceThreshold;
        bool lengthConverged = delta <= allowed;
        bool uniformityConverged = mean > RhinoMath.ZeroTolerance
            && (stdDev / mean) <= MorphologyConfig.RemeshUniformityWeight;
        bool converged = lengthConverged && uniformityConverged;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.RemeshResult(remeshed, targetEdge, mean, stdDev, uniformity, iterationsPerformed, converged, original.Faces.Count, remeshed.Faces.Count),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshRepair(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshRepairRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.RepairMesh(mesh, r.Flags.ToByte(), r.WeldTolerance, context).Bind(repaired => ComputeRepairMetrics(mesh, repaired, r.Flags, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshSeparate(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshSeparationRequest)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.SeparateMeshComponents(mesh, context).Bind(components => ComputeSeparationMetrics(components, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshWeld(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshWeldRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.WeldMeshVertices(mesh, r.Tolerance, r.RecalculateNormals, context).Bind(welded => ComputeWeldMetrics(mesh, welded, r.Tolerance, r.RecalculateNormals, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRepairMetrics(
        Mesh original,
        Mesh repaired,
        Morphology.RepairFlags operations,
        IGeometryContext context) =>
        ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.MeshRepairResult(
                repaired,
                original.Vertices.Count,
                repaired.Vertices.Count,
                original.Faces.Count,
                repaired.Faces.Count,
                operations,
                MorphologyCompute.ValidateMeshQuality(repaired, context).IsSuccess ? 1.0 : 0.0,
                original.DisjointMeshCount > 1,
                original.Normals.Count != original.Vertices.Count || original.Normals.Any(n => n.IsZero)),
        ]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSeparationMetrics(
        Mesh[] components,
        IGeometryContext _) =>
        ([.. components.Select(static m => m.Vertices.Count),], [.. components.Select(static m => m.Faces.Count),],
         [.. components.Select(static m => m.GetBoundingBox(accurate: false)),]) is var (vertCounts, faceCounts, bounds)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                new Morphology.MeshSeparationResult(
                    components,
                    components.Length,
                    vertCounts.Sum(),
                    faceCounts.Sum(),
                    vertCounts,
                    faceCounts,
                    bounds),
            ])
            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeWeldMetrics(
        Mesh original,
        Mesh welded,
        double tolerance,
        bool normalsRecalculated,
        IGeometryContext _) =>
        Enumerable.Range(0, Math.Min(original.Vertices.Count, welded.Vertices.Count))
            .Aggregate((Sum: 0.0, Max: 0.0, Count: 0), (acc, i) => ((Point3d)original.Vertices[i]).DistanceTo(welded.Vertices[i]) is double dist
                ? (acc.Sum + dist, Math.Max(acc.Max, dist), acc.Count + 1)
                : acc) is (double sum, double max, int count)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                new Morphology.MeshWeldResult(
                    welded,
                    original.Vertices.Count,
                    welded.Vertices.Count,
                    original.Vertices.Count - welded.Vertices.Count,
                    tolerance,
                    count > 0 ? sum / count : 0.0,
                    max,
                    normalsRecalculated),
            ])
            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteBrepToMesh(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Brep brep, Morphology.BrepToMeshRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Brep"))
            : MorphologyCompute.BrepToMesh(brep, r.Parameters, r.JoinMeshes, context).Bind(mesh => ComputeBrepToMeshMetrics(brep, mesh, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeBrepToMeshMetrics(
        Brep brep,
        Mesh mesh,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = ComputeMeshMetrics(mesh, context);
        int validFaceCount = Enumerable.Range(0, mesh.Faces.Count).Count(i => {
            MeshFace f = mesh.Faces[i];
            Point3d a = mesh.Vertices[f.A];
            Point3d b = mesh.Vertices[f.B];
            Point3d c = mesh.Vertices[f.C];
            Vector3d cross = Vector3d.CrossProduct(b - a, c - a);
            return cross.Length > RhinoMath.ZeroTolerance;
        });
        int degenerateCount = mesh.Faces.Count - validFaceCount;
        double mean = edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0;
        double variance = edgeLengths.Length > 0 ? edgeLengths.Average(e => Math.Pow(e - mean, 2.0)) : 0.0;
        double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
        double aspectRatioScore = aspectRatios.Length > 0 ? Math.Exp(-aspectRatios.Average() / 3.0) : 0.0;
        double angleScore = minAngles.Length > 0 ? (1.0 - (Math.Abs(minAngles.Average() - MorphologyConfig.IdealTriangleAngleRadians) / MorphologyConfig.IdealTriangleAngleRadians)) : 0.0;
        double degenerateScore = mesh.Faces.Count > 0 ? 1.0 - Math.Min(degenerateCount / (double)mesh.Faces.Count, 1.0) : 0.0;
        double qualityScore = (aspectRatioScore + angleScore + degenerateScore) / 3.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.BrepToMeshResult(
                mesh,
                brep.Faces.Count,
                mesh.Faces.Count,
                edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0,
                mean,
                stdDev,
                aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                aspectRatios.Length > 0 ? aspectRatios.Max() : 0.0,
                minAngles.Length > 0 ? minAngles.Min() : 0.0,
                minAngles.Length > 0 ? minAngles.Average() : 0.0,
                degenerateCount,
                qualityScore),
        ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshThicken(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshThickenRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.ThickenMesh(mesh, r.Thickness, r.Solidify, r.Direction, context).Bind(thickened => ComputeThickenMetrics(mesh, thickened, r.Thickness, r.Solidify, r.Direction, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeThickenMetrics(
        Mesh original,
        Mesh thickened,
        double thickness,
        bool solidify,
        Vector3d direction,
        IGeometryContext _) {
        Mesh? offsetForWallFaces = original.Offset(distance: thickness, solidify: solidify, direction: direction, wallFacesOut: out List<int>? wallFaces);
        int wallCount = wallFaces?.Count ?? 0;
        BoundingBox originalBounds = original.GetBoundingBox(accurate: false);
        BoundingBox thickenedBounds = thickened.GetBoundingBox(accurate: false);
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.MeshThickenResult(
                thickened,
                thickness,
                solidify && thickened.IsClosed,
                original.Vertices.Count,
                thickened.Vertices.Count,
                original.Faces.Count,
                thickened.Faces.Count,
                wallCount,
                originalBounds,
                thickenedBounds),
        ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshUnwrap(
        object input,
        Morphology.Request request,
        IGeometryContext context) =>
        (input, request) is not (Mesh mesh, Morphology.MeshUnwrapRequest r)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh"))
            : MorphologyCompute.UnwrapMesh(mesh, r.Method.ToByte(), context).Bind(unwrapped => ComputeUnwrapMetrics(mesh, unwrapped, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeUnwrapMetrics(
        Mesh original,
        Mesh unwrapped,
        IGeometryContext _) =>
        unwrapped.TextureCoordinates.Count > 0
            ? Enumerable.Range(0, unwrapped.TextureCoordinates.Count)
                .Aggregate(
                    (MinU: double.MaxValue, MaxU: double.MinValue, MinV: double.MaxValue, MaxV: double.MinValue),
                    (acc, i) => unwrapped.TextureCoordinates[i] is Point2f uv
                        ? (Math.Min(acc.MinU, uv.X), Math.Max(acc.MaxU, uv.X), Math.Min(acc.MinV, uv.Y), Math.Max(acc.MaxV, uv.Y))
                        : acc) is var (minU, maxU, minV, maxV) && (maxU - minU) * (maxV - minV) is double uvArea
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                        new Morphology.MeshUnwrapResult(
                            unwrapped, true, original.Faces.Count, unwrapped.TextureCoordinates.Count,
                            minU, maxU, minV, maxV,
                            uvArea > RhinoMath.ZeroTolerance ? Math.Min(uvArea, 1.0) : 0.0),
                    ])
                    : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount)
            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                new Morphology.MeshUnwrapResult(
                    unwrapped, false, original.Faces.Count, 0,
                    double.MaxValue, double.MinValue, double.MaxValue, double.MinValue, 0.0),
            ]);
}

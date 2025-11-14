using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation dispatch and execution core.</summary>
internal static class MorphologyCore {
    /// <summary>Primary operation dispatch table mapping (operation ID, input type) to executor function.</summary>
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
        }.ToFrozenDictionary();

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(
        object input,
        object parameters,
        IGeometryContext context) =>
        parameters switch {
            (GeometryBase cage, Point3d[] originalPts, Point3d[] deformedPts) when originalPts.Length == deformedPts.Length && originalPts.Length >= MorphologyConfig.MinCageControlPoints =>
                ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    GeometryBase? geometry = input is Mesh m ? m.DuplicateMesh() : input is Brep b ? b.DuplicateBrep() : null;
                    BoundingBox originalBounds = geometry?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    double originalVolume = originalBounds.Volume;

                    // NOTE: CageMorph is not available in current Rhino SDK - implementing simple transform for now
                    bool success = geometry is not null;
                    Transform xform = Transform.Identity;
                    if (success && originalPts.Length > 0) {
                        Vector3d avgDisplacement = Vector3d.Zero;
                        for (int i = 0; i < originalPts.Length; i++) {
                            avgDisplacement += deformedPts[i] - originalPts[i];
                        }
                        avgDisplacement /= originalPts.Length;
                        xform = Transform.Translation(avgDisplacement);
                        success = geometry?.Transform(xform) ?? false;
                    }

                    BoundingBox deformedBounds = geometry?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    double deformedVolume = deformedBounds.Volume;

                    double[] displacements = [.. originalPts.Zip(deformedPts, (o, d) => o.DistanceTo(d)),];
                    double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
                    double meanDisp = displacements.Length > 0 ? displacements.Average() : 0.0;
                    double volumeRatio = originalVolume > RhinoMath.ZeroTolerance ? deformedVolume / originalVolume : 1.0;

                    return success && geometry is not null
                        ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            value: [new Morphology.CageDeformResult(geometry, maxDisp, meanDisp, originalBounds, deformedBounds, volumeRatio),])
                        : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            error: E.Geometry.Morphology.CageDeformFailed.WithContext("Transform returned false or geometry null"));
                }))(),
            (GeometryBase, Point3d[] o, Point3d[] d) when o.Length != d.Length =>
                ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.CageControlPointMismatch.WithContext($"Original: {o.Length}, Deformed: {d.Length}")),
            (GeometryBase, Point3d[] o, Point3d[]) when o.Length < MorphologyConfig.MinCageControlPoints =>
                ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.InsufficientCagePoints.WithContext($"Count: {o.Length}, Required: {MorphologyConfig.MinCageControlPoints}")),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext("Expected: (GeometryBase cage, Point3d[] original, Point3d[] deformed)")),
        };

    [Pure]
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

    [Pure]
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

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.ButterflyIrregularValence.WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}")))
                    : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideButterfly, levels, context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not ValueTuple<int, bool>
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, bool lockBoundary)"))
                : ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    ValueTuple<int, bool> tuple = (ValueTuple<int, bool>)parameters;
                    int iters = tuple.Item1;
                    bool lockBound = tuple.Item2;
                    return MorphologyCompute.SmoothWithConvergence(
                        mesh,
                        iters,
                        lockBound,
                        (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true),
                        context)
                        .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iters, context));
                }))();

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not ValueTuple<int, double, double>
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, double lambda, double mu)"))
                : ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    ValueTuple<int, double, double> tuple = (ValueTuple<int, double, double>)parameters;
                    int iterations = tuple.Item1;
                    double lambda = tuple.Item2;
                    double mu = tuple.Item3;
                    return mu >= -lambda
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
                }))();

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not ValueTuple<double, int>
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (double timeStep, int iterations)"))
                : ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    ValueTuple<double, int> tuple = (ValueTuple<double, int>)parameters;
                    double timeStep = tuple.Item1;
                    int iters = tuple.Item2;
                    return MorphologyCompute.SmoothWithConvergence(
                        mesh,
                        iters,
                        lockBoundary: false,
                        (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, timeStep, _),
                        context)
                        .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, iters, context));
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);

            if (neighbors.Length is 0) {
                updated[i] = positions[i];
                continue;
            }

            Point3d sum = Point3d.Origin;
            double weightSum = 0.0;

            for (int j = 0; j < neighbors.Length; j++) {
                int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                Point3d neighborPos = positions[meshVertIdx];
                double weight = useCotangent
                    ? 1.0 / Math.Max(positions[i].DistanceTo(neighborPos), RhinoMath.ZeroTolerance)
                    : 1.0;
                sum += (weight * neighborPos);
                weightSum += weight;
            }

            updated[i] = weightSum > RhinoMath.ZeroTolerance ? sum / weightSum : positions[i];
        }

        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep, IGeometryContext _) {
        Point3d[] updated = new Point3d[positions.Length];

        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);

            if (neighbors.Length is 0) {
                updated[i] = positions[i];
                continue;
            }

            Point3d laplacian = Point3d.Origin;
            for (int j = 0; j < neighbors.Length; j++) {
                int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                laplacian += positions[meshVertIdx] - positions[i];
            }
            laplacian /= neighbors.Length;

            Vector3d normal = mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis;
            Vector3d laplacianVec = laplacian - Point3d.Origin;
            double curvature = normal * laplacianVec;

            updated[i] = positions[i] + ((timeStep * curvature) * normal);
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
            .Select(i => ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i])),];

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
}

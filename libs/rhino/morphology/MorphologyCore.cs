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
    /// <summary>Gets the request handler for a given morphology request and geometry type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<GeometryBase, Morphology.MorphologyRequest, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>? GetRequestHandler(
        Morphology.MorphologyRequest request,
        Type geometryType) =>
        (request, geometryType) switch {
            (Morphology.CageDeformation, Type t) when t == typeof(Mesh) || t == typeof(Brep) => ExecuteCageDeform,
            (Morphology.CatmullClarkSubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivision,
            (Morphology.LoopSubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivision,
            (Morphology.ButterflySubdivision, Type t) when t == typeof(Mesh) => ExecuteSubdivision,
            (Morphology.LaplacianSmoothing, Type t) when t == typeof(Mesh) => ExecuteSmoothLaplacian,
            (Morphology.TaubinSmoothing, Type t) when t == typeof(Mesh) => ExecuteSmoothTaubin,
            (Morphology.MeanCurvatureEvolution, Type t) when t == typeof(Mesh) => ExecuteMeanCurvature,
            (Morphology.MeshOffsetRequest, Type t) when t == typeof(Mesh) => ExecuteOffset,
            (Morphology.MeshReduction, Type t) when t == typeof(Mesh) => ExecuteReduce,
            (Morphology.IsotropicRemeshing, Type t) when t == typeof(Mesh) => ExecuteRemesh,
            (Morphology.BrepConversion, Type t) when t == typeof(Brep) => ExecuteBrepToMesh,
            (Morphology.MeshRepairRequest, Type t) when t == typeof(Mesh) => ExecuteMeshRepair,
            (Morphology.MeshThickening, Type t) when t == typeof(Mesh) => ExecuteMeshThicken,
            (Morphology.MeshUnwrapping, Type t) when t == typeof(Mesh) => ExecuteMeshUnwrap,
            (Morphology.MeshSeparation, Type t) when t == typeof(Mesh) => ExecuteMeshSeparate,
            (Morphology.MeshWelding, Type t) when t == typeof(Mesh) => ExecuteMeshWeld,
            _ => null,
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not (Mesh or Brep), _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh or Brep")),
            (_, not Morphology.CageDeformation cage) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: CageDeformation")),
            (GeometryBase geom, Morphology.CageDeformation c) => MorphologyCompute.CageDeform(geom, c.Cage, c.OriginalControlPoints, c.DeformedControlPoints, context)
                .Bind(deformed => {
                    BoundingBox originalBounds = geom.GetBoundingBox(accurate: false);
                    BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
                    double[] displacements = [.. c.OriginalControlPoints.Zip(c.DeformedControlPoints, static (o, d) => o.DistanceTo(d)),];
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
                }),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.SubdivisionRequest sub) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: SubdivisionRequest")),
            (Mesh mesh, Morphology.SubdivisionRequest s) when MorphologyConfig.RequiresTriangulatedMesh(s) && mesh.Faces.TriangleCount != mesh.Faces.Count =>
                ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: (s is Morphology.LoopSubdivision ? E.Geometry.Morphology.LoopRequiresTriangles : E.Geometry.Morphology.ButterflyRequiresTriangles)
                        .WithContext(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}"))),
            (Mesh mesh, Morphology.SubdivisionRequest s) => MorphologyCompute.SubdivideIterative(mesh, s, context)
                .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.LaplacianSmoothing lap) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: LaplacianSmoothing")),
            (Mesh mesh, Morphology.LaplacianSmoothing l) => MorphologyCompute.SmoothWithConvergence(
                    mesh, l.Iterations, l.LockBoundary, (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true), context)
                .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, l.Iterations, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.TaubinSmoothing tau) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: TaubinSmoothing")),
            (Mesh mesh, Morphology.TaubinSmoothing t) when t.Mu >= -t.Lambda => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({t.Mu:F4}) must be < -λ ({(-t.Lambda):F4})"))),
            (Mesh mesh, Morphology.TaubinSmoothing t) => MorphologyCompute.SmoothWithConvergence(
                    mesh, t.Iterations, lockBoundary: false, (m, pos, _) => {
                        Point3d[] step1 = LaplacianUpdate(m, pos, useCotangent: false);
                        Point3d[] blended1 = new Point3d[pos.Length];
                        for (int i = 0; i < pos.Length; i++) {
                            blended1[i] = pos[i] + (t.Lambda * (step1[i] - pos[i]));
                        }
                        Point3d[] step2 = LaplacianUpdate(m, blended1, useCotangent: false);
                        Point3d[] result = new Point3d[pos.Length];
                        for (int i = 0; i < pos.Length; i++) {
                            result[i] = blended1[i] + (t.Mu * (step2[i] - blended1[i]));
                        }
                        return result;
                    }, context)
                .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, t.Iterations, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeanCurvature(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeanCurvatureEvolution mce) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeanCurvatureEvolution")),
            (Mesh mesh, Morphology.MeanCurvatureEvolution m) => MorphologyCompute.SmoothWithConvergence(
                    mesh, m.Iterations, lockBoundary: false, (me, pos, _) => MeanCurvatureFlowUpdate(me, pos, m.TimeStep), context)
                .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, m.Iterations, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshOffsetRequest off) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshOffsetRequest")),
            (Mesh mesh, Morphology.MeshOffsetRequest o) => MorphologyCompute.OffsetMesh(mesh, o.Distance, o.BothSides, context)
                .Bind(offset => ComputeOffsetMetrics(mesh, offset, o.Distance, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshReduction red) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshReduction")),
            (Mesh mesh, Morphology.MeshReduction r) => MorphologyCompute.ReduceMesh(mesh, r.TargetFaceCount, r.PreserveBoundary, r.Accuracy, context)
                .Bind(reduced => ComputeReductionMetrics(mesh, reduced, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.IsotropicRemeshing rem) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: IsotropicRemeshing")),
            (Mesh mesh, Morphology.IsotropicRemeshing r) => MorphologyCompute.RemeshIsotropic(mesh, r.TargetEdgeLength, r.MaxIterations, r.PreserveFeatures, context)
                .Bind(remeshData => ComputeRemeshMetrics(mesh, remeshData.Remeshed, r.TargetEdgeLength, remeshData.IterationsPerformed, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshRepair(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshRepairRequest rep) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshRepairRequest")),
            (Mesh mesh, Morphology.MeshRepairRequest r) => MorphologyCompute.RepairMesh(mesh, r.Operations, r.WeldTolerance, context)
                .Bind(repaired => ComputeRepairMetrics(mesh, repaired, r.Operations, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshSeparate(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshSeparation) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshSeparation")),
            (Mesh mesh, Morphology.MeshSeparation) => MorphologyCompute.SeparateMeshComponents(mesh, context)
                .Bind(components => ComputeSeparationMetrics(components, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshWeld(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshWelding weld) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshWelding")),
            (Mesh mesh, Morphology.MeshWelding w) => MorphologyCompute.WeldMeshVertices(mesh, w.Tolerance, w.RecalculateNormals, context)
                .Bind(welded => ComputeWeldMetrics(mesh, welded, w.Tolerance, w.RecalculateNormals, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteBrepToMesh(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Brep, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Brep")),
            (_, not Morphology.BrepConversion conv) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: BrepConversion")),
            (Brep brep, Morphology.BrepConversion c) => MorphologyCompute.BrepToMesh(brep, c.Parameters, c.JoinMeshes, context)
                .Bind(mesh => ComputeBrepToMeshMetrics(brep, mesh, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshThicken(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshThickening thick) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshThickening")),
            (Mesh mesh, Morphology.MeshThickening t) => MorphologyCompute.ThickenMesh(mesh, t.Thickness, t.Solidify, t.Direction, context)
                .Bind(thickened => ComputeThickenMetrics(mesh, thickened, t.Thickness, t.Solidify, t.Direction, context)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshUnwrap(
        GeometryBase input,
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        (input, request) switch {
            (not Mesh, _) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected: Mesh")),
            (_, not Morphology.MeshUnwrapping unwrap) => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext($"Expected: MeshUnwrapping")),
            (Mesh mesh, Morphology.MeshUnwrapping u) => MorphologyCompute.UnwrapMesh(mesh, u.Method, context)
                .Bind(unwrapped => ComputeUnwrapMetrics(mesh, unwrapped, context)),
        };

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
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRepairMetrics(
        Mesh original,
        Mesh repaired,
        Morphology.MeshRepairOperations operations,
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
        components.Aggregate((VertCounts: new List<int>(), FaceCounts: new List<int>(), Bounds: new List<BoundingBox>()), (acc, m) => {
            acc.VertCounts.Add(m.Vertices.Count);
            acc.FaceCounts.Add(m.Faces.Count);
            acc.Bounds.Add(m.GetBoundingBox(accurate: false));
            return acc;
        }) is (List<int> vertCounts, List<int> faceCounts, List<BoundingBox> bounds)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                new Morphology.MeshSeparationResult(
                    components,
                    components.Length,
                    vertCounts.Sum(),
                    faceCounts.Sum(),
                    [.. vertCounts,],
                    [.. faceCounts,],
                    [.. bounds,]),
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
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeUnwrapMetrics(
        Mesh original,
        Mesh unwrapped,
        IGeometryContext _) {
        bool hasUVs = unwrapped.TextureCoordinates.Count > 0;
        double minU = double.MaxValue;
        double maxU = double.MinValue;
        double minV = double.MaxValue;
        double maxV = double.MinValue;
        double coverage = 0.0;
        if (hasUVs) {
            for (int i = 0; i < unwrapped.TextureCoordinates.Count; i++) {
                Point2f uv = unwrapped.TextureCoordinates[i];
                minU = Math.Min(minU, uv.X);
                maxU = Math.Max(maxU, uv.X);
                minV = Math.Min(minV, uv.Y);
                maxV = Math.Max(maxV, uv.Y);
            }
            double uvArea = (maxU - minU) * (maxV - minV);
            coverage = uvArea > RhinoMath.ZeroTolerance ? Math.Min(uvArea, 1.0) : 0.0;
        }
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.MeshUnwrapResult(
                unwrapped,
                hasUVs,
                original.Faces.Count,
                unwrapped.TextureCoordinates.Count,
                minU,
                maxU,
                minV,
                maxV,
                coverage),
        ]);
    }
}

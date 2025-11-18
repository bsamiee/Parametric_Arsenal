using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation orchestration over algebraic requests.</summary>
internal static class MorphologyCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Morphology.IMorphologyResult>> Apply(
        Morphology.MorphologyRequest request,
        IGeometryContext context) =>
        request switch {
            Morphology.MeshCageDeformationRequest meshCage => ExecuteCageDeformation(meshCage, context),
            Morphology.BrepCageDeformationRequest brepCage => ExecuteCageDeformation(brepCage, context),
            Morphology.SubdivisionRequest subdivision => ExecuteSubdivision(subdivision, context),
            Morphology.SmoothingRequest smoothing => ExecuteSmoothing(smoothing, context),
            Morphology.MeshOffsetRequest offset => ExecuteMeshOffset(offset, context),
            Morphology.MeshReductionRequest reduction => ExecuteMeshReduction(reduction, context),
            Morphology.MeshRemeshRequest remesh => ExecuteMeshRemesh(remesh, context),
            Morphology.BrepMeshingRequest brepMeshing => ExecuteBrepMeshing(brepMeshing, context),
            Morphology.MeshRepairRequest repair => ExecuteMeshRepair(repair, context),
            Morphology.MeshThickenRequest thicken => ExecuteMeshThicken(thicken, context),
            Morphology.MeshUnwrapRequest unwrap => ExecuteMeshUnwrap(unwrap, context),
            Morphology.MeshSeparationRequest separation => ExecuteMeshSeparate(separation, context),
            Morphology.MeshWeldRequest weld => ExecuteMeshWeld(weld, context),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext(
                string.Create(CultureInfo.InvariantCulture, $"Request: {request.GetType().Name}"))),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeformation(
        Morphology.MeshCageDeformationRequest request,
        IGeometryContext context) =>
        RunCageDeformation(request.Mesh, request, request.Cage, request.OriginalControlPoints, request.DeformedControlPoints, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeformation(
        Morphology.BrepCageDeformationRequest request,
        IGeometryContext context) =>
        RunCageDeformation(request.Brep, request, request.Cage, request.OriginalControlPoints, request.DeformedControlPoints, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> RunCageDeformation<TGeometry>(
        TGeometry geometry,
        Morphology.MorphologyRequest request,
        GeometryBase cage,
        IReadOnlyList<Point3d> originalPoints,
        IReadOnlyList<Point3d> deformedPoints,
        IGeometryContext context) where TGeometry : GeometryBase {
        Point3d[] originals = originalPoints.ToArray();
        Point3d[] deformed = deformedPoints.ToArray();
        return ExecuteWithConfig(
            geometry,
            request,
            context,
            geom => MorphologyCompute.CageDeform(
                geometry: geom,
                _: cage,
                originalControlPoints: originals,
                deformedControlPoints: deformed,
                __: context).Bind(result => BuildCageResult(geom, result, originals, deformed)));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision(
        Morphology.SubdivisionRequest request,
        IGeometryContext context) {
        bool requiresTriangles = request.Strategy is Morphology.LoopSubdivisionStrategy or Morphology.ButterflySubdivisionStrategy;
        return requiresTriangles && request.Mesh.Faces.TriangleCount != request.Mesh.Faces.Count
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: (request.Strategy is Morphology.LoopSubdivisionStrategy
                ? E.Geometry.Morphology.LoopRequiresTriangles
                : E.Geometry.Morphology.ButterflyRequiresTriangles)
                .WithContext(string.Create(CultureInfo.InvariantCulture, $"TriangleCount: {request.Mesh.Faces.TriangleCount}, FaceCount: {request.Mesh.Faces.Count}")))
            : ExecuteWithConfig(
                request.Mesh,
                request,
                context,
                mesh => MorphologyCompute.SubdivideIterative(
                    mesh: mesh,
                    levels: request.Levels,
                    generator: request.Strategy switch {
                        Morphology.CatmullClarkSubdivisionStrategy => static current => current.DuplicateMesh(),
                        Morphology.LoopSubdivisionStrategy => MorphologyCompute.SubdivideLoop,
                        Morphology.ButterflySubdivisionStrategy => MorphologyCompute.SubdivideButterfly,
                        _ => _ => null,
                    },
                    context: context).Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context)));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothing(
        Morphology.SmoothingRequest request,
        IGeometryContext context) =>
        request.Strategy switch {
            Morphology.LaplacianSmoothingStrategy laplacian => ExecuteWithConfig(
                request.Mesh,
                request,
                context,
                mesh => MorphologyCompute.SmoothWithConvergence(
                    mesh: mesh,
                    maxIterations: laplacian.Iterations,
                    lockBoundary: laplacian.LockBoundary,
                    updateFunc: (m, pos, _) => LaplacianUpdate(m, pos, useCotangent: true),
                    context: context).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, laplacian.Iterations, context))),
            Morphology.TaubinSmoothingStrategy taubin => taubin.Mu >= -taubin.Lambda
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(
                    string.Create(CultureInfo.InvariantCulture, $"μ ({taubin.Mu:F4}) must be < -λ ({(-taubin.Lambda):F4})")))
                : ExecuteWithConfig(
                    request.Mesh,
                    request,
                    context,
                    mesh => MorphologyCompute.SmoothWithConvergence(
                        mesh: mesh,
                        maxIterations: taubin.Iterations,
                        lockBoundary: false,
                        updateFunc: (m, pos, _) => {
                            Point3d[] step1 = LaplacianUpdate(m, pos, useCotangent: false);
                            Point3d[] blended = new Point3d[pos.Length];
                            for (int i = 0; i < pos.Length; i++) {
                                blended[i] = pos[i] + (taubin.Lambda * (step1[i] - pos[i]));
                            }
                            Point3d[] step2 = LaplacianUpdate(m, blended, useCotangent: false);
                            Point3d[] result = new Point3d[pos.Length];
                            for (int i = 0; i < pos.Length; i++) {
                                result[i] = blended[i] + (taubin.Mu * (step2[i] - blended[i]));
                            }
                            return result;
                        },
                        context: context).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, taubin.Iterations, context))),
            Morphology.MeanCurvatureFlowStrategy flow => ExecuteWithConfig(
                request.Mesh,
                request,
                context,
                mesh => MorphologyCompute.SmoothWithConvergence(
                    mesh: mesh,
                    maxIterations: flow.Iterations,
                    lockBoundary: false,
                    updateFunc: (m, pos, _) => MeanCurvatureFlowUpdate(m, pos, flow.TimeStep),
                    context: context).Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, flow.Iterations, context))),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext(
                string.Create(CultureInfo.InvariantCulture, $"SmoothingStrategy: {request.Strategy.GetType().Name}"))),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshOffset(
        Morphology.MeshOffsetRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.OffsetMesh(mesh, request.Distance, request.BothSides, context)
                .Bind(offset => ComputeOffsetMetrics(mesh, offset, request.Distance, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshReduction(
        Morphology.MeshReductionRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.ReduceMesh(mesh, request.TargetFaceCount, request.PreserveBoundary, request.Accuracy, context)
                .Bind(reduced => ComputeReductionMetrics(mesh, reduced, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshRemesh(
        Morphology.MeshRemeshRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.RemeshIsotropic(mesh, request.TargetEdgeLength, request.MaxIterations, request.PreserveFeatures, context)
                .Bind(remeshData => ComputeRemeshMetrics(mesh, remeshData.Remeshed, request.TargetEdgeLength, remeshData.IterationsPerformed, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteBrepMeshing(
        Morphology.BrepMeshingRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Brep,
            request,
            context,
            brep => MorphologyCompute.BrepToMesh(brep, request.Parameters, request.JoinMeshes, context)
                .Bind(mesh => ComputeBrepToMeshMetrics(brep, mesh, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshRepair(
        Morphology.MeshRepairRequest request,
        IGeometryContext context) {
        IReadOnlyList<Morphology.MeshRepairOperation> operations = request.Operations ?? Array.Empty<Morphology.MeshRepairOperation>();
        return ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.RepairMesh(mesh, operations, request.WeldTolerance, context)
                .Bind(repaired => ComputeRepairMetrics(mesh, repaired, operations, context)));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshSeparate(
        Morphology.MeshSeparationRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.SeparateMeshComponents(mesh, context)
                .Bind(components => ComputeSeparationMetrics(components, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshWeld(
        Morphology.MeshWeldRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.WeldMeshVertices(mesh, request.Tolerance, request.RecalculateNormals, context)
                .Bind(welded => ComputeWeldMetrics(mesh, welded, request.Tolerance, request.RecalculateNormals, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshThicken(
        Morphology.MeshThickenRequest request,
        IGeometryContext context) =>
        ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.ThickenMesh(mesh, request.Thickness, request.Solidify, request.Direction, context)
                .Bind(thickened => ComputeThickenMetrics(mesh, thickened, request.Thickness, request.Solidify, request.Direction, context)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshUnwrap(
        Morphology.MeshUnwrapRequest request,
        IGeometryContext context) {
        MeshUnwrapMethod method = request.Strategy switch {
            Morphology.AngleBasedUnwrapStrategy => MeshUnwrapMethod.AngleBased,
            Morphology.ConformalEnergyUnwrapStrategy => MeshUnwrapMethod.ConformalEnergyMinimization,
            _ => MeshUnwrapMethod.AngleBased,
        };
        return ExecuteWithConfig(
            request.Mesh,
            request,
            context,
            mesh => MorphologyCompute.UnwrapMesh(mesh, method, context)
                .Bind(unwrapped => ComputeUnwrapMetrics(mesh, unwrapped, context)));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteWithConfig<TGeometry>(
        TGeometry geometry,
        Morphology.MorphologyRequest request,
        IGeometryContext context,
        Func<TGeometry, Result<IReadOnlyList<Morphology.IMorphologyResult>>> operation) where TGeometry : GeometryBase =>
        MorphologyConfig.TryResolveMetadata(request, out (Arsenal.Core.Validation.V Validation, string Name) metadata)
            ? UnifiedOperation.Apply(
                geometry,
                operation,
                new OperationConfig<TGeometry, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = metadata.Validation,
                    OperationName = string.Create(CultureInfo.InvariantCulture, $"Morphology.{metadata.Name}"),
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext(
                string.Create(CultureInfo.InvariantCulture, $"Request: {request.GetType().Name}")));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> BuildCageResult(
        GeometryBase original,
        GeometryBase deformed,
        IReadOnlyList<Point3d> originalPoints,
        IReadOnlyList<Point3d> deformedPoints) {
        BoundingBox originalBounds = original.GetBoundingBox(accurate: false);
        BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
        double[] displacements = new double[Math.Min(originalPoints.Count, deformedPoints.Count)];
        for (int i = 0; i < displacements.Length; i++) {
            displacements[i] = originalPoints[i].DistanceTo(deformedPoints[i]);
        }
        double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
        double meanDisp = displacements.Length > 0 ? displacements.Average() : 0.0;
        double volumeRatio = RhinoMath.IsValidDouble(originalBounds.Volume) && originalBounds.Volume > RhinoMath.ZeroTolerance
            ? deformedBounds.Volume / originalBounds.Volume
            : 1.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.CageDeformResult(
                deformed,
                maxDisp,
                meanDisp,
                originalBounds,
                deformedBounds,
                volumeRatio),
        ]);
    }

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
        bool uniformityConverged = mean > RhinoMath.ZeroTolerance && (stdDev / mean) <= MorphologyConfig.RemeshUniformityWeight;
        bool converged = lengthConverged && uniformityConverged;

        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [
                new Morphology.RemeshResult(
                    remeshed,
                    targetEdge,
                    mean,
                    stdDev,
                    uniformity,
                    iterationsPerformed,
                    converged,
                    original.Faces.Count,
                    remeshed.Faces.Count),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRepairMetrics(
        Mesh original,
        Mesh repaired,
        IReadOnlyList<Morphology.MeshRepairOperation> operations,
        IGeometryContext context) =>
        ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
            new Morphology.MeshRepairResult(
                repaired,
                original.Vertices.Count,
                repaired.Vertices.Count,
                original.Faces.Count,
                repaired.Faces.Count,
                [.. operations,],
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

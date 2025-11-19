using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation dispatch and executor implementations.</summary>
internal static class MorphologyCore {
    /// <summary>Execute morphology operation with algebraic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Morphology.IMorphologyResult>> Execute<T>(
        T input,
        Morphology.Operation operation,
        IGeometryContext context) where T : GeometryBase =>
        operation switch {
            Morphology.CageDeformOperation cage => ExecuteCageDeform(input, cage, context),
            Morphology.CatmullClarkSubdivision catmull => ExecuteSubdivision(input, catmull, context),
            Morphology.LoopSubdivision loop => ExecuteSubdivision(input, loop, context),
            Morphology.ButterflySubdivision butterfly => ExecuteSubdivision(input, butterfly, context),
            Morphology.LaplacianSmoothing laplacian => ExecuteSmoothing(input, laplacian, context),
            Morphology.TaubinSmoothing taubin => ExecuteSmoothing(input, taubin, context),
            Morphology.MeanCurvatureFlowSmoothing mcf => ExecuteSmoothing(input, mcf, context),
            Morphology.MeshOffsetOperation offset => ExecuteOffset(input, offset, context),
            Morphology.MeshReductionOperation reduction => ExecuteReduction(input, reduction, context),
            Morphology.IsotropicRemeshOperation remesh => ExecuteRemesh(input, remesh, context),
            Morphology.BrepToMeshOperation brepToMesh => ExecuteBrepToMesh(input, brepToMesh, context),
            Morphology.MeshRepairStrategy repair => ExecuteRepair(input, repair, context),
            Morphology.MeshThickenOperation thicken => ExecuteThicken(input, thicken, context),
            Morphology.UnwrapStrategy unwrap => ExecuteUnwrap(input, unwrap, context),
            Morphology.MeshSeparateOperation => ExecuteSeparate(input, context),
            Morphology.MeshWeldOperation weld => ExecuteWeld(input, weld, context),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Operation: {operation.GetType().Name}, InputType: {typeof(T).Name}")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform<T>(
        T input,
        Morphology.CageDeformOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not (Mesh or Brep)
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"CageDeform requires Mesh or Brep, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(geom =>
                    MorphologyCompute.CageDeform(geom, operation.Cage, operation.OriginalControlPoints, operation.DeformedControlPoints, context)
                        .Bind(deformed => {
                            BoundingBox originalBounds = geom.GetBoundingBox(accurate: false);
                            BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
                            double[] displacements = [.. operation.OriginalControlPoints.Zip(operation.DeformedControlPoints, static (o, d) => o.DistanceTo(d)),];
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
                        })),
                config: new OperationConfig<T, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.Topology,
                    OperationName = "Morphology.CageDeform",
                    EnableDiagnostics = false,
                });

    /// <summary>Execute subdivision with CatmullClark, Loop, or Butterfly algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision<T>(
        T input,
        Morphology.SubdivisionStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Subdivision requires Mesh, got: {typeof(T).Name}"))
            : strategy switch {
                Morphology.LoopSubdivision when mesh.Faces.TriangleCount != mesh.Faces.Count =>
                    ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Geometry.Morphology.LoopRequiresTriangles.WithContext(
                            string.Create(CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}"))),
                Morphology.ButterflySubdivision when mesh.Faces.TriangleCount != mesh.Faces.Count =>
                    ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Geometry.Morphology.ButterflyRequiresTriangles.WithContext(
                            string.Create(CultureInfo.InvariantCulture, $"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}"))),
                _ => MorphologyConfig.Operations.TryGetValue(strategy.GetType(), out MorphologyConfig.MorphologyOperationMetadata? meta) && meta.AlgorithmCode is byte algorithmCode
                    ? UnifiedOperation.Apply(
                        input: mesh,
                        operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                            MorphologyCompute.SubdivideIterative(
                                m,
                                algorithmCode,
                                strategy.Levels,
                                context).Bind(subdivided => {
                                    (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = ComputeMeshMetrics(subdivided, context);
                                    return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                                        new Morphology.SubdivisionResult(
                                            subdivided,
                                            m.Faces.Count,
                                            subdivided.Faces.Count,
                                            edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                                            edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0,
                                            edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0,
                                            aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                                            minAngles.Length > 0 ? minAngles.Min() : 0.0),
                                    ]);
                                })),
                        config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                            Context = context,
                            ValidationMode = meta.ValidationMode,
                            OperationName = meta.OperationName,
                            EnableDiagnostics = false,
                        })
                    : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Unknown subdivision strategy: {strategy.GetType().Name}")),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothing<T>(
        T input,
        Morphology.SmoothingStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Smoothing requires Mesh, got: {typeof(T).Name}"))
            : strategy switch {
                Morphology.TaubinSmoothing taubin when taubin.Mu >= -taubin.Lambda =>
                    ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(
                            string.Create(CultureInfo.InvariantCulture, $"μ ({taubin.Mu:F4}) must be < -λ ({(-taubin.Lambda):F4})"))),
                Morphology.LaplacianSmoothing laplacian => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.SmoothWithConvergence(
                            m,
                            laplacian.Iterations,
                            laplacian.LockBoundary,
                            (mesh, pos, _) => LaplacianUpdate(mesh, pos, useCotangent: true),
                            context).Bind(smoothed => ComputeSmoothingMetrics(m, smoothed, laplacian.Iterations, context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = V.Standard | V.MeshSpecific,
                        OperationName = "Morphology.LaplacianSmoothing",
                        EnableDiagnostics = false,
                    }),
                Morphology.TaubinSmoothing taubin => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.SmoothWithConvergence(
                            m,
                            taubin.Iterations,
                            lockBoundary: false,
                            (mesh, pos, _) => {
                                Point3d[] step1 = LaplacianUpdate(mesh, pos, useCotangent: false);
                                Point3d[] blended1 = new Point3d[pos.Length];
                                for (int i = 0; i < pos.Length; i++) {
                                    blended1[i] = pos[i] + (taubin.Lambda * (step1[i] - pos[i]));
                                }
                                Point3d[] step2 = LaplacianUpdate(mesh, blended1, useCotangent: false);
                                Point3d[] result = new Point3d[pos.Length];
                                for (int i = 0; i < pos.Length; i++) {
                                    result[i] = blended1[i] + (taubin.Mu * (step2[i] - blended1[i]));
                                }
                                return result;
                            },
                            context).Bind(smoothed => ComputeSmoothingMetrics(m, smoothed, taubin.Iterations, context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = V.Standard | V.MeshSpecific,
                        OperationName = "Morphology.TaubinSmoothing",
                        EnableDiagnostics = false,
                    }),
                Morphology.MeanCurvatureFlowSmoothing mcf => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.SmoothWithConvergence(
                            m,
                            mcf.Iterations,
                            lockBoundary: false,
                            (mesh, pos, _) => [.. Enumerable.Range(0, pos.Length).Select(i => {
                                int topologyIndex = mesh.TopologyVertices.TopologyVertexIndex(i);
                                int[] neighbors = topologyIndex >= 0 ? mesh.TopologyVertices.ConnectedTopologyVertices(topologyIndex) : [];
                                return neighbors.Length is 0
                                    ? pos[i]
                                    : pos[i] + ((mcf.TimeStep * (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis) *
                                        ((neighbors.Aggregate(Point3d.Origin, (acc, neighborIdx) =>
                                            acc + (pos[mesh.TopologyVertices.MeshVertexIndices(neighborIdx)[0]] - pos[i])) / neighbors.Length) - Point3d.Origin)) *
                                        (mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis));
                            }),
                            ],
                            context).Bind(smoothed => ComputeSmoothingMetrics(m, smoothed, mcf.Iterations, context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = V.Standard | V.MeshSpecific,
                        OperationName = "Morphology.MeanCurvatureFlow",
                        EnableDiagnostics = false,
                    }),
                _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Unsupported smoothing strategy: {strategy.GetType().Name}")),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset<T>(
        T input,
        Morphology.MeshOffsetOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshOffset requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.OffsetMesh(m, operation.Distance, operation.BothSides, context)
                        .Bind(offset => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                            new Morphology.OffsetResult(
                                offset,
                                Math.Min(m.Vertices.Count, offset.Vertices.Count) switch {
                                    0 => 0.0,
                                    int sampleCount => Enumerable.Range(0, sampleCount).Average(i => ((Point3d)m.Vertices[i]).DistanceTo(offset.Vertices[i])),
                                },
                                !MorphologyCompute.ValidateMeshQuality(offset, context).IsSuccess,
                                m.Vertices.Count,
                                offset.Vertices.Count,
                                m.Faces.Count,
                                offset.Faces.Count),
                        ]))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.MeshSpecific,
                    OperationName = "Morphology.MeshOffset",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduction<T>(
        T input,
        Morphology.MeshReductionOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshReduction requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.ReduceMesh(m, operation.TargetFaceCount, operation.PreserveBoundary, operation.Accuracy, context)
                        .Bind(reduced => {
                            (double[] edgeLengths, double[] aspectRatios, double[] _) = ComputeMeshMetrics(reduced, context);
                            return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                                value: [new Morphology.ReductionResult(
                                    reduced,
                                    m.Faces.Count,
                                    reduced.Faces.Count,
                                    m.Faces.Count > 0 ? (double)reduced.Faces.Count / m.Faces.Count : 1.0,
                                    MorphologyCompute.ValidateMeshQuality(reduced, context).IsSuccess ? 1.0 : 0.0,
                                    aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                                    edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                                    edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0),
                                ]);
                        })),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.MeshSpecific | V.Topology,
                    OperationName = "Morphology.MeshReduce",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh<T>(
        T input,
        Morphology.IsotropicRemeshOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"IsotropicRemesh requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.RemeshIsotropic(m, operation.TargetEdgeLength, operation.MaxIterations, operation.PreserveFeatures, context)
                        .Bind(remeshData => {
                            int edgeCount = remeshData.Remeshed.TopologyEdges.Count;
                            (double sum, double sumSq) = (0.0, 0.0);
                            for (int i = 0; i < edgeCount; i++) {
                                double len = remeshData.Remeshed.TopologyEdges.EdgeLine(i).Length;
                                sum += len;
                                sumSq += len * len;
                            }
                            double mean = edgeCount > 0 ? sum / edgeCount : 0.0;
                            double variance = edgeCount > 0 ? (sumSq / edgeCount) - (mean * mean) : 0.0;
                            double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
                            double uniformity = mean > context.AbsoluteTolerance ? Math.Exp(-stdDev / Math.Max(mean, RhinoMath.ZeroTolerance)) : 0.0;
                            double delta = Math.Abs(mean - operation.TargetEdgeLength);
                            double allowed = operation.TargetEdgeLength * MorphologyConfig.RemeshConvergenceThreshold;
                            bool lengthConverged = delta <= allowed;
                            bool uniformityConverged = mean > RhinoMath.ZeroTolerance && (stdDev / mean) <= MorphologyConfig.RemeshUniformityWeight;
                            bool converged = lengthConverged && uniformityConverged;
                            return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                                value: [new Morphology.RemeshResult(remeshData.Remeshed, operation.TargetEdgeLength, mean, stdDev, uniformity, remeshData.IterationsPerformed, converged, m.Faces.Count, remeshData.Remeshed.Faces.Count),]);
                        })),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.MeshSpecific,
                    OperationName = "Morphology.MeshRemesh",
                    EnableDiagnostics = false,
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

    /// <summary>Compute edge lengths, aspect ratios, and minimum angles for triangulated mesh.</summary>
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
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRepair<T>(
        T input,
        Morphology.MeshRepairStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshRepair requires Mesh, got: {typeof(T).Name}"))
            : strategy switch {
                Morphology.CompositeRepair composite when MorphologyConfig.Operations.TryGetValue(typeof(Morphology.CompositeRepair), out MorphologyConfig.MorphologyOperationMetadata? compositeMeta) => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m => {
                        Morphology.MeshRepairStrategy? unknown = composite.Strategies.FirstOrDefault(s => !MorphologyConfig.Operations.TryGetValue(s.GetType(), out MorphologyConfig.MorphologyOperationMetadata? meta) || meta.RepairFlags is null);
                        return unknown is not null
                            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Unknown repair strategy in composite: {unknown.GetType().Name}"))
                            : ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                                MorphologyConfig.MorphologyOperationMetadata[] metadatas =
                                    [.. composite.Strategies.Select(s => MorphologyConfig.Operations[s.GetType()])];
                                byte flags = metadatas.Aggregate(
                                    MorphologyConfig.RepairNone,
                                    (acc, meta) => (byte)(acc | meta.RepairFlags!.Value));
                                return MorphologyCompute.RepairMesh(m, flags, composite.WeldTolerance, context)
                                    .Bind(repaired => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                                        new Morphology.MeshRepairResult(
                                            repaired,
                                            m.Vertices.Count,
                                            repaired.Vertices.Count,
                                            m.Faces.Count,
                                            repaired.Faces.Count,
                                            flags,
                                            MorphologyCompute.ValidateMeshQuality(repaired, context).IsSuccess ? 1.0 : 0.0,
                                            m.DisjointMeshCount > 1,
                                            m.Normals.Count != m.Vertices.Count || m.Normals.Any(n => n.IsZero)),
                                    ]));
                            }))();
                    }),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = compositeMeta.ValidationMode,
                        OperationName = compositeMeta.OperationName,
                        EnableDiagnostics = false,
                    }),
                _ when MorphologyConfig.Operations.TryGetValue(strategy.GetType(), out MorphologyConfig.MorphologyOperationMetadata? meta) && meta.RepairFlags is not null && meta.DefaultTolerance is not null => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.RepairMesh(m, meta.RepairFlags.Value, meta.DefaultTolerance.Value, context)
                            .Bind(repaired => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                                new Morphology.MeshRepairResult(
                                    repaired,
                                    m.Vertices.Count,
                                    repaired.Vertices.Count,
                                    m.Faces.Count,
                                    repaired.Faces.Count,
                                    meta.RepairFlags.Value,
                                    MorphologyCompute.ValidateMeshQuality(repaired, context).IsSuccess ? 1.0 : 0.0,
                                    m.DisjointMeshCount > 1,
                                    m.Normals.Count != m.Vertices.Count || m.Normals.Any(n => n.IsZero)),
                            ]))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                        EnableDiagnostics = false,
                    }),
                _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Unknown repair strategy: {strategy.GetType().Name}")),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSeparate<T>(
        T input,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshSeparate requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.SeparateMeshComponents(m, context)
                        .Bind(components => components.Aggregate((VertCounts: new List<int>(), FaceCounts: new List<int>(), Bounds: new List<BoundingBox>()), (acc, mesh) => {
                            acc.VertCounts.Add(mesh.Vertices.Count);
                            acc.FaceCounts.Add(mesh.Faces.Count);
                            acc.Bounds.Add(mesh.GetBoundingBox(accurate: false));
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
                            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.Topology,
                    OperationName = "Morphology.MeshSeparate",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteWeld<T>(
        T input,
        Morphology.MeshWeldOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshWeld requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.WeldMeshVertices(m, operation.Tolerance, operation.RecalculateNormals, context)
                        .Bind(welded => Enumerable.Range(0, Math.Min(m.Vertices.Count, welded.Vertices.Count))
                            .Aggregate((Sum: 0.0, Max: 0.0, Count: 0), (acc, i) => ((Point3d)m.Vertices[i]).DistanceTo(welded.Vertices[i]) is double dist
                                ? (acc.Sum + dist, Math.Max(acc.Max, dist), acc.Count + 1)
                                : acc) is (double sum, double max, int count)
                            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                                new Morphology.MeshWeldResult(
                                    welded,
                                    m.Vertices.Count,
                                    welded.Vertices.Count,
                                    m.Vertices.Count - welded.Vertices.Count,
                                    operation.Tolerance,
                                    count > 0 ? sum / count : 0.0,
                                    max,
                                    operation.RecalculateNormals),
                            ])
                            : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidCount))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.MeshSpecific,
                    OperationName = "Morphology.MeshWeld",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteBrepToMesh<T>(
        T input,
        Morphology.BrepToMeshOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Brep brep
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"BrepToMesh requires Brep, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: brep,
                operation: (Func<Brep, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(b =>
                    MorphologyCompute.BrepToMesh(b, operation.Parameters, operation.JoinMeshes, context)
                        .Bind(mesh => {
                            (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = ComputeMeshMetrics(mesh, context);
                            int validFaceCount = Enumerable.Range(0, mesh.Faces.Count).Count(i => {
                                MeshFace f = mesh.Faces[i];
                                Point3d a = mesh.Vertices[f.A];
                                Point3d bVertex = mesh.Vertices[f.B];
                                Point3d c = mesh.Vertices[f.C];
                                Vector3d cross = Vector3d.CrossProduct(bVertex - a, c - a);
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
                                    b.Faces.Count,
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
                        })),
                config: new OperationConfig<Brep, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.BoundingBox,
                    OperationName = "Morphology.BrepToMesh",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteThicken<T>(
        T input,
        Morphology.MeshThickenOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshThicken requires Mesh, got: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.ThickenMesh(m, operation.OffsetDistance, operation.Solidify, operation.Direction, context)
                        .Bind(thickened => {
                            Mesh? offsetForWallFaces = m.Offset(distance: operation.OffsetDistance, solidify: operation.Solidify, direction: operation.Direction, wallFacesOut: out List<int>? wallFaces);
                            int wallCount = wallFaces?.Count ?? 0;
                            BoundingBox originalBounds = m.GetBoundingBox(accurate: false);
                            BoundingBox thickenedBounds = thickened.GetBoundingBox(accurate: false);
                            return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(value: [
                                new Morphology.MeshThickenResult(
                                    thickened,
                                    operation.OffsetDistance,
                                    operation.Solidify && thickened.IsClosed,
                                    m.Vertices.Count,
                                    thickened.Vertices.Count,
                                    m.Faces.Count,
                                    thickened.Faces.Count,
                                    wallCount,
                                    originalBounds,
                                    thickenedBounds),
                            ]);
                        })),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = V.Standard | V.MeshSpecific,
                    OperationName = "Morphology.MeshThicken",
                    EnableDiagnostics = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteUnwrap<T>(
        T input,
        Morphology.UnwrapStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"MeshUnwrap requires Mesh, got: {typeof(T).Name}"))
            : MorphologyConfig.Operations.TryGetValue(strategy.GetType(), out MorphologyConfig.MorphologyOperationMetadata? meta) && meta.AlgorithmCode is not null
                ? UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.UnwrapMesh(m, meta.AlgorithmCode.Value, context)
                        .Bind(unwrapped => {
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
                                    m.Faces.Count,
                                    unwrapped.TextureCoordinates.Count,
                                    minU,
                                    maxU,
                                    minV,
                                    maxV,
                                    coverage),
                            ]);
                        })),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                        EnableDiagnostics = false,
                    })
                : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Unknown unwrap strategy: {strategy.GetType().Name}"));
}

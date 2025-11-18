using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable RCS1163 // Unused parameter

/// <summary>Morphology operation dispatch and execution engine with proper geometry validation.</summary>
internal static class MorphologyCore {
    /// <summary>
    /// Executes morphology request on geometry. CRITICAL: Validates GEOMETRY through UnifiedOperation, NOT the request.
    /// This ensures ValidationRules run on the actual Mesh/Brep, catching non-manifold, empty, bad normals, etc.
    /// </summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRequest<T>(
        T geometry,
        Morphology.MorphologyRequest request,
        IGeometryContext context) where T : GeometryBase =>
        request switch {
            Morphology.CageDeformRequest r => ExecuteCageDeform(geometry, r, context),
            Morphology.SubdivideCatmullClarkRequest r => ExecuteSubdivision(geometry, r, context),
            Morphology.SubdivideLoopRequest r => ExecuteSubdivision(geometry, r, context),
            Morphology.SubdiveButterflyRequest r => ExecuteSubdivision(geometry, r, context),
            Morphology.SmoothLaplacianRequest r => ExecuteSmoothLaplacian(geometry, r, context),
            Morphology.SmoothTaubinRequest r => ExecuteSmoothTaubin(geometry, r, context),
            Morphology.EvolveMeanCurvatureRequest r => ExecuteEvolveMeanCurvature(geometry, r, context),
            Morphology.OffsetRequest r => ExecuteOffset(geometry, r, context),
            Morphology.ReduceRequest r => ExecuteReduce(geometry, r, context),
            Morphology.RemeshRequest r => ExecuteRemesh(geometry, r, context),
            Morphology.BrepToMeshRequest r => ExecuteBrepToMesh(geometry, r, context),
            Morphology.MeshRepairRequest r => ExecuteMeshRepair(geometry, r, context),
            Morphology.MeshThickenRequest r => ExecuteMeshThicken(geometry, r, context),
            Morphology.MeshUnwrapRequest r => ExecuteMeshUnwrap(geometry, r, context),
            Morphology.MeshSeparateRequest r => ExecuteMeshSeparate(geometry, r, context),
            Morphology.MeshWeldRequest r => ExecuteMeshWeld(geometry, r, context),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Request: {request.GetType().Name}, Geometry: {typeof(T).Name}")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform<T>(
        T geometry,
        Morphology.CageDeformRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh and not Brep
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh or Brep, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(geom =>
                    MorphologyCompute.CageDeform(
                        geometry: geom,
                        cage: request.Cage,
                        originalControlPoints: request.OriginalControlPoints,
                        deformedControlPoints: request.DeformedControlPoints,
                        context: context)
                    .Bind(deformed => ComputeCageDeformMetrics(
                        original: geom,
                        deformed: deformed,
                        originalPoints: request.OriginalControlPoints,
                        deformedPoints: request.DeformedControlPoints,
                        context: context))),
                config: new OperationConfig<T, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.CageDeformRequest), typeof(T)),
                    OperationName = "Morphology.CageDeform",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivision<T>(
        T geometry,
        Morphology.MorphologyRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : request switch {
                Morphology.SubdivideCatmullClarkRequest r => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.SubdivideIterative(
                            mesh: m,
                            algorithm: SubdivisionAlgorithm.CatmullClark,
                            levels: r.Levels,
                            context: context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(
                            original: m,
                            subdivided: subdivided,
                            context: context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.SubdivideCatmullClarkRequest), typeof(Mesh)),
                        OperationName = "Morphology.SubdivideCatmullClark",
                    }),
                Morphology.SubdivideLoopRequest r => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        m.Faces.TriangleCount != m.Faces.Count
                            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                                error: E.Geometry.Morphology.LoopRequiresTriangles.WithContext(
                                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {m.Faces.TriangleCount}, FaceCount: {m.Faces.Count}")))
                            : MorphologyCompute.SubdivideIterative(
                                mesh: m,
                                algorithm: SubdivisionAlgorithm.Loop,
                                levels: r.Levels,
                                context: context)
                            .Bind(subdivided => ComputeSubdivisionMetrics(
                                original: m,
                                subdivided: subdivided,
                                context: context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.SubdivideLoopRequest), typeof(Mesh)),
                        OperationName = "Morphology.SubdivideLoop",
                    }),
                Morphology.SubdiveButterflyRequest r => UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        m.Faces.TriangleCount != m.Faces.Count
                            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                                error: E.Geometry.Morphology.ButterflyRequiresTriangles.WithContext(
                                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TriangleCount: {m.Faces.TriangleCount}, FaceCount: {m.Faces.Count}")))
                            : MorphologyCompute.SubdivideIterative(
                                mesh: m,
                                algorithm: SubdivisionAlgorithm.Butterfly,
                                levels: r.Levels,
                                context: context)
                            .Bind(subdivided => ComputeSubdivisionMetrics(
                                original: m,
                                subdivided: subdivided,
                                context: context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.SubdiveButterflyRequest), typeof(Mesh)),
                        OperationName = "Morphology.SubdivideButterfly",
                    }),
                _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.UnsupportedConfiguration),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian<T>(
        T geometry,
        Morphology.SmoothLaplacianRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.SmoothWithConvergence(
                        mesh: m,
                        maxIterations: request.Iterations,
                        lockBoundary: request.LockBoundary,
                        updateFunc: (mesh, positions, _) => MorphologyCompute.LaplacianUpdate(mesh: mesh, positions: positions, useCotangent: true),
                        context: context)
                    .Bind(smoothed => ComputeSmoothingMetrics(
                        original: m,
                        smoothed: smoothed,
                        iterations: request.Iterations,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.SmoothLaplacianRequest), typeof(Mesh)),
                    OperationName = "Morphology.SmoothLaplacian",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin<T>(
        T geometry,
        Morphology.SmoothTaubinRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : request.Mu >= -request.Lambda
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.TaubinParametersInvalid.WithContext(
                        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"μ ({request.Mu:F4}) must be < -λ ({(-request.Lambda):F4})")))
                : UnifiedOperation.Apply(
                    input: mesh,
                    operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                        MorphologyCompute.SmoothWithConvergence(
                            mesh: m,
                            maxIterations: request.Iterations,
                            lockBoundary: false,
                            updateFunc: (mesh, positions, _) => MorphologyCompute.TaubinUpdate(
                                mesh: mesh,
                                positions: positions,
                                lambda: request.Lambda,
                                mu: request.Mu),
                            context: context)
                        .Bind(smoothed => ComputeSmoothingMetrics(
                            original: m,
                            smoothed: smoothed,
                            iterations: request.Iterations,
                            context: context))),
                    config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.SmoothTaubinRequest), typeof(Mesh)),
                        OperationName = "Morphology.SmoothTaubin",
                    });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature<T>(
        T geometry,
        Morphology.EvolveMeanCurvatureRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.SmoothWithConvergence(
                        mesh: m,
                        maxIterations: request.Iterations,
                        lockBoundary: false,
                        updateFunc: (mesh, positions, _) => MorphologyCompute.MeanCurvatureFlowUpdate(
                            mesh: mesh,
                            positions: positions,
                            timeStep: request.TimeStep),
                        context: context)
                    .Bind(evolved => ComputeSmoothingMetrics(
                        original: m,
                        smoothed: evolved,
                        iterations: request.Iterations,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.EvolveMeanCurvatureRequest), typeof(Mesh)),
                    OperationName = "Morphology.EvolveMeanCurvature",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteOffset<T>(
        T geometry,
        Morphology.OffsetRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.OffsetMesh(
                        mesh: m,
                        distance: request.Distance,
                        bothSides: request.BothSides,
                        context: context)
                    .Bind(offset => ComputeOffsetMetrics(
                        original: m,
                        offset: offset,
                        requestedDistance: request.Distance,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.OffsetRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshOffset",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteReduce<T>(
        T geometry,
        Morphology.ReduceRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.ReduceMesh(
                        mesh: m,
                        targetFaceCount: request.TargetFaces,
                        preserveBoundary: request.PreserveBoundary,
                        accuracy: request.Accuracy,
                        context: context)
                    .Bind(reduced => ComputeReductionMetrics(
                        original: m,
                        reduced: reduced,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.ReduceRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshReduce",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteRemesh<T>(
        T geometry,
        Morphology.RemeshRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.RemeshIsotropic(
                        mesh: m,
                        targetEdgeLength: request.TargetEdgeLength,
                        maxIterations: request.MaxIterations,
                        preserveFeatures: request.PreserveFeatures,
                        context: context)
                    .Bind(remeshData => ComputeRemeshMetrics(
                        original: m,
                        remeshed: remeshData.Remeshed,
                        targetEdge: request.TargetEdgeLength,
                        iterationsPerformed: remeshData.IterationsPerformed,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.RemeshRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshRemesh",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteBrepToMesh<T>(
        T geometry,
        Morphology.BrepToMeshRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Brep brep
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Brep, got {typeof(T).Name}"))
            : request.Parameters is null
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.Morphology.MeshingParametersInvalid.WithContext("Parameters cannot be null"))
                : UnifiedOperation.Apply(
                    input: brep,
                    operation: (Func<Brep, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(b =>
                        MorphologyCompute.BrepToMesh(
                            brep: b,
                            meshParams: request.Parameters,
                            joinMeshes: request.JoinMeshes,
                            context: context)
                        .Bind(mesh => ComputeBrepToMeshMetrics(
                            brep: b,
                            mesh: mesh,
                            context: context))),
                    config: new OperationConfig<Brep, Morphology.IMorphologyResult> {
                        Context = context,
                        ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.BrepToMeshRequest), typeof(Brep)),
                        OperationName = "Morphology.BrepToMesh",
                    });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshRepair<T>(
        T geometry,
        Morphology.MeshRepairRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.RepairMesh(
                        mesh: m,
                        flags: request.Operations,
                        weldTolerance: request.WeldTolerance,
                        context: context)
                    .Bind(repaired => ComputeRepairMetrics(
                        original: m,
                        repaired: repaired,
                        operations: request.Operations,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.MeshRepairRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshRepair",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshThicken<T>(
        T geometry,
        Morphology.MeshThickenRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.ThickenMesh(
                        mesh: m,
                        thickness: request.Thickness,
                        solidify: request.Solidify,
                        direction: request.Direction,
                        context: context)
                    .Bind(thickened => ComputeThickenMetrics(
                        original: m,
                        thickened: thickened,
                        thickness: request.Thickness,
                        solidify: request.Solidify,
                        direction: request.Direction,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.MeshThickenRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshThicken",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshUnwrap<T>(
        T geometry,
        Morphology.MeshUnwrapRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.UnwrapMesh(
                        mesh: m,
                        unwrapMethod: request.UnwrapMethod,
                        context: context)
                    .Bind(unwrapped => ComputeUnwrapMetrics(
                        original: m,
                        unwrapped: unwrapped,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.MeshUnwrapRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshUnwrap",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshSeparate<T>(
        T geometry,
        Morphology.MeshSeparateRequest _request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.SeparateMeshComponents(
                        mesh: m,
                        context: context)
                    .Bind(components => ComputeSeparationMetrics(
                        components: components,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.MeshSeparateRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshSeparate",
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteMeshWeld<T>(
        T geometry,
        Morphology.MeshWeldRequest request,
        IGeometryContext context) where T : GeometryBase =>
        geometry is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InvalidGeometryType.WithContext($"Expected Mesh, got {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(m =>
                    MorphologyCompute.WeldMeshVertices(
                        mesh: m,
                        tolerance: request.Tolerance,
                        weldNormals: request.WeldNormals,
                        context: context)
                    .Bind(welded => ComputeWeldMetrics(
                        original: m,
                        welded: welded,
                        tolerance: request.Tolerance,
                        normalsRecalculated: request.WeldNormals,
                        context: context))),
                config: new OperationConfig<Mesh, Morphology.IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(typeof(Morphology.MeshWeldRequest), typeof(Mesh)),
                    OperationName = "Morphology.MeshWeld",
                });

    /// <summary>Metric computation methods - all exhaustively pattern match with no default cases.</summary>

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeCageDeformMetrics(
        GeometryBase original,
        GeometryBase deformed,
        Point3d[] originalPoints,
        Point3d[] deformedPoints,
        IGeometryContext context) {
        BoundingBox originalBounds = original.GetBoundingBox(accurate: false);
        BoundingBox deformedBounds = deformed.GetBoundingBox(accurate: false);
        double[] displacements = [.. originalPoints.Zip(deformedPoints, static (o, d) => o.DistanceTo(d)),];
        double volumeRatio = RhinoMath.IsValidDouble(originalBounds.Volume) && originalBounds.Volume > RhinoMath.ZeroTolerance
            ? deformedBounds.Volume / originalBounds.Volume
            : 1.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.CageDeformResult(
                Deformed: deformed,
                MaxDisplacement: displacements.Length > 0 ? displacements.Max() : 0.0,
                MeanDisplacement: displacements.Length > 0 ? displacements.Average() : 0.0,
                OriginalBounds: originalBounds,
                DeformedBounds: deformedBounds,
                VolumeRatio: volumeRatio),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSubdivisionMetrics(
        Mesh original,
        Mesh subdivided,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = MorphologyCompute.ComputeMeshMetrics(subdivided, context);
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.SubdivisionResult(
                Subdivided: subdivided,
                OriginalFaceCount: original.Faces.Count,
                SubdividedFaceCount: subdivided.Faces.Count,
                MinEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                MaxEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0,
                MeanEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0,
                MeanAspectRatio: aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                MinTriangleAngleRadians: minAngles.Length > 0 ? minAngles.Min() : 0.0),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) {
        int vertexCount = Math.Min(original.Vertices.Count, smoothed.Vertices.Count);
        (double sumSq, double maxDisp, int count) = (0.0, 0.0, 0);
        for (int i = 0; i < vertexCount; i++) {
            double dist = ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i]);
            sumSq += dist * dist;
            maxDisp = Math.Max(maxDisp, dist);
            count++;
        }
        double rms = Math.Sqrt(sumSq / Math.Max(count, 1));
        bool qualityValid = MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess;
        bool converged = rms < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.SmoothingResult(
                Smoothed: smoothed,
                IterationsPerformed: iterations,
                RMSDisplacement: rms,
                MaxVertexDisplacement: maxDisp,
                QualityScore: qualityValid ? 1.0 : 0.0,
                Converged: converged),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeOffsetMetrics(
        Mesh original,
        Mesh offset,
        double _requestedDistance,
        IGeometryContext context) {
        int sampleCount = Math.Min(original.Vertices.Count, offset.Vertices.Count);
        double actualDistance = sampleCount switch {
            0 => 0.0,
            int n => Enumerable.Range(0, n).Average(i => ((Point3d)original.Vertices[i]).DistanceTo(offset.Vertices[i])),
        };
        bool hasDegeneracies = !MorphologyCompute.ValidateMeshQuality(offset, context).IsSuccess;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.OffsetResult(
                Offset: offset,
                ActualDistance: actualDistance,
                HasDegeneracies: hasDegeneracies,
                OriginalVertexCount: original.Vertices.Count,
                OffsetVertexCount: offset.Vertices.Count,
                OriginalFaceCount: original.Faces.Count,
                OffsetFaceCount: offset.Faces.Count),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeReductionMetrics(
        Mesh original,
        Mesh reduced,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] _) = MorphologyCompute.ComputeMeshMetrics(reduced, context);
        double reductionRatio = original.Faces.Count > 0 ? (double)reduced.Faces.Count / original.Faces.Count : 1.0;
        bool qualityValid = MorphologyCompute.ValidateMeshQuality(reduced, context).IsSuccess;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.ReductionResult(
                Reduced: reduced,
                OriginalFaceCount: original.Faces.Count,
                ReducedFaceCount: reduced.Faces.Count,
                ReductionRatio: reductionRatio,
                QualityScore: qualityValid ? 1.0 : 0.0,
                MeanAspectRatio: aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                MinEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                MaxEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0),
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
        double uniformity = mean > context.AbsoluteTolerance
            ? Math.Exp(-stdDev / Math.Max(mean, RhinoMath.ZeroTolerance))
            : 0.0;
        double delta = Math.Abs(mean - targetEdge);
        double allowed = targetEdge * MorphologyConfig.RemeshConvergenceThreshold;
        bool lengthConverged = delta <= allowed;
        bool uniformityConverged = mean > RhinoMath.ZeroTolerance && (stdDev / mean) <= MorphologyConfig.RemeshUniformityWeight;
        bool converged = lengthConverged && uniformityConverged;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.RemeshResult(
                Remeshed: remeshed,
                TargetEdgeLength: targetEdge,
                MeanEdgeLength: mean,
                EdgeLengthStdDev: stdDev,
                UniformityScore: uniformity,
                IterationsPerformed: iterationsPerformed,
                Converged: converged,
                OriginalFaceCount: original.Faces.Count,
                RemeshedFaceCount: remeshed.Faces.Count),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeRepairMetrics(
        Mesh original,
        Mesh repaired,
        byte operations,
        IGeometryContext context) {
        bool hadHoles = original.DisjointMeshCount > 1;
        bool hadBadNormals = original.Normals.Count != original.Vertices.Count || original.Normals.Any(n => n.IsZero);
        bool qualityValid = MorphologyCompute.ValidateMeshQuality(repaired, context).IsSuccess;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.MeshRepairResult(
                Repaired: repaired,
                OriginalVertexCount: original.Vertices.Count,
                RepairedVertexCount: repaired.Vertices.Count,
                OriginalFaceCount: original.Faces.Count,
                RepairedFaceCount: repaired.Faces.Count,
                OperationsPerformed: operations,
                QualityScore: qualityValid ? 1.0 : 0.0,
                HadHoles: hadHoles,
                HadBadNormals: hadBadNormals),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSeparationMetrics(
        Mesh[] components,
        IGeometryContext _context) {
        int[] vertCounts = new int[components.Length];
        int[] faceCounts = new int[components.Length];
        BoundingBox[] bounds = new BoundingBox[components.Length];
        for (int i = 0; i < components.Length; i++) {
            vertCounts[i] = components[i].Vertices.Count;
            faceCounts[i] = components[i].Faces.Count;
            bounds[i] = components[i].GetBoundingBox(accurate: false);
        }
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.MeshSeparationResult(
                Components: components,
                ComponentCount: components.Length,
                TotalVertexCount: vertCounts.Sum(),
                TotalFaceCount: faceCounts.Sum(),
                VertexCountPerComponent: vertCounts,
                FaceCountPerComponent: faceCounts,
                BoundsPerComponent: bounds),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeWeldMetrics(
        Mesh original,
        Mesh welded,
        double tolerance,
        bool normalsRecalculated,
        IGeometryContext _context) {
        int vertexCount = Math.Min(original.Vertices.Count, welded.Vertices.Count);
        (double sum, double max, int count) = (0.0, 0.0, 0);
        for (int i = 0; i < vertexCount; i++) {
            double dist = ((Point3d)original.Vertices[i]).DistanceTo(welded.Vertices[i]);
            sum += dist;
            max = Math.Max(max, dist);
            count++;
        }
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.MeshWeldResult(
                Welded: welded,
                OriginalVertexCount: original.Vertices.Count,
                WeldedVertexCount: welded.Vertices.Count,
                VerticesRemoved: original.Vertices.Count - welded.Vertices.Count,
                WeldTolerance: tolerance,
                MeanVertexDisplacement: count > 0 ? sum / count : 0.0,
                MaxVertexDisplacement: max,
                NormalsRecalculated: normalsRecalculated),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeBrepToMeshMetrics(
        Brep brep,
        Mesh mesh,
        IGeometryContext context) {
        (double[] edgeLengths, double[] aspectRatios, double[] minAngles) = MorphologyCompute.ComputeMeshMetrics(mesh, context);
        int validFaceCount = 0;
        for (int i = 0; i < mesh.Faces.Count; i++) {
            MeshFace f = mesh.Faces[i];
            Point3d a = mesh.Vertices[f.A];
            Point3d b = mesh.Vertices[f.B];
            Point3d c = mesh.Vertices[f.C];
            Vector3d cross = Vector3d.CrossProduct(b - a, c - a);
            validFaceCount += cross.Length > RhinoMath.ZeroTolerance ? 1 : 0;
        }
        int degenerateCount = mesh.Faces.Count - validFaceCount;
        double mean = edgeLengths.Length > 0 ? edgeLengths.Average() : 0.0;
        double variance = edgeLengths.Length > 0 ? edgeLengths.Average(e => Math.Pow(e - mean, 2.0)) : 0.0;
        double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
        double aspectRatioScore = aspectRatios.Length > 0 ? Math.Exp(-aspectRatios.Average() / 3.0) : 0.0;
        double angleScore = minAngles.Length > 0
            ? (1.0 - (Math.Abs(minAngles.Average() - MorphologyConfig.IdealTriangleAngleRadians) / MorphologyConfig.IdealTriangleAngleRadians))
            : 0.0;
        double degenerateScore = mesh.Faces.Count > 0 ? 1.0 - Math.Min(degenerateCount / (double)mesh.Faces.Count, 1.0) : 0.0;
        double qualityScore = (aspectRatioScore + angleScore + degenerateScore) / 3.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.BrepToMeshResult(
                Mesh: mesh,
                BrepFaceCount: brep.Faces.Count,
                MeshFaceCount: mesh.Faces.Count,
                MinEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Min() : 0.0,
                MaxEdgeLength: edgeLengths.Length > 0 ? edgeLengths.Max() : 0.0,
                MeanEdgeLength: mean,
                EdgeLengthStdDev: stdDev,
                MeanAspectRatio: aspectRatios.Length > 0 ? aspectRatios.Average() : 0.0,
                MaxAspectRatio: aspectRatios.Length > 0 ? aspectRatios.Max() : 0.0,
                MinTriangleAngleRadians: minAngles.Length > 0 ? minAngles.Min() : 0.0,
                MeanTriangleAngleRadians: minAngles.Length > 0 ? minAngles.Average() : 0.0,
                DegenerateFaceCount: degenerateCount,
                QualityScore: qualityScore),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeThickenMetrics(
        Mesh original,
        Mesh thickened,
        double thickness,
        bool solidify,
        Vector3d direction,
        IGeometryContext _context) {
        Mesh? offsetForWallFaces = original.Offset(distance: thickness, solidify: solidify, direction: direction, wallFacesOut: out List<int>? wallFaces);
        int wallCount = wallFaces?.Count ?? 0;
        BoundingBox originalBounds = original.GetBoundingBox(accurate: false);
        BoundingBox thickenedBounds = thickened.GetBoundingBox(accurate: false);
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.MeshThickenResult(
                Thickened: thickened,
                OffsetDistance: thickness,
                IsSolid: solidify && thickened.IsClosed,
                OriginalVertexCount: original.Vertices.Count,
                ThickenedVertexCount: thickened.Vertices.Count,
                OriginalFaceCount: original.Faces.Count,
                ThickenedFaceCount: thickened.Faces.Count,
                WallFaceCount: wallCount,
                OriginalBounds: originalBounds,
                ThickenedBounds: thickenedBounds),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeUnwrapMetrics(
        Mesh original,
        Mesh unwrapped,
        IGeometryContext _context) {
        bool hasUVs = unwrapped.TextureCoordinates.Count > 0;
        double minU = double.MaxValue;
        double maxU = double.MinValue;
        double minV = double.MaxValue;
        double maxV = double.MinValue;
        for (int i = 0; hasUVs && i < unwrapped.TextureCoordinates.Count; i++) {
            Point2f uv = unwrapped.TextureCoordinates[i];
            minU = Math.Min(minU, uv.X);
            maxU = Math.Max(maxU, uv.X);
            minV = Math.Min(minV, uv.Y);
            maxV = Math.Max(maxV, uv.Y);
        }
        double coverage = hasUVs && (maxU - minU) * (maxV - minV) is double uvArea && uvArea > RhinoMath.ZeroTolerance
            ? Math.Min(uvArea, 1.0)
            : 0.0;
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [new Morphology.MeshUnwrapResult(
                Unwrapped: unwrapped,
                HasTextureCoordinates: hasUVs,
                OriginalFaceCount: original.Faces.Count,
                TextureCoordinateCount: unwrapped.TextureCoordinates.Count,
                MinU: minU,
                MaxU: maxU,
                MinV: minV,
                MaxV: maxV,
                UVCoverage: coverage),
            ]);
    }

    /// <summary>Subdivision algorithm enum for dispatch.</summary>
    internal enum SubdivisionAlgorithm {
        CatmullClark = 0,
        Loop = 1,
        Butterfly = 2,
    }
}

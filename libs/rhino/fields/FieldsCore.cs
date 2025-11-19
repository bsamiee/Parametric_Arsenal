using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with UnifiedOperation integration.</summary>
[Pure]
internal static class FieldsCore {
    private sealed record DistanceOperationMetadata(
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<IReadOnlyList<Fields.ScalarFieldSamples>>> DistanceExecutor,
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<IReadOnlyList<Fields.VectorFieldSamples>>> GradientExecutor,
        FieldsConfig.DistanceFieldMetadata Metadata);

    private sealed record FieldOperationDispatch(
        FieldsConfig.FieldOperationMetadata Metadata,
        Type ResultType,
        Func<Fields.FieldRequest, IGeometryContext, Result<object>> Executor);

    private static readonly FrozenDictionary<Type, DistanceOperationMetadata> DistanceDispatch =
        FieldsConfig.DistanceFields
            .ToDictionary(
                keySelector: static entry => entry.Key,
                elementSelector: static entry => new DistanceOperationMetadata(
                    DistanceExecutor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.ScalarFieldSamples>)[result,])
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.ScalarFieldSamples>)[result,])
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.ScalarFieldSamples>)[result,])
                                : static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.ScalarFieldSamples>)[result,]),
                    GradientExecutor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bufferSize, context) => ExecuteGradientField<Mesh>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.VectorFieldSamples>)[result,])
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bufferSize, context) => ExecuteGradientField<Brep>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.VectorFieldSamples>)[result,])
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bufferSize, context) => ExecuteGradientField<Curve>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.VectorFieldSamples>)[result,])
                                : static (geometry, sampling, bufferSize, context) => ExecuteGradientField<Surface>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<Fields.VectorFieldSamples>)[result,]),
                    Metadata: entry.Value))
            .ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, FieldOperationDispatch> OperationDispatch =
        new Dictionary<Type, FieldOperationDispatch> {
            [typeof(Fields.CurlFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.CurlFieldRequest)],
                ResultType: typeof(Fields.VectorFieldSamples),
                Executor: static (request, _) => {
                    Fields.CurlFieldRequest typed = (Fields.CurlFieldRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    Vector3d gridDelta = (bounds.Max - bounds.Min) / (typed.Sampling.Resolution - 1);
                    return bounds.IsValid
                        ? FieldsCompute.ComputeCurl(
                            vectorField: typed.Field.Vectors,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            gridDelta: gridDelta)
                            .Map(result => (object)new Fields.VectorFieldSamples(result.Grid, result.Curl))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.DivergenceFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.DivergenceFieldRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.DivergenceFieldRequest typed = (Fields.DivergenceFieldRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    Vector3d gridDelta = (bounds.Max - bounds.Min) / (typed.Sampling.Resolution - 1);
                    return bounds.IsValid
                        ? FieldsCompute.ComputeDivergence(
                            vectorField: typed.Field.Vectors,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            gridDelta: gridDelta)
                            .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.Divergence))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.LaplacianFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.LaplacianFieldRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.LaplacianFieldRequest typed = (Fields.LaplacianFieldRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    Vector3d gridDelta = (bounds.Max - bounds.Min) / (typed.Sampling.Resolution - 1);
                    return bounds.IsValid
                        ? FieldsCompute.ComputeLaplacian(
                            scalarField: typed.Field.Values,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            gridDelta: gridDelta)
                            .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.Laplacian))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.VectorPotentialFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.VectorPotentialFieldRequest)],
                ResultType: typeof(Fields.VectorFieldSamples),
                Executor: static (request, _) => {
                    Fields.VectorPotentialFieldRequest typed = (Fields.VectorPotentialFieldRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    Vector3d gridDelta = (bounds.Max - bounds.Min) / (typed.Sampling.Resolution - 1);
                    return bounds.IsValid
                        ? FieldsCompute.ComputeVectorPotential(
                            vectorField: typed.Field.Vectors,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            gridDelta: gridDelta)
                            .Map(result => (object)new Fields.VectorFieldSamples(result.Grid, result.Potential))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.ScalarInterpolationRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.ScalarInterpolationRequest)],
                ResultType: typeof(double),
                Executor: static (request, _) => {
                    Fields.ScalarInterpolationRequest typed = (Fields.ScalarInterpolationRequest)request;
                    bool degenerateBounds = RhinoMath.EpsilonEquals(typed.Bounds.Max.X, typed.Bounds.Min.X, RhinoMath.SqrtEpsilon)
                        || RhinoMath.EpsilonEquals(typed.Bounds.Max.Y, typed.Bounds.Min.Y, RhinoMath.SqrtEpsilon)
                        || RhinoMath.EpsilonEquals(typed.Bounds.Max.Z, typed.Bounds.Min.Z, RhinoMath.SqrtEpsilon);
                    Fields.InterpolationMode mode = degenerateBounds ? new Fields.NearestInterpolationMode() : typed.Mode;
                    return typed.Field.Values.Length == typed.Field.Grid.Length
                        ? FieldsCompute.InterpolateScalar(
                            query: typed.Query,
                            scalarField: typed.Field.Values,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            bounds: typed.Bounds,
                            mode: mode)
                            .Map(result => (object)result)
                        : ResultFactory.Create<object>(
                            error: E.Geometry.InvalidFieldInterpolation.WithContext("Scalar field length must match grid points"));
                }),
            [typeof(Fields.VectorInterpolationRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.VectorInterpolationRequest)],
                ResultType: typeof(Vector3d),
                Executor: static (request, _) => {
                    Fields.VectorInterpolationRequest typed = (Fields.VectorInterpolationRequest)request;
                    bool degenerateBounds = RhinoMath.EpsilonEquals(typed.Bounds.Max.X, typed.Bounds.Min.X, RhinoMath.SqrtEpsilon)
                        || RhinoMath.EpsilonEquals(typed.Bounds.Max.Y, typed.Bounds.Min.Y, RhinoMath.SqrtEpsilon)
                        || RhinoMath.EpsilonEquals(typed.Bounds.Max.Z, typed.Bounds.Min.Z, RhinoMath.SqrtEpsilon);
                    Fields.InterpolationMode mode = degenerateBounds ? new Fields.NearestInterpolationMode() : typed.Mode;
                    return typed.Field.Vectors.Length == typed.Field.Grid.Length
                        ? FieldsCompute.InterpolateVector(
                            query: typed.Query,
                            vectorField: typed.Field.Vectors,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            bounds: typed.Bounds,
                            mode: mode)
                            .Map(result => (object)result)
                        : ResultFactory.Create<object>(
                            error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points"));
                }),
            [typeof(Fields.StreamlineRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.StreamlineRequest)],
                ResultType: typeof(Curve[]),
                Executor: static (request, context) => {
                    Fields.StreamlineRequest typed = (Fields.StreamlineRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    return bounds.IsValid
                        ? typed.Field.Vectors.Length == typed.Field.Grid.Length
                            ? typed.Seeds.Length > 0
                                ? FieldsCompute.IntegrateStreamlines(
                                    vectorField: typed.Field.Vectors,
                                    gridPoints: typed.Field.Grid,
                                    seeds: typed.Seeds,
                                    stepSize: typed.Sampling.StepSize,
                                    scheme: typed.Scheme,
                                    resolution: typed.Sampling.Resolution,
                                    bounds: bounds,
                                    context: context)
                                    .Map(result => (object)result)
                                : ResultFactory.Create<object>(error: E.Geometry.InvalidStreamlineSeeds)
                            : ResultFactory.Create<object>(
                                error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points"))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.IsosurfaceRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.IsosurfaceRequest)],
                ResultType: typeof(Mesh[]),
                Executor: static (request, _) => {
                    Fields.IsosurfaceRequest typed = (Fields.IsosurfaceRequest)request;
                    return typed.Field.Values.Length != typed.Field.Grid.Length
                        ? ResultFactory.Create<object>(error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points"))
                        : typed.Isovalues.Length == 0
                            ? ResultFactory.Create<object>(error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required"))
                            : typed.Isovalues.Any(value => !RhinoMath.IsValidDouble(value))
                                ? ResultFactory.Create<object>(error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles"))
                                : FieldsCompute.ExtractIsosurfaces(
                                    scalarField: typed.Field.Values,
                                    gridPoints: typed.Field.Grid,
                                    resolution: typed.Sampling.Resolution,
                                    isovalues: typed.Isovalues)
                                    .Map(result => (object)result);
                }),
            [typeof(Fields.HessianFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.HessianFieldRequest)],
                ResultType: typeof(Fields.HessianFieldSamples),
                Executor: static (request, _) => {
                    Fields.HessianFieldRequest typed = (Fields.HessianFieldRequest)request;
                    BoundingBox bounds = typed.Bounds;
                    Vector3d gridDelta = (bounds.Max - bounds.Min) / (typed.Sampling.Resolution - 1);
                    return bounds.IsValid
                        ? FieldsCompute.ComputeHessian(
                            scalarField: typed.Field.Values,
                            grid: typed.Field.Grid,
                            resolution: typed.Sampling.Resolution,
                            gridDelta: gridDelta)
                            .Map(result => (object)new Fields.HessianFieldSamples(result.Grid, result.Hessian))
                        : ResultFactory.Create<object>(error: E.Geometry.InvalidFieldBounds);
                }),
            [typeof(Fields.DirectionalDerivativeRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.DirectionalDerivativeRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.DirectionalDerivativeRequest typed = (Fields.DirectionalDerivativeRequest)request;
                    return FieldsCompute.ComputeDirectionalDerivative(
                        gradientField: typed.Field.Vectors,
                        grid: typed.Field.Grid,
                        direction: typed.Direction)
                        .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.DirectionalDerivatives));
                }),
            [typeof(Fields.FieldMagnitudeRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.FieldMagnitudeRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.FieldMagnitudeRequest typed = (Fields.FieldMagnitudeRequest)request;
                    return FieldsCompute.ComputeFieldMagnitude(
                        vectorField: typed.Field.Vectors,
                        grid: typed.Field.Grid)
                        .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.Magnitudes));
                }),
            [typeof(Fields.NormalizeFieldRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.NormalizeFieldRequest)],
                ResultType: typeof(Fields.VectorFieldSamples),
                Executor: static (request, _) => {
                    Fields.NormalizeFieldRequest typed = (Fields.NormalizeFieldRequest)request;
                    return FieldsCompute.NormalizeVectorField(
                        vectorField: typed.Field.Vectors,
                        grid: typed.Field.Grid)
                        .Map(result => (object)new Fields.VectorFieldSamples(result.Grid, result.Normalized));
                }),
            [typeof(Fields.ScalarVectorProductRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.ScalarVectorProductRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.ScalarVectorProductRequest typed = (Fields.ScalarVectorProductRequest)request;
                    return FieldsCompute.ScalarVectorProduct(
                        scalarField: typed.Scalars.Values,
                        vectorField: typed.Vectors.Vectors,
                        grid: typed.Scalars.Grid,
                        component: typed.Component)
                        .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.Product));
                }),
            [typeof(Fields.VectorDotProductRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.VectorDotProductRequest)],
                ResultType: typeof(Fields.ScalarFieldSamples),
                Executor: static (request, _) => {
                    Fields.VectorDotProductRequest typed = (Fields.VectorDotProductRequest)request;
                    return FieldsCompute.VectorDotProduct(
                        vectorField1: typed.First.Vectors,
                        vectorField2: typed.Second.Vectors,
                        grid: typed.First.Grid)
                        .Map(result => (object)new Fields.ScalarFieldSamples(result.Grid, result.DotProduct));
                }),
            [typeof(Fields.CriticalPointsRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.CriticalPointsRequest)],
                ResultType: typeof(Fields.CriticalPoint[]),
                Executor: static (request, _) => {
                    Fields.CriticalPointsRequest typed = (Fields.CriticalPointsRequest)request;
                    int gridLength = typed.ScalarField.Grid.Length;
                    bool scalarAligned = typed.ScalarField.Values.Length == gridLength;
                    bool gradientSamplesAligned = typed.GradientField.Vectors.Length == gridLength;
                    bool gradientGridAligned = typed.GradientField.Grid.Length == gridLength;
                    bool hessianGridAligned = typed.Hessian.Grid.Length == gridLength;
                    bool hessianSamplesAligned = typed.Hessian.Values[0, 0].Length == gridLength;
                    bool aligned = scalarAligned && gradientSamplesAligned && gradientGridAligned && hessianGridAligned && hessianSamplesAligned;
                    return aligned
                        ? FieldsCompute.DetectCriticalPoints(
                            scalarField: typed.ScalarField.Values,
                            gradientField: typed.GradientField.Vectors,
                            hessian: typed.Hessian.Values,
                            grid: typed.ScalarField.Grid,
                            resolution: typed.Sampling.Resolution)
                            .Map(result => (object)result)
                        : ResultFactory.Create<object>(
                            error: E.Geometry.InvalidCriticalPointDetection.WithContext("Scalar, gradient, and Hessian fields must share identical grid alignment"));
                }),
            [typeof(Fields.FieldStatisticsRequest)] = new(
                Metadata: FieldsConfig.Operations[typeof(Fields.FieldStatisticsRequest)],
                ResultType: typeof(Fields.FieldStatistics),
                Executor: static (request, _) => {
                    Fields.FieldStatisticsRequest typed = (Fields.FieldStatisticsRequest)request;
                    return FieldsCompute.ComputeFieldStatistics(
                        scalarField: typed.Field.Values,
                        grid: typed.Field.Grid)
                        .Map(result => (object)result);
                }),
        }.ToFrozenDictionary();

    [Pure]
    internal static Result<Fields.ScalarFieldSamples> DistanceField(
        Fields.DistanceFieldRequest request,
        IGeometryContext context) =>
        request.Geometry is null
            ? ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(request.Geometry.GetType(), out DistanceOperationMetadata? metadata)
                ? ResultFactory.Create<Fields.ScalarFieldSamples>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {request.Geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: request.Geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Fields.ScalarFieldSamples>>>)(item => metadata.DistanceExecutor(item, request.Sampling, metadata.Metadata.BufferSize, context)),
                    config: new OperationConfig<GeometryBase, Fields.ScalarFieldSamples> {
                        Context = context,
                        ValidationMode = metadata.Metadata.ValidationMode,
                        OperationName = metadata.Metadata.DistanceOperationName,
                        EnableDiagnostics = false,
                    }).Map(results => results[0]);

    [Pure]
    internal static Result<Fields.VectorFieldSamples> GradientField(
        Fields.GradientFieldRequest request,
        IGeometryContext context) =>
        request.Geometry is null
            ? ResultFactory.Create<Fields.VectorFieldSamples>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(request.Geometry.GetType(), out DistanceOperationMetadata? metadata)
                ? ResultFactory.Create<Fields.VectorFieldSamples>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Gradient field not supported for {request.Geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: request.Geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Fields.VectorFieldSamples>>>)(item => metadata.GradientExecutor(item, request.Sampling, metadata.Metadata.BufferSize, context)),
                    config: new OperationConfig<GeometryBase, Fields.VectorFieldSamples> {
                        Context = context,
                        ValidationMode = metadata.Metadata.ValidationMode,
                        OperationName = metadata.Metadata.GradientOperationName,
                        EnableDiagnostics = false,
                    }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.VectorFieldSamples> CurlField(
        Fields.CurlFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.VectorFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> DivergenceField(
        Fields.DivergenceFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> LaplacianField(
        Fields.LaplacianFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.VectorFieldSamples> VectorPotentialField(
        Fields.VectorPotentialFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.VectorFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<double> InterpolateScalar(
        Fields.ScalarInterpolationRequest request,
        IGeometryContext context) =>
        ApplyOperation<double>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> InterpolateVector(
        Fields.VectorInterpolationRequest request,
        IGeometryContext context) =>
        ApplyOperation<Vector3d>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> Streamlines(
        Fields.StreamlineRequest request,
        IGeometryContext context) =>
        ApplyOperation<Curve[]>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh[]> Isosurfaces(
        Fields.IsosurfaceRequest request,
        IGeometryContext context) =>
        ApplyOperation<Mesh[]>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.HessianFieldSamples> HessianField(
        Fields.HessianFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.HessianFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> DirectionalDerivativeField(
        Fields.DirectionalDerivativeRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> FieldMagnitude(
        Fields.FieldMagnitudeRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.VectorFieldSamples> NormalizeField(
        Fields.NormalizeFieldRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.VectorFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> ScalarVectorProduct(
        Fields.ScalarVectorProductRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.ScalarFieldSamples> VectorDotProduct(
        Fields.VectorDotProductRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.ScalarFieldSamples>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.CriticalPoint[]> CriticalPoints(
        Fields.CriticalPointsRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.CriticalPoint[]>(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.FieldStatistics> ComputeStatistics(
        Fields.FieldStatisticsRequest request,
        IGeometryContext context) =>
        ApplyOperation<Fields.FieldStatistics>(request: request, context: context);

    [Pure]
    private static Result<TResult> ApplyOperation<TResult>(
        Fields.FieldRequest request,
        IGeometryContext context) =>
        OperationDispatch.TryGetValue(request.GetType(), out FieldOperationDispatch? dispatch)
            ? dispatch.ResultType != typeof(TResult)
                ? ResultFactory.Create<TResult>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Operation {request.GetType().Name} does not produce {typeof(TResult).Name}"))
                : UnifiedOperation.Apply(
                    input: request,
                    operation: (Func<Fields.FieldRequest, Result<IReadOnlyList<object>>>)(item => dispatch.Executor(item, context).Map(result => (IReadOnlyList<object>)[result,])),
                    config: new OperationConfig<Fields.FieldRequest, object> {
                        Context = context,
                        ValidationMode = dispatch.Metadata.ValidationMode,
                        OperationName = dispatch.Metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(results => (TResult)results[0])
            : ResultFactory.Create<TResult>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"No fields operation configured for {request.GetType().Name}"));

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
        BoundingBox bounds = sampling.Bounds ?? typed.GetBoundingBox(accurate: true);
        if (!bounds.IsValid) {
            return ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.InvalidFieldBounds);
        }
        int resolution = sampling.Resolution;
        int totalSamples = resolution * resolution * resolution;
        int actualBufferSize = Math.Max(totalSamples, bufferSize);
        Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(actualBufferSize);
        double[] distances = ArrayPool<double>.Shared.Rent(actualBufferSize);
        try {
            Vector3d delta = (bounds.Max - bounds.Min) / (resolution - 1);
            int gridIndex = 0;
            for (int i = 0; i < resolution; i++) {
                for (int j = 0; j < resolution; j++) {
                    for (int k = 0; k < resolution; k++) {
                        grid[gridIndex++] = new(bounds.Min.X + (i * delta.X), bounds.Min.Y + (j * delta.Y), bounds.Min.Z + (k * delta.Z));
                    }
                }
            }
            for (int i = 0; i < totalSamples; i++) {
                Point3d closest = typed switch {
                    Mesh m => m.ClosestPoint(grid[i]),
                    Brep b => b.ClosestPoint(grid[i]),
                    Curve c => c.ClosestPoint(grid[i], out double t) ? c.PointAt(t) : grid[i],
                    Surface s => s.ClosestPoint(grid[i], out double u, out double v) ? s.PointAt(u, v) : grid[i],
                    _ => grid[i],
                };
                double unsignedDist = grid[i].DistanceTo(closest);
                bool inside = typed switch {
                    Brep brep => brep.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance * FieldsConfig.InsideOutsideToleranceMultiplier, strictlyIn: false),
                    Mesh mesh when mesh.IsClosed => mesh.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance, strictlyIn: false),
                    _ => false,
                };
                distances[i] = inside ? -unsignedDist : unsignedDist;
            }
            Point3d[] finalGrid = [.. grid[..totalSamples]];
            double[] finalDistances = [.. distances[..totalSamples]];
            return ResultFactory.Create(value: new Fields.ScalarFieldSamples(finalGrid, finalDistances));
        } finally {
            ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
            ArrayPool<double>.Shared.Return(distances, clearArray: true);
        }
    }

    [Pure]
    private static Result<Fields.VectorFieldSamples> ExecuteGradientField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase =>
        ExecuteDistanceField<T>(geometry, sampling, bufferSize, context).Bind(distanceField => {
            BoundingBox bounds = sampling.Bounds ?? geometry.GetBoundingBox(accurate: true);
            Vector3d gridDelta = (bounds.Max - bounds.Min) / (sampling.Resolution - 1);
            return FieldsCompute.ComputeGradient(
                distances: distanceField.Values,
                grid: distanceField.Grid,
                resolution: sampling.Resolution,
                gridDelta: gridDelta)
                .Map(result => new Fields.VectorFieldSamples(result.Grid, result.Gradients));
        });
}

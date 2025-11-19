using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with UnifiedOperation integration.</summary>
[Pure]
internal static class FieldsCore {
    private sealed record DistanceOperationMetadata(
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<IReadOnlyList<(Point3d[], double[])>>> Executor,
        FieldsConfig.DistanceFieldMetadata Metadata);

    private static readonly FrozenDictionary<Type, DistanceOperationMetadata> DistanceDispatch =
        FieldsConfig.DistanceFields
            .ToDictionary(
                keySelector: static entry => entry.Key,
                elementSelector: static entry => new DistanceOperationMetadata(
                    Executor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bufferSize, context).Map(static result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bufferSize, context).Map(static result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bufferSize, context).Map(static result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                                : static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bufferSize, context).Map(static result => (IReadOnlyList<(Point3d[], double[])>)[result,]),
                    Metadata: entry.Value))
            .ToFrozenDictionary();

    [Pure]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context) =>
        geometry is null
            ? ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(geometry.GetType(), out DistanceOperationMetadata? metadata)
                ? ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<(Point3d[], double[])>>>)(item => metadata.Executor(item, sampling, metadata.Metadata.BufferSize, context)),
                    config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = metadata.Metadata.ValidationMode,
                        OperationName = metadata.Metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Gradients)> GradientField(
        (Point3d[] Grid, double[] Distances) distanceField,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.GradientOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: distanceField,
                operation: (Func<(Point3d[], double[]), Result<IReadOnlyList<(Point3d[], Vector3d[])>>>)(field =>
                    FieldsCompute.ComputeGradient(distances: field.Item2, grid: field.Item1, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], Vector3d[])>)[r,])),
                config: new OperationConfig<(Point3d[], double[]), (Point3d[], Vector3d[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Curl)> CurlField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.CurlOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: (vectorField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], Vector3d[])>>>)(field =>
                    FieldsCompute.ComputeCurl(vectorField: field.Item1, grid: field.Item2, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], Vector3d[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], Vector3d[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Divergence)> DivergenceField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.DivergenceOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: (vectorField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.ComputeDivergence(vectorField: field.Item1, grid: field.Item2, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Laplacian)> LaplacianField(
        double[] scalarField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.LaplacianOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: (scalarField, gridPoints),
                operation: (Func<(double[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.ComputeLaplacian(scalarField: field.Item1, grid: field.Item2, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(double[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    internal static Result<(Point3d[] Grid, double[,][] Hessian)> HessianField(
        double[] scalarField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.HessianOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: (scalarField, gridPoints),
                operation: (Func<(double[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[,][])>>>)(field =>
                    FieldsCompute.ComputeHessian(scalarField: field.Item1, grid: field.Item2, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[,][])>)[r,])),
                config: new OperationConfig<(double[], Point3d[]), (Point3d[], double[,][])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[,][])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Potential)> VectorPotentialField(
        Vector3d[] magneticField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context) =>
        (FieldsConfig.DifferentialOperations[typeof(FieldsConfig.VectorPotentialOp)], (bounds.Max - bounds.Min) / (sampling.Resolution - 1)) is (FieldsConfig.FieldOperationMetadata meta, Vector3d gridDelta)
            ? UnifiedOperation.Apply(
                input: (magneticField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], Vector3d[])>>>)(field =>
                    FieldsCompute.ComputeVectorPotential(vectorField: field.Item1, grid: field.Item2, resolution: sampling.Resolution, gridDelta: gridDelta)
                        .Map(static r => (IReadOnlyList<(Point3d[], Vector3d[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], Vector3d[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidFieldBounds);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        Fields.InterpolationMode mode,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (query, scalarField, gridPoints),
            operation: (Func<(Point3d, double[], Point3d[]), Result<IReadOnlyList<double>>>)(data =>
                FieldsCompute.InterpolateScalar(query: data.Item1, scalarField: data.Item2, grid: data.Item3, resolution: sampling.Resolution, bounds: bounds, mode: mode)
                    .Map(static r => (IReadOnlyList<double>)[r,])),
            config: new OperationConfig<(Point3d, double[], Point3d[]), double> {
                Context = context,
                ValidationMode = FieldsConfig.InterpolationMetadata.ValidationMode,
                OperationName = FieldsConfig.InterpolationMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        Fields.InterpolationMode mode,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (query, vectorField, gridPoints),
            operation: (Func<(Point3d, Vector3d[], Point3d[]), Result<IReadOnlyList<Vector3d>>>)(data =>
                FieldsCompute.InterpolateVector(query: data.Item1, vectorField: data.Item2, grid: data.Item3, resolution: sampling.Resolution, bounds: bounds, mode: mode)
                    .Map(static r => (IReadOnlyList<Vector3d>)[r,])),
            config: new OperationConfig<(Point3d, Vector3d[], Point3d[]), Vector3d> {
                Context = context,
                ValidationMode = FieldsConfig.InterpolationMetadata.ValidationMode,
                OperationName = FieldsConfig.InterpolationMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> Streamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        Fields.IntegrationScheme scheme,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (vectorField, gridPoints, seeds),
            operation: (Func<(Vector3d[], Point3d[], Point3d[]), Result<IReadOnlyList<Curve[]>>>)(data =>
                FieldsCompute.IntegrateStreamlines(vectorField: data.Item1, gridPoints: data.Item2, seeds: data.Item3, stepSize: sampling.StepSize, scheme: scheme, resolution: sampling.Resolution, bounds: bounds, context: context)
                    .Map(static r => (IReadOnlyList<Curve[]>)[r,])),
            config: new OperationConfig<(Vector3d[], Point3d[], Point3d[]), Curve[]> {
                Context = context,
                ValidationMode = FieldsConfig.StreamlineMetadata.ValidationMode,
                OperationName = FieldsConfig.StreamlineMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh[]> Isosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        double[] isovalues,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (scalarField, gridPoints, isovalues),
            operation: (Func<(double[], Point3d[], double[]), Result<IReadOnlyList<Mesh[]>>>)(data =>
                FieldsCompute.ExtractIsosurfaces(scalarField: data.Item1, gridPoints: data.Item2, resolution: sampling.Resolution, isovalues: data.Item3)
                    .Map(static r => (IReadOnlyList<Mesh[]>)[r,])),
            config: new OperationConfig<(double[], Point3d[], double[]), Mesh[]> {
                Context = context,
                ValidationMode = FieldsConfig.IsosurfaceMetadata.ValidationMode,
                OperationName = FieldsConfig.IsosurfaceMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    internal static Result<Fields.CriticalPoint[]> CriticalPoints(
        double[] scalarField,
        Vector3d[] gradientField,
        double[,][] hessian,
        Point3d[] gridPoints,
        Fields.FieldSampling sampling,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (scalarField, gradientField, hessian, gridPoints),
            operation: (Func<(double[], Vector3d[], double[,][], Point3d[]), Result<IReadOnlyList<Fields.CriticalPoint[]>>>)(data =>
                FieldsCompute.DetectCriticalPoints(scalarField: data.Item1, gradientField: data.Item2, hessian: data.Item3, grid: data.Item4, resolution: sampling.Resolution)
                    .Map(static r => (IReadOnlyList<Fields.CriticalPoint[]>)[r,])),
            config: new OperationConfig<(double[], Vector3d[], double[,][], Point3d[]), Fields.CriticalPoint[]> {
                Context = context,
                ValidationMode = FieldsConfig.CriticalPointMetadata.ValidationMode,
                OperationName = FieldsConfig.CriticalPointMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.FieldStatistics> Statistics(
        double[] scalarField,
        Point3d[] gridPoints,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: (scalarField, gridPoints),
            operation: (Func<(double[], Point3d[]), Result<IReadOnlyList<Fields.FieldStatistics>>>)(data =>
                FieldsCompute.ComputeFieldStatistics(scalarField: data.Item1, grid: data.Item2)
                    .Map(static r => (IReadOnlyList<Fields.FieldStatistics>)[r,])),
            config: new OperationConfig<(double[], Point3d[]), Fields.FieldStatistics> {
                Context = context,
                ValidationMode = FieldsConfig.StatisticsMetadata.ValidationMode,
                OperationName = FieldsConfig.StatisticsMetadata.OperationName,
                EnableDiagnostics = false,
            }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> DirectionalDerivativeField(
        Vector3d[] gradientField,
        Point3d[] gridPoints,
        Vector3d direction,
        IGeometryContext context) =>
        FieldsConfig.CompositionOperations[typeof(FieldsConfig.DirectionalDerivativeOp)] is FieldsConfig.FieldOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: (gradientField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.ComputeDirectionalDerivative(gradientField: field.Item1, grid: field.Item2, direction: direction)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDirectionalDerivative);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Magnitudes)> FieldMagnitude(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        IGeometryContext context) =>
        FieldsConfig.CompositionOperations[typeof(FieldsConfig.MagnitudeOp)] is FieldsConfig.FieldOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: (vectorField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.ComputeFieldMagnitude(vectorField: field.Item1, grid: field.Item2)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldMagnitude);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Normalized)> NormalizeField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        IGeometryContext context) =>
        FieldsConfig.CompositionOperations[typeof(FieldsConfig.NormalizeOp)] is FieldsConfig.FieldOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: (vectorField, gridPoints),
                operation: (Func<(Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], Vector3d[])>>>)(field =>
                    FieldsCompute.NormalizeVectorField(vectorField: field.Item1, grid: field.Item2)
                        .Map(static r => (IReadOnlyList<(Point3d[], Vector3d[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Point3d[]), (Point3d[], Vector3d[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidFieldNormalization);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Fields.VectorComponent component,
        IGeometryContext context) =>
        FieldsConfig.CompositionOperations[typeof(FieldsConfig.ScalarVectorProductOp)] is FieldsConfig.FieldOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: (scalarField, vectorField, gridPoints),
                operation: (Func<(double[], Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.ScalarVectorProduct(scalarField: field.Item1, vectorField: field.Item2, grid: field.Item3, component: component)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(double[], Vector3d[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DotProduct)> VectorDotProduct(
        Vector3d[] vectorField1,
        Vector3d[] vectorField2,
        Point3d[] gridPoints,
        IGeometryContext context) =>
        FieldsConfig.CompositionOperations[typeof(FieldsConfig.VectorDotProductOp)] is FieldsConfig.FieldOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: (vectorField1, vectorField2, gridPoints),
                operation: (Func<(Vector3d[], Vector3d[], Point3d[]), Result<IReadOnlyList<(Point3d[], double[])>>>)(field =>
                    FieldsCompute.VectorDotProduct(vectorField1: field.Item1, vectorField2: field.Item2, grid: field.Item3)
                        .Map(static r => (IReadOnlyList<(Point3d[], double[])>)[r,])),
                config: new OperationConfig<(Vector3d[], Vector3d[], Point3d[]), (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase =>
        ((T)geometry, sampling.Bounds ?? ((T)geometry).GetBoundingBox(accurate: true), sampling.Resolution) is (T typed, BoundingBox bounds, int resolution) && bounds.IsValid
            ? ((Func<Result<(Point3d[], double[])>>)(() => {
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
                    return ResultFactory.Create(value: (Grid: [.. grid[..totalSamples]], Distances: [.. distances[..totalSamples]]));
                } finally {
                    ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
                    ArrayPool<double>.Shared.Return(distances, clearArray: true);
                }
            }))()
            : ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
}

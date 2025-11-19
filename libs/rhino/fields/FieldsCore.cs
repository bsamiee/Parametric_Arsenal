using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
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
    private sealed record DistanceOperationEntry(
        Func<GeometryBase, Fields.FieldSampling, BoundingBox, int, IGeometryContext, Result<Fields.ScalarFieldSamples>> Executor,
        FieldsConfig.DistanceFieldMetadata Metadata);

    private static readonly FrozenDictionary<Type, DistanceOperationEntry> DistanceDispatch =
        FieldsConfig.DistanceFields
            .ToDictionary(
                keySelector: static entry => entry.Key,
                elementSelector: static entry => new DistanceOperationEntry(
                    Executor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bounds, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bounds, bufferSize, context)
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bounds, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bounds, bufferSize, context)
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bounds, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bounds, bufferSize, context)
                                : static (geometry, sampling, bounds, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bounds, bufferSize, context),
                    Metadata: entry.Value))
            .ToFrozenDictionary();

    internal static Result<Fields.ScalarFieldSamples> DistanceField(Fields.DistanceFieldRequest request, IGeometryContext context) =>
        ExecuteGeometryOperation(
            geometry: request.Geometry,
            sampling: request.Sampling,
            context: context,
            operationNameSelector: static meta => meta.DistanceOperationName,
            executor: static (geometry, sampling, bounds, entry, ctx) =>
                entry.Executor(geometry, sampling, bounds, entry.Metadata.BufferSize, ctx));

    internal static Result<Fields.VectorFieldSamples> GradientField(Fields.GradientFieldRequest request, IGeometryContext context) =>
        ExecuteGeometryOperation(
            geometry: request.Geometry,
            sampling: request.Sampling,
            context: context,
            operationNameSelector: static meta => meta.GradientOperationName,
            executor: static (geometry, sampling, bounds, entry, ctx) =>
                entry.Executor(geometry, sampling, bounds, entry.Metadata.BufferSize, ctx)
                    .Bind(distance => FieldsCompute.ComputeGradient(
                        distances: distance.Values,
                        grid: distance.Grid,
                        resolution: sampling.Resolution,
                        gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1))));

    internal static Result<Fields.VectorFieldSamples> CurlField(Fields.CurlFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeCurl(
                vectorField: item.VectorField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                gridDelta: (item.Bounds.Max - item.Bounds.Min) / (item.Sampling.Resolution - 1)));

    internal static Result<Fields.ScalarFieldSamples> DivergenceField(Fields.DivergenceFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeDivergence(
                vectorField: item.VectorField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                gridDelta: (item.Bounds.Max - item.Bounds.Min) / (item.Sampling.Resolution - 1)));

    internal static Result<Fields.ScalarFieldSamples> LaplacianField(Fields.LaplacianFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeLaplacian(
                scalarField: item.ScalarField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                gridDelta: (item.Bounds.Max - item.Bounds.Min) / (item.Sampling.Resolution - 1)));

    internal static Result<Fields.VectorFieldSamples> VectorPotentialField(Fields.VectorPotentialFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeVectorPotential(
                vectorField: item.VectorField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                gridDelta: (item.Bounds.Max - item.Bounds.Min) / (item.Sampling.Resolution - 1)));

    internal static Result<double> InterpolateScalar(Fields.ScalarInterpolationRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.InterpolateScalar(
                query: item.Query,
                scalarField: item.ScalarField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                bounds: item.Bounds,
                mode: RequiresNearest(item.Bounds) ? new Fields.NearestInterpolationMode() : item.Mode));

    internal static Result<Vector3d> InterpolateVector(Fields.VectorInterpolationRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.InterpolateVector(
                query: item.Query,
                vectorField: item.VectorField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                bounds: item.Bounds,
                mode: RequiresNearest(item.Bounds) ? new Fields.NearestInterpolationMode() : item.Mode));

    internal static Result<Curve[]> Streamlines(Fields.StreamlineRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: item => FieldsCompute.IntegrateStreamlines(
                vectorField: item.VectorField,
                gridPoints: item.Grid,
                seeds: item.Seeds,
                stepSize: item.Sampling.StepSize,
                scheme: item.Scheme,
                resolution: item.Sampling.Resolution,
                bounds: item.Bounds,
                context: context));

    internal static Result<Mesh[]> Isosurfaces(Fields.IsosurfaceRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ExtractIsosurfaces(
                scalarField: item.ScalarField,
                gridPoints: item.Grid,
                resolution: item.Sampling.Resolution,
                isovalues: item.Isovalues));

    internal static Result<Fields.HessianFieldSamples> HessianField(Fields.HessianFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeHessian(
                scalarField: item.ScalarField,
                grid: item.Grid,
                resolution: item.Sampling.Resolution,
                gridDelta: (item.Bounds.Max - item.Bounds.Min) / (item.Sampling.Resolution - 1)));

    internal static Result<Fields.ScalarFieldSamples> DirectionalDerivativeField(Fields.DirectionalDerivativeRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeDirectionalDerivative(
                gradientField: item.GradientField,
                grid: item.Grid,
                direction: item.Direction));

    internal static Result<Fields.ScalarFieldSamples> FieldMagnitude(Fields.FieldMagnitudeRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeFieldMagnitude(
                vectorField: item.VectorField,
                grid: item.Grid));

    internal static Result<Fields.VectorFieldSamples> NormalizeField(Fields.NormalizeFieldRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.NormalizeVectorField(
                vectorField: item.VectorField,
                grid: item.Grid));

    internal static Result<Fields.ScalarFieldSamples> ScalarVectorProduct(Fields.ScalarVectorProductRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ScalarVectorProduct(
                scalarField: item.ScalarField,
                vectorField: item.VectorField,
                grid: item.Grid,
                component: item.Component));

    internal static Result<Fields.ScalarFieldSamples> VectorDotProduct(Fields.VectorDotProductRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.VectorDotProduct(
                vectorField1: item.FirstField,
                vectorField2: item.SecondField,
                grid: item.Grid));

    internal static Result<Fields.CriticalPoint[]> CriticalPoints(Fields.CriticalPointsRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.DetectCriticalPoints(
                scalarField: item.ScalarField,
                gradientField: item.GradientField,
                hessian: item.Hessian,
                grid: item.Grid,
                resolution: item.Sampling.Resolution));

    internal static Result<Fields.FieldStatistics> ComputeStatistics(Fields.FieldStatisticsRequest request, IGeometryContext context) =>
        ExecuteOperation(
            request: request,
            context: context,
            executor: static item => FieldsCompute.ComputeFieldStatistics(
                scalarField: item.ScalarField,
                grid: item.Grid));

    private static Result<TOut> ExecuteGeometryOperation<TOut>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context,
        Func<FieldsConfig.DistanceFieldMetadata, string> operationNameSelector,
        Func<GeometryBase, Fields.FieldSampling, BoundingBox, DistanceOperationEntry, IGeometryContext, Result<TOut>> executor) =>
        geometry is null
            ? ResultFactory.Create<TOut>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(geometry.GetType(), out DistanceOperationEntry? entry)
                ? ResultFactory.Create<TOut>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<TOut>>>)(item => {
                        BoundingBox bounds = sampling.Bounds ?? item.GetBoundingBox(accurate: true);
                        return !bounds.IsValid
                            ? ResultFactory.Create<IReadOnlyList<TOut>>(error: E.Geometry.InvalidFieldBounds)
                            : executor(item, sampling, bounds, entry, context)
                                .Map(result => (IReadOnlyList<TOut>)[result,]);
                    }),
                    config: new OperationConfig<GeometryBase, TOut> {
                        Context = context,
                        ValidationMode = entry.Metadata.ValidationMode,
                        OperationName = operationNameSelector(entry.Metadata),
                    }).Map(result => result[0]);

    private static Result<TOut> ExecuteOperation<TRequest, TOut>(
        TRequest request,
        IGeometryContext context,
        Func<TRequest, Result<TOut>> executor) where TRequest : Fields.FieldOperation =>
        !FieldsConfig.FieldOperations.TryGetValue(typeof(TRequest), out FieldsConfig.FieldOperationMetadata? metadata)
            ? ResultFactory.Create<TOut>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unsupported field request: {typeof(TRequest).Name}"))
            : UnifiedOperation.Apply(
                input: request,
                operation: (Func<TRequest, Result<IReadOnlyList<TOut>>>)(item => executor(item).Map(value => (IReadOnlyList<TOut>)[value,])),
                config: new OperationConfig<TRequest, TOut> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                }).Map(result => result[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresNearest(BoundingBox bounds) =>
        RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Fields.ScalarFieldSamples> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
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
                        grid[gridIndex++] = new(
                            bounds.Min.X + (i * delta.X),
                            bounds.Min.Y + (j * delta.Y),
                            bounds.Min.Z + (k * delta.Z));
                    }
                }
            }

            for (int i = 0; i < totalSamples; i++) {
                Point3d closest = typed switch {
                    Mesh mesh => mesh.ClosestPoint(grid[i]),
                    Brep brep => brep.ClosestPoint(grid[i]),
                    Curve curve => curve.ClosestPoint(grid[i], out double t) ? curve.PointAt(t) : grid[i],
                    Surface surface => surface.ClosestPoint(grid[i], out double u, out double v) ? surface.PointAt(u, v) : grid[i],
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
}

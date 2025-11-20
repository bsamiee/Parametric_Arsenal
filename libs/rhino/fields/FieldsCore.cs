using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
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
    private sealed record DistanceOperationMetadata(
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<IReadOnlyList<(Point3d[], double[])>>> Executor,
        FieldsConfig.DistanceFieldMetadata Metadata);

    private static readonly FrozenDictionary<Type, DistanceOperationMetadata> DistanceDispatch =
        FieldsConfig.DistanceFields
            .ToDictionary(
                keySelector: static entry => entry.Key,
                elementSelector: static entry => new DistanceOperationMetadata(
                    Executor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                                : static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,]),
                    Metadata: entry.Value))
            .ToFrozenDictionary();

    [Pure]
    internal static Result<Fields.FieldResult> Execute(Fields.FieldOperation operation, IGeometryContext context) =>
        operation switch {
            Fields.DistanceFieldRequest req => ExecuteDistanceField(req, context).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.GradientFieldRequest req => ExecuteGradientField(req, context).Map(r => (Fields.FieldResult)new Fields.FieldResult.Vector(r)),
            Fields.CurlFieldRequest req => ExecuteCurlField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Vector(r)),
            Fields.DivergenceFieldRequest req => ExecuteDivergenceField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.LaplacianFieldRequest req => ExecuteLaplacianField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.VectorPotentialFieldRequest req => ExecuteVectorPotentialField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Vector(r)),
            Fields.InterpolateScalarRequest req => ExecuteInterpolateScalar(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.ScalarValue(r)),
            Fields.InterpolateVectorRequest req => ExecuteInterpolateVector(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.VectorValue(r)),
            Fields.StreamlinesRequest req => ExecuteStreamlines(req, context).Map(r => (Fields.FieldResult)new Fields.FieldResult.Curves(r)),
            Fields.IsosurfacesRequest req => ExecuteIsosurfaces(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Meshes(r)),
            Fields.HessianFieldRequest req => ExecuteHessianField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Hessian(r)),
            Fields.DirectionalDerivativeFieldRequest req => ExecuteDirectionalDerivativeField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.FieldMagnitudeRequest req => ExecuteFieldMagnitude(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.NormalizeFieldRequest req => ExecuteNormalizeField(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Vector(r)),
            Fields.ScalarVectorProductRequest req => ExecuteScalarVectorProduct(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.VectorDotProductRequest req => ExecuteVectorDotProduct(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Scalar(r)),
            Fields.CriticalPointsRequest req => ExecuteCriticalPoints(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.CriticalPoints(r)),
            Fields.ComputeStatisticsRequest req => ExecuteComputeStatistics(req).Map(r => (Fields.FieldResult)new Fields.FieldResult.Statistics(r)),
            _ => throw new UnreachableException($"Unhandled field operation: {operation.GetType().Name}"),
        };

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteDistanceField(Fields.DistanceFieldRequest request, IGeometryContext context) {
        GeometryBase geometry = request.Geometry;
        Fields.FieldSampling sampling = request.Sampling ?? Fields.FieldSampling.Default;
        return geometry is null
            ? ResultFactory.Create<Fields.ScalarFieldSamples>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(geometry.GetType(), out DistanceOperationMetadata? metadata)
                ? ResultFactory.Create<Fields.ScalarFieldSamples>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<(Point3d[], double[])>>>)(item => metadata.Executor(item, sampling, metadata.Metadata.BufferSize, context)),
                    config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = metadata.Metadata.ValidationMode,
                        OperationName = metadata.Metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(results => new Fields.ScalarFieldSamples(Grid: results[0].Item1, Values: results[0].Item2));
    }

    [Pure]
    private static Result<Fields.VectorFieldSamples> ExecuteGradientField(Fields.GradientFieldRequest request, IGeometryContext context) {
        GeometryBase geometry = request.Geometry;
        Fields.FieldSampling sampling = request.Sampling ?? Fields.FieldSampling.Default;
        return ExecuteDistanceField(new Fields.DistanceFieldRequest(Geometry: geometry, Sampling: sampling), context)
            .Bind(distanceField => {
                BoundingBox bounds = sampling.Bounds ?? geometry.GetBoundingBox(accurate: true);
                Vector3d gridDelta = (bounds.Max - bounds.Min) / (sampling.Resolution - 1);
                return FieldsCompute.ComputeGradient(
                    distances: distanceField.Values,
                    grid: distanceField.Grid,
                    resolution: sampling.Resolution,
                    gridDelta: gridDelta)
                .Map(r => new Fields.VectorFieldSamples(Grid: r.Grid, Vectors: r.Gradients));
            });
    }

    [Pure]
    private static Result<Fields.VectorFieldSamples> ExecuteCurlField(Fields.CurlFieldRequest request) {
        Vector3d gridDelta = (request.Bounds.Max - request.Bounds.Min) / (request.Sampling.Resolution - 1);
        return ResultFactory.Create(value: (request.VectorField, request.GridPoints))
            .Ensure(v => v.VectorField.Length == v.GridPoints.Length, error: E.Geometry.InvalidCurlComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeCurl(vectorField: request.VectorField, grid: request.GridPoints, resolution: request.Sampling.Resolution, gridDelta: gridDelta))
            .Map(r => new Fields.VectorFieldSamples(Grid: r.Grid, Vectors: r.Curl));
    }

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteDivergenceField(Fields.DivergenceFieldRequest request) {
        Vector3d gridDelta = (request.Bounds.Max - request.Bounds.Min) / (request.Sampling.Resolution - 1);
        return ResultFactory.Create(value: (request.VectorField, request.GridPoints))
            .Ensure(v => v.VectorField.Length == v.GridPoints.Length, error: E.Geometry.InvalidDivergenceComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeDivergence(vectorField: request.VectorField, grid: request.GridPoints, resolution: request.Sampling.Resolution, gridDelta: gridDelta))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.Divergence));
    }

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteLaplacianField(Fields.LaplacianFieldRequest request) {
        Vector3d gridDelta = (request.Bounds.Max - request.Bounds.Min) / (request.Sampling.Resolution - 1);
        return ResultFactory.Create(value: (request.ScalarField, request.GridPoints))
            .Ensure(v => v.ScalarField.Length == v.GridPoints.Length, error: E.Geometry.InvalidLaplacianComputation.WithContext("Scalar field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeLaplacian(scalarField: request.ScalarField, grid: request.GridPoints, resolution: request.Sampling.Resolution, gridDelta: gridDelta))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.Laplacian));
    }

    [Pure]
    private static Result<Fields.VectorFieldSamples> ExecuteVectorPotentialField(Fields.VectorPotentialFieldRequest request) {
        Vector3d gridDelta = (request.Bounds.Max - request.Bounds.Min) / (request.Sampling.Resolution - 1);
        return ResultFactory.Create(value: (request.MagneticField, request.GridPoints))
            .Ensure(v => v.MagneticField.Length == v.GridPoints.Length, error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Magnetic field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeVectorPotential(
                vectorField: request.MagneticField,
                grid: request.GridPoints,
                resolution: request.Sampling.Resolution,
                gridDelta: gridDelta)
            .Map(r => new Fields.VectorFieldSamples(Grid: r.Grid, Vectors: r.Potential)));
    }

    [Pure]
    private static Result<double> ExecuteInterpolateScalar(Fields.InterpolateScalarRequest request) {
        Fields.InterpolationMode mode = (RhinoMath.EpsilonEquals(request.Bounds.Max.X, request.Bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(request.Bounds.Max.Y, request.Bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(request.Bounds.Max.Z, request.Bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon))
            ? new Fields.NearestInterpolationMode()
            : request.Mode ?? new Fields.TrilinearInterpolationMode();
        return ResultFactory.Create(value: (request.ScalarField, request.GridPoints, mode))
            .Ensure(state => state.ScalarField.Length == state.GridPoints.Length, error: E.Geometry.InvalidFieldInterpolation.WithContext("Scalar field length must match grid points"))
            .Bind(state => FieldsCompute.InterpolateScalar(query: request.Query, scalarField: state.ScalarField, grid: state.GridPoints, resolution: request.Sampling.Resolution, bounds: request.Bounds, mode: state.mode));
    }

    [Pure]
    private static Result<Vector3d> ExecuteInterpolateVector(Fields.InterpolateVectorRequest request) {
        Fields.InterpolationMode mode = (RhinoMath.EpsilonEquals(request.Bounds.Max.X, request.Bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(request.Bounds.Max.Y, request.Bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(request.Bounds.Max.Z, request.Bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon))
            ? new Fields.NearestInterpolationMode()
            : request.Mode ?? new Fields.TrilinearInterpolationMode();
        return ResultFactory.Create(value: (request.VectorField, request.GridPoints, mode))
            .Ensure(state => state.VectorField.Length == state.GridPoints.Length, error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points"))
            .Bind(state => FieldsCompute.InterpolateVector(query: request.Query, vectorField: state.VectorField, grid: state.GridPoints, resolution: request.Sampling.Resolution, bounds: request.Bounds, mode: state.mode));
    }

    [Pure]
    private static Result<Curve[]> ExecuteStreamlines(Fields.StreamlinesRequest request, IGeometryContext context) =>
        ResultFactory.Create(value: (request.VectorField, request.GridPoints, request.Seeds))
            .Ensure(state => state.VectorField.Length == state.GridPoints.Length, error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points"))
            .Ensure(state => state.Seeds.Length > 0, error: E.Geometry.InvalidStreamlineSeeds)
            .Bind(state => FieldsCompute.IntegrateStreamlines(
                vectorField: state.VectorField,
                gridPoints: state.GridPoints,
                seeds: state.Seeds,
                stepSize: request.Sampling.StepSize,
                scheme: request.Scheme ?? new Fields.RungeKutta4IntegrationScheme(),
                resolution: request.Sampling.Resolution,
                bounds: request.Bounds,
                context: context));

    [Pure]
    private static Result<Mesh[]> ExecuteIsosurfaces(Fields.IsosurfacesRequest request) =>
        ResultFactory.Create(value: (request.ScalarField, request.GridPoints, request.Isovalues))
            .Ensure(state => state.ScalarField.Length == state.GridPoints.Length, error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points"))
            .Ensure(state => state.Isovalues.Length > 0, error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required"))
            .Ensure(v => v.Isovalues.All(value => RhinoMath.IsValidDouble(value)), error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles"))
            .Bind(state => FieldsCompute.ExtractIsosurfaces(
                scalarField: state.ScalarField,
                gridPoints: state.GridPoints,
                resolution: request.Sampling.Resolution,
                isovalues: state.Isovalues));

    [Pure]
    private static Result<Fields.HessianFieldSamples> ExecuteHessianField(Fields.HessianFieldRequest request) {
        Vector3d gridDelta = (request.Bounds.Max - request.Bounds.Min) / (request.Sampling.Resolution - 1);
        return ResultFactory.Create(value: (request.ScalarField, request.GridPoints))
            .Ensure(v => v.ScalarField.Length == v.GridPoints.Length, error: E.Geometry.InvalidHessianComputation.WithContext("Scalar field length must match grid points"))
            .Bind(state => FieldsCompute.ComputeHessian(
                scalarField: state.ScalarField,
                grid: state.GridPoints,
                resolution: request.Sampling.Resolution,
                gridDelta: gridDelta))
            .Map(r => new Fields.HessianFieldSamples(Grid: r.Grid, Hessian: r.Hessian));
    }

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteDirectionalDerivativeField(Fields.DirectionalDerivativeFieldRequest request) =>
        ResultFactory.Create(value: (request.GradientField, request.GridPoints))
            .Ensure(v => v.GradientField.Length == v.GridPoints.Length, error: E.Geometry.InvalidDirectionalDerivative.WithContext("Gradient field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeDirectionalDerivative(gradientField: request.GradientField, grid: request.GridPoints, direction: request.Direction))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.DirectionalDerivatives));

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteFieldMagnitude(Fields.FieldMagnitudeRequest request) =>
        ResultFactory.Create(value: (request.VectorField, request.GridPoints))
            .Ensure(v => v.VectorField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldMagnitude.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeFieldMagnitude(vectorField: request.VectorField, grid: request.GridPoints))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.Magnitudes));

    [Pure]
    private static Result<Fields.VectorFieldSamples> ExecuteNormalizeField(Fields.NormalizeFieldRequest request) =>
        ResultFactory.Create(value: (request.VectorField, request.GridPoints))
            .Ensure(v => v.VectorField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldNormalization.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.NormalizeVectorField(vectorField: request.VectorField, grid: request.GridPoints))
            .Map(r => new Fields.VectorFieldSamples(Grid: r.Grid, Vectors: r.Normalized));

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteScalarVectorProduct(Fields.ScalarVectorProductRequest request) =>
        ResultFactory.Create(value: (request.ScalarField, request.VectorField, request.GridPoints))
            .Ensure(v => v.ScalarField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Scalar field length must match grid points"))
            .Ensure(v => v.VectorField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ScalarVectorProduct(scalarField: request.ScalarField, vectorField: request.VectorField, grid: request.GridPoints, component: request.Component))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.Product));

    [Pure]
    private static Result<Fields.ScalarFieldSamples> ExecuteVectorDotProduct(Fields.VectorDotProductRequest request) =>
        ResultFactory.Create(value: (request.FirstField, request.SecondField, request.GridPoints))
            .Ensure(v => v.FirstField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("First vector field length must match grid points"))
            .Ensure(v => v.SecondField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Second vector field length must match grid points"))
            .Bind(_ => FieldsCompute.VectorDotProduct(vectorField1: request.FirstField, vectorField2: request.SecondField, grid: request.GridPoints))
            .Map(r => new Fields.ScalarFieldSamples(Grid: r.Grid, Values: r.DotProduct));

    [Pure]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    private static Result<Fields.CriticalPoint[]> ExecuteCriticalPoints(Fields.CriticalPointsRequest request) =>
        (request.Hessian.GetLength(0) == 3
            && request.Hessian.GetLength(1) == 3
            && Enumerable.Range(0, 3).All(row =>
                Enumerable.Range(0, 3).All(col =>
                    request.Hessian[row, col] is not null
                    && request.Hessian[row, col].Length == request.GridPoints.Length))) switch {
                        false => ResultFactory.Create<Fields.CriticalPoint[]>(
                            error: E.Geometry.InvalidCriticalPointDetection.WithContext("Hessian must be a 3x3 tensor with entries per grid sample")),
                        true => FieldsCompute.DetectCriticalPoints(
                            scalarField: request.ScalarField,
                            gradientField: request.GradientField,
                            hessian: request.Hessian,
                            grid: request.GridPoints,
                            resolution: request.Sampling.Resolution),
                    };

    [Pure]
    private static Result<Fields.FieldStatistics> ExecuteComputeStatistics(Fields.ComputeStatisticsRequest request) =>
        ResultFactory.Create(value: (request.ScalarField, request.GridPoints))
            .Ensure(v => v.ScalarField.Length == v.GridPoints.Length, error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeFieldStatistics(scalarField: request.ScalarField, grid: request.GridPoints));

    [Pure]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
        BoundingBox bounds = sampling.Bounds ?? typed.GetBoundingBox(accurate: true);
        if (!bounds.IsValid) {
            return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
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
                    _ => throw new UnreachableException($"Unsupported geometry type: {typed.GetType().Name}"),
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
            return ResultFactory.Create(value: (Grid: finalGrid, Distances: finalDistances));
        } finally {
            ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
            ArrayPool<double>.Shared.Return(distances, clearArray: true);
        }
    }
}

using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with UnifiedOperation integration and metadata-driven orchestration.</summary>
[Pure]
internal static class FieldsCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Fields.InterpolationMode ResolveInterpolationMode(BoundingBox bounds, Fields.InterpolationMode? mode) =>
        (RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon))
            ? new Fields.NearestInterpolation()
            : mode ?? new Fields.TrilinearInterpolation();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Distances)> ExecuteDistanceField(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context) =>
        geometry is null
            ? ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !FieldsConfig.DistanceFields.TryGetValue(geometry.GetType(), out FieldsConfig.FieldOperationMetadata? metadata)
                ? ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<(Point3d[], double[])>>>)(item => ComputeDistanceField(item, sampling, metadata.BufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])),
                    config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = metadata.ValidationMode,
                        OperationName = metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ComputeDistanceField(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) {
        BoundingBox bounds = sampling.Bounds ?? geometry.GetBoundingBox(accurate: true);
        return !bounds.IsValid
            ? ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds)
            : ((Func<Result<(Point3d[], double[])>>)(() => {
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
                        Point3d closest = geometry switch {
                            Mesh m => m.ClosestPoint(grid[i]),
                            Brep b => b.ClosestPoint(grid[i]),
                            Curve c => c.ClosestPoint(grid[i], out double t) ? c.PointAt(t) : grid[i],
                            Surface s => s.ClosestPoint(grid[i], out double u, out double v) ? s.PointAt(u, v) : grid[i],
                            _ => grid[i],
                        };
                        double unsignedDist = grid[i].DistanceTo(closest);
                        bool inside = geometry switch {
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
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Gradients)> ExecuteGradient(Fields.GradientFieldOp operation) {
        FieldsConfig.FieldOperationMetadata metadata = FieldsConfig.Operations[typeof(Fields.GradientFieldOp)];
        Vector3d gridDelta = (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1);
        return FieldsCompute.ComputeGradient(
            distances: operation.ScalarField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            gridDelta: gridDelta);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Curl)> ExecuteCurl(Fields.CurlFieldOp operation) =>
        FieldsCompute.ComputeCurl(
            vectorField: operation.VectorField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            gridDelta: (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Divergence)> ExecuteDivergence(Fields.DivergenceFieldOp operation) =>
        FieldsCompute.ComputeDivergence(
            vectorField: operation.VectorField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            gridDelta: (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Laplacian)> ExecuteLaplacian(Fields.LaplacianFieldOp operation) =>
        FieldsCompute.ComputeLaplacian(
            scalarField: operation.ScalarField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            gridDelta: (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Potential)> ExecuteVectorPotential(Fields.VectorPotentialFieldOp operation) =>
        (operation.MagneticField.Length == operation.GridPoints.Length) switch {
            false => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Magnetic field length must match grid points")),
            true => FieldsCompute.ComputeVectorPotential(
                vectorField: operation.MagneticField,
                grid: operation.GridPoints,
                resolution: operation.Sampling.Resolution,
                gridDelta: (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<double> ExecuteInterpolateScalar(Fields.InterpolateScalarOp operation) =>
        FieldsCompute.InterpolateScalar(
            query: operation.Query,
            scalarField: operation.ScalarField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            bounds: operation.Bounds,
            mode: operation.Mode);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> ExecuteInterpolateVector(Fields.InterpolateVectorOp operation) =>
        FieldsCompute.InterpolateVector(
            query: operation.Query,
            vectorField: operation.VectorField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            bounds: operation.Bounds,
            mode: operation.Mode);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> ExecuteStreamlines(Fields.StreamlinesOp operation, IGeometryContext context) =>
        (operation.VectorField.Length == operation.GridPoints.Length, operation.Seeds.Length > 0) switch {
            (false, _) => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidStreamlineSeeds),
            (true, true) => FieldsCompute.IntegrateStreamlines(
                vectorField: operation.VectorField,
                gridPoints: operation.GridPoints,
                seeds: operation.Seeds,
                stepSize: operation.Sampling.StepSize,
                scheme: operation.Scheme,
                resolution: operation.Sampling.Resolution,
                bounds: operation.Bounds,
                context: context),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh[]> ExecuteIsosurfaces(Fields.IsosurfacesOp operation) =>
        (operation.ScalarField.Length == operation.GridPoints.Length, operation.Isovalues.Length > 0, operation.Isovalues.All(static value => RhinoMath.IsValidDouble(value))) switch {
            (false, _, _) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required")),
            (_, _, false) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles")),
            (true, true, true) => FieldsCompute.ExtractIsosurfaces(
                scalarField: operation.ScalarField,
                gridPoints: operation.GridPoints,
                resolution: operation.Sampling.Resolution,
                isovalues: operation.Isovalues),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    internal static Result<(Point3d[] Grid, double[,][] Hessian)> ExecuteHessian(Fields.HessianFieldOp operation) =>
        FieldsCompute.ComputeHessian(
            scalarField: operation.ScalarField,
            grid: operation.GridPoints,
            resolution: operation.Sampling.Resolution,
            gridDelta: (operation.Bounds.Max - operation.Bounds.Min) / (operation.Sampling.Resolution - 1));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> ExecuteDirectionalDerivative(Fields.DirectionalDerivativeFieldOp operation) =>
        FieldsCompute.ComputeDirectionalDerivative(
            gradientField: operation.GradientField,
            grid: operation.GridPoints,
            direction: operation.Direction);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Magnitudes)> ExecuteFieldMagnitude(Fields.FieldMagnitudeOp operation) =>
        FieldsCompute.ComputeFieldMagnitude(
            vectorField: operation.VectorField,
            grid: operation.GridPoints);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Normalized)> ExecuteNormalizeField(Fields.NormalizeFieldOp operation) =>
        FieldsCompute.NormalizeVectorField(
            vectorField: operation.VectorField,
            grid: operation.GridPoints);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Product)> ExecuteScalarVectorProduct(Fields.ScalarVectorProductOp operation) =>
        FieldsCompute.ScalarVectorProduct(
            scalarField: operation.ScalarField,
            vectorField: operation.VectorField,
            grid: operation.GridPoints,
            component: operation.Component);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DotProduct)> ExecuteVectorDotProduct(Fields.VectorDotProductOp operation) =>
        FieldsCompute.VectorDotProduct(
            vectorField1: operation.VectorField1,
            vectorField2: operation.VectorField2,
            grid: operation.GridPoints);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    internal static Result<Fields.CriticalPoint[]> ExecuteCriticalPoints(Fields.CriticalPointsOp operation) =>
        (operation.Hessian.GetLength(0) == 3
            && operation.Hessian.GetLength(1) == 3
            && Enumerable.Range(0, 3).All(row =>
                Enumerable.Range(0, 3).All(col =>
                    operation.Hessian[row, col] is not null
                    && operation.Hessian[row, col].Length == operation.GridPoints.Length))) switch {
                        false => ResultFactory.Create<Fields.CriticalPoint[]>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Hessian must be a 3x3 tensor with entries per grid sample")),
                        true => FieldsCompute.DetectCriticalPoints(
                            scalarField: operation.ScalarField,
                            gradientField: operation.GradientField,
                            hessian: operation.Hessian,
                            grid: operation.GridPoints,
                            resolution: operation.Sampling.Resolution),
                    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.FieldStatistics> ExecuteStatistics(Fields.StatisticsOp operation) =>
        (operation.ScalarField.Length == operation.GridPoints.Length) switch {
            false => ResultFactory.Create<Fields.FieldStatistics>(error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field length must match grid points")),
            true => FieldsCompute.ComputeFieldStatistics(
                scalarField: operation.ScalarField,
                grid: operation.GridPoints),
        };
}

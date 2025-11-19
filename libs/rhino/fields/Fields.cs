using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations for computational field analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the primary API entry point")]
public static class Fields {
    /// <summary>Field sampling specification with grid resolution, bounds, and step size; constructor clamps resolution to [8, 256] and stepSize to [√ε, 1.0].</summary>
    public sealed record FieldSampling {
        /// <summary>Initializes field sampling with automatic clamping: resolution [8, 256], stepSize [√ε, 1.0], null values use defaults.</summary>
        public FieldSampling(int? resolution = null, BoundingBox? bounds = null, double? stepSize = null) {
            this.Resolution = RhinoMath.Clamp(
                resolution ?? FieldsConfig.DefaultResolution,
                FieldsConfig.MinResolution,
                FieldsConfig.MaxResolution);
            this.Bounds = bounds;
            this.StepSize = RhinoMath.Clamp(
                stepSize ?? FieldsConfig.DefaultStepSize,
                FieldsConfig.MinStepSize,
                FieldsConfig.MaxStepSize);
        }
        /// <summary>Default sampling instance (resolution: 32, step size: 0.01).</summary>
        public static FieldSampling Default { get; } = new();
        /// <summary>Grid resolution (cube root of sample count), clamped to [8, 256].</summary>
        public int Resolution { get; }
        /// <summary>Sample region bounding box (null uses geometry bounds).</summary>
        public BoundingBox? Bounds { get; }
        /// <summary>Integration/sampling step size, clamped to [√ε, 1.0].</summary>
        public double StepSize { get; }
    }

    /// <summary>Base type for field interpolation strategies.</summary>
    public abstract record InterpolationMode;
    /// <summary>Nearest-neighbor interpolation.</summary>
    public sealed record NearestInterpolationMode : InterpolationMode;
    /// <summary>Trilinear interpolation.</summary>
    public sealed record TrilinearInterpolationMode : InterpolationMode;

    /// <summary>Base type for streamline integration methods.</summary>
    public abstract record IntegrationScheme;
    /// <summary>Explicit Euler integration.</summary>
    public sealed record EulerIntegrationScheme : IntegrationScheme;
    /// <summary>Midpoint (RK2) integration.</summary>
    public sealed record MidpointIntegrationScheme : IntegrationScheme;
    /// <summary>Classical RK4 integration.</summary>
    public sealed record RungeKutta4IntegrationScheme : IntegrationScheme;

    /// <summary>Vector component selector for field composition.</summary>
    public abstract record VectorComponent;
    /// <summary>X-component selector.</summary>
    public sealed record XComponent : VectorComponent;
    /// <summary>Y-component selector.</summary>
    public sealed record YComponent : VectorComponent;
    /// <summary>Z-component selector.</summary>
    public sealed record ZComponent : VectorComponent;

    /// <summary>Critical point classification.</summary>
    public abstract record CriticalPointKind;
    /// <summary>Local minimum point.</summary>
    public sealed record MinimumCriticalPoint : CriticalPointKind;
    /// <summary>Local maximum point.</summary>
    public sealed record MaximumCriticalPoint : CriticalPointKind;
    /// <summary>Saddle point.</summary>
    public sealed record SaddleCriticalPoint : CriticalPointKind;

    /// <summary>Critical point with location, classification (min/max/saddle), scalar value, and eigendecomposition.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct CriticalPoint(Point3d Location, CriticalPointKind Kind, double Value, Vector3d[] Eigenvectors, double[] Eigenvalues);

    /// <summary>Field statistics including min, max, mean, standard deviation, and extreme value locations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FieldStatistics(double Min, double Max, double Mean, double StdDev, Point3d MinLocation, Point3d MaxLocation);

    /// <summary>Compute signed distance field: geometry → (grid points[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        FieldSampling? sampling,
        IGeometryContext context) where T : GeometryBase =>
        FieldsCore.DistanceField(
            geometry: geometry,
            sampling: sampling ?? FieldSampling.Default,
            context: context);

    /// <summary>Compute gradient field: geometry → (grid points[], gradient vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Gradients)> GradientField<T>(
        T geometry,
        FieldSampling? sampling,
        IGeometryContext context) where T : GeometryBase =>
        DistanceField(geometry: geometry, sampling: sampling, context: context).Bind(distanceField => {
            FieldSampling samplingValue = sampling ?? FieldSampling.Default;
            BoundingBox bounds = samplingValue.Bounds ?? geometry.GetBoundingBox(accurate: true);
            Vector3d gridDelta = (bounds.Max - bounds.Min) / (samplingValue.Resolution - 1);
            return FieldsCompute.ComputeGradient(
                distances: distanceField.Distances,
                grid: distanceField.Grid,
                resolution: samplingValue.Resolution,
                gridDelta: gridDelta);
        });

    /// <summary>Compute curl field: vector field → (grid points[], curl vectors[]) where curl = ∇×F with row-major grid order.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Curl)> CurlField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidCurlComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeCurl(vectorField: vectorField, grid: gridPoints, resolution: sampling.Resolution, gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)));

    /// <summary>Compute divergence field: vector field → (grid points[], divergence scalars[]) where divergence = ∇·F with row-major grid order.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Divergence)> DivergenceField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidDivergenceComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeDivergence(vectorField: vectorField, grid: gridPoints, resolution: sampling.Resolution, gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)));

    /// <summary>Compute Laplacian field: scalar field → (grid points[], Laplacian scalars[]) where Laplacian = ∇²f with row-major grid order.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Laplacian)> LaplacianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (scalarField, gridPoints))
            .Ensure(v => v.scalarField.Length == v.gridPoints.Length, error: E.Geometry.InvalidLaplacianComputation.WithContext("Scalar field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeLaplacian(scalarField: scalarField, grid: gridPoints, resolution: sampling.Resolution, gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)));

    /// <summary>Compute vector potential field: magnetic field B → (grid points[], vector potential A[]) solving ∇²A = -∇×B in Coulomb gauge.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Potential)> VectorPotentialField(
        Vector3d[] magneticField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        (magneticField.Length == gridPoints.Length) switch {
            false => ResultFactory.Create<(Point3d[], Vector3d[])>(
                error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Magnetic field length must match grid points")),
            true => FieldsCompute.ComputeVectorPotential(
                vectorField: magneticField,
                grid: gridPoints,
                resolution: sampling.Resolution,
                gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)),
        };

    /// <summary>Interpolate scalar field at query point: (field, grid, query) → scalar value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds,
        InterpolationMode? mode = null) {
        bool hasDegenerateAxis = RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon);
        InterpolationMode method = hasDegenerateAxis ? new NearestInterpolationMode() : mode ?? new TrilinearInterpolationMode();
        return ResultFactory.Create(value: (Field: scalarField, Grid: gridPoints))
            .Ensure(state => state.Field.Length == state.Grid.Length, error: E.Geometry.InvalidFieldInterpolation.WithContext("Scalar field length must match grid points"))
            .Bind(state => FieldsCompute.InterpolateScalar(
                query: query,
                scalarField: state.Field,
                grid: state.Grid,
                resolution: sampling.Resolution,
                bounds: bounds,
                mode: method));
    }

    /// <summary>Interpolate vector field at query point: (field, grid, query) → vector value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds,
        InterpolationMode? mode = null) {
        bool hasDegenerateAxis = RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon);
        InterpolationMode method = hasDegenerateAxis ? new NearestInterpolationMode() : mode ?? new TrilinearInterpolationMode();
        return ResultFactory.Create(value: (Field: vectorField, Grid: gridPoints))
            .Ensure(state => state.Field.Length == state.Grid.Length, error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points"))
            .Bind(state => FieldsCompute.InterpolateVector(
                query: query,
                vectorField: state.Field,
                grid: state.Grid,
                resolution: sampling.Resolution,
                bounds: bounds,
                mode: method));
    }

    /// <summary>Trace streamlines along vector field: (field, seeds) → curves[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        FieldSampling sampling,
        BoundingBox bounds,
        IGeometryContext context,
        IntegrationScheme? scheme = null) =>
        ResultFactory.Create(value: (vectorField, gridPoints, seeds))
            .Ensure(state => state.vectorField.Length == state.gridPoints.Length, error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points"))
            .Ensure(state => state.seeds.Length > 0, error: E.Geometry.InvalidStreamlineSeeds)
            .Bind(state => FieldsCompute.IntegrateStreamlines(
                vectorField: state.vectorField,
                gridPoints: state.gridPoints,
                seeds: state.seeds,
                stepSize: sampling.StepSize,
                scheme: scheme ?? new RungeKutta4IntegrationScheme(),
                resolution: sampling.Resolution,
                bounds: bounds,
                context: context));

    /// <summary>Extract isosurfaces from scalar field: (field, isovalues) → meshes[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        double[] isovalues) =>
        ResultFactory.Create(value: (ScalarField: scalarField, GridPoints: gridPoints, Isovalues: isovalues))
            .Ensure(state => state.ScalarField.Length == state.GridPoints.Length, error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points"))
            .Ensure(state => state.Isovalues.Length > 0, error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required"))
            .Ensure(v => v.Isovalues.All(value => RhinoMath.IsValidDouble(value)), error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles"))
            .Bind(state => FieldsCompute.ExtractIsosurfaces(
                scalarField: state.ScalarField,
                gridPoints: state.GridPoints,
                resolution: sampling.Resolution,
                isovalues: state.Isovalues));

    /// <summary>Compute Hessian field: scalar field → (grid points[], 3×3 hessian matrices[]) assuming uniform grid spacing.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    public static Result<(Point3d[] Grid, double[,][] Hessian)> HessianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (ScalarField: scalarField, GridPoints: gridPoints))
            .Ensure(v => v.ScalarField.Length == v.GridPoints.Length, error: E.Geometry.InvalidHessianComputation.WithContext("Scalar field length must match grid points"))
            .Bind(state => FieldsCompute.ComputeHessian(
                scalarField: state.ScalarField,
                grid: state.GridPoints,
                resolution: sampling.Resolution,
                gridDelta: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)));

    /// <summary>Compute directional derivative field: (gradient field, direction) → (grid points[], directional derivatives[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> DirectionalDerivativeField(
        Vector3d[] gradientField,
        Point3d[] gridPoints,
        Vector3d direction) =>
        ResultFactory.Create(value: (gradientField, gridPoints))
            .Ensure(v => v.gradientField.Length == v.gridPoints.Length, error: E.Geometry.InvalidDirectionalDerivative.WithContext("Gradient field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeDirectionalDerivative(gradientField: gradientField, grid: gridPoints, direction: direction));

    /// <summary>Compute vector field magnitude: vector field → (grid points[], magnitudes[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Magnitudes)> FieldMagnitude(
        Vector3d[] vectorField,
        Point3d[] gridPoints) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldMagnitude.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeFieldMagnitude(vectorField: vectorField, grid: gridPoints));

    /// <summary>Normalize vector field: vector field → (grid points[], normalized vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Normalized)> NormalizeField(
        Vector3d[] vectorField,
        Point3d[] gridPoints) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldNormalization.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.NormalizeVectorField(vectorField: vectorField, grid: gridPoints));

    /// <summary>Scalar-vector field product: (scalar field, vector field, component) → (grid points[], product[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        VectorComponent component) =>
        ResultFactory.Create(value: (scalarField, vectorField, gridPoints))
            .Ensure(v => v.scalarField.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Scalar field length must match grid points"))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ScalarVectorProduct(scalarField: scalarField, vectorField: vectorField, grid: gridPoints, component: component));

    /// <summary>Vector-vector dot product field: (vector field 1, vector field 2) → (grid points[], dot products[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] DotProduct)> VectorDotProduct(
        Vector3d[] vectorField1,
        Vector3d[] vectorField2,
        Point3d[] gridPoints) =>
        ResultFactory.Create(value: (vectorField1, vectorField2, gridPoints))
            .Ensure(v => v.vectorField1.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("First vector field length must match grid points"))
            .Ensure(v => v.vectorField2.Length == v.gridPoints.Length, error: E.Geometry.InvalidFieldComposition.WithContext("Second vector field length must match grid points"))
            .Bind(_ => FieldsCompute.VectorDotProduct(vectorField1: vectorField1, vectorField2: vectorField2, grid: gridPoints));

    /// <summary>Detect and classify critical points: (scalar field, gradient, hessian) → critical points[] classified as min/max/saddle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    public static Result<CriticalPoint[]> CriticalPoints(
        double[] scalarField,
        Vector3d[] gradientField,
        double[,][] hessian,
        Point3d[] gridPoints,
        FieldSampling sampling) =>
        (hessian.GetLength(0) == 3
            && hessian.GetLength(1) == 3
            && Enumerable.Range(0, 3).All(row =>
                Enumerable.Range(0, 3).All(col =>
                    hessian[row, col] is not null
                    && hessian[row, col].Length == gridPoints.Length))) switch {
                        false => ResultFactory.Create<CriticalPoint[]>(
                            error: E.Geometry.InvalidCriticalPointDetection.WithContext("Hessian must be a 3x3 tensor with entries per grid sample")),
                        true => FieldsCompute.DetectCriticalPoints(
                            scalarField: scalarField,
                            gradientField: gradientField,
                            hessian: hessian,
                            grid: gridPoints,
                            resolution: sampling.Resolution),
                    };

    /// <summary>Compute field statistics: scalar field → (min, max, mean, stddev, extreme locations).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FieldStatistics> ComputeStatistics(
        double[] scalarField,
        Point3d[] gridPoints) =>
        (scalarField.Length == gridPoints.Length) switch {
            false => ResultFactory.Create<FieldStatistics>(
                error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field length must match grid points")),
            true => FieldsCompute.ComputeFieldStatistics(
                scalarField: scalarField,
                grid: gridPoints),
        };
}

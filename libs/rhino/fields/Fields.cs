using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations for computational field analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the primary API entry point")]
public static class Fields {
    /// <summary>Field sampling specification with grid resolution, bounds, and step size.</summary>
    public sealed record FieldSampling(
        int Resolution,
        BoundingBox? Bounds,
        double StepSize) {
        /// <summary>Default sampling instance (resolution: 32, step size: 0.01).</summary>
        public static FieldSampling Default { get; } = new(
            Resolution: FieldsConfig.DefaultResolution,
            Bounds: null,
            StepSize: FieldsConfig.DefaultStepSize);

        /// <summary>Create sampling with optional overrides and automatic clamping.</summary>
        public static FieldSampling Create(int? resolution = null, BoundingBox? bounds = null, double? stepSize = null) =>
            new(
                Resolution: Math.Clamp(
                    resolution ?? FieldsConfig.DefaultResolution,
                    FieldsConfig.MinResolution,
                    FieldsConfig.MaxResolution),
                Bounds: bounds,
                StepSize: Math.Clamp(
                    stepSize ?? FieldsConfig.DefaultStepSize,
                    FieldsConfig.MinStepSize,
                    FieldsConfig.MaxStepSize));
    }

    /// <summary>Base type for field interpolation strategies.</summary>
    public abstract record InterpolationMode;
    /// <summary>Nearest-neighbor interpolation.</summary>
    public sealed record NearestInterpolation : InterpolationMode;
    /// <summary>Trilinear interpolation.</summary>
    public sealed record TrilinearInterpolation : InterpolationMode;

    /// <summary>Base type for streamline integration methods.</summary>
    public abstract record IntegrationScheme;
    /// <summary>Explicit Euler integration.</summary>
    public sealed record EulerIntegration : IntegrationScheme;
    /// <summary>Midpoint (RK2) integration.</summary>
    public sealed record MidpointIntegration : IntegrationScheme;
    /// <summary>Classical RK4 integration.</summary>
    public sealed record RungeKutta4Integration : IntegrationScheme;

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

    /// <summary>Base type for field operations.</summary>
    public abstract record Operation;

    /// <summary>Compute signed distance field from geometry.</summary>
    public sealed record DistanceFieldOp(FieldSampling Sampling) : Operation;

    /// <summary>Compute gradient field from scalar field.</summary>
    public sealed record GradientFieldOp(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute curl field from vector field.</summary>
    public sealed record CurlFieldOp(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute divergence field from vector field.</summary>
    public sealed record DivergenceFieldOp(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute Laplacian field from scalar field.</summary>
    public sealed record LaplacianFieldOp(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute vector potential field from magnetic field.</summary>
    public sealed record VectorPotentialFieldOp(Vector3d[] MagneticField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute Hessian field from scalar field.</summary>
    public sealed record HessianFieldOp(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : Operation;

    /// <summary>Compute directional derivative field from gradient field.</summary>
    public sealed record DirectionalDerivativeFieldOp(Vector3d[] GradientField, Point3d[] GridPoints, Vector3d Direction) : Operation;

    /// <summary>Compute field magnitude from vector field.</summary>
    public sealed record FieldMagnitudeOp(Vector3d[] VectorField, Point3d[] GridPoints) : Operation;

    /// <summary>Normalize vector field.</summary>
    public sealed record NormalizeFieldOp(Vector3d[] VectorField, Point3d[] GridPoints) : Operation;

    /// <summary>Compute scalar-vector field product.</summary>
    public sealed record ScalarVectorProductOp(double[] ScalarField, Vector3d[] VectorField, Point3d[] GridPoints, VectorComponent Component) : Operation;

    /// <summary>Compute vector-vector dot product field.</summary>
    public sealed record VectorDotProductOp(Vector3d[] VectorField1, Vector3d[] VectorField2, Point3d[] GridPoints) : Operation;

    /// <summary>Interpolate scalar field at query point.</summary>
    public sealed record InterpolateScalarOp(Point3d Query, double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode Mode) : Operation;

    /// <summary>Interpolate vector field at query point.</summary>
    public sealed record InterpolateVectorOp(Point3d Query, Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode Mode) : Operation;

    /// <summary>Trace streamlines along vector field.</summary>
    public sealed record StreamlinesOp(Vector3d[] VectorField, Point3d[] GridPoints, Point3d[] Seeds, FieldSampling Sampling, BoundingBox Bounds, IntegrationScheme Scheme) : Operation;

    /// <summary>Extract isosurfaces from scalar field.</summary>
    public sealed record IsosurfacesOp(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, double[] Isovalues) : Operation;

    /// <summary>Detect and classify critical points.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    public sealed record CriticalPointsOp(double[] ScalarField, Vector3d[] GradientField, double[,][] Hessian, Point3d[] GridPoints, FieldSampling Sampling) : Operation;

    /// <summary>Compute field statistics.</summary>
    public sealed record StatisticsOp(double[] ScalarField, Point3d[] GridPoints) : Operation;

    /// <summary>Compute signed distance field: geometry → (grid points[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        FieldSampling? sampling,
        IGeometryContext context) where T : GeometryBase =>
        FieldsCore.ExecuteDistanceField(
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
            return FieldsCore.ExecuteGradient(
                operation: new GradientFieldOp(
                    ScalarField: distanceField.Distances,
                    GridPoints: distanceField.Grid,
                    Sampling: samplingValue,
                    Bounds: bounds));
        });

    /// <summary>Compute curl field: vector field → (grid points[], curl vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Curl)> CurlField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        FieldsCore.ExecuteCurl(
            operation: new CurlFieldOp(
                VectorField: vectorField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds));

    /// <summary>Compute divergence field: vector field → (grid points[], divergence scalars[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Divergence)> DivergenceField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        FieldsCore.ExecuteDivergence(
            operation: new DivergenceFieldOp(
                VectorField: vectorField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds));

    /// <summary>Compute Laplacian field: scalar field → (grid points[], Laplacian scalars[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Laplacian)> LaplacianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        FieldsCore.ExecuteLaplacian(
            operation: new LaplacianFieldOp(
                ScalarField: scalarField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds));

    /// <summary>Compute vector potential field: magnetic field B → (grid points[], vector potential A[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Potential)> VectorPotentialField(
        Vector3d[] magneticField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        FieldsCore.ExecuteVectorPotential(
            operation: new VectorPotentialFieldOp(
                MagneticField: magneticField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds));

    /// <summary>Interpolate scalar field at query point: (field, grid, query) → scalar value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds,
        InterpolationMode? mode = null) =>
        FieldsCore.ExecuteInterpolateScalar(
            operation: new InterpolateScalarOp(
                Query: query,
                ScalarField: scalarField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds,
                Mode: FieldsCore.ResolveInterpolationMode(bounds: bounds, mode: mode)));

    /// <summary>Interpolate vector field at query point: (field, grid, query) → vector value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds,
        InterpolationMode? mode = null) =>
        FieldsCore.ExecuteInterpolateVector(
            operation: new InterpolateVectorOp(
                Query: query,
                VectorField: vectorField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds,
                Mode: FieldsCore.ResolveInterpolationMode(bounds: bounds, mode: mode)));

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
        FieldsCore.ExecuteStreamlines(
            operation: new StreamlinesOp(
                VectorField: vectorField,
                GridPoints: gridPoints,
                Seeds: seeds,
                Sampling: sampling,
                Bounds: bounds,
                Scheme: scheme ?? new RungeKutta4Integration()),
            context: context);

    /// <summary>Extract isosurfaces from scalar field: (field, isovalues) → meshes[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        double[] isovalues) =>
        FieldsCore.ExecuteIsosurfaces(
            operation: new IsosurfacesOp(
                ScalarField: scalarField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Isovalues: isovalues));

    /// <summary>Compute Hessian field: scalar field → (grid points[], 3×3 hessian matrices[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    public static Result<(Point3d[] Grid, double[,][] Hessian)> HessianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSampling sampling,
        BoundingBox bounds) =>
        FieldsCore.ExecuteHessian(
            operation: new HessianFieldOp(
                ScalarField: scalarField,
                GridPoints: gridPoints,
                Sampling: sampling,
                Bounds: bounds));

    /// <summary>Compute directional derivative field: (gradient field, direction) → (grid points[], directional derivatives[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> DirectionalDerivativeField(
        Vector3d[] gradientField,
        Point3d[] gridPoints,
        Vector3d direction) =>
        FieldsCore.ExecuteDirectionalDerivative(
            operation: new DirectionalDerivativeFieldOp(
                GradientField: gradientField,
                GridPoints: gridPoints,
                Direction: direction));

    /// <summary>Compute vector field magnitude: vector field → (grid points[], magnitudes[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Magnitudes)> FieldMagnitude(
        Vector3d[] vectorField,
        Point3d[] gridPoints) =>
        FieldsCore.ExecuteFieldMagnitude(
            operation: new FieldMagnitudeOp(
                VectorField: vectorField,
                GridPoints: gridPoints));

    /// <summary>Normalize vector field: vector field → (grid points[], normalized vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Normalized)> NormalizeField(
        Vector3d[] vectorField,
        Point3d[] gridPoints) =>
        FieldsCore.ExecuteNormalizeField(
            operation: new NormalizeFieldOp(
                VectorField: vectorField,
                GridPoints: gridPoints));

    /// <summary>Scalar-vector field product: (scalar field, vector field, component) → (grid points[], product[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        VectorComponent component) =>
        FieldsCore.ExecuteScalarVectorProduct(
            operation: new ScalarVectorProductOp(
                ScalarField: scalarField,
                VectorField: vectorField,
                GridPoints: gridPoints,
                Component: component));

    /// <summary>Vector-vector dot product field: (vector field 1, vector field 2) → (grid points[], dot products[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] DotProduct)> VectorDotProduct(
        Vector3d[] vectorField1,
        Vector3d[] vectorField2,
        Point3d[] gridPoints) =>
        FieldsCore.ExecuteVectorDotProduct(
            operation: new VectorDotProductOp(
                VectorField1: vectorField1,
                VectorField2: vectorField2,
                GridPoints: gridPoints));

    /// <summary>Detect and classify critical points: (scalar field, gradient, hessian) → critical points[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    public static Result<CriticalPoint[]> CriticalPoints(
        double[] scalarField,
        Vector3d[] gradientField,
        double[,][] hessian,
        Point3d[] gridPoints,
        FieldSampling sampling) =>
        FieldsCore.ExecuteCriticalPoints(
            operation: new CriticalPointsOp(
                ScalarField: scalarField,
                GradientField: gradientField,
                Hessian: hessian,
                GridPoints: gridPoints,
                Sampling: sampling));

    /// <summary>Compute field statistics: scalar field → (min, max, mean, stddev, extreme locations).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FieldStatistics> ComputeStatistics(
        double[] scalarField,
        Point3d[] gridPoints) =>
        FieldsCore.ExecuteStatistics(
            operation: new StatisticsOp(
                ScalarField: scalarField,
                GridPoints: gridPoints));
}

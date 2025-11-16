using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations for computational field analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the primary API entry point")]
public static class Fields {
    /// <summary>Field specification for grid resolution, bounds, and step size.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct FieldSpec(
        int resolution = FieldsConfig.DefaultResolution,
        BoundingBox? bounds = null,
        double? stepSize = null) {
        /// <summary>Grid resolution (cube root of sample count).</summary>
        private static int NormalizeResolution(int resolution) =>
            resolution >= FieldsConfig.MinResolution
                ? resolution
                : FieldsConfig.DefaultResolution;
        public readonly int Resolution = RhinoMath.Clamp(
            NormalizeResolution(resolution),
            FieldsConfig.MinResolution,
            FieldsConfig.MaxResolution);
        /// <summary>Sample region bounding box (null uses geometry bounds).</summary>
        public readonly BoundingBox? Bounds = bounds;
        /// <summary>Integration/sampling step size.</summary>
        public readonly double StepSize = stepSize is { } value && value >= FieldsConfig.MinStepSize && value <= FieldsConfig.MaxStepSize
            ? value
            : FieldsConfig.DefaultStepSize;
    }

    /// <summary>Critical point classification result with location, type (minimum/maximum/saddle), value, and eigendecomposition.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct CriticalPoint(Point3d Location, byte Type, double Value, Vector3d[] Eigenvectors, double[] Eigenvalues);

    /// <summary>Field statistics including min, max, mean, standard deviation, and extreme value locations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FieldStatistics(double Min, double Max, double Mean, double StdDev, Point3d MinLocation, Point3d MaxLocation);

    /// <summary>Compute signed distance field: geometry → (grid points[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        FieldsCore.OperationRegistry.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out (Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, Core.Validation.V ValidationMode, int BufferSize, byte IntegrationMethod) config) switch {
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<(Point3d[], double[])>>>)(item =>
                    config.Execute(item, spec, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result])),
                config: new OperationConfig<T, (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = config.ValidationMode,
                    OperationName = $"Fields.DistanceField.{typeof(T).Name}",
                    EnableDiagnostics = false,
                }).Map(results => results[0]),
            false => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {typeof(T).Name}")),
        };

    /// <summary>Compute gradient field: geometry → (grid points[], gradient vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Gradients)> GradientField<T>(
        T geometry,
        FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        DistanceField(geometry: geometry, spec: spec, context: context).Bind(distanceField => {
            BoundingBox bounds = spec.Bounds ?? geometry.GetBoundingBox(accurate: true);
            Vector3d gridDelta = (bounds.Max - bounds.Min) / (spec.Resolution - 1);
            return FieldsCompute.ComputeGradient(
                distances: distanceField.Distances,
                grid: distanceField.Grid,
                resolution: spec.Resolution,
                gridDelta: gridDelta);
        });

    /// <summary>Compute curl field: vector field → (grid points[], curl vectors[]) where curl = ∇×F, row-major grid order, zero derivatives at boundaries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Curl)> CurlField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidCurlComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeCurl(vectorField: vectorField, grid: gridPoints, resolution: spec.Resolution, gridDelta: (bounds.Max - bounds.Min) / (spec.Resolution - 1)));

    /// <summary>Compute divergence field: vector field → (grid points[], divergence scalars[]) where divergence = ∇·F, row-major grid order, zero derivatives at boundaries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Divergence)> DivergenceField(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (vectorField, gridPoints))
            .Ensure(v => v.vectorField.Length == v.gridPoints.Length, error: E.Geometry.InvalidDivergenceComputation.WithContext("Vector field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeDivergence(vectorField: vectorField, grid: gridPoints, resolution: spec.Resolution, gridDelta: (bounds.Max - bounds.Min) / (spec.Resolution - 1)));

    /// <summary>Compute Laplacian field: scalar field → (grid points[], Laplacian scalars[]) where Laplacian = ∇²f, row-major grid order, zero second derivatives at boundaries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Laplacian)> LaplacianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds) =>
        ResultFactory.Create(value: (scalarField, gridPoints))
            .Ensure(v => v.scalarField.Length == v.gridPoints.Length, error: E.Geometry.InvalidLaplacianComputation.WithContext("Scalar field length must match grid points"))
            .Bind(_ => FieldsCompute.ComputeLaplacian(scalarField: scalarField, grid: gridPoints, resolution: spec.Resolution, gridDelta: (bounds.Max - bounds.Min) / (spec.Resolution - 1)));

    /// <summary>Compute vector potential field: magnetic field B → (grid points[], vector potential A[]) where B = ∇×A. Uses Coulomb gauge approximation via x-axis line integral (accurate only for fields with simple structure aligned with x-axis; general 3D magnetic fields require full volume integral).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Potential)> VectorPotentialField(
        Vector3d[] magneticField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds) =>
        (magneticField.Length == gridPoints.Length) switch {
            false => ResultFactory.Create<(Point3d[], Vector3d[])>(
                error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Magnetic field length must match grid points")),
            true => FieldsCompute.ComputeVectorPotential(
                vectorField: magneticField,
                grid: gridPoints,
                resolution: spec.Resolution,
                gridDelta: (bounds.Max - bounds.Min) / (spec.Resolution - 1)),
        };

    /// <summary>Interpolate scalar field at query point: (field, grid, query) → scalar value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds,
        byte interpolationMethod = FieldsConfig.InterpolationTrilinear) {
        return (scalarField.Length == gridPoints.Length,
            RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon)) switch {
                (false, _) => ResultFactory.Create<double>(
                    error: E.Geometry.InvalidFieldInterpolation.WithContext("Scalar field length must match grid points")),
                (true, true) => FieldsCompute.InterpolateScalar(
                    query: query,
                    scalarField: scalarField,
                    grid: gridPoints,
                    resolution: spec.Resolution,
                    bounds: bounds,
                    interpolationMethod: FieldsConfig.InterpolationNearest),
                (true, false) => FieldsCompute.InterpolateScalar(
                    query: query,
                    scalarField: scalarField,
                    grid: gridPoints,
                    resolution: spec.Resolution,
                    bounds: bounds,
                    interpolationMethod: interpolationMethod),
            };
    }

    /// <summary>Interpolate vector field at query point: (field, grid, query) → vector value.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds,
        byte interpolationMethod = FieldsConfig.InterpolationTrilinear) {
        return (vectorField.Length == gridPoints.Length,
            RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon)) switch {
                (false, _) => ResultFactory.Create<Vector3d>(
                    error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points")),
                (true, true) => FieldsCompute.InterpolateVector(
                    query: query,
                    vectorField: vectorField,
                    grid: gridPoints,
                    resolution: spec.Resolution,
                    bounds: bounds,
                    interpolationMethod: FieldsConfig.InterpolationNearest),
                (true, false) => FieldsCompute.InterpolateVector(
                    query: query,
                    vectorField: vectorField,
                    grid: gridPoints,
                    resolution: spec.Resolution,
                    bounds: bounds,
                    interpolationMethod: interpolationMethod),
            };
    }

    /// <summary>Trace streamlines along vector field: (field, seeds) → curves[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        FieldSpec spec,
        BoundingBox bounds,
        IGeometryContext context) =>
        (vectorField.Length == gridPoints.Length, seeds.Length > 0) switch {
            (false, _) => ResultFactory.Create<Curve[]>(
                error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<Curve[]>(
                error: E.Geometry.InvalidStreamlineSeeds),
            (true, true) => FieldsCompute.IntegrateStreamlines(
                vectorField: vectorField,
                gridPoints: gridPoints,
                seeds: seeds,
                stepSize: spec.StepSize,
                integrationMethod: FieldsConfig.IntegrationRK4,
                resolution: spec.Resolution,
                bounds: bounds,
                context: context),
        };

    /// <summary>Extract isosurfaces from scalar field: (field, isovalues) → meshes[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSpec spec,
        double[] isovalues) =>
        (scalarField.Length == gridPoints.Length, isovalues.Length > 0, isovalues.All(v => RhinoMath.IsValidDouble(v))) switch {
            (false, _, _) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required")),
            (_, _, false) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles")),
            (true, true, true) => FieldsCompute.ExtractIsosurfaces(
                scalarField: scalarField,
                gridPoints: gridPoints,
                resolution: spec.Resolution,
                isovalues: isovalues),
        };

    /// <summary>Compute Hessian field (second derivative matrix): scalar field → (grid points[], hessian matrices[3,3][]). Assumes uniform grid spacing derived from bounds and resolution; non-uniform grids will produce incorrect second derivatives.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    public static Result<(Point3d[] Grid, double[,][] Hessian)> HessianField(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSpec spec,
        BoundingBox bounds) =>
        (scalarField.Length == gridPoints.Length) switch {
            false => ResultFactory.Create<(Point3d[], double[,][])>(
                error: E.Geometry.InvalidHessianComputation.WithContext("Scalar field length must match grid points")),
            true => FieldsCompute.ComputeHessian(
                scalarField: scalarField,
                grid: gridPoints,
                resolution: spec.Resolution,
                gridDelta: (bounds.Max - bounds.Min) / (spec.Resolution - 1)),
        };

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

    /// <summary>Scalar-vector field product: (scalar field, vector field, component) → (grid points[], product[]) where component 0=X, 1=Y, 2=Z.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        int component) =>
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

    /// <summary>Detect and classify critical points in scalar field: (field, gradient, hessian) → critical points[] with type classification (minimum, maximum, saddle).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    public static Result<CriticalPoint[]> CriticalPoints(
        double[] scalarField,
        Vector3d[] gradientField,
        double[,][] hessian,
        Point3d[] gridPoints,
        FieldSpec spec) =>
        (scalarField.Length == gridPoints.Length, gradientField.Length == gridPoints.Length) switch {
            (false, _) => ResultFactory.Create<CriticalPoint[]>(
                error: E.Geometry.InvalidCriticalPointDetection.WithContext("Scalar field length must match grid points")),
            (_, false) => ResultFactory.Create<CriticalPoint[]>(
                error: E.Geometry.InvalidCriticalPointDetection.WithContext("Gradient field length must match grid points")),
            (true, true) => FieldsCompute.DetectCriticalPoints(
                scalarField: scalarField,
                gradientField: gradientField,
                hessian: hessian,
                grid: gridPoints,
                resolution: spec.Resolution),
        };

    /// <summary>Compute field statistics: scalar field → (min, max, mean, std dev, min location, max location).</summary>
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

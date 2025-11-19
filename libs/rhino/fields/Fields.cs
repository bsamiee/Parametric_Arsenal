using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations for computational field analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the primary API entry point")]
public static class Fields {
    /// <summary>Field sampling specification with grid resolution, bounds, and step size.</summary>
    public sealed record FieldSampling {
        /// <summary>Initializes field sampling with automatic clamping of resolution and step size.</summary>
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

    /// <summary>Base type for all field requests.</summary>
    public abstract record FieldRequest;

    /// <summary>Scalar field samples paired with their grid positions.</summary>
    public sealed record ScalarFieldSamples(Point3d[] Grid, double[] Values);

    /// <summary>Vector field samples paired with their grid positions.</summary>
    public sealed record VectorFieldSamples(Point3d[] Grid, Vector3d[] Vectors);

    /// <summary>Hessian tensor samples paired with their grid positions.</summary>
    public sealed record HessianFieldSamples(Point3d[] Grid, double[,][] Values);

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

    /// <summary>Distance field sampling request.</summary>
    public sealed record DistanceFieldRequest(GeometryBase Geometry, FieldSampling Sampling) : FieldRequest;

    /// <summary>Gradient field sampling request.</summary>
    public sealed record GradientFieldRequest(GeometryBase Geometry, FieldSampling Sampling) : FieldRequest;

    /// <summary>Vector field curl request.</summary>
    public sealed record CurlFieldRequest(VectorFieldSamples Field, FieldSampling Sampling, BoundingBox Bounds) : FieldRequest;

    /// <summary>Vector field divergence request.</summary>
    public sealed record DivergenceFieldRequest(VectorFieldSamples Field, FieldSampling Sampling, BoundingBox Bounds) : FieldRequest;

    /// <summary>Scalar Laplacian field request.</summary>
    public sealed record LaplacianFieldRequest(ScalarFieldSamples Field, FieldSampling Sampling, BoundingBox Bounds) : FieldRequest;

    /// <summary>Vector potential field request.</summary>
    public sealed record VectorPotentialFieldRequest(VectorFieldSamples Field, FieldSampling Sampling, BoundingBox Bounds) : FieldRequest;

    /// <summary>Hessian field request.</summary>
    public sealed record HessianFieldRequest(ScalarFieldSamples Field, FieldSampling Sampling, BoundingBox Bounds) : FieldRequest;

    /// <summary>Directional derivative request.</summary>
    public sealed record DirectionalDerivativeRequest(VectorFieldSamples Field, Vector3d Direction) : FieldRequest;

    /// <summary>Vector field magnitude request.</summary>
    public sealed record FieldMagnitudeRequest(VectorFieldSamples Field) : FieldRequest;

    /// <summary>Vector field normalization request.</summary>
    public sealed record NormalizeFieldRequest(VectorFieldSamples Field) : FieldRequest;

    /// <summary>Scalar-vector composition request.</summary>
    public sealed record ScalarVectorProductRequest(ScalarFieldSamples Scalars, VectorFieldSamples Vectors, VectorComponent Component) : FieldRequest;

    /// <summary>Vector-vector dot product request.</summary>
    public sealed record VectorDotProductRequest(VectorFieldSamples First, VectorFieldSamples Second) : FieldRequest;

    /// <summary>Critical point detection request.</summary>
    public sealed record CriticalPointsRequest(ScalarFieldSamples ScalarField, VectorFieldSamples GradientField, HessianFieldSamples Hessian, FieldSampling Sampling) : FieldRequest;

    /// <summary>Field statistics request.</summary>
    public sealed record FieldStatisticsRequest(ScalarFieldSamples Field) : FieldRequest;

    /// <summary>Scalar interpolation request with automatic trilinear fallback.</summary>
    public sealed record ScalarInterpolationRequest : FieldRequest {
        public ScalarInterpolationRequest(
            Point3d query,
            ScalarFieldSamples field,
            FieldSampling sampling,
            BoundingBox bounds,
            InterpolationMode? mode = null) {
            this.Query = query;
            this.Field = field;
            this.Sampling = sampling;
            this.Bounds = bounds;
            this.Mode = mode ?? new TrilinearInterpolationMode();
        }

        public Point3d Query { get; init; }

        public ScalarFieldSamples Field { get; init; }

        public FieldSampling Sampling { get; init; }

        public BoundingBox Bounds { get; init; }

        public InterpolationMode Mode { get; init; }
    }

    /// <summary>Vector interpolation request with automatic trilinear fallback.</summary>
    public sealed record VectorInterpolationRequest : FieldRequest {
        public VectorInterpolationRequest(
            Point3d query,
            VectorFieldSamples field,
            FieldSampling sampling,
            BoundingBox bounds,
            InterpolationMode? mode = null) {
            this.Query = query;
            this.Field = field;
            this.Sampling = sampling;
            this.Bounds = bounds;
            this.Mode = mode ?? new TrilinearInterpolationMode();
        }

        public Point3d Query { get; init; }

        public VectorFieldSamples Field { get; init; }

        public FieldSampling Sampling { get; init; }

        public BoundingBox Bounds { get; init; }

        public InterpolationMode Mode { get; init; }
    }

    /// <summary>Streamline integration request with RK4 default.</summary>
    public sealed record StreamlineRequest : FieldRequest {
        public StreamlineRequest(
            VectorFieldSamples field,
            Point3d[] seeds,
            FieldSampling sampling,
            BoundingBox bounds,
            IntegrationScheme? scheme = null) {
            this.Field = field;
            this.Seeds = seeds;
            this.Sampling = sampling;
            this.Bounds = bounds;
            this.Scheme = scheme ?? new RungeKutta4IntegrationScheme();
        }

        public VectorFieldSamples Field { get; init; }

        public Point3d[] Seeds { get; init; }

        public FieldSampling Sampling { get; init; }

        public BoundingBox Bounds { get; init; }

        public IntegrationScheme Scheme { get; init; }
    }

    /// <summary>Isosurface extraction request.</summary>
    public sealed record IsosurfaceRequest(ScalarFieldSamples Field, FieldSampling Sampling, double[] Isovalues) : FieldRequest;

    /// <summary>Compute signed distance field: geometry → scalar samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DistanceField(
        DistanceFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.DistanceField(request: request, context: context);

    /// <summary>Compute gradient field: geometry → vector samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> GradientField(
        GradientFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.GradientField(request: request, context: context);

    /// <summary>Compute curl of vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> CurlField(
        CurlFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.CurlField(request: request, context: context);

    /// <summary>Compute divergence of vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DivergenceField(
        DivergenceFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.DivergenceField(request: request, context: context);

    /// <summary>Compute Laplacian of scalar field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> LaplacianField(
        LaplacianFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.LaplacianField(request: request, context: context);

    /// <summary>Compute vector potential from vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> VectorPotentialField(
        VectorPotentialFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.VectorPotentialField(request: request, context: context);

    /// <summary>Compute scalar interpolation at query.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> InterpolateScalar(
        ScalarInterpolationRequest request,
        IGeometryContext context) =>
        FieldsCore.InterpolateScalar(request: request, context: context);

    /// <summary>Compute vector interpolation at query.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Vector3d> InterpolateVector(
        VectorInterpolationRequest request,
        IGeometryContext context) =>
        FieldsCore.InterpolateVector(request: request, context: context);

    /// <summary>Trace streamlines along vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(
        StreamlineRequest request,
        IGeometryContext context) =>
        FieldsCore.Streamlines(request: request, context: context);

    /// <summary>Extract isosurfaces from scalar field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(
        IsosurfaceRequest request,
        IGeometryContext context) =>
        FieldsCore.Isosurfaces(request: request, context: context);

    /// <summary>Compute Hessian tensor field from scalar samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<HessianFieldSamples> HessianField(
        HessianFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.HessianField(request: request, context: context);

    /// <summary>Compute directional derivative along vector field direction.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DirectionalDerivativeField(
        DirectionalDerivativeRequest request,
        IGeometryContext context) =>
        FieldsCore.DirectionalDerivativeField(request: request, context: context);

    /// <summary>Compute vector field magnitude.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> FieldMagnitude(
        FieldMagnitudeRequest request,
        IGeometryContext context) =>
        FieldsCore.FieldMagnitude(request: request, context: context);

    /// <summary>Normalize vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> NormalizeField(
        NormalizeFieldRequest request,
        IGeometryContext context) =>
        FieldsCore.NormalizeField(request: request, context: context);

    /// <summary>Compute scalar-vector product field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> ScalarVectorProduct(
        ScalarVectorProductRequest request,
        IGeometryContext context) =>
        FieldsCore.ScalarVectorProduct(request: request, context: context);

    /// <summary>Compute vector-vector dot product field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> VectorDotProduct(
        VectorDotProductRequest request,
        IGeometryContext context) =>
        FieldsCore.VectorDotProduct(request: request, context: context);

    /// <summary>Detect and classify critical points.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CriticalPoint[]> CriticalPoints(
        CriticalPointsRequest request,
        IGeometryContext context) =>
        FieldsCore.CriticalPoints(request: request, context: context);

    /// <summary>Compute scalar field statistics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FieldStatistics> ComputeStatistics(
        FieldStatisticsRequest request,
        IGeometryContext context) =>
        FieldsCore.ComputeStatistics(request: request, context: context);
}

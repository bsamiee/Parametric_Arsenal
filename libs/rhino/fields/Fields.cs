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

    /// <summary>Result structure for scalar field samples.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ScalarFieldSamples(Point3d[] Grid, double[] Values);

    /// <summary>Result structure for vector field samples.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct VectorFieldSamples(Point3d[] Grid, Vector3d[] Vectors);

    /// <summary>Result structure for Hessian tensor samples.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct HessianFieldSamples(Point3d[] Grid, double[,][] Hessian);

    /// <summary>Critical point with location, classification (min/max/saddle), scalar value, and eigendecomposition.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct CriticalPoint(Point3d Location, CriticalPointKind Kind, double Value, Vector3d[] Eigenvectors, double[] Eigenvalues);

    /// <summary>Field statistics including min, max, mean, standard deviation, and extreme value locations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FieldStatistics(double Min, double Max, double Mean, double StdDev, Point3d MinLocation, Point3d MaxLocation);

    /// <summary>Base type for all field operations.</summary>
    public abstract record FieldOperation;

    public sealed record DistanceFieldRequest(GeometryBase Geometry, FieldSampling Sampling) : FieldOperation {
        public DistanceFieldRequest(GeometryBase Geometry) : this(Geometry, FieldSampling.Default) { }
    }

    public sealed record GradientFieldRequest(GeometryBase Geometry, FieldSampling Sampling) : FieldOperation {
        public GradientFieldRequest(GeometryBase Geometry) : this(Geometry, FieldSampling.Default) { }
    }

    public sealed record CurlFieldRequest(Vector3d[] VectorField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    public sealed record DivergenceFieldRequest(Vector3d[] VectorField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    public sealed record LaplacianFieldRequest(double[] ScalarField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    public sealed record VectorPotentialFieldRequest(Vector3d[] VectorField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    public sealed record ScalarInterpolationRequest(
        Point3d Query,
        double[] ScalarField,
        Point3d[] Grid,
        FieldSampling Sampling,
        BoundingBox Bounds,
        InterpolationMode Mode) : FieldOperation {
        public ScalarInterpolationRequest(Point3d Query, double[] ScalarField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds)
            : this(Query, ScalarField, Grid, Sampling, Bounds, new TrilinearInterpolationMode()) { }
    }

    public sealed record VectorInterpolationRequest(
        Point3d Query,
        Vector3d[] VectorField,
        Point3d[] Grid,
        FieldSampling Sampling,
        BoundingBox Bounds,
        InterpolationMode Mode) : FieldOperation {
        public VectorInterpolationRequest(Point3d Query, Vector3d[] VectorField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds)
            : this(Query, VectorField, Grid, Sampling, Bounds, new TrilinearInterpolationMode()) { }
    }

    public sealed record StreamlineRequest(
        Vector3d[] VectorField,
        Point3d[] Grid,
        Point3d[] Seeds,
        FieldSampling Sampling,
        BoundingBox Bounds,
        IntegrationScheme Scheme) : FieldOperation {
        public StreamlineRequest(Vector3d[] VectorField, Point3d[] Grid, Point3d[] Seeds, FieldSampling Sampling, BoundingBox Bounds)
            : this(VectorField, Grid, Seeds, Sampling, Bounds, new RungeKutta4IntegrationScheme()) { }
    }

    public sealed record IsosurfaceRequest(double[] ScalarField, Point3d[] Grid, FieldSampling Sampling, double[] Isovalues) : FieldOperation;

    public sealed record HessianFieldRequest(double[] ScalarField, Point3d[] Grid, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    public sealed record DirectionalDerivativeRequest(Vector3d[] GradientField, Point3d[] Grid, Vector3d Direction) : FieldOperation;

    public sealed record FieldMagnitudeRequest(Vector3d[] VectorField, Point3d[] Grid) : FieldOperation;

    public sealed record NormalizeFieldRequest(Vector3d[] VectorField, Point3d[] Grid) : FieldOperation;

    public sealed record ScalarVectorProductRequest(double[] ScalarField, Vector3d[] VectorField, Point3d[] Grid, VectorComponent Component) : FieldOperation;

    public sealed record VectorDotProductRequest(Vector3d[] FirstField, Vector3d[] SecondField, Point3d[] Grid) : FieldOperation;

    public sealed record CriticalPointsRequest(double[] ScalarField, Vector3d[] GradientField, double[,][] Hessian, Point3d[] Grid, FieldSampling Sampling) : FieldOperation;

    public sealed record FieldStatisticsRequest(double[] ScalarField, Point3d[] Grid) : FieldOperation;

    /// <summary>Compute signed distance field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DistanceField(DistanceFieldRequest request, IGeometryContext context) =>
        FieldsCore.DistanceField(request: request, context: context);

    /// <summary>Compute gradient field samples from geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> GradientField(GradientFieldRequest request, IGeometryContext context) =>
        FieldsCore.GradientField(request: request, context: context);

    /// <summary>Compute curl of a vector field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> CurlField(CurlFieldRequest request, IGeometryContext context) =>
        FieldsCore.CurlField(request: request, context: context);

    /// <summary>Compute divergence of a vector field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DivergenceField(DivergenceFieldRequest request, IGeometryContext context) =>
        FieldsCore.DivergenceField(request: request, context: context);

    /// <summary>Compute Laplacian of a scalar field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> LaplacianField(LaplacianFieldRequest request, IGeometryContext context) =>
        FieldsCore.LaplacianField(request: request, context: context);

    /// <summary>Compute vector potential satisfying ∇²A = -∇×B.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> VectorPotentialField(VectorPotentialFieldRequest request, IGeometryContext context) =>
        FieldsCore.VectorPotentialField(request: request, context: context);

    /// <summary>Interpolate scalar field at query point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> InterpolateScalar(ScalarInterpolationRequest request, IGeometryContext context) =>
        FieldsCore.InterpolateScalar(request: request, context: context);

    /// <summary>Interpolate vector field at query point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Vector3d> InterpolateVector(VectorInterpolationRequest request, IGeometryContext context) =>
        FieldsCore.InterpolateVector(request: request, context: context);

    /// <summary>Trace streamlines along vector field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(StreamlineRequest request, IGeometryContext context) =>
        FieldsCore.Streamlines(request: request, context: context);

    /// <summary>Extract isosurfaces from scalar field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(IsosurfaceRequest request, IGeometryContext context) =>
        FieldsCore.Isosurfaces(request: request, context: context);

    /// <summary>Compute Hessian tensor field from scalar field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<HessianFieldSamples> HessianField(HessianFieldRequest request, IGeometryContext context) =>
        FieldsCore.HessianField(request: request, context: context);

    /// <summary>Compute directional derivative field along vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> DirectionalDerivativeField(DirectionalDerivativeRequest request, IGeometryContext context) =>
        FieldsCore.DirectionalDerivativeField(request: request, context: context);

    /// <summary>Compute vector field magnitude.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> FieldMagnitude(FieldMagnitudeRequest request, IGeometryContext context) =>
        FieldsCore.FieldMagnitude(request: request, context: context);

    /// <summary>Normalize vector field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<VectorFieldSamples> NormalizeField(NormalizeFieldRequest request, IGeometryContext context) =>
        FieldsCore.NormalizeField(request: request, context: context);

    /// <summary>Compute scalar-vector product field for a selected component.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> ScalarVectorProduct(ScalarVectorProductRequest request, IGeometryContext context) =>
        FieldsCore.ScalarVectorProduct(request: request, context: context);

    /// <summary>Compute dot product between two vector fields.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ScalarFieldSamples> VectorDotProduct(VectorDotProductRequest request, IGeometryContext context) =>
        FieldsCore.VectorDotProduct(request: request, context: context);

    /// <summary>Detect and classify critical points.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CriticalPoint[]> CriticalPoints(CriticalPointsRequest request, IGeometryContext context) =>
        FieldsCore.CriticalPoints(request: request, context: context);

    /// <summary>Compute descriptive statistics for scalar field samples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FieldStatistics> ComputeStatistics(FieldStatisticsRequest request, IGeometryContext context) =>
        FieldsCore.ComputeStatistics(request: request, context: context);
}

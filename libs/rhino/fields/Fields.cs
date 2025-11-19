using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations modeled as algebraic requests.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the public API entry point")]
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

    /// <summary>Critical point with location, classification (min/max/saddle), scalar value, and eigendecomposition.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct CriticalPoint(Point3d Location, CriticalPointKind Kind, double Value, Vector3d[] Eigenvectors, double[] Eigenvalues);

    /// <summary>Field statistics including min, max, mean, standard deviation, and extreme value locations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FieldStatistics(double Min, double Max, double Mean, double StdDev, Point3d MinLocation, Point3d MaxLocation);

    /// <summary>Scalar field samples with grid data and sampling metadata.</summary>
    public sealed record ScalarFieldSamples(Point3d[] Grid, double[] Values, FieldSampling Sampling, BoundingBox Bounds);

    /// <summary>Vector field samples with grid data and sampling metadata.</summary>
    public sealed record VectorFieldSamples(Point3d[] Grid, Vector3d[] Vectors, FieldSampling Sampling, BoundingBox Bounds);

    /// <summary>Hessian field samples storing tensors per grid point.</summary>
    public sealed record HessianFieldSamples(Point3d[] Grid, double[,][] Hessians, FieldSampling Sampling, BoundingBox Bounds);

    /// <summary>Base type for algebraic field operations.</summary>
    public abstract record FieldOperation<TResult>;

    /// <summary>Distance field operation from geometry.</summary>
    public sealed record DistanceFieldOperation<TGeometry>(TGeometry Geometry, FieldSampling? Sampling = null) : FieldOperation<ScalarFieldSamples> where TGeometry : GeometryBase;

    /// <summary>Gradient computation from scalar field.</summary>
    public sealed record GradientFieldOperation(ScalarFieldSamples Field) : FieldOperation<VectorFieldSamples>;

    /// <summary>Curl computation from vector field.</summary>
    public sealed record CurlFieldOperation(VectorFieldSamples Field) : FieldOperation<VectorFieldSamples>;

    /// <summary>Divergence computation from vector field.</summary>
    public sealed record DivergenceFieldOperation(VectorFieldSamples Field) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Laplacian computation from scalar field.</summary>
    public sealed record LaplacianFieldOperation(ScalarFieldSamples Field) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Vector potential computation from vector field.</summary>
    public sealed record VectorPotentialFieldOperation(VectorFieldSamples Field) : FieldOperation<VectorFieldSamples>;

    /// <summary>Scalar interpolation query.</summary>
    public sealed record ScalarInterpolationOperation(ScalarFieldSamples Field, Point3d Query, InterpolationMode? Mode = null) : FieldOperation<double>;

    /// <summary>Vector interpolation query.</summary>
    public sealed record VectorInterpolationOperation(VectorFieldSamples Field, Point3d Query, InterpolationMode? Mode = null) : FieldOperation<Vector3d>;

    /// <summary>Streamline integration request.</summary>
    public sealed record StreamlineIntegrationOperation(VectorFieldSamples Field, Point3d[] Seeds, IntegrationScheme? Scheme = null) : FieldOperation<Curve[]>;

    /// <summary>Isosurface extraction request.</summary>
    public sealed record IsosurfaceExtractionOperation(ScalarFieldSamples Field, double[] Isovalues) : FieldOperation<Mesh[]>;

    /// <summary>Hessian computation from scalar field.</summary>
    public sealed record HessianFieldOperation(ScalarFieldSamples Field) : FieldOperation<HessianFieldSamples>;

    /// <summary>Directional derivative computation from gradient field.</summary>
    public sealed record DirectionalDerivativeOperation(VectorFieldSamples Field, Vector3d Direction) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Vector field magnitude computation.</summary>
    public sealed record FieldMagnitudeOperation(VectorFieldSamples Field) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Vector field normalization.</summary>
    public sealed record NormalizeFieldOperation(VectorFieldSamples Field) : FieldOperation<VectorFieldSamples>;

    /// <summary>Scalar-vector field product.</summary>
    public sealed record ScalarVectorProductOperation(ScalarFieldSamples Scalars, VectorFieldSamples Vectors, VectorComponent Component) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Vector-vector dot product field.</summary>
    public sealed record VectorDotProductOperation(VectorFieldSamples First, VectorFieldSamples Second) : FieldOperation<ScalarFieldSamples>;

    /// <summary>Critical point detection request.</summary>
    public sealed record CriticalPointDetectionOperation(ScalarFieldSamples Scalars, VectorFieldSamples Gradients, HessianFieldSamples Hessian) : FieldOperation<CriticalPoint[]>;

    /// <summary>Field statistics computation.</summary>
    public sealed record FieldStatisticsOperation(ScalarFieldSamples Field) : FieldOperation<FieldStatistics>;

    /// <summary>Execute a field operation using unified orchestration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult> Execute<TResult>(FieldOperation<TResult> operation, IGeometryContext context) =>
        FieldsCore.Execute(operation: operation, context: context);
}

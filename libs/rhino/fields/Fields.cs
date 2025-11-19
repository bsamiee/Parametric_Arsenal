using System.Diagnostics;
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

    /// <summary>Critical point with location, classification (min/max/saddle), scalar value, and eigendecomposition.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct CriticalPoint(Point3d Location, CriticalPointKind Kind, double Value, Vector3d[] Eigenvectors, double[] Eigenvalues);

    /// <summary>Field statistics including min, max, mean, standard deviation, and extreme value locations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FieldStatistics(double Min, double Max, double Mean, double StdDev, Point3d MinLocation, Point3d MaxLocation);

    /// <summary>Scalar field samples with grid points and scalar values.</summary>
    [DebuggerDisplay("Grid={Grid.Length}, Values={Values.Length}")]
    public sealed record ScalarFieldSamples(Point3d[] Grid, double[] Values);

    /// <summary>Vector field samples with grid points and vector values.</summary>
    [DebuggerDisplay("Grid={Grid.Length}, Vectors={Vectors.Length}")]
    public sealed record VectorFieldSamples(Point3d[] Grid, Vector3d[] Vectors);

    /// <summary>Hessian field samples with grid points and 3×3 Hessian matrices.</summary>
    [DebuggerDisplay("Grid={Grid.Length}")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    public sealed record HessianFieldSamples(Point3d[] Grid, double[,][] Hessian);

    /// <summary>Base type for all field operation requests.</summary>
    public abstract record FieldOperation;

    /// <summary>Request signed distance field computation for geometry.</summary>
    /// <param name="Geometry">Input geometry (Mesh, Brep, Curve, or Surface).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    public sealed record DistanceFieldRequest(GeometryBase Geometry, FieldSampling? Sampling) : FieldOperation;

    /// <summary>Request gradient field computation from distance field.</summary>
    /// <param name="Geometry">Input geometry (Mesh, Brep, Curve, or Surface).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    public sealed record GradientFieldRequest(GeometryBase Geometry, FieldSampling? Sampling) : FieldOperation;

    /// <summary>Request curl field computation: ∇×F.</summary>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    public sealed record CurlFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    /// <summary>Request divergence field computation: ∇·F.</summary>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    public sealed record DivergenceFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    /// <summary>Request Laplacian field computation: ∇²f.</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GridPoints">Grid sample points (must match scalar field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    public sealed record LaplacianFieldRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    /// <summary>Request vector potential field computation solving ∇²A = -∇×B.</summary>
    /// <param name="MagneticField">Input magnetic field B (vector field).</param>
    /// <param name="GridPoints">Grid sample points (must match field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    public sealed record VectorPotentialFieldRequest(Vector3d[] MagneticField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    /// <summary>Request scalar field interpolation at query point.</summary>
    /// <param name="Query">Query point for interpolation.</param>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GridPoints">Grid sample points (must match scalar field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    /// <param name="Mode">Interpolation mode (nearest or trilinear).</param>
    public sealed record InterpolateScalarRequest(Point3d Query, double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode? Mode) : FieldOperation;

    /// <summary>Request vector field interpolation at query point.</summary>
    /// <param name="Query">Query point for interpolation.</param>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    /// <param name="Mode">Interpolation mode (nearest or trilinear).</param>
    public sealed record InterpolateVectorRequest(Point3d Query, Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode? Mode) : FieldOperation;

    /// <summary>Request streamline tracing along vector field from seed points.</summary>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    /// <param name="Seeds">Seed points for streamline integration.</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    /// <param name="Scheme">Integration scheme (Euler, Midpoint, or RK4).</param>
    public sealed record StreamlinesRequest(Vector3d[] VectorField, Point3d[] GridPoints, Point3d[] Seeds, FieldSampling Sampling, BoundingBox Bounds, IntegrationScheme? Scheme) : FieldOperation;

    /// <summary>Request isosurface extraction from scalar field using marching cubes.</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GridPoints">Grid sample points (must match scalar field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Isovalues">Isovalue thresholds for surface extraction.</param>
    public sealed record IsosurfacesRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, double[] Isovalues) : FieldOperation;

    /// <summary>Request Hessian matrix field computation (second derivatives).</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GridPoints">Grid sample points (must match scalar field length).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    /// <param name="Bounds">Bounding box defining field extent.</param>
    public sealed record HessianFieldRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds) : FieldOperation;

    /// <summary>Request directional derivative field computation: ∇f·d.</summary>
    /// <param name="GradientField">Input gradient field values.</param>
    /// <param name="GridPoints">Grid sample points (must match gradient field length).</param>
    /// <param name="Direction">Direction vector for derivative computation.</param>
    public sealed record DirectionalDerivativeFieldRequest(Vector3d[] GradientField, Point3d[] GridPoints, Vector3d Direction) : FieldOperation;

    /// <summary>Request vector field magnitude computation: |F|.</summary>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    public sealed record FieldMagnitudeRequest(Vector3d[] VectorField, Point3d[] GridPoints) : FieldOperation;

    /// <summary>Request vector field normalization: F/|F|.</summary>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match vector field length).</param>
    public sealed record NormalizeFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints) : FieldOperation;

    /// <summary>Request scalar-vector product field: s·F_component.</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="VectorField">Input vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match both field lengths).</param>
    /// <param name="Component">Vector component to extract (X, Y, or Z).</param>
    public sealed record ScalarVectorProductRequest(double[] ScalarField, Vector3d[] VectorField, Point3d[] GridPoints, VectorComponent Component) : FieldOperation;

    /// <summary>Request vector-vector dot product field: F1·F2.</summary>
    /// <param name="VectorField1">First vector field values.</param>
    /// <param name="VectorField2">Second vector field values.</param>
    /// <param name="GridPoints">Grid sample points (must match both field lengths).</param>
    public sealed record VectorDotProductRequest(Vector3d[] VectorField1, Vector3d[] VectorField2, Point3d[] GridPoints) : FieldOperation;

    /// <summary>Request critical point detection and classification (min/max/saddle).</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GradientField">Gradient field values (∇f).</param>
    /// <param name="Hessian">Hessian matrix field (3×3 second derivatives).</param>
    /// <param name="GridPoints">Grid sample points (must match field lengths).</param>
    /// <param name="Sampling">Field sampling configuration (resolution, bounds, step size).</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    public sealed record CriticalPointsRequest(double[] ScalarField, Vector3d[] GradientField, double[,][] Hessian, Point3d[] GridPoints, FieldSampling Sampling) : FieldOperation;

    /// <summary>Request field statistics computation (min, max, mean, stddev).</summary>
    /// <param name="ScalarField">Input scalar field values.</param>
    /// <param name="GridPoints">Grid sample points (must match scalar field length).</param>
    public sealed record ComputeStatisticsRequest(double[] ScalarField, Point3d[] GridPoints) : FieldOperation;

    /// <summary>Execute field operation request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult> Execute<TResult>(FieldOperation operation, IGeometryContext context) =>
        FieldsCore.Execute<TResult>(operation: operation, context: context);
}

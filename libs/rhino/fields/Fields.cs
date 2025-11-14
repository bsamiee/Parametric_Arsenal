using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
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
        public readonly int Resolution = resolution >= FieldsConfig.MinResolution
            ? resolution
            : FieldsConfig.DefaultResolution;
        /// <summary>Sample region bounding box (null uses geometry bounds).</summary>
        public readonly BoundingBox? Bounds = bounds;
        /// <summary>Integration/sampling step size.</summary>
        public readonly double StepSize =
            stepSize.HasValue && stepSize.Value >= FieldsConfig.MinStepSize && stepSize.Value <= FieldsConfig.MaxStepSize
                ? stepSize.Value
                : FieldsConfig.DefaultStepSize;

    /// <summary>Compute signed distance field: geometry → (grid points[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        FieldsCore.OperationRegistry.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out (Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> execute, byte integrationMethod) config) switch {
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<(Point3d[], double[])>>>)(item =>
                    config.execute(item, spec, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result])),
                config: new OperationConfig<T, (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = FieldsConfig.ValidationModes.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out V mode) ? mode : V.Standard,
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
        DistanceField(geometry: geometry, spec: spec, context: context).Bind(distanceField =>
            FieldsCompute.ComputeGradient(
                distances: distanceField.Distances,
                grid: distanceField.Grid,
                resolution: spec.Resolution));

    /// <summary>Trace streamlines along vector field: (field, seeds) → curves[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        FieldSpec spec,
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
}

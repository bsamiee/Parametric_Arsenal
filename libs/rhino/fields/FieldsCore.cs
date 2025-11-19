using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Frozen;
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
    private static readonly FrozenDictionary<Type, FieldsConfig.DistanceFieldMetadata> DistanceFields =
        FieldsConfig.DistanceFields;

    [Pure]
    internal static Result<TResult> Execute<TResult>(Fields.FieldOperation<TResult> operation, IGeometryContext context) =>
        operation switch {
            null => ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext("Operation cannot be null")),
            Fields.DistanceFieldOperation<Mesh> mesh => ExecuteDistance(mesh, context),
            Fields.DistanceFieldOperation<Brep> brep => ExecuteDistance(brep, context),
            Fields.DistanceFieldOperation<Curve> curve => ExecuteDistance(curve, context),
            Fields.DistanceFieldOperation<Surface> surface => ExecuteDistance(surface, context),
            Fields.GradientFieldOperation gradient => Apply(
                operation: gradient,
                context: context,
                executor: static (op, _) => ExecuteGradient(op)),
            Fields.CurlFieldOperation curl => Apply(
                operation: curl,
                context: context,
                executor: static (op, _) => ExecuteCurl(op)),
            Fields.DivergenceFieldOperation divergence => Apply(
                operation: divergence,
                context: context,
                executor: static (op, _) => ExecuteDivergence(op)),
            Fields.LaplacianFieldOperation laplacian => Apply(
                operation: laplacian,
                context: context,
                executor: static (op, _) => ExecuteLaplacian(op)),
            Fields.VectorPotentialFieldOperation potential => Apply(
                operation: potential,
                context: context,
                executor: static (op, _) => ExecuteVectorPotential(op)),
            Fields.ScalarInterpolationOperation interpolateScalar => Apply(
                operation: interpolateScalar,
                context: context,
                executor: static (op, _) => ExecuteScalarInterpolation(op)),
            Fields.VectorInterpolationOperation interpolateVector => Apply(
                operation: interpolateVector,
                context: context,
                executor: static (op, _) => ExecuteVectorInterpolation(op)),
            Fields.StreamlineIntegrationOperation streamlines => Apply(
                operation: streamlines,
                context: context,
                executor: static (op, ctx) => ExecuteStreamlines(op, ctx)),
            Fields.IsosurfaceExtractionOperation isosurfaces => Apply(
                operation: isosurfaces,
                context: context,
                executor: static (op, _) => ExecuteIsosurfaces(op)),
            Fields.HessianFieldOperation hessian => Apply(
                operation: hessian,
                context: context,
                executor: static (op, _) => ExecuteHessian(op)),
            Fields.DirectionalDerivativeOperation directional => Apply(
                operation: directional,
                context: context,
                executor: static (op, _) => ExecuteDirectionalDerivative(op)),
            Fields.FieldMagnitudeOperation magnitude => Apply(
                operation: magnitude,
                context: context,
                executor: static (op, _) => ExecuteFieldMagnitude(op)),
            Fields.NormalizeFieldOperation normalize => Apply(
                operation: normalize,
                context: context,
                executor: static (op, _) => ExecuteNormalizeField(op)),
            Fields.ScalarVectorProductOperation scalarVector => Apply(
                operation: scalarVector,
                context: context,
                executor: static (op, _) => ExecuteScalarVectorProduct(op)),
            Fields.VectorDotProductOperation vectorDot => Apply(
                operation: vectorDot,
                context: context,
                executor: static (op, _) => ExecuteVectorDotProduct(op)),
            Fields.CriticalPointDetectionOperation criticalPoints => Apply(
                operation: criticalPoints,
                context: context,
                executor: static (op, _) => ExecuteCriticalPoints(op)),
            Fields.FieldStatisticsOperation statistics => Apply(
                operation: statistics,
                context: context,
                executor: static (op, _) => ExecuteStatistics(op)),
            _ => ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unsupported field operation: {operation.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<TResult> Apply<TResult, TOperation>(
        TOperation operation,
        IGeometryContext context,
        Func<TOperation, IGeometryContext, Result<TResult>> executor) where TOperation : Fields.FieldOperation<TResult> =>
        FieldsConfig.Operations.TryGetValue(typeof(TOperation), out FieldsConfig.OperationMetadata? metadata)
            ? UnifiedOperation.Apply(
                input: operation,
                operation: (Func<TOperation, Result<IReadOnlyList<TResult>>>)(item =>
                    executor(item, context).Map(static result => (IReadOnlyList<TResult>)[result,])),
                config: new OperationConfig<TOperation, TResult> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0])
            : ResultFactory.Create<TResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown field operation: {typeof(TOperation).Name}"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Fields.ScalarFieldSamples> ExecuteDistance<TGeometry>(
        Fields.DistanceFieldOperation<TGeometry> operation,
        IGeometryContext context) where TGeometry : GeometryBase =>
        operation.Geometry is null
            ? ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceFields.TryGetValue(typeof(TGeometry), out FieldsConfig.DistanceFieldMetadata? metadata)
                ? ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {typeof(TGeometry).Name}"))
                : UnifiedOperation.Apply(
                    input: operation.Geometry,
                    operation: (Func<TGeometry, Result<IReadOnlyList<Fields.ScalarFieldSamples>>>)(geometry =>
                        ComputeDistanceSamples(
                            geometry: geometry,
                            sampling: operation.Sampling ?? Fields.FieldSampling.Default,
                            metadata: metadata,
                            context: context).Map(static result => (IReadOnlyList<Fields.ScalarFieldSamples>)[result,])),
                    config: new OperationConfig<TGeometry, Fields.ScalarFieldSamples> {
                        Context = context,
                        ValidationMode = metadata.ValidationMode,
                        OperationName = metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(static results => results[0]);

    private static Result<Fields.ScalarFieldSamples> ComputeDistanceSamples<TGeometry>(
        TGeometry geometry,
        Fields.FieldSampling sampling,
        FieldsConfig.DistanceFieldMetadata metadata,
        IGeometryContext context) where TGeometry : GeometryBase {
        BoundingBox bounds = sampling.Bounds ?? geometry.GetBoundingBox(accurate: true);
        return bounds.IsValid switch {
            false => ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.InvalidFieldBounds),
            true => ExecuteDistanceField(
                geometry: geometry,
                sampling: sampling,
                bounds: bounds,
                bufferSize: metadata.BufferSize,
                context: context),
        };
    }

    private static Result<Fields.ScalarFieldSamples> ExecuteDistanceField<TGeometry>(
        TGeometry geometry,
        Fields.FieldSampling sampling,
        BoundingBox bounds,
        int bufferSize,
        IGeometryContext context) where TGeometry : GeometryBase {
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
                        grid[gridIndex++] = new Point3d(
                            bounds.Min.X + (i * delta.X),
                            bounds.Min.Y + (j * delta.Y),
                            bounds.Min.Z + (k * delta.Z));
                    }
                }
            }

            for (int i = 0; i < totalSamples; i++) {
                Point3d closest = geometry switch {
                    Mesh mesh => mesh.ClosestPoint(grid[i]),
                    Brep brep => brep.ClosestPoint(grid[i]),
                    Curve curve => curve.ClosestPoint(grid[i], out double t) ? curve.PointAt(t) : grid[i],
                    Surface surface => surface.ClosestPoint(grid[i], out double u, out double v) ? surface.PointAt(u, v) : grid[i],
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
            return ResultFactory.Create(value: new Fields.ScalarFieldSamples(
                Grid: finalGrid,
                Values: finalDistances,
                Sampling: sampling,
                Bounds: bounds));
        } finally {
            ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
            ArrayPool<double>.Shared.Return(distances, clearArray: true);
        }
    }

    private static Result<Fields.VectorFieldSamples> ExecuteGradient(Fields.GradientFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeGradient(
                distances: operation.Field.Values,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.VectorFieldSamples(
                Grid: result.Grid,
                Vectors: result.Gradients,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.VectorFieldSamples> ExecuteCurl(Fields.CurlFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeCurl(
                vectorField: operation.Field.Vectors,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.VectorFieldSamples(
                Grid: result.Grid,
                Vectors: result.Curl,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.ScalarFieldSamples> ExecuteDivergence(Fields.DivergenceFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeDivergence(
                vectorField: operation.Field.Vectors,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.ScalarFieldSamples(
                Grid: result.Grid,
                Values: result.Divergence,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.ScalarFieldSamples> ExecuteLaplacian(Fields.LaplacianFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeLaplacian(
                scalarField: operation.Field.Values,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.ScalarFieldSamples(
                Grid: result.Grid,
                Values: result.Laplacian,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.VectorFieldSamples> ExecuteVectorPotential(Fields.VectorPotentialFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeVectorPotential(
                vectorField: operation.Field.Vectors,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.VectorFieldSamples(
                Grid: result.Grid,
                Vectors: result.Potential,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<double> ExecuteScalarInterpolation(Fields.ScalarInterpolationOperation operation) =>
        (operation.Field.Values.Length == operation.Field.Grid.Length) switch {
            false => ResultFactory.Create<double>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Scalar field length must match grid points")),
            true => FieldsCompute.InterpolateScalar(
                query: operation.Query,
                scalarField: operation.Field.Values,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                bounds: operation.Field.Bounds,
                mode: ResolveInterpolationMode(operation.Mode, operation.Field.Bounds)),
        };

    private static Result<Vector3d> ExecuteVectorInterpolation(Fields.VectorInterpolationOperation operation) =>
        (operation.Field.Vectors.Length == operation.Field.Grid.Length) switch {
            false => ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Vector field length must match grid points")),
            true => FieldsCompute.InterpolateVector(
                query: operation.Query,
                vectorField: operation.Field.Vectors,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                bounds: operation.Field.Bounds,
                mode: ResolveInterpolationMode(operation.Mode, operation.Field.Bounds)),
        };

    private static Result<Curve[]> ExecuteStreamlines(Fields.StreamlineIntegrationOperation operation, IGeometryContext context) =>
        (operation.Field.Vectors.Length == operation.Field.Grid.Length, operation.Seeds.Length > 0) switch {
            (false, _) => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidStreamlineSeeds),
            (true, true) => FieldsCompute.IntegrateStreamlines(
                vectorField: operation.Field.Vectors,
                gridPoints: operation.Field.Grid,
                seeds: operation.Seeds,
                stepSize: operation.Field.Sampling.StepSize,
                scheme: operation.Scheme ?? new Fields.RungeKutta4IntegrationScheme(),
                resolution: operation.Field.Sampling.Resolution,
                bounds: operation.Field.Bounds,
                context: context),
        };

    private static Result<Mesh[]> ExecuteIsosurfaces(Fields.IsosurfaceExtractionOperation operation) =>
        (operation.Field.Values.Length == operation.Field.Grid.Length, operation.Isovalues.Length > 0, AreValidIsovalues(operation.Isovalues)) switch {
            (false, _, _) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required")),
            (_, _, false) => ResultFactory.Create<Mesh[]>(error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles")),
            (true, true, true) => FieldsCompute.ExtractIsosurfaces(
                scalarField: operation.Field.Values,
                gridPoints: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                isovalues: operation.Isovalues),
        };

    private static Result<Fields.HessianFieldSamples> ExecuteHessian(Fields.HessianFieldOperation operation) =>
        ResolveGridDelta(operation.Field.Sampling, operation.Field.Bounds)
            .Bind(delta => FieldsCompute.ComputeHessian(
                scalarField: operation.Field.Values,
                grid: operation.Field.Grid,
                resolution: operation.Field.Sampling.Resolution,
                gridDelta: delta))
            .Map(result => new Fields.HessianFieldSamples(
                Grid: result.Grid,
                Hessians: result.Hessian,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.ScalarFieldSamples> ExecuteDirectionalDerivative(Fields.DirectionalDerivativeOperation operation) =>
        (operation.Field.Vectors.Length == operation.Field.Grid.Length) switch {
            false => ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.InvalidDirectionalDerivative.WithContext("Gradient field length must match grid points")),
            true => FieldsCompute.ComputeDirectionalDerivative(
                gradientField: operation.Field.Vectors,
                grid: operation.Field.Grid,
                direction: operation.Direction)
                .Map(result => new Fields.ScalarFieldSamples(
                    Grid: result.Grid,
                    Values: result.DirectionalDerivatives,
                    Sampling: operation.Field.Sampling,
                    Bounds: operation.Field.Bounds)),
        };

    private static Result<Fields.ScalarFieldSamples> ExecuteFieldMagnitude(Fields.FieldMagnitudeOperation operation) =>
        FieldsCompute.ComputeFieldMagnitude(
            vectorField: operation.Field.Vectors,
            grid: operation.Field.Grid)
            .Map(result => new Fields.ScalarFieldSamples(
                Grid: result.Grid,
                Values: result.Magnitudes,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.VectorFieldSamples> ExecuteNormalizeField(Fields.NormalizeFieldOperation operation) =>
        FieldsCompute.NormalizeVectorField(
            vectorField: operation.Field.Vectors,
            grid: operation.Field.Grid)
            .Map(result => new Fields.VectorFieldSamples(
                Grid: result.Grid,
                Vectors: result.Normalized,
                Sampling: operation.Field.Sampling,
                Bounds: operation.Field.Bounds));

    private static Result<Fields.ScalarFieldSamples> ExecuteScalarVectorProduct(Fields.ScalarVectorProductOperation operation) =>
        (operation.Scalars.Grid.Length == operation.Vectors.Grid.Length) switch {
            false => ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.InvalidFieldComposition.WithContext("Grid definitions must match")),
            true => FieldsCompute.ScalarVectorProduct(
                scalarField: operation.Scalars.Values,
                vectorField: operation.Vectors.Vectors,
                grid: operation.Scalars.Grid,
                component: operation.Component)
                .Map(result => new Fields.ScalarFieldSamples(
                    Grid: result.Grid,
                    Values: result.Product,
                    Sampling: operation.Scalars.Sampling,
                    Bounds: operation.Scalars.Bounds)),
        };

    private static Result<Fields.ScalarFieldSamples> ExecuteVectorDotProduct(Fields.VectorDotProductOperation operation) =>
        (operation.First.Grid.Length == operation.Second.Grid.Length) switch {
            false => ResultFactory.Create<Fields.ScalarFieldSamples>(error: E.Geometry.InvalidFieldComposition.WithContext("Grid definitions must match")),
            true => FieldsCompute.VectorDotProduct(
                vectorField1: operation.First.Vectors,
                vectorField2: operation.Second.Vectors,
                grid: operation.First.Grid)
                .Map(result => new Fields.ScalarFieldSamples(
                    Grid: result.Grid,
                    Values: result.DotProduct,
                    Sampling: operation.First.Sampling,
                    Bounds: operation.First.Bounds)),
        };

    private static Result<Fields.CriticalPoint[]> ExecuteCriticalPoints(Fields.CriticalPointDetectionOperation operation) =>
        ValidateHessian(operation)
            .Bind(_ => FieldsCompute.DetectCriticalPoints(
                scalarField: operation.Scalars.Values,
                gradientField: operation.Gradients.Vectors,
                hessian: operation.Hessian.Hessians,
                grid: operation.Scalars.Grid,
                resolution: operation.Scalars.Sampling.Resolution));

    private static Result<Fields.FieldStatistics> ExecuteStatistics(Fields.FieldStatisticsOperation operation) =>
        FieldsCompute.ComputeFieldStatistics(
            scalarField: operation.Field.Values,
            grid: operation.Field.Grid);

    private static Result<Vector3d> ResolveGridDelta(Fields.FieldSampling sampling, BoundingBox bounds) =>
        bounds.IsValid switch {
            false => ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidFieldBounds),
            true => ResultFactory.Create(value: (bounds.Max - bounds.Min) / (sampling.Resolution - 1)),
        };

    private static Fields.InterpolationMode ResolveInterpolationMode(Fields.InterpolationMode? requested, BoundingBox bounds) {
        bool degenerate = RhinoMath.EpsilonEquals(bounds.Max.X, bounds.Min.X, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Y, bounds.Min.Y, epsilon: RhinoMath.SqrtEpsilon)
            || RhinoMath.EpsilonEquals(bounds.Max.Z, bounds.Min.Z, epsilon: RhinoMath.SqrtEpsilon);
        return degenerate
            ? new Fields.NearestInterpolationMode()
            : requested ?? new Fields.TrilinearInterpolationMode();
    }

    private static bool AreValidIsovalues(double[] isovalues) {
        for (int i = 0; i < isovalues.Length; i++) {
            if (!RhinoMath.IsValidDouble(isovalues[i])) {
                return false;
            }
        }

        return true;
    }

    private static Result<bool> ValidateHessian(Fields.CriticalPointDetectionOperation operation) {
        int scalarLength = operation.Scalars.Grid.Length;
        int gradientLength = operation.Gradients.Grid.Length;
        int hessianLength = operation.Hessian.Grid.Length;
        double[,][] tensors = operation.Hessian.Hessians;
        bool validShape = tensors.GetLength(0) == 3 && tensors.GetLength(1) == 3;
        bool validEntries = HasValidHessianEntries(tensors, scalarLength);
        return (scalarLength == gradientLength, scalarLength == hessianLength, validShape && validEntries) switch {
            (false, _, _) => ResultFactory.Create<bool>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Scalar and gradient grids must match")),
            (_, false, _) => ResultFactory.Create<bool>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Scalar and hessian grids must match")),
            (_, _, false) => ResultFactory.Create<bool>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Hessian tensor must be 3x3 with entries per grid sample")),
            (true, true, true) => ResultFactory.Create(value: true),
        };
    }

    private static bool HasValidHessianEntries(double[,][] hessian, int expectedLength) {
        for (int row = 0; row < hessian.GetLength(0); row++) {
            for (int col = 0; col < hessian.GetLength(1); col++) {
                double[]? entries = hessian[row, col];
                if (entries is null || entries.Length != expectedLength) {
                    return false;
                }
            }
        }

        return true;
    }
}

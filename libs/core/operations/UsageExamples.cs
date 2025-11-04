namespace Arsenal.Core.Operations;

using Rhino.Geometry;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>
/// Comprehensive examples demonstrating advanced UnifiedOperation patterns
/// </summary>
public static class UsageExamples {

	// ==================== BASIC PATTERNS ====================

	/// <summary>Example 1: Simple single/batch operation with validation</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsBasic(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = OperationConfig<GeometryBase, Point3d>.WithValidation(
			context,
			ValidationMode.Standard);

		return UnifiedOperation.Apply(
			input,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 2: Operation with pre/post transformations</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsTransformed(
		object input,
		IGeometryContext context,
		Transform preTransform,
		Func<Point3d, Point3d> postTransform) {
		OperationConfig<GeometryBase, Point3d> config = OperationConfig<GeometryBase, Point3d>.WithTransforms(
			context,
			preTransform: geom => {
				GeometryBase transformed = geom.Duplicate();
				transformed.Transform(preTransform);
				return ResultFactory.Create(value: transformed);
			},
			postTransform: pt => ResultFactory.Create(value: postTransform(pt)));

		return UnifiedOperation.Apply(
			input,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 3: Parallel batch processing with error accumulation</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsParallel(
		IReadOnlyList<GeometryBase> geometries,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = OperationConfig<GeometryBase, Point3d>.Parallel(
			context,
			maxDegreeOfParallelism: Environment.ProcessorCount);

		return UnifiedOperation.Apply(
			geometries,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	// ==================== ADVANCED PATTERNS ====================

	/// <summary>Example 4: Conditional execution with filtering</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsConditional(
		object input,
		IGeometryContext context,
		Func<GeometryBase, bool> shouldProcess) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ValidationMode = ValidationMode.Standard,
			InputFilter = shouldProcess,
			OutputFilter = pt => pt.IsValid,
		};

		return UnifiedOperation.Apply(
			input,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 5: Deferred validation (validation inside operation)</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsDeferred(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ValidationMode = ValidationMode.Standard | ValidationMode.MassProperties,
		};

		return UnifiedOperation.ApplyDeferred(
			input,
			(geometry, mode) => ResultFactory.Create(value: geometry)
				.Validate(args: [context, mode])
				.Bind(g => ExtractFromGeometry(g, context)),
			config);
	}

	/// <summary>Example 6: Cached operations for expensive computations</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsCached(
		IReadOnlyList<GeometryBase> geometries,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ValidationMode = ValidationMode.Standard,
			EnableCache = true,
		};

		return UnifiedOperation.ApplyCached(
			geometries,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 7: Error strategy - skip failed items</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsResilient(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ValidationMode = ValidationMode.Standard,
			ErrorStrategy = ErrorStrategy.SkipFailed,
			SkipInvalid = true,
		};

		return UnifiedOperation.Apply(
			input,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 8: Monadic traversal (single result per input)</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractCentroidsTraversal(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = OperationConfig<GeometryBase, Point3d>.Default(context);

		return UnifiedOperation.Traverse(
			input,
			geometry => ExtractCentroid(geometry, context),
			config);
	}

	/// <summary>Example 9: Applicative composition (multiple operations, accumulate errors)</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractAllPointsComposed(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ErrorStrategy = ErrorStrategy.AccumulateAll,
		};

		var operations = new List<Func<GeometryBase, Result<Point3d>>> {
			geom => ExtractCentroid(geom, context),
			geom => ExtractStartPoint(geom, context),
			geom => ExtractEndPoint(geom, context),
		};

		return UnifiedOperation.Compose(input, operations, config);
	}

	/// <summary>Example 10: Conditional execution with ApplyWhen</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsWhenClosed(
		object input,
		IGeometryContext context) {
		OperationConfig<GeometryBase, Point3d> config = OperationConfig<GeometryBase, Point3d>.Default(context);

		return UnifiedOperation.ApplyWhen(
			input,
			predicate: geometry => geometry is Curve { IsClosed: true },
			operation: geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	// ==================== REAL-WORLD SCENARIOS ====================

	/// <summary>Example 11: Complex pipeline with all features</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsComplete(
		IReadOnlyList<GeometryBase> geometries,
		IGeometryContext context,
		Transform? preTransform = null) {
		OperationConfig<GeometryBase, Point3d> config = new() {
			Context = context,
			ValidationMode = ValidationMode.Standard | ValidationMode.BoundingBox,
			ErrorStrategy = ErrorStrategy.AccumulateAll,
			EnableParallel = geometries.Count > 100,
			MaxDegreeOfParallelism = Environment.ProcessorCount,
			PreTransform = preTransform.HasValue
				? geom => {
					GeometryBase transformed = geom.Duplicate();
					transformed.Transform(preTransform.Value);
					return ResultFactory.Create(value: transformed);
				}
				: null,
			InputFilter = geom => geom is Curve or Brep,
			OutputFilter = pt => pt.IsValid && !pt.IsUnset,
			PostTransform = pt => ResultFactory.Create(value: new Point3d(
				Math.Round(pt.X, context.DecimalPrecision),
				Math.Round(pt.Y, context.DecimalPrecision),
				Math.Round(pt.Z, context.DecimalPrecision))),
			SkipInvalid = true,
			EnableCache = true,
			ErrorPrefix = "PointExtraction",
		};

		return UnifiedOperation.ApplyCached(
			geometries,
			geometry => ExtractFromGeometry(geometry, context),
			config);
	}

	/// <summary>Example 12: Update existing PointExtractionEngine pattern</summary>
	public static Result<IReadOnlyList<Point3d>> ExtractPointsModern<T>(
		T input,
		ExtractionMethod method,
		IGeometryContext context,
		int? count = null,
		double? length = null,
		bool includeEnds = true) where T : notnull {
		// Configuration based on extraction method
		OperationConfig<GeometryBase, Point3d> config = method switch {
			ExtractionMethod.Analytical => new() {
				Context = context,
				ValidationMode = ValidationMode.Standard | ValidationMode.MassProperties,
				ErrorStrategy = ErrorStrategy.AccumulateAll,
			},
			ExtractionMethod.Extremal => new() {
				Context = context,
				ValidationMode = ValidationMode.Standard,
				ErrorStrategy = ErrorStrategy.FailFast,
			},
			_ => OperationConfig<GeometryBase, Point3d>.Default(context),
		};

		// Dense operation using UnifiedOperation
		return UnifiedOperation.Apply(
			input,
			geometry => ExtractByMethod(geometry, method, context, count, length, includeEnds),
			config);
	}

	// ==================== HELPER METHODS ====================

	private static Result<IReadOnlyList<Point3d>> ExtractFromGeometry(
		GeometryBase geometry,
		IGeometryContext context) =>
		geometry switch {
			Curve c => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd]),
			Point pt => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[pt.Location]),
			_ => ResultFactory.Create(value: (IReadOnlyList<Point3d>)Array.Empty<Point3d>()),
		};

	private static Result<Point3d> ExtractCentroid(GeometryBase geometry, IGeometryContext context) =>
		geometry switch {
			Curve c when AreaMassProperties.Compute(c)?.Centroid is { IsValid: true } centroid =>
				ResultFactory.Create(value: centroid),
			_ => ResultFactory.Create<Point3d>(error: new(
				Domain: ErrorDomain.Validation,
				Code: "NO_CENTROID",
				Message: "Cannot compute centroid for geometry type")),
		};

	private static Result<Point3d> ExtractStartPoint(GeometryBase geometry, IGeometryContext context) =>
		geometry switch {
			Curve c => ResultFactory.Create(value: c.PointAtStart),
			_ => ResultFactory.Create<Point3d>(error: new(
				Domain: ErrorDomain.Validation,
				Code: "NO_START_POINT",
				Message: "Geometry type does not have start point")),
		};

	private static Result<Point3d> ExtractEndPoint(GeometryBase geometry, IGeometryContext context) =>
		geometry switch {
			Curve c => ResultFactory.Create(value: c.PointAtEnd),
			_ => ResultFactory.Create<Point3d>(error: new(
				Domain: ErrorDomain.Validation,
				Code: "NO_END_POINT",
				Message: "Geometry type does not have end point")),
		};

	private static Result<IReadOnlyList<Point3d>> ExtractByMethod(
		GeometryBase geometry,
		ExtractionMethod method,
		IGeometryContext context,
		int? count,
		double? length,
		bool includeEnds) =>
		ResultFactory.Create(value: (IReadOnlyList<Point3d>)Array.Empty<Point3d>());
}

public enum ExtractionMethod {
	Uniform,
	Analytical,
	Extremal,
	Quadrant,
}

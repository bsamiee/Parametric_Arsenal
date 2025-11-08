using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction with singular API.</summary>
public static class Extract {
    /// <summary>Type-safe semantic extraction mode specifier for point extraction operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Semantic(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Centroids and characteristic vertices (corners, midpoints) via mass properties.</summary>
        public static readonly Semantic Analytical = new(1);

        /// <summary>Geometric extrema: curve endpoints, surface domain corners, bounding box vertices.</summary>
        public static readonly Semantic Extremal = new(2);

        /// <summary>NURBS Greville points computed from knot vectors.</summary>
        public static readonly Semantic Greville = new(3);

        /// <summary>Curve inflection points where curvature sign changes.</summary>
        public static readonly Semantic Inflection = new(4);

        /// <summary>Circle/ellipse quadrant points at 0°, 90°, 180°, 270°.</summary>
        public static readonly Semantic Quadrant = new(5);

        /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
        public static readonly Semantic EdgeMidpoints = new(6);

        /// <summary>Topology face centroids via area mass properties.</summary>
        public static readonly Semantic FaceCentroids = new(7);
    }

    /// <summary>Type-safe parametric extraction mode specifier for division and discontinuity operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Parametric(byte kind, object param, bool includeEnds) {
        internal readonly byte Kind = kind;
        internal readonly object Param = param;
        internal readonly bool IncludeEnds = includeEnds;

        /// <summary>Uniform division by count with optional endpoint inclusion.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Parametric UniformCount(int count, bool includeEnds = true) =>
            new(kind: 10, param: count, includeEnds: includeEnds);

        /// <summary>Uniform division by length with optional endpoint inclusion.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Parametric UniformLength(double length, bool includeEnds = true) =>
            new(kind: 11, param: length, includeEnds: includeEnds);

        /// <summary>Directional extrema extraction along specified vector.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Parametric Directional(Vector3d direction) =>
            new(kind: 12, param: direction, includeEnds: true);

        /// <summary>Discontinuity detection at specified continuity threshold.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Parametric Discontinuities(Continuity continuity) =>
            new(kind: 13, param: continuity, includeEnds: true);
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            Vector3d dir when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidDirection),
            Parametric { Kind: 10, Param: int c } when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            Parametric { Kind: 11, Param: double l } when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            Parametric { Kind: 12, Param: Vector3d dir } when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidDirection),
            Semantic sem => UnifiedOperation.Apply(
                input,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(sem.Kind, typeof(T)) }),
            Parametric para => UnifiedOperation.Apply(
                input,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(para.Kind, typeof(T)) }),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity =>
                UnifiedOperation.Apply(
                    input,
                    (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                    new OperationConfig<T, Point3d> { Context = context, ValidationMode = V.Standard }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction),
        };

    /// <summary>Extracts points from heterogeneous geometry collections with unified error accumulation and parallel execution support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(
        IReadOnlyList<T> geometries,
        object spec,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false,
        bool enableDiagnostics = false) where T : GeometryBase =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidLength),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidCount),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidLength),
            Vector3d dir when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidDirection),
            Parametric { Kind: 10, Param: int c } when c <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidCount),
            Parametric { Kind: 11, Param: double l } when l <= 0 => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidLength),
            Parametric { Kind: 12, Param: Vector3d dir } when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidDirection),
            Semantic sem => UnifiedOperation.Apply(
                geometries,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> {
                    Context = context,
                    ValidationMode = ExtractionConfig.GetValidationMode(sem.Kind, typeof(T)),
                    AccumulateErrors = accumulateErrors,
                    EnableParallel = enableParallel,
                    OperationName = "Extract.PointsMultiple",
                    EnableDiagnostics = enableDiagnostics,
                }),
            Parametric para => UnifiedOperation.Apply(
                geometries,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> {
                    Context = context,
                    ValidationMode = ExtractionConfig.GetValidationMode(para.Kind, typeof(T)),
                    AccumulateErrors = accumulateErrors,
                    EnableParallel = enableParallel,
                    OperationName = "Extract.PointsMultiple",
                    EnableDiagnostics = enableDiagnostics,
                }),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity => UnifiedOperation.Apply(
                geometries,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> {
                    Context = context,
                    ValidationMode = V.Standard,
                    AccumulateErrors = accumulateErrors,
                    EnableParallel = enableParallel,
                    OperationName = "Extract.PointsMultiple",
                    EnableDiagnostics = enableDiagnostics,
                }),
            _ => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Geometry.InvalidExtraction),
        };
}

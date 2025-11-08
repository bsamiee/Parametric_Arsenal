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
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            Vector3d dir when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidDirection),
            Semantic sem => UnifiedOperation.Apply(
                input,
                (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(sem.Kind, typeof(T)) }),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity =>
                UnifiedOperation.Apply(
                    input,
                    (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                    new OperationConfig<T, Point3d> { Context = context, ValidationMode = V.Standard }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction),
        };

    /// <summary>Evaluates curve at specified parameter returning Point3d location.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Point3d> At(Curve curve, double t, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<Point3d>>>)(c =>
                c.Domain.IncludesParameter(t)
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAt(t),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidParameter)),
            config: new OperationConfig<Curve, Point3d> { Context = context, ValidationMode = V.Standard, })
        .Map(results => results[0]);

    /// <summary>Evaluates surface at specified UV parameter returning Point3d location.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Point3d> At(Surface surface, (double u, double v) uv, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: surface,
            operation: (Func<Surface, Result<IReadOnlyList<Point3d>>>)(s =>
                s.Domain(0).IncludesParameter(uv.u) && s.Domain(1).IncludesParameter(uv.v)
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[s.PointAt(uv.u, uv.v),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidParameter)),
            config: new OperationConfig<Surface, Point3d> { Context = context, ValidationMode = V.Standard, })
        .Map(results => results[0]);

    /// <summary>Evaluates curve at multiple parameters returning Point3d locations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> At(Curve curve, double[] parameters, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<Point3d>>>)(c =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. parameters
                    .Where(t => c.Domain.IncludesParameter(t))
                    .Select(t => c.PointAt(t)),])),
            config: new OperationConfig<Curve, Point3d> { Context = context, ValidationMode = V.Standard, });

    /// <summary>Computes closest point on curve to test point returning location and parameter.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Point, double Parameter)> Closest(Curve curve, Point3d test, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<(Point3d, double)>>>)(c =>
                c.ClosestPoint(testPoint: test, t: out double t)
                    ? ResultFactory.Create(value: (IReadOnlyList<(Point3d, double)>)[(c.PointAt(t), t),])
                    : ResultFactory.Create<IReadOnlyList<(Point3d, double)>>(error: E.Geometry.ClosestPointFailed)),
            config: new OperationConfig<Curve, (Point3d, double)> { Context = context, ValidationMode = V.Standard, })
        .Map(results => results[0]);

    /// <summary>Computes closest point on surface to test point returning location and UV parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Point, double U, double V)> Closest(Surface surface, Point3d test, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: surface,
            operation: (Func<Surface, Result<IReadOnlyList<(Point3d, double, double)>>>)(s =>
                s.ClosestPoint(testPoint: test, u: out double u, v: out double v)
                    ? ResultFactory.Create(value: (IReadOnlyList<(Point3d, double, double)>)[(s.PointAt(u, v), u, v),])
                    : ResultFactory.Create<IReadOnlyList<(Point3d, double, double)>>(error: E.Geometry.ClosestPointFailed)),
            config: new OperationConfig<Surface, (Point3d, double, double)> { Context = context, ValidationMode = V.Standard, })
        .Map(results => results[0]);
}

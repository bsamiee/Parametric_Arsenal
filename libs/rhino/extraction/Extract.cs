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

    /// <summary>Type-safe parametric extraction mode specifier for uniform and directional sampling.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Parametric(object spec) {
        /// <summary>Uniform count-based sampling: divides by count with optional endpoint inclusion.</summary>
        public static Parametric UniformCount(int count, bool includeEnds = true) => new((count, includeEnds));

        /// <summary>Uniform length-based sampling: divides by length with optional endpoint inclusion.</summary>
        public static Parametric UniformLength(double length, bool includeEnds = true) => new((length, includeEnds));

        /// <summary>Directional extrema sampling: extracts points at min/max projection along vector.</summary>
        public static Parametric Directional(Vector3d direction) => new(direction);

        /// <summary>Discontinuity sampling: extracts points at parametric discontinuities.</summary>
        public static Parametric Discontinuities(Continuity continuity) => new(continuity);

        internal object Spec { get; } = spec;
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        PointsMultiple(input: [input,], spec: spec, context: context, accumulateErrors: false, enableParallel: false)
            .Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(
        IReadOnlyList<T> input,
        object spec,
        IGeometryContext context,
        bool accumulateErrors = false,
        bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = [.. (enableParallel ? input.AsParallel() : input.AsEnumerable()).Select(item => Points(item, spec, context)),];
        return accumulateErrors
            ? results.All(r => r.IsSuccess)
                ? ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),])
                : ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors).ToArray(),])
            : results.FirstOrDefault(r => !r.IsSuccess) is Result<IReadOnlyList<Point3d>> failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]);
    }
}

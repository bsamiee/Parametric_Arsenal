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
    /// <summary>Semantic extraction marker for parameterless methods.</summary>
    public readonly struct Semantic(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Extracts mass property centroids and characteristic points (vertices, corners, midpoints).</summary>
        public static readonly Semantic Analytical = new(1);

        /// <summary>Extracts geometric extrema (curve endpoints, surface domain corners, bounding box corners).</summary>
        public static readonly Semantic Extremal = new(2);

        /// <summary>Extracts Greville points from NURBS curves and surfaces.</summary>
        public static readonly Semantic Greville = new(3);

        /// <summary>Extracts inflection points from curves where curvature changes sign.</summary>
        public static readonly Semantic Inflection = new(4);

        /// <summary>Extracts quadrant points from circular and elliptical curves.</summary>
        public static readonly Semantic Quadrant = new(5);

        /// <summary>Extracts edge midpoints from Brep, Mesh, and polycurve topology.</summary>
        public static readonly Semantic EdgeMidpoints = new(6);

        /// <summary>Extracts face centroids from Brep and Mesh topology.</summary>
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
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity or Semantic =>
                UnifiedOperation.Apply(
                    input,
                    (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)),
                    new OperationConfig<T, Point3d> { Context = context, V = V.None }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction),
        };
}

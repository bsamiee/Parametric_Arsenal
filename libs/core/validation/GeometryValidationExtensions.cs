using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Core.Validation;

/// <summary>Extension methods for complex geometry validations that cannot be compiled into expression trees.</summary>
public static class GeometryValidationExtensions {
    /// <summary>Checks if curve has self-intersections within tolerance (expression tree compatible).</summary>
    [Pure]
    public static bool HasSelfIntersections(this Curve curve, IGeometryContext context) =>
        Intersection.CurveSelf(curve, context.AbsoluteTolerance) switch {
            CurveIntersections { Count: > 0 } => true,
            _ => false,
        };

    /// <summary>Validates Brep topology structure (expression tree compatible).</summary>
    [Pure]
    public static bool IsValidTopology(this Brep brep) => brep.IsValidTopology();

    /// <summary>Validates Brep geometry correctness (expression tree compatible).</summary>
    [Pure]
    public static bool IsValidGeometry(this Brep brep) => brep.IsValidGeometry();

    /// <summary>Validates Brep tolerances and flags (expression tree compatible).</summary>
    [Pure]
    public static bool IsValidTolerancesAndFlags(this Brep brep) => brep.IsValidTolerancesAndFlags();
}

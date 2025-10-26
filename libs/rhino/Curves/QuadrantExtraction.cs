using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Arsenal.Rhino.Document;
using Arsenal.Rhino.Points;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Curves;

/// <summary>Extracts quadrant points from curves using geometric analysis.</summary>
public static class QuadrantExtraction
{
    /// <summary>Extracts quadrant points: cardinal points for circles/ellipses, axis intersections for planar curves.</summary>
    public static Result<Point3d[]> ExtractPoints(Curve? curve)
    {

        Result<Curve> curveValidation = Guard.RequireNonNull(curve, nameof(curve));
        if (!curveValidation.Ok)
        {
            return Result<Point3d[]>.Fail(curveValidation.Error!);
        }

        Result curveStateValidation = ValidateCurveState(curve!);
        if (!curveStateValidation.Ok)
        {
            return Result<Point3d[]>.Fail(curveStateValidation.Error!);
        }

        double tolerance = Tolerances.Abs();

        // Use RhinoCommon's analytical curve detection methods (preferred approach)
        // Try circle detection first - most precise for circular curves
        if (curve!.TryGetCircle(out Circle circle, tolerance))
        {
            return Result<Point3d[]>.Success(GetCircleQuadrants(circle));
        }

        // Try ellipse detection - analytical approach for elliptical curves
        if (curve.TryGetEllipse(out Ellipse ellipse, tolerance))
        {
            return Result<Point3d[]>.Success(GetEllipseQuadrants(ellipse));
        }

        // Robust planar fallback using RhinoCommon intersection methods
        return GetPlanarQuadrantsFallback(curve);
    }

    /// <summary>Projects world X and Y axes onto a plane to get cardinal directions with robust fallback handling.</summary>
    private static (Vector3d projectedX, Vector3d projectedY) GetWorldAxesOnPlane(Plane plane)
    {
        Vector3d worldX = Vector3d.XAxis;
        Vector3d worldY = Vector3d.YAxis;
        double tolerance = Tolerances.Abs();

        // Project world axes onto the plane using vector projection formula
        double dotX = worldX * plane.Normal;
        double dotY = worldY * plane.Normal;

        Vector3d projectedX = worldX - dotX * plane.Normal;
        Vector3d projectedY = worldY - dotY * plane.Normal;

        // Robust handling of edge cases where axis is parallel to plane normal
        if (projectedX.Length > tolerance)
        {
            projectedX.Unitize();
        }
        else
        {
            // If world X is parallel to plane normal, use plane's X axis
            projectedX = plane.XAxis;
        }

        if (projectedY.Length > tolerance)
        {
            projectedY.Unitize();
        }
        else
        {
            // If world Y is parallel to plane normal, use plane's Y axis
            projectedY = plane.YAxis;
        }

        return (projectedX, projectedY);
    }

    /// <summary>Returns four cardinal points on circle at 0°, 90°, 180°, 270° using world-aligned axes.</summary>
    private static Point3d[] GetCircleQuadrants(Circle circle)
    {
        Point3d center = circle.Center;
        double radius = circle.Radius;

        // Get world-aligned cardinal directions on the circle's plane
        (Vector3d projectedX, Vector3d projectedY) = GetWorldAxesOnPlane(circle.Plane);

        Point3d[] quadrants =
        [
            center + projectedX * radius, // Right (positive X)
            center + projectedY * radius, // Top (positive Y)
            center - projectedX * radius, // Left (negative X)
            center - projectedY * radius // Bottom (negative Y)
        ];

        return quadrants;
    }

    /// <summary>Returns four cardinal points on ellipse at world-aligned axes using analytical calculation.</summary>
    private static Point3d[] GetEllipseQuadrants(Ellipse ellipse)
    {
        Plane plane = ellipse.Plane;
        Point3d center = plane.Origin;
        double radius1 = ellipse.Radius1;
        double radius2 = ellipse.Radius2;

        // Get world-aligned cardinal directions on the ellipse's plane
        (Vector3d projectedX, Vector3d projectedY) = GetWorldAxesOnPlane(plane);

        // For ellipses, calculate intersection points with projected world axes
        // Convert projected vectors to ellipse local coordinates
        double localX_X = projectedX * plane.XAxis;
        double localX_Y = projectedX * plane.YAxis;
        double localY_X = projectedY * plane.XAxis;
        double localY_Y = projectedY * plane.YAxis;

        // Calculate ellipse points along projected directions using parametric equation
        // For direction (dx, dy), scale factor s where: (s*dx)²/a² + (s*dy)²/b² = 1
        double scaleX = 1.0 / System.Math.Sqrt(
            localX_X * localX_X / (radius1 * radius1) +
            localX_Y * localX_Y / (radius2 * radius2));
        double scaleY = 1.0 / System.Math.Sqrt(
            localY_X * localY_X / (radius1 * radius1) +
            localY_Y * localY_Y / (radius2 * radius2));

        Point3d[] quadrants =
        [
            center + projectedX * scaleX, // Right (positive X direction)
            center + projectedY * scaleY, // Top (positive Y direction)
            center - projectedX * scaleX, // Left (negative X direction)
            center - projectedY * scaleY // Bottom (negative Y direction)
        ];

        return quadrants;
    }

    /// <summary>Extracts quadrant points by intersecting curve with X/Y axes through bounding box center using RhinoCommon intersection methods.</summary>
    private static Result<Point3d[]> GetPlanarQuadrantsFallback(Curve curve)
    {
        // Use RhinoCommon's analytical planarity check
        if (!curve.TryGetPlane(out Plane plane, Tolerances.Abs()))
        {
            return Result<Point3d[]>.Fail("Curve is not planar");
        }

        // Get curve bounding box center as reference point
        BoundingBox bbox = curve.GetBoundingBox(plane);
        Point3d center = bbox.Center;
        double axisLength = bbox.Diagonal.Length;


        if (axisLength <= Tolerances.Abs())
        {
            return Result<Point3d[]>.Fail("Curve bounding box is degenerate");
        }

        // Create axis lines through center
        Line xAxisLine = new(
            center - plane.XAxis * axisLength,
            center + plane.XAxis * axisLength
        );
        Line yAxisLine = new(
            center - plane.YAxis * axisLength,
            center + plane.YAxis * axisLength
        );

        double tolerance = Tolerances.Abs();
        List<Point3d> quadrantPoints = [];

        // Use RhinoCommon's optimized CurveLine intersection (preferred over CurveCurve for lines)
        CurveIntersections? xIntersections = Intersection.CurveLine(curve, xAxisLine, tolerance, tolerance);
        if (xIntersections is not null && xIntersections.Count > 0)
        {
            quadrantPoints.AddRange(xIntersections.Select(intersection => intersection.PointA));
        }

        CurveIntersections? yIntersections = Intersection.CurveLine(curve, yAxisLine, tolerance, tolerance);
        if (yIntersections is not null && yIntersections.Count > 0)
        {
            quadrantPoints.AddRange(yIntersections.Select(intersection => intersection.PointA));
        }

        if (quadrantPoints.Count == 0)
        {
            return Result<Point3d[]>.Fail("No quadrant points found via axis intersection");
        }

        // Deduplicate points using shared utility
        Result<Point3d[]> deduplicationResult = PointDeduplication.RemoveWithDocTolerance(quadrantPoints);
        if (!deduplicationResult.Ok)
        {
            return Result<Point3d[]>.Fail(deduplicationResult.Error!);
        }

        return Result<Point3d[]>.Success(deduplicationResult.Value!);
    }

    /// <summary>Validates curve state using comprehensive RhinoCommon validation methods.</summary>
    private static Result ValidateCurveState(Curve curve)
    {
        if (!curve.IsValid)
        {
            return Result.Fail("Curve is not valid");
        }

        if (!curve.Domain.IsIncreasing)
        {
            return Result.Fail("Curve domain is degenerate or invalid");
        }

        return Result.Success();
    }
}

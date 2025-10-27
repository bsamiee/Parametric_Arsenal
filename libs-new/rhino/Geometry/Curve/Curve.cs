using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Spatial;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Geometry.Curve;

/// <summary>RhinoCommon-backed curve operations.</summary>
public sealed class CurveOperations : ICurve
{
    /// <summary>Finds the closest point on the curve to the test point.</summary>
    /// <param name="curve">The curve to project onto.</param>
    /// <param name="testPoint">The point to project.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the closest point information or a failure.</returns>
    public Result<CurveClosestPoint> ClosestPoint(global::Rhino.Geometry.Curve curve, Point3d testPoint, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<global::Rhino.Geometry.Curve> curveResult = ValidateCurve(curve);
        if (!curveResult.IsSuccess)
        {
            return Result<CurveClosestPoint>.Fail(curveResult.Failure!);
        }

        if (!testPoint.IsValid)
        {
            return Result<CurveClosestPoint>.Fail(new Failure("curve.point.invalid", "Test point is not valid."));
        }

        if (!curve.ClosestPoint(testPoint, out double parameter))
        {
            return Result<CurveClosestPoint>.Fail(new Failure("curve.closestPoint", "Failed to project point onto curve."));
        }

        Interval domain = curve.Domain;
        if (!domain.IncludesParameter(parameter))
        {
            return Result<CurveClosestPoint>.Fail(new Failure("curve.parameter.outOfRange",
                $"Closest point parameter {parameter} lies outside domain [{domain.T0}, {domain.T1}]."));
        }

        Point3d projection = curve.PointAt(parameter);
        double distance = testPoint.DistanceTo(projection);

        return Result<CurveClosestPoint>.Success(new CurveClosestPoint(projection, parameter, distance));
    }

    /// <summary>Computes the tangent vector at the specified parameter.</summary>
    /// <param name="curve">The curve to evaluate.</param>
    /// <param name="parameter">The parameter to evaluate at.</param>
    /// <returns>A result containing the unit tangent vector or a failure.</returns>
    public Result<Vector3d> TangentAt(global::Rhino.Geometry.Curve curve, double parameter)
    {
        Result<global::Rhino.Geometry.Curve> curveResult = ValidateCurve(curve);
        if (!curveResult.IsSuccess)
        {
            return Result<Vector3d>.Fail(curveResult.Failure!);
        }

        Interval domain = curve.Domain;
        if (!domain.IncludesParameter(parameter))
        {
            return Result<Vector3d>.Fail(new Failure("curve.parameter.outOfRange",
                $"Parameter {parameter} lies outside domain [{domain.T0}, {domain.T1}]."));
        }

        try
        {
            Vector3d tangent = curve.TangentAt(parameter);
            if (!tangent.IsValid || tangent.IsTiny())
            {
                return Result<Vector3d>.Fail(new Failure("curve.tangent.invalid", "Computed tangent is invalid or zero-length."));
            }

            tangent.Unitize();
            return Result<Vector3d>.Success(tangent);
        }
        catch (Exception ex)
        {
            return Result<Vector3d>.Fail(Failure.From(ex));
        }
    }

    /// <summary>Computes quadrant points for circular or elliptical curves.</summary>
    /// <param name="curve">The curve to compute quadrant points for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the quadrant points or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> QuadrantPoints(global::Rhino.Geometry.Curve curve, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<global::Rhino.Geometry.Curve> curveResult = ValidateCurve(curve);
        if (!curveResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(curveResult.Failure!);
        }

        double tolerance = context.AbsoluteTolerance;

        if (curve.TryGetCircle(out Circle circle, tolerance))
        {
            return Result<IReadOnlyList<Point3d>>.Success(GetCircleQuadrants(circle));
        }

        if (curve.TryGetEllipse(out Ellipse ellipse, tolerance))
        {
            return Result<IReadOnlyList<Point3d>>.Success(GetEllipseQuadrants(ellipse));
        }

        if (!curve.TryGetPlane(out Plane plane, tolerance))
        {
            return Result<IReadOnlyList<Point3d>>.Fail(new Failure("curve.planar", "Curve is not planar; quadrant sampling is undefined."));
        }

        BoundingBox bbox = curve.GetBoundingBox(plane);
        if (!bbox.IsValid || bbox.Diagonal.Length <= tolerance)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(new Failure("curve.degenerate", "Curve bounding box is degenerate."));
        }

        Point3d center = bbox.Center;
        double axisLength = bbox.Diagonal.Length;

        Line xAxis = new(center - plane.XAxis * axisLength, center + plane.XAxis * axisLength);
        Line yAxis = new(center - plane.YAxis * axisLength, center + plane.YAxis * axisLength);

        List<Point3d> hits = [];

        CurveIntersections? xHits = Intersection.CurveLine(curve, xAxis, tolerance, tolerance);
        if (xHits is not null)
        {
            hits.AddRange(xHits.Select(evt => evt.PointA));
        }

        CurveIntersections? yHits = Intersection.CurveLine(curve, yAxis, tolerance, tolerance);
        if (yHits is not null)
        {
            hits.AddRange(yHits.Select(evt => evt.PointA));
        }

        if (hits.Count == 0)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(new Failure("curve.quadrants", "No quadrant intersections were found."));
        }

        Result<IReadOnlyList<Point3d>> deduplicated = PointIndex.Deduplicate(hits, tolerance);
        return deduplicated.IsSuccess
            ? deduplicated
            : Result<IReadOnlyList<Point3d>>.Fail(deduplicated.Failure!);
    }

    /// <summary>Computes the midpoint of the curve.</summary>
    /// <param name="curve">The curve to compute the midpoint for.</param>
    /// <returns>A result containing the midpoint or a failure.</returns>
    public Result<Point3d> Midpoint(global::Rhino.Geometry.Curve curve)
    {
        Result<global::Rhino.Geometry.Curve> curveResult = ValidateCurve(curve);
        if (!curveResult.IsSuccess)
        {
            return Result<Point3d>.Fail(curveResult.Failure!);
        }

        Interval domain = curve.Domain;
        double mid = domain.Mid;

        try
        {
            Point3d point = curve.PointAt(mid);
            return Result<Point3d>.Success(point);
        }
        catch (Exception ex)
        {
            return Result<Point3d>.Fail(Failure.From(ex));
        }
    }

    private static Result<global::Rhino.Geometry.Curve> ValidateCurve(global::Rhino.Geometry.Curve? curve)
    {
        Result<global::Rhino.Geometry.Curve> guard = Guard.AgainstNull(curve, nameof(curve));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<global::Rhino.Geometry.Curve>.Fail(new Failure("curve.invalid", "Curve is not valid."));
        }

        return guard;
    }

    private static IReadOnlyList<Point3d> GetCircleQuadrants(Circle circle)
    {
        Point3d center = circle.Center;
        double radius = circle.Radius;

        (Vector3d projectedX, Vector3d projectedY) = ProjectWorldAxes(circle.Plane);

        return [
            center + projectedX * radius,
            center + projectedY * radius,
            center - projectedX * radius,
            center - projectedY * radius
        ];
    }

    private static IReadOnlyList<Point3d> GetEllipseQuadrants(Ellipse ellipse)
    {
        Plane plane = ellipse.Plane;
        Point3d center = plane.Origin;

        (Vector3d projectedX, Vector3d projectedY) = ProjectWorldAxes(plane);
        (double scaleX, double scaleY) = GetEllipseScales(ellipse, projectedX, projectedY);

        return [
            center + projectedX * scaleX,
            center + projectedY * scaleY,
            center - projectedX * scaleX,
            center - projectedY * scaleY
        ];
    }

    private static (Vector3d projectedX, Vector3d projectedY) ProjectWorldAxes(Plane plane)
    {
        Vector3d worldX = Vector3d.XAxis;
        Vector3d worldY = Vector3d.YAxis;

        Vector3d projectedX = worldX - Vector3d.Multiply(worldX * plane.Normal, plane.Normal);
        Vector3d projectedY = worldY - Vector3d.Multiply(worldY * plane.Normal, plane.Normal);

        if (!projectedX.Unitize())
        {
            projectedX = plane.XAxis;
        }

        if (!projectedY.Unitize())
        {
            projectedY = plane.YAxis;
        }

        return (projectedX, projectedY);
    }

    private static (double scaleX, double scaleY) GetEllipseScales(Ellipse ellipse, Vector3d projectedX, Vector3d projectedY)
    {
        Plane plane = ellipse.Plane;

        double dx = projectedX * plane.XAxis;
        double dy = projectedX * plane.YAxis;
        double ex = projectedY * plane.XAxis;
        double ey = projectedY * plane.YAxis;

        double a = ellipse.Radius1;
        double b = ellipse.Radius2;

        double scaleX = 1.0 / Math.Sqrt(dx * dx / (a * a) + dy * dy / (b * b));
        double scaleY = 1.0 / Math.Sqrt(ex * ex / (a * a) + ey * ey / (b * b));

        return (scaleX, scaleY);
    }
}

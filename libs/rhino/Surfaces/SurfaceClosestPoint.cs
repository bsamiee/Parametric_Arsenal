using System.Collections.Generic;
using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Surfaces;

/// <summary>Surface analysis utilities.</summary>
public static class SurfaceClosestPoint
{
    /// <summary>Finds closest point on surface to test point.</summary>
    public static Result<SurfaceClosestPointResult> Find(Surface? surface, Point3d testPoint)
    {
        if (surface is null)
        {
            return Result<SurfaceClosestPointResult>.Fail($"{nameof(surface)} cannot be null");
        }

        if (!surface.IsValid)
        {
            return Result<SurfaceClosestPointResult>.Fail("Surface is not valid");
        }

        if (!surface.ClosestPoint(testPoint, out double u, out double v))
        {
            return Result<SurfaceClosestPointResult>.Fail("Failed to find closest point on surface");
        }

        Interval uDomain = surface.Domain(0);
        Interval vDomain = surface.Domain(1);

        if (!uDomain.IncludesParameter(u) || !vDomain.IncludesParameter(v))
        {
            return Result<SurfaceClosestPointResult>.Fail("Closest point UV parameters are outside surface domain");
        }
        Point3d closestPoint = surface.PointAt(u, v);
        double distance = testPoint.DistanceTo(closestPoint);

        SurfaceClosestPointResult result = new(closestPoint, u, v, distance);
        return Result<SurfaceClosestPointResult>.Success(result);
    }

    /// <summary>Evaluates surface properties at UV parameters.</summary>
    public static Result<SurfaceEvaluationResult> EvaluateAt(Surface? surface, double u, double v)
    {
        if (surface is null)
        {
            return Result<SurfaceEvaluationResult>.Fail($"{nameof(surface)} cannot be null");
        }

        if (!surface.IsValid)
        {
            return Result<SurfaceEvaluationResult>.Fail("Surface is not valid");
        }

        Interval uDomain = surface.Domain(0);
        Interval vDomain = surface.Domain(1);

        if (!uDomain.IncludesParameter(u))
        {
            return Result<SurfaceEvaluationResult>.Fail($"U parameter {u} is outside surface domain [{uDomain.T0}, {uDomain.T1}]");
        }

        if (!vDomain.IncludesParameter(v))
        {
            return Result<SurfaceEvaluationResult>.Fail($"V parameter {v} is outside surface domain [{vDomain.T0}, {vDomain.T1}]");
        }

        try
        {
            Point3d point = surface.PointAt(u, v);
            Vector3d normal = surface.NormalAt(u, v);

            surface.Evaluate(u, v, 1, out Point3d _, out Vector3d[] derivatives);

            Vector3d tangentU = derivatives[0];
            Vector3d tangentV = derivatives[1];

            SurfaceEvaluationResult result = new(point, normal, tangentU, tangentV, u, v);
            return Result<SurfaceEvaluationResult>.Success(result);
        }
        catch (System.Exception ex)
        {
            return Result<SurfaceEvaluationResult>.Fail($"Surface evaluation failed: {ex.Message}");
        }
    }

    /// <summary>Analyzes surface deviation from reference points.</summary>
    public static Result<SurfaceDeviationResult[]> AnalyzeDeviation(Surface? surface, IEnumerable<Point3d>? referencePoints)
    {
        if (surface is null)
        {
            return Result<SurfaceDeviationResult[]>.Fail($"{nameof(surface)} cannot be null");
        }

        if (referencePoints is null)
        {
            return Result<SurfaceDeviationResult[]>.Fail($"{nameof(referencePoints)} cannot be null");
        }

        if (!surface.IsValid)
        {
            return Result<SurfaceDeviationResult[]>.Fail("Surface is not valid");
        }

        List<SurfaceDeviationResult> results = [];

        foreach (Point3d refPoint in referencePoints)
        {
            Result<SurfaceClosestPointResult> closestResult = Find(surface, refPoint);
            if (!closestResult.Ok)
            {
                return Result<SurfaceDeviationResult[]>.Fail($"Failed to find closest point for reference point {refPoint}: {closestResult.Error}");
            }

            SurfaceClosestPointResult closest = closestResult.Value;

            Vector3d deviationVector = refPoint - closest.Point;
            double deviationMagnitude = deviationVector.Length;

            SurfaceDeviationResult deviation = new(
                refPoint,
                closest.Point,
                deviationVector,
                deviationMagnitude,
                closest.U,
                closest.V
            );

            results.Add(deviation);
        }

        return Result<SurfaceDeviationResult[]>.Success([.. results]);
    }
}

/// <summary>Closest point result containing point, UV parameters, and distance.</summary>
public readonly record struct SurfaceClosestPointResult(Point3d Point, double U, double V, double Distance);

/// <summary>Surface evaluation result containing point, normal, and tangent vectors at UV parameters.</summary>
public readonly record struct SurfaceEvaluationResult(
    Point3d Point,
    Vector3d Normal,
    Vector3d TangentU,
    Vector3d TangentV,
    double U,
    double V
);

/// <summary>Surface deviation analysis result.</summary>
public readonly record struct SurfaceDeviationResult(
    Point3d ReferencePoint,
    Point3d SurfacePoint,
    Vector3d DeviationVector,
    double DeviationMagnitude,
    double U,
    double V
);

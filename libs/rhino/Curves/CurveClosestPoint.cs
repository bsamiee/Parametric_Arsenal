using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Curves;

/// <summary>Curve closest point utilities providing structured results.</summary>
public static class CurveClosestPoint
{
    /// <summary>Finds closest point on curve to test point.</summary>
    public static Result<CurveClosestPointResult> Find(Curve? curve, Point3d testPoint)
    {

        Result<Curve> curveValidation = Guard.RequireNonNull(curve, nameof(curve));
        if (!curveValidation.Ok)
        {
            return Result<CurveClosestPointResult>.Fail(curveValidation.Error!);
        }

        Result curveStateValidation = ValidateCurveState(curve!);
        if (!curveStateValidation.Ok)
        {
            return Result<CurveClosestPointResult>.Fail(curveStateValidation.Error!);
        }

        if (!testPoint.IsValid)
        {
            return Result<CurveClosestPointResult>.Fail("Point is not valid");
        }

        // Use RhinoCommon's optimized closest point method
        bool success = curve!.ClosestPoint(testPoint, out double parameter);
        if (!success)
        {
            return Result<CurveClosestPointResult>.Fail("Failed to find closest point on curve");
        }

        if (!curve.Domain.IncludesParameter(parameter))
        {
            return Result<CurveClosestPointResult>.Fail(
                $"Closest point parameter {parameter} is outside curve domain [{curve.Domain.T0}, {curve.Domain.T1}]");
        }

        Point3d closestPoint = curve.PointAt(parameter);
        double distance = testPoint.DistanceTo(closestPoint);

        CurveClosestPointResult result = new(closestPoint, parameter, distance);

        return Result<CurveClosestPointResult>.Success(result);
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

/// <summary>Represents the closest point on a curve to a test point.</summary>
/// <param name="Point">Closest point on the curve.</param>
/// <param name="Parameter">Parameter value at the closest point.</param>
/// <param name="Distance">Distance from test point to closest point.</param>
public readonly record struct CurveClosestPointResult(Point3d Point, double Parameter, double Distance);

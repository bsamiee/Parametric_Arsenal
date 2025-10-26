using System;
using Arsenal.Core;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Document;

/// <summary>Centralized tolerance access with fallback chain: specified doc → active doc → default.</summary>
public static class Tolerances
{
    /// <summary>Default absolute tolerance when no document is available.</summary>
    private const double DefaultAbsoluteTolerance = 0.01;

    /// <summary>Default angle tolerance in radians when no document is available.</summary>
    private const double DefaultAngleToleranceRadians = 0.017453292519943295; // 1 degree in radians

    /// <summary>Gets absolute tolerance from document with fallback to active document or default.</summary>
    public static double Abs(RhinoDoc? doc = null)
    {
        return doc?.ModelAbsoluteTolerance
               ?? RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance
               ?? DefaultAbsoluteTolerance;
    }

    /// <summary>Gets angle tolerance in radians from document with fallback to active document or default.</summary>
    public static double AngleRad(RhinoDoc? doc = null)
    {
        return doc?.ModelAngleToleranceRadians
               ?? RhinoDoc.ActiveDoc?.ModelAngleToleranceRadians
               ?? DefaultAngleToleranceRadians;
    }

    /// <summary>Gets angle tolerance in degrees from document with fallback to active document or default.</summary>
    public static double AngleDeg(RhinoDoc? doc = null)
    {
        return RhinoMath.ToDegrees(AngleRad(doc));
    }

    /// <summary>Determines whether two values are nearly equal within the specified epsilon tolerance.</summary>
    public static Result<bool> NearlyEqual(double a, double b, double eps)
    {
        Result<double> epsValidation = Guard.RequireNonNegative(eps, nameof(eps));
        if (!epsValidation.Ok)
        {
            return Result<bool>.Fail(epsValidation.Error!);
        }

        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return Result<bool>.Fail("Values must be finite numbers");
        }

        bool result = Math.Abs(a - b) <= eps;
        return Result<bool>.Success(result);
    }

    /// <summary>Determines whether two values are nearly equal using document absolute tolerance.</summary>
    public static bool NearlyEqual(double a, double b, RhinoDoc? doc = null)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return false;
        }

        double tolerance = Abs(doc);
        return Math.Abs(a - b) <= tolerance;
    }

    /// <summary>Determines whether two points are nearly equal using document absolute tolerance.</summary>
    public static Result<bool> NearlyEqual(Point3d a, Point3d b, RhinoDoc? doc = null)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return Result<bool>.Fail("Points must be valid");
        }

        double tolerance = Abs(doc);
        bool result = a.EpsilonEquals(b, tolerance);
        return Result<bool>.Success(result);
    }

    /// <summary>Validates that a tolerance value is within reasonable bounds for Rhino operations.</summary>
    public static Result<double> ValidateTolerance(double tolerance, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        if (!double.IsFinite(tolerance))
        {
            return Result<double>.Fail($"{paramName} must be a finite number");
        }

        // Rhino recommended tolerance range: 0.00001 to 0.1
        const double minTolerance = 1e-5;
        const double maxTolerance = 0.1;

        return tolerance switch
        {
            <= 0 => Result<double>.Fail($"{paramName} must be positive, but was {tolerance}"),
            < minTolerance => Result<double>.Fail(
                $"{paramName} is too small (< {minTolerance}), consider changing document units instead"),
            > maxTolerance => Result<double>.Fail(
                $"{paramName} is too large (> {maxTolerance}), may cause geometric issues"),
            _ => Result<double>.Success(tolerance)
        };
    }
}

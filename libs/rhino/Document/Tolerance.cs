using System;
using Arsenal.Core.Result;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Document;

/// <summary>Utilities for working with Rhino document tolerances.</summary>
public static class Tolerance
{
    private const double DefaultAbsoluteTolerance = 0.01;
    private const double DefaultAngleToleranceRadians = Math.PI / 180.0;

    /// <summary>Gets absolute tolerance from document or default.</summary>
    public static double Absolute(RhinoDoc? doc = null)
    {
        return doc?.ModelAbsoluteTolerance
               ?? RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance
               ?? DefaultAbsoluteTolerance;
    }

    /// <summary>Gets angle tolerance in radians from document or default.</summary>
    public static double AngleRadians(RhinoDoc? doc = null)
    {
        return doc?.ModelAngleToleranceRadians
               ?? RhinoDoc.ActiveDoc?.ModelAngleToleranceRadians
               ?? DefaultAngleToleranceRadians;
    }

    /// <summary>Gets angle tolerance in degrees from document or default.</summary>
    public static double AngleDegrees(RhinoDoc? doc = null) => RhinoMath.ToDegrees(AngleRadians(doc));

    /// <summary>Checks if two values are nearly equal within epsilon.</summary>
    public static Result<bool> NearlyEqual(double a, double b, double epsilon)
    {
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            return Result<bool>.Fail(new Failure("tolerance.invalid", "Epsilon must be a non-negative finite number."));
        }

        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return Result<bool>.Fail(new Failure("tolerance.invalid", "Values must be finite numbers."));
        }

        return Result<bool>.Success(Math.Abs(a - b) <= epsilon);
    }

    /// <summary>Checks if two values are nearly equal using document tolerance.</summary>
    public static bool NearlyEqual(double a, double b, RhinoDoc? doc = null)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return false;
        }

        double tolerance = Absolute(doc);
        return Math.Abs(a - b) <= tolerance;
    }

    /// <summary>Checks if two points are nearly equal using document tolerance.</summary>
    public static Result<bool> NearlyEqual(Point3d a, Point3d b, RhinoDoc? doc = null)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return Result<bool>.Fail(new Failure("tolerance.invalid", "Points must be valid."));
        }

        double tolerance = Absolute(doc);
        return Result<bool>.Success(a.EpsilonEquals(b, tolerance));
    }

    /// <summary>Validates tolerance value is within reasonable bounds.</summary>
    public static Result<double> Validate(double tolerance, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!double.IsFinite(tolerance))
        {
            return Result<double>.Fail(new Failure("tolerance.invalid", $"{name} must be a finite number."));
        }

        const double minTolerance = 1e-5;
        const double maxTolerance = 0.1;

        if (tolerance <= 0)
        {
            return Result<double>.Fail(new Failure("tolerance.invalid", $"{name} must be positive."));
        }

        if (tolerance < minTolerance)
        {
            return Result<double>.Fail(new Failure("tolerance.tooSmall",
                $"{name} is smaller than {minTolerance}. Consider adjusting document units."));
        }

        if (tolerance > maxTolerance)
        {
            return Result<double>.Fail(new Failure("tolerance.tooLarge",
                $"{name} exceeds {maxTolerance} and may cause inaccuracies."));
        }

        return Result<double>.Success(tolerance);
    }
}

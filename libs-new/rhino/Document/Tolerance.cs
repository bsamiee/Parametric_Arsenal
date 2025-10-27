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

    /// <summary>Gets the absolute tolerance from a document or default value.</summary>
    /// <param name="doc">The document to query, or null to use the active document.</param>
    /// <returns>The absolute tolerance value.</returns>
    public static double Absolute(RhinoDoc? doc = null)
    {
        return doc?.ModelAbsoluteTolerance
               ?? RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance
               ?? DefaultAbsoluteTolerance;
    }

    /// <summary>Gets the angle tolerance in radians from a document or default value.</summary>
    /// <param name="doc">The document to query, or null to use the active document.</param>
    /// <returns>The angle tolerance in radians.</returns>
    public static double AngleRadians(RhinoDoc? doc = null)
    {
        return doc?.ModelAngleToleranceRadians
               ?? RhinoDoc.ActiveDoc?.ModelAngleToleranceRadians
               ?? DefaultAngleToleranceRadians;
    }

    /// <summary>Gets the angle tolerance in degrees from a document or default value.</summary>
    /// <param name="doc">The document to query, or null to use the active document.</param>
    /// <returns>The angle tolerance in degrees.</returns>
    public static double AngleDegrees(RhinoDoc? doc = null) => RhinoMath.ToDegrees(AngleRadians(doc));

    /// <summary>Checks if two values are nearly equal within a specified epsilon.</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <param name="epsilon">The tolerance for comparison.</param>
    /// <returns>A result containing true if values are nearly equal, or a failure.</returns>
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
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <param name="doc">The document to get tolerance from, or null to use active document.</param>
    /// <returns>True if values are nearly equal within document tolerance.</returns>
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
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <param name="doc">The document to get tolerance from, or null to use active document.</param>
    /// <returns>A result containing true if points are nearly equal, or a failure.</returns>
    public static Result<bool> NearlyEqual(Point3d a, Point3d b, RhinoDoc? doc = null)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return Result<bool>.Fail(new Failure("tolerance.invalid", "Points must be valid."));
        }

        double tolerance = Absolute(doc);
        return Result<bool>.Success(a.EpsilonEquals(b, tolerance));
    }

    /// <summary>Validates a tolerance value is within reasonable bounds.</summary>
    /// <param name="tolerance">The tolerance value to validate.</param>
    /// <param name="name">The name of the tolerance for error messages.</param>
    /// <returns>A result containing the validated tolerance or a failure.</returns>
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

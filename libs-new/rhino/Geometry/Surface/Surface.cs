using System;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Surface;

/// <summary>RhinoCommon-backed surface operations.</summary>
public sealed class SurfaceOperations : ISurface
{
    /// <summary>Finds the closest point on the surface to the test point.</summary>
    /// <param name="surface">The surface to project onto.</param>
    /// <param name="testPoint">The point to project.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the closest point information or a failure.</returns>
    public Result<SurfaceClosestPoint> ClosestPoint(global::Rhino.Geometry.Surface surface, Point3d testPoint, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<global::Rhino.Geometry.Surface> surfaceResult = ValidateSurface(surface);
        if (!surfaceResult.IsSuccess)
        {
            return Result<SurfaceClosestPoint>.Fail(surfaceResult.Failure!);
        }

        if (!testPoint.IsValid)
        {
            return Result<SurfaceClosestPoint>.Fail(new Failure("surface.point.invalid", "Test point is not valid."));
        }

        if (!surface.ClosestPoint(testPoint, out double u, out double v))
        {
            return Result<SurfaceClosestPoint>.Fail(new Failure("surface.closestPoint", "Failed to project point onto surface."));
        }

        Interval uDomain = surface.Domain(0);
        Interval vDomain = surface.Domain(1);

        if (!uDomain.IncludesParameter(u) || !vDomain.IncludesParameter(v))
        {
            return Result<SurfaceClosestPoint>.Fail(new Failure("surface.parameter.outOfRange",
                $"Closest point parameters ({u}, {v}) lie outside surface domain."));
        }

        Point3d closest = surface.PointAt(u, v);
        double distance = testPoint.DistanceTo(closest);

        return Result<SurfaceClosestPoint>.Success(new SurfaceClosestPoint(closest, u, v, distance));
    }

    /// <summary>Computes the surface frame at the specified parameters.</summary>
    /// <param name="surface">The surface to evaluate.</param>
    /// <param name="u">The U parameter.</param>
    /// <param name="v">The V parameter.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the surface frame or a failure.</returns>
    public Result<SurfaceFrame> FrameAt(global::Rhino.Geometry.Surface surface, double u, double v, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<global::Rhino.Geometry.Surface> surfaceResult = ValidateSurface(surface);
        if (!surfaceResult.IsSuccess)
        {
            return Result<SurfaceFrame>.Fail(surfaceResult.Failure!);
        }

        Interval uDomain = surface.Domain(0);
        Interval vDomain = surface.Domain(1);

        if (!uDomain.IncludesParameter(u))
        {
            return Result<SurfaceFrame>.Fail(new Failure("surface.parameter.outOfRange",
                $"U parameter {u} lies outside [{uDomain.T0}, {uDomain.T1}]."));
        }

        if (!vDomain.IncludesParameter(v))
        {
            return Result<SurfaceFrame>.Fail(new Failure("surface.parameter.outOfRange",
                $"V parameter {v} lies outside [{vDomain.T0}, {vDomain.T1}]."));
        }

        try
        {
            bool evaluated = surface.Evaluate(u, v, 1, out Point3d point, out Vector3d[] derivatives);
            if (!evaluated || derivatives.Length < 2)
            {
                return Result<SurfaceFrame>.Fail(new Failure("surface.evaluate", "Surface evaluation did not return required derivative information."));
            }

            Vector3d tangentU = derivatives[0];
            Vector3d tangentV = derivatives[1];
            Vector3d normal = Vector3d.CrossProduct(tangentU, tangentV);

            if (!normal.IsValid || !normal.Unitize())
            {
                normal = surface.NormalAt(u, v);
                if (!normal.IsValid || !normal.Unitize())
                {
                    return Result<SurfaceFrame>.Fail(new Failure("surface.normal", "Failed to compute a valid surface normal."));
                }
            }

            return Result<SurfaceFrame>.Success(new SurfaceFrame(point, tangentU, tangentV, normal));
        }
        catch (Exception ex)
        {
            return Result<SurfaceFrame>.Fail(Failure.From(ex));
        }
    }

    private static Result<global::Rhino.Geometry.Surface> ValidateSurface(global::Rhino.Geometry.Surface? surface)
    {
        Result<global::Rhino.Geometry.Surface> guard = Guard.AgainstNull(surface, nameof(surface));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<global::Rhino.Geometry.Surface>.Fail(new Failure("surface.invalid", "Surface is not valid."));
        }

        return guard;
    }
}
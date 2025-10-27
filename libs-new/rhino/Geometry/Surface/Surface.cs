using System;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;
using RhinoSurface = Rhino.Geometry.Surface;

namespace Arsenal.Rhino.Geometry.Surface;

/// <summary>Surface operations using RhinoCommon.</summary>
public sealed class SurfaceOperations : ISurface
{
    /// <summary>Finds closest point on surface to test point.</summary>
    public Result<SurfaceClosestPoint> ClosestPoint(RhinoSurface surface, Point3d testPoint, GeoContext context)
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
            return Result<SurfaceClosestPoint>.Fail(new Failure("surface.closestPoint",
                "Failed to project point onto surface."));
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

    private static Result<RhinoSurface> ValidateSurface(RhinoSurface? surface)
    {
        Result<RhinoSurface> guard = Guard.AgainstNull(surface, nameof(surface));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<RhinoSurface>.Fail(new Failure("surface.invalid", "Surface is not valid."));
        }

        return guard;
    }
}

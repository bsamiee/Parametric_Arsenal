using System;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;
using RhinoSurface = Rhino.Geometry.Surface;

namespace Arsenal.Rhino.Analysis.Surface;

/// <summary>Surface analysis operations using RhinoCommon.</summary>
public sealed class SurfaceAnalysis : ISurfaceAnalysis
{
    /// <summary>Computes surface frame at UV parameters.</summary>
    public Result<SurfaceFrame> FrameAt(RhinoSurface surface, double u, double v, GeoContext context)
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

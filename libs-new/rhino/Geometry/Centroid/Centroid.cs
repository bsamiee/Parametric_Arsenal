using System;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Base;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Centroid;

/// <summary>RhinoCommon-backed centroid operations.</summary>
public sealed class Centroid : ICentroid
{
    private readonly IClassifier _classifier;

    /// <summary>Initializes a new instance of the Centroid class.</summary>
    /// <param name="classifier">The geometry classifier to use for determining mass property modes.</param>
    public Centroid(IClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <summary>Computes the centroid of the specified geometry.</summary>
    /// <param name="geometry">The geometry to compute the centroid for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the centroid point or a failure.</returns>
    public Result<Point3d> Compute(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<GeometryBase> geometryResult = Guard.AgainstNull(geometry, nameof(geometry));
        if (!geometryResult.IsSuccess)
        {
            return Result<Point3d>.Fail(geometryResult.Failure!);
        }

        if (!geometry.IsValid)
        {
            return Result<Point3d>.Fail(new Failure("geometry.invalid", $"Geometry of type {geometry.ObjectType} is invalid."));
        }

        try
        {
            MassPropertyMode mode = _classifier.MassPropertyMode(geometry).Match(m => m, _ => MassPropertyMode.Fallback);

            return geometry switch
            {
                null => Result<Point3d>.Fail(new Failure("geometry.null", "Geometry cannot be null.")),
                global::Rhino.Geometry.Brep brep => mode == MassPropertyMode.Volume
                    ? ComputeFromBrep(brep, useVolume: true)
                    : ComputeFromBrep(brep, useVolume: false),
                global::Rhino.Geometry.Mesh mesh => ComputeFromMesh(mesh),
                Extrusion extrusion => WithBrep(extrusion.ToBrep(), brep => ComputeFromBrep(brep, mode == MassPropertyMode.Volume)),
                SubD subd => WithBrep(subd.ToBrep(), brep => ComputeFromBrep(brep, mode == MassPropertyMode.Volume)),
                global::Rhino.Geometry.Surface surface => ComputeFromSurface(surface),
                global::Rhino.Geometry.Curve curve when mode == MassPropertyMode.Area => ComputeFromCurveClosed(curve),
                global::Rhino.Geometry.Curve curve => ComputeFromCurveOpen(curve),
                Point point => Result<Point3d>.Success(point.Location),
                _ => Result<Point3d>.Success(BoundingBoxCentroid(geometry.GetBoundingBox(true)))
            };
        }
        catch (Exception ex)
        {
            return Result<Point3d>.Fail(Failure.From(ex));
        }
    }

    private static Result<Point3d> ComputeFromBrep(global::Rhino.Geometry.Brep brep, bool useVolume)
    {
        return TryComputeCentroid(
            useVolume ? () => VolumeMassProperties.Compute(brep) : null,
            () => AreaMassProperties.Compute(brep),
            () => BoundingBoxCentroid(brep.GetBoundingBox(true)));
    }

    private static Result<Point3d> ComputeFromMesh(global::Rhino.Geometry.Mesh mesh)
    {
        return TryComputeCentroid(
            () => VolumeMassProperties.Compute(mesh),
            () => AreaMassProperties.Compute(mesh),
            () => BoundingBoxCentroid(mesh.GetBoundingBox(true)));
    }

    private static Result<Point3d> ComputeFromSurface(global::Rhino.Geometry.Surface surface)
    {
        return TryComputeCentroid(
            null,
            () => AreaMassProperties.Compute(surface),
            () => BoundingBoxCentroid(surface.GetBoundingBox(true)));
    }

    private static Result<Point3d> ComputeFromCurveClosed(global::Rhino.Geometry.Curve curve)
    {
        try
        {
            using LengthMassProperties? length = LengthMassProperties.Compute(curve);
            if (length is not null && length.Centroid.IsValid)
            {
                return Result<Point3d>.Success(length.Centroid);
            }
        }
        catch
        {
            // fall back to bounding box
        }

        return Result<Point3d>.Success(BoundingBoxCentroid(curve.GetBoundingBox(true)));
    }

    private static Result<Point3d> ComputeFromCurveOpen(global::Rhino.Geometry.Curve curve)
    {
        return Result<Point3d>.Success(BoundingBoxCentroid(curve.GetBoundingBox(true)));
    }

    private static Result<Point3d> TryComputeCentroid(
        Func<VolumeMassProperties?>? volumeFunc,
        Func<AreaMassProperties?>? areaFunc,
        Func<Point3d> fallback)
    {
        VolumeMassProperties? volumeProps = null;
        AreaMassProperties? areaProps = null;

        try
        {
            if (volumeFunc is not null)
            {
                volumeProps = volumeFunc();
                if (volumeProps is not null && volumeProps.Centroid.IsValid)
                {
                    return Result<Point3d>.Success(volumeProps.Centroid);
                }
            }

            if (areaFunc is not null)
            {
                areaProps = areaFunc();
                if (areaProps is not null && areaProps.Centroid.IsValid)
                {
                    return Result<Point3d>.Success(areaProps.Centroid);
                }
            }
        }
        finally
        {
            volumeProps?.Dispose();
            areaProps?.Dispose();
        }

        Point3d fallbackPoint = fallback();
        return Result<Point3d>.Success(fallbackPoint);
    }

    private static Result<Point3d> WithBrep(global::Rhino.Geometry.Brep? brep, Func<global::Rhino.Geometry.Brep, Result<Point3d>> selector)
    {
        if (brep is null)
        {
            return Result<Point3d>.Fail(new Failure("geometry.brepConversion", "Failed to convert geometry to brep."));
        }

        using (brep)
        {
            return selector(brep);
        }
    }

    private static Point3d BoundingBoxCentroid(BoundingBox box)
    {
        return box is { IsValid: true, Center.IsValid: true } ? box.Center : Point3d.Origin;
    }
}

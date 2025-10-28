using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Core;
using Arsenal.Rhino.Geometry.Curves;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Boundary computation service for Rhino geometry.</summary>
public interface IBoundsCalculator
{
    /// <summary>Creates a bounding box that includes the specified points.</summary>
    Result<BoundingBox> FromPoints(IEnumerable<Point3d>? points, double margin = 0, BoundingBox baseBounds = default);

    /// <summary>Creates a bounding box that includes the specified curves.</summary>
    Result<BoundingBox> FromCurves(IEnumerable<Curve?>? curves, GeoContext context, double labelMargin = 0,
        BoundingBox baseBounds = default);

    /// <summary>Creates a bounding box that includes the specified geometry.</summary>
    Result<BoundingBox> FromGeometry(IEnumerable<GeometryBase?>? geometries, GeoContext context, double labelMargin = 0,
        BoundingBox baseBounds = default);
}

/// <summary>Default implementation of <see cref="IBoundsCalculator"/> using curve and centroid services.</summary>
public sealed class BoundsCalculator : IBoundsCalculator
{
    private readonly ICurve _curves;
    private readonly ICentroid _centroids;

    /// <summary>Initializes a new instance of the <see cref="BoundsCalculator"/> class.</summary>
    public BoundsCalculator(ICurve curves, ICentroid centroids)
    {
        _curves = curves ?? throw new ArgumentNullException(nameof(curves));
        _centroids = centroids ?? throw new ArgumentNullException(nameof(centroids));
    }

    /// <inheritdoc/>
    public Result<BoundingBox> FromPoints(IEnumerable<Point3d>? points, double margin = 0,
        BoundingBox baseBounds = default)
    {
        BoundingBox accumulator = baseBounds;
        bool hasData = accumulator.IsValid;

        if (points is null)
        {
            return Result<BoundingBox>.Success(accumulator);
        }

        foreach (Point3d point in points)
        {
            BoundingBox pointBox = new(point, point);
            if (margin > 0)
            {
                pointBox.Inflate(margin);
            }

            Include(ref accumulator, pointBox);
            hasData = true;
        }

        return Result<BoundingBox>.Success(hasData ? accumulator : baseBounds);
    }

    /// <inheritdoc/>
    public Result<BoundingBox> FromCurves(IEnumerable<Curve?>? curves, GeoContext context, double labelMargin = 0,
        BoundingBox baseBounds = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        BoundingBox accumulator = baseBounds;
        bool hasData = accumulator.IsValid;

        if (curves is null)
        {
            return Result<BoundingBox>.Success(accumulator);
        }

        foreach (Curve? curve in curves)
        {
            if (curve is null)
            {
                continue;
            }

            BoundingBox curveBox = curve.GetBoundingBox(true);
            Include(ref accumulator, curveBox);
            hasData = true;

            if (labelMargin <= 0)
            {
                continue;
            }

            Result<Point3d> midpoint = _curves.Midpoint(curve);
            if (!midpoint.IsSuccess)
            {
                return Result<BoundingBox>.Fail(midpoint.Failure!);
            }

            Include(ref accumulator, CreateMarginBox(midpoint.Value, labelMargin));
        }

        return Result<BoundingBox>.Success(hasData ? accumulator : baseBounds);
    }

    /// <inheritdoc/>
    public Result<BoundingBox> FromGeometry(IEnumerable<GeometryBase?>? geometries, GeoContext context,
        double labelMargin = 0, BoundingBox baseBounds = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        BoundingBox accumulator = baseBounds;
        bool hasData = accumulator.IsValid;

        if (geometries is null)
        {
            return Result<BoundingBox>.Success(accumulator);
        }

        foreach (GeometryBase? geometry in geometries)
        {
            if (geometry is null)
            {
                continue;
            }

            BoundingBox geometryBox = geometry.GetBoundingBox(true);
            Include(ref accumulator, geometryBox);
            hasData = true;

            if (labelMargin <= 0)
            {
                continue;
            }

            Result<Point3d> centroid = _centroids.Compute(geometry, context);
            if (!centroid.IsSuccess)
            {
                return Result<BoundingBox>.Fail(centroid.Failure!);
            }

            Include(ref accumulator, CreateMarginBox(centroid.Value, labelMargin));
        }

        return Result<BoundingBox>.Success(hasData ? accumulator : baseBounds);
    }

    private static void Include(ref BoundingBox accumulator, BoundingBox addition)
    {
        if (!addition.IsValid)
        {
            return;
        }

        if (!accumulator.IsValid)
        {
            accumulator = addition;
            return;
        }

        accumulator.Union(addition);
    }

    private static BoundingBox CreateMarginBox(Point3d center, double margin)
    {
        BoundingBox box = new(center, center);
        box.Inflate(margin);
        return box;
    }
}

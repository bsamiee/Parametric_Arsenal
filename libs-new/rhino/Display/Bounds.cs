using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Facade;
using Rhino.Geometry;

namespace Arsenal.Rhino.Display;

/// <summary>Preview-oriented helpers for computing bounding boxes with optional label padding.</summary>
public sealed class PreviewBounds
{
    private readonly GeometryOps _geometry;

    /// <summary>Initializes a new instance of the PreviewBounds class.</summary>
    /// <param name="geometry">The geometry operations facade to use for calculations.</param>
    public PreviewBounds(GeometryOps geometry)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
    }

    /// <summary>Computes a bounding box for a collection of points with optional margin.</summary>
    /// <param name="points">The points to compute bounds for.</param>
    /// <param name="margin">Optional margin to inflate the bounding box.</param>
    /// <returns>The computed bounding box.</returns>
    public BoundingBox ForPoints(IEnumerable<Point3d>? points, double margin = 0)
    {
        if (points is null)
        {
            return BoundingBox.Empty;
        }

        BoundingBox box = new(points);
        if (!box.IsValid || margin <= 0)
        {
            return box;
        }

        box.Inflate(margin);
        return box;
    }

    /// <summary>Computes a bounding box for curves with optional label margin around midpoints.</summary>
    /// <param name="curves">The curves to compute bounds for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <param name="labelMargin">Optional margin around curve midpoints for labels.</param>
    /// <returns>A result containing the computed bounding box or a failure.</returns>
    public Result<BoundingBox> ForCurves(IEnumerable<global::Rhino.Geometry.Curve>? curves, GeoContext context, double labelMargin = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<global::Rhino.Geometry.Curve>> collectionResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!collectionResult.IsSuccess)
        {
            return Result<BoundingBox>.Fail(collectionResult.Failure!);
        }

        IReadOnlyCollection<global::Rhino.Geometry.Curve> collection = collectionResult.Value!;

        BoundingBox box = BoundingBox.Empty;
        foreach (global::Rhino.Geometry.Curve curve in collection)
        {
            box.Union(curve.GetBoundingBox(true));

            if (labelMargin > 0)
            {
                Result<Point3d> midpoint = _geometry.CurveMidpoint(curve);
                if (midpoint.IsSuccess)
                {
                    box.Union(CreateMarginBox(midpoint.Value, labelMargin));
                }
            }
        }

        return Result<BoundingBox>.Success(box);
    }

    /// <summary>Computes a bounding box for geometry with optional label margin around centroids.</summary>
    /// <param name="geometries">The geometry to compute bounds for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <param name="labelMargin">Optional margin around geometry centroids for labels.</param>
    /// <returns>A result containing the computed bounding box or a failure.</returns>
    public Result<BoundingBox> ForGeometry(IEnumerable<GeometryBase>? geometries, GeoContext context, double labelMargin = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<GeometryBase>> collectionResult = Guard.AgainstEmpty(geometries, nameof(geometries));
        if (!collectionResult.IsSuccess)
        {
            return Result<BoundingBox>.Fail(collectionResult.Failure!);
        }

        IReadOnlyCollection<GeometryBase> collection = collectionResult.Value!;

        BoundingBox box = BoundingBox.Empty;
        foreach (GeometryBase geometry in collection)
        {
            box.Union(geometry.GetBoundingBox(true));

            if (labelMargin > 0)
            {
                Result<Point3d> centroidResult = _geometry.Centroid(geometry, context);
                if (centroidResult.IsSuccess)
                {
                    box.Union(CreateMarginBox(centroidResult.Value, labelMargin));
                }
            }
        }

        return Result<BoundingBox>.Success(box);
    }

    private static BoundingBox CreateMarginBox(Point3d center, double margin)
    {
        return new BoundingBox(
            center.X - margin, center.Y - margin, center.Z - margin,
            center.X + margin, center.Y + margin, center.Z + margin);
    }
}

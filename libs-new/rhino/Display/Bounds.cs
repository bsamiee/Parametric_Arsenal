using System;
using System.Collections.Generic;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Facade;
using Rhino.Geometry;

namespace Arsenal.Rhino.Display;

/// <summary>Preview bounding box utilities with label padding.</summary>
public sealed class PreviewBounds
{
    private readonly GeometryOps _geometry;

    /// <summary>Initializes preview bounds with geometry operations.</summary>
    public PreviewBounds(GeometryOps geometry)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
    }

    /// <summary>Computes bounding box for points with optional margin.</summary>
    public static BoundingBox ForPoints(IEnumerable<Point3d>? points, double margin = 0)
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

    /// <summary>Computes bounding box for curves with optional label margin.</summary>
    public Result<BoundingBox> ForCurves(IEnumerable<Curve>? curves, GeoContext context, double labelMargin = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<Curve>> collectionResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!collectionResult.IsSuccess)
        {
            return Result<BoundingBox>.Fail(collectionResult.Failure!);
        }

        IReadOnlyCollection<Curve> collection = collectionResult.Value!;

        BoundingBox box = BoundingBox.Empty;
        foreach (Curve curve in collection)
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

    /// <summary>Computes bounding box for geometry with optional label margin.</summary>
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

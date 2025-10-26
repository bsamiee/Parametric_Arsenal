using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Unified bounding box utilities for preview and viewport operations.</summary>
public static class PreviewBounds
{
    /// <summary>Default margin for preview dots in world coordinates.</summary>
    public const double DefaultDotMargin = 1.0;

    /// <summary>Computes bounding box from locations using a generic selector pattern.</summary>
    public static BoundingBox FromLocations<T>(
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        double margin = 0,
        BoundingBox baseBounds = default)
    {
        if (items is null)
        {
            return baseBounds;
        }

        ArgumentNullException.ThrowIfNull(locationSelector);

        BoundingBox bbox = baseBounds;

        if (margin > 0)
        {
            // Add margin around each location for preview elements
            foreach (T item in items)
            {
                Point3d location = locationSelector(item);
                bbox.Union(CreateMarginBounds(location, margin));
            }
        }
        else
        {
            // Simple point union without margin
            foreach (T item in items)
            {
                bbox.Union(locationSelector(item));
            }
        }

        return bbox;
    }

    /// <summary>Computes bounding box for a collection of points with optional margin.</summary>
    public static BoundingBox ForPoints(
        IEnumerable<Point3d>? points,
        double margin = 0,
        BoundingBox baseBounds = default)
    {
        if (points is null)
        {
            return baseBounds;
        }

        // Use SDK optimization for point collections without margin
        if (margin <= 0)
        {
            BoundingBox result = baseBounds;
            result.Union(new BoundingBox(points));
            return result;
        }

        // Fall back to location-based calculation with margin
        return FromLocations(points, p => p, margin, baseBounds);
    }

    /// <summary>Computes bounding box for a collection of curves with optional margin at midpoints.</summary>
    public static BoundingBox ForCurves(
        IEnumerable<Curve>? curves,
        double marginAtMidpoint = 0,
        BoundingBox baseBounds = default)
    {
        return ForGeometryWithLabelMargin(curves, GeometryCentroids.GetCentroid, marginAtMidpoint, baseBounds);
    }

    /// <summary>Computes bounding box for a collection of geometry faces with optional margin at centroids.</summary>
    public static BoundingBox ForFaces(
        IEnumerable<GeometryBase>? faces,
        double marginAtCentroid = 0,
        BoundingBox baseBounds = default)
    {
        return ForGeometryWithLabelMargin(faces, GeometryCentroids.GetCentroid, marginAtCentroid, baseBounds);
    }

    /// <summary>Computes bounding box for generic geometry collection.</summary>
    public static BoundingBox ForGeometry(
        IEnumerable<GeometryBase>? geometries,
        BoundingBox baseBounds = default)
    {
        if (geometries is null)
        {
            return baseBounds;
        }

        BoundingBox bbox = baseBounds;

        foreach (GeometryBase geom in geometries)
        {
            bbox.Union(geom.GetBoundingBox(true));
        }

        return bbox;
    }

    /// <summary>Inflates a bounding box by a specified amount in all directions.</summary>
    public static BoundingBox Inflate(BoundingBox bbox, double amount)
    {
        if (!bbox.IsValid || amount <= 0)
        {
            return bbox;
        }

        BoundingBox inflated = bbox;
        inflated.Inflate(amount);
        return inflated;
    }

    /// <summary>Computes the union of multiple bounding boxes.</summary>
    public static BoundingBox Union(params BoundingBox[] boxes)
    {
        if (boxes.Length == 0)
        {
            return BoundingBox.Empty;
        }

        BoundingBox result = boxes[0];
        for (int i = 1; i < boxes.Length; i++)
        {
            result.Union(boxes[i]);
        }

        return result;
    }

    /// <summary>Consolidated pattern for geometry bounds with optional label margin.</summary>
    private static BoundingBox ForGeometryWithLabelMargin<T>(
        IEnumerable<T>? geometries,
        Func<T, Point3d> labelLocationSelector,
        double labelMargin,
        BoundingBox baseBounds) where T : GeometryBase
    {
        if (geometries is null)
        {
            return baseBounds;
        }

        BoundingBox bbox = baseBounds;

        foreach (T geometry in geometries)
        {
            // Include geometry bounds
            bbox.Union(geometry.GetBoundingBox(true));

            // Add margin at label location if needed
            if (labelMargin > 0)
            {
                Point3d labelLocation = labelLocationSelector(geometry);
                bbox.Union(CreateMarginBounds(labelLocation, labelMargin));
            }
        }

        return bbox;
    }

    /// <summary>Creates a bounding box around a point with specified margin.</summary>
    private static BoundingBox CreateMarginBounds(Point3d center, double margin)
    {
        return new BoundingBox(
            center.X - margin, center.Y - margin, center.Z - margin,
            center.X + margin, center.Y + margin, center.Z + margin);
    }
}

using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Spatial;
using Rhino.Geometry;

namespace Arsenal.Grasshopper.Preview;

/// <summary>Preview-oriented wrapper around Rhino boundary calculations.</summary>
public sealed class PreviewBounds
{
    /// <summary>Default margin applied to dot previews.</summary>
    public const double DefaultDotMargin = 1.0;

    private readonly IBoundsCalculator _bounds;

    /// <summary>Initializes a new instance of the <see cref="PreviewBounds"/> class.</summary>
    public PreviewBounds(IBoundsCalculator bounds)
    {
        _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
    }

    /// <summary>Computes preview bounds for point data.</summary>
    public Result<BoundingBox> ForPoints(IEnumerable<Point3d>? points, double margin = 0, BoundingBox baseBounds = default) =>
        _bounds.FromPoints(points, margin, baseBounds);

    /// <summary>Computes preview bounds for curve data.</summary>
    public Result<BoundingBox> ForCurves(IEnumerable<Curve>? curves, GeoContext context, double labelMargin = 0, BoundingBox baseBounds = default) =>
        _bounds.FromCurves(curves, context, labelMargin, baseBounds);

    /// <summary>Computes preview bounds for general geometry data.</summary>
    public Result<BoundingBox> ForGeometry(IEnumerable<GeometryBase>? geometries, GeoContext context, double labelMargin = 0, BoundingBox baseBounds = default) =>
        _bounds.FromGeometry(geometries, context, labelMargin, baseBounds);
}

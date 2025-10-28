using System;
using System.Collections.Generic;
using System.Drawing;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Core;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Arsenal.Grasshopper.Preview;

/// <summary>Interface for rendering numbered dots in Grasshopper previews.</summary>
public interface INumberedDotRenderer
{
    /// <summary>Renders numbered dots at specified locations using a location selector.</summary>
    Result DrawAtLocations<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null);

    /// <summary>Renders numbered dots at point locations.</summary>
    Result DrawAtPoints(
        IGH_PreviewArgs? args,
        IEnumerable<Point3d>? points,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null);

    /// <summary>Renders numbered dots at curve midpoints.</summary>
    Result DrawAtCurveMidpoints(
        IGH_PreviewArgs? args,
        IEnumerable<Curve>? curves,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null);

    /// <summary>Renders numbered dots at geometry centroids.</summary>
    Result DrawAtCentroids(
        IGH_PreviewArgs? args,
        IEnumerable<GeometryBase>? geometries,
        ICentroid centroid,
        GeoContext context,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null);

    /// <summary>Renders numbered dots with custom text formatting.</summary>
    Result DrawWithCustomText<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        Func<T, int, string> textFormatter,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null);
}

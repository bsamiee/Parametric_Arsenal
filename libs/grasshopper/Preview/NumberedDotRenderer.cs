using System;
using System.Collections.Generic;
using System.Drawing;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Core;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Arsenal.Grasshopper.Preview;

/// <summary>Default implementation for rendering numbered dots in Grasshopper previews.</summary>
public sealed class NumberedDotRenderer : INumberedDotRenderer
{
    /// <summary>Gets the singleton instance.</summary>
    public static NumberedDotRenderer Instance { get; } = new();

    private NumberedDotRenderer()
    {
    }
    /// <inheritdoc/>
    public Result DrawAtLocations<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        Result<IGH_PreviewArgs> argsResult = Guard.AgainstNull(args, nameof(args));
        if (!argsResult.IsSuccess)
        {
            return Result.Fail(argsResult.Failure!);
        }

        if (items is null)
        {
            return Result.Success();
        }

        ArgumentNullException.ThrowIfNull(locationSelector);

        Color bgColor = backgroundColor ?? PreviewColors.DotBackground;
        Color fgColor = textColor ?? PreviewColors.DotText;

        DisplayPipeline display = args!.Display;
        int index = startIndex;

        foreach (T item in items)
        {
            Point3d location = locationSelector(item);
            string label = index.ToString();
            display.DrawDot(location, label, bgColor, fgColor);
            index++;
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public Result DrawAtPoints(
        IGH_PreviewArgs? args,
        IEnumerable<Point3d>? points,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        return DrawAtLocations(args, points, p => p, startIndex, backgroundColor, textColor);
    }

    /// <inheritdoc/>
    public Result DrawAtCurveMidpoints(
        IGH_PreviewArgs? args,
        IEnumerable<Curve>? curves,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        return DrawAtLocations(
            args,
            curves,
            curve => curve.PointAt(curve.Domain.Mid),
            startIndex,
            backgroundColor,
            textColor);
    }

    /// <inheritdoc/>
    public Result DrawAtCentroids(
        IGH_PreviewArgs? args,
        IEnumerable<GeometryBase>? geometries,
        ICentroid centroid,
        GeoContext context,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        ArgumentNullException.ThrowIfNull(centroid);
        ArgumentNullException.ThrowIfNull(context);

        return DrawAtLocations(
            args,
            geometries,
            geometry =>
            {
                Result<Point3d> centroidResult = centroid.Compute(geometry, context);
                return centroidResult.IsSuccess ? centroidResult.Value : Point3d.Origin;
            },
            startIndex,
            backgroundColor,
            textColor);
    }

    /// <inheritdoc/>
    public Result DrawWithCustomText<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        Func<T, int, string> textFormatter,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        Result<IGH_PreviewArgs> argsResult = Guard.AgainstNull(args, nameof(args));
        if (!argsResult.IsSuccess)
        {
            return Result.Fail(argsResult.Failure!);
        }

        if (items is null)
        {
            return Result.Success();
        }

        ArgumentNullException.ThrowIfNull(locationSelector);
        ArgumentNullException.ThrowIfNull(textFormatter);

        Color bgColor = backgroundColor ?? PreviewColors.DotBackground;
        Color fgColor = textColor ?? PreviewColors.DotText;

        DisplayPipeline display = args!.Display;
        int index = startIndex;

        foreach (T item in items)
        {
            Point3d location = locationSelector(item);
            string label = textFormatter(item, index);
            display.DrawDot(location, label, bgColor, fgColor);
            index++;
        }

        return Result.Success();
    }
}

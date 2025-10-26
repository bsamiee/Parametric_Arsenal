using System;
using System.Collections.Generic;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Arsenal.Grasshopper.Preview;

/// <summary>Default preview colors for consistent styling.</summary>
public static class PreviewColors
{
    /// <summary>Default background color for preview dots.</summary>
    public static readonly Color DotBackground = Color.FromArgb(80, 80, 80);
    /// <summary>Default text color for preview dots.</summary>
    public static readonly Color DotText = Color.White;
}

/// <summary>Specialized renderer for numbered dot preview style.</summary>
public static class NumberedDotRenderer
{
    /// <summary>Renders numbered dots at specified locations using a location selector.</summary>
    public static Result DrawAtLocations<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        Result<IGH_PreviewArgs> argsValidation = Guard.RequireNonNull(args, nameof(args));
        if (!argsValidation.Ok)
        {
            return Result.Fail(argsValidation.Error!);
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

    /// <summary>Renders numbered dots at point locations.</summary>
    public static Result DrawAtPoints(
        IGH_PreviewArgs? args,
        IEnumerable<Point3d>? points,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        return DrawAtLocations(args, points, p => p, startIndex, backgroundColor, textColor);
    }

    /// <summary>Renders numbered dots at curve midpoints.</summary>
    public static Result DrawAtCurveMidpoints(
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

    /// <summary>Renders numbered dots at geometry centroids.</summary>
    public static Result DrawAtCentroids(
        IGH_PreviewArgs? args,
        IEnumerable<GeometryBase>? geometries,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        return DrawAtLocations(
            args,
            geometries,
            GeometryCentroids.GetCentroid,
            startIndex,
            backgroundColor,
            textColor);
    }

    /// <summary>Renders numbered dots with custom text formatting.</summary>
    public static Result DrawWithCustomText<T>(
        IGH_PreviewArgs? args,
        IEnumerable<T>? items,
        Func<T, Point3d> locationSelector,
        Func<T, int, string> textFormatter,
        int startIndex = 0,
        Color? backgroundColor = null,
        Color? textColor = null)
    {
        Result<IGH_PreviewArgs> argsValidation = Guard.RequireNonNull(args, nameof(args));
        if (!argsValidation.Ok)
        {
            return Result.Fail(argsValidation.Error!);
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

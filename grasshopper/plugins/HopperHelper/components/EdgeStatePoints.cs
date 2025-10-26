using System;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Preview;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using HopperHelper.Utils;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts edge state points from geometry: quadrants for circles, midpoints for edges.</summary>
public class EdgeStatePoints() : ExtractorBase<Point3d>("Edge State Points", "EdgePts",
    "Extracts edge state points: circle quadrants or edge midpoints",
    "HopperHelper", "Extraction")
{
    protected override Result<Point3d[]> ExtractElements(GeometryBase geometry)
    {
        return ElementExtraction.ExtractEdgeStatePoints(geometry);
    }

    protected override IGH_Param CreateOutputParameter()
    {
        return new Grasshopper.Kernel.Parameters.Param_Point
        {
            Name = "Edge State Points",
            NickName = "SP",
            Description = "Edge state points: quadrants for circles, midpoints for edges and curves",
            Access = GH_ParamAccess.list
        };
    }

    protected override string GetElementTypeName()
    {
        return "edge state points";
    }

    protected override Result DrawPreview(IGH_PreviewArgs args, Point3d[] elements)
    {
        return NumberedDotRenderer.DrawAtPoints(args, elements);
    }

    protected override BoundingBox CalculateClippingBox(Point3d[] elements)
    {
        return PreviewBounds.ForPoints(elements, PreviewBounds.DefaultDotMargin);
    }

    public override Guid ComponentGuid => new("F8A7B9C2-4D5E-4F67-8A9B-1C3D5E7F9A2B");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => null;
}

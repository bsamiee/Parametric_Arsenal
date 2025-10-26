using System;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Preview;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using HopperHelper.Utils;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts all vertices from input geometry as Point3d objects.</summary>
public class VertexExtractor() : ExtractorBase<Point3d>("Vertex Extractor", "Vertices",
    "Extracts all vertices from input geometry as points",
    "HopperHelper", "Extraction")
{
    protected override Result<Point3d[]> ExtractElements(GeometryBase geometry)
    {
        return ElementExtraction.ExtractVertices(geometry);
    }

    protected override IGH_Param CreateOutputParameter()
    {
        return new Grasshopper.Kernel.Parameters.Param_Point
        {
            Name = "Vertices",
            NickName = "V",
            Description = "Extracted vertices as points",
            Access = GH_ParamAccess.list
        };
    }

    protected override string GetElementTypeName()
    {
        return "vertices";
    }

    protected override Result DrawPreview(IGH_PreviewArgs args, Point3d[] elements)
    {
        return NumberedDotRenderer.DrawAtPoints(args, elements);
    }

    protected override BoundingBox CalculateClippingBox(Point3d[] elements)
    {
        return PreviewBounds.ForPoints(elements, PreviewBounds.DefaultDotMargin);
    }

    public override Guid ComponentGuid => new("B2C3D4E5-F6A7-8901-BCDE-F23456789012");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => null;
}

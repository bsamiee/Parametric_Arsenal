using System;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Preview;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using HopperHelper.Utils;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts all edges from input geometry as curve objects.</summary>
public class EdgeExtractor() : ExtractorBase<Curve>("Edge Extractor", "Edges",
    "Extracts all edges from input geometry as curves",
    "HopperHelper", "Extraction")
{
    protected override Result<Curve[]> ExtractElements(GeometryBase geometry)
    {
        return ElementExtraction.ExtractEdges(geometry);
    }

    protected override IGH_Param CreateOutputParameter()
    {
        return new Grasshopper.Kernel.Parameters.Param_Curve
        {
            Name = "Edges",
            NickName = "E",
            Description = "Extracted edges as curves",
            Access = GH_ParamAccess.list
        };
    }

    protected override string GetElementTypeName()
    {
        return "edges";
    }

    protected override Result DrawPreview(IGH_PreviewArgs args, Curve[] elements)
    {
        return NumberedDotRenderer.DrawAtCurveMidpoints(args, elements);
    }

    protected override BoundingBox CalculateClippingBox(Curve[] elements)
    {
        return PreviewBounds.ForCurves(elements, PreviewBounds.DefaultDotMargin);
    }

    public override Guid ComponentGuid => new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => null;
}

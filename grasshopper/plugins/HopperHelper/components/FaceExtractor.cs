using System;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Preview;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using HopperHelper.Utils;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts all faces from input geometry as surface or mesh face objects.</summary>
public class FaceExtractor() : ExtractorBase<GeometryBase>("Face Extractor", "Faces",
    "Extracts all faces from input geometry as surfaces or mesh faces",
    "HopperHelper", "Extraction")
{
    protected override Result<GeometryBase[]> ExtractElements(GeometryBase geometry)
    {
        return ElementExtraction.ExtractFaces(geometry);
    }

    protected override IGH_Param CreateOutputParameter()
    {
        return new Grasshopper.Kernel.Parameters.Param_Geometry
        {
            Name = "Faces",
            NickName = "F",
            Description = "Extracted faces as surfaces or mesh faces",
            Access = GH_ParamAccess.list
        };
    }

    protected override string GetElementTypeName()
    {
        return "faces";
    }

    protected override Result DrawPreview(IGH_PreviewArgs args, GeometryBase[] elements)
    {
        return NumberedDotRenderer.DrawAtCentroids(args, elements);
    }

    protected override BoundingBox CalculateClippingBox(GeometryBase[] elements)
    {
        return PreviewBounds.ForFaces(elements, PreviewBounds.DefaultDotMargin);
    }

    public override Guid ComponentGuid => new("C3D4E5F6-A7B8-9012-CDEF-345678901234");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => null;
}

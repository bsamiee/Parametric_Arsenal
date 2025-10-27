using System;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Components;
using Arsenal.Grasshopper.Conversion;
using Arsenal.Rhino.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts the centroid point from input geometry using the refactored GeometryCentroids library.</summary>
public class CentroidExtractor() : GhComponentBase("Centroid Extractor", "Centroid",
    "Extracts the centroid point from input geometry using automatic SDK method selection",
    "HopperHelper", "Extraction")
{
    /// <summary>Registers input and output parameters for the component.</summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Input geometry to extract centroid from (Point, Curve, Surface, Mesh, Brep, SubD, etc.)",
            GH_ParamAccess.item);
    }

    /// <summary>Registers output parameters for the component.</summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Centroid", "C",
            "The centroid point of the input geometry",
            GH_ParamAccess.item);
    }

    protected override void GuardedSolve(IGH_DataAccess DA)
    {
        // Get input geometry using type-safe parameter access
        Result<IGH_GeometricGoo> inputResult = ParameterAccess.GetItem<IGH_GeometricGoo>(DA, 0);
        if (!ReportIfFailed(inputResult))
        {
            return;
        }

        // Convert to GeometryBase using safe geometry conversion
        Result<GeometryBase> geometryResult = GeometryConversion.ToGeometryBase(inputResult.Value!);
        if (!ReportIfFailed(geometryResult))
        {
            return;
        }

        // Calculate centroid using refactored GeometryCentroids library
        Point3d centroid = GeometryCentroids.GetCentroid(geometryResult.Value!);

        // Set output
        DA.SetData(0, centroid);
    }

    /// <summary>Unique component GUID.</summary>
    public override Guid ComponentGuid => new("B7E8F9A0-1234-5678-9ABC-DEF012345678");

    /// <summary>Component exposure level.</summary>
    public override GH_Exposure Exposure => GH_Exposure.primary;

    /// <summary>Component icon (null for default).</summary>
    protected override Bitmap? Icon => null;
}
using System;
using System.Collections.Generic;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Components;
using Arsenal.Grasshopper.Conversion;
using Arsenal.Rhino.Analysis;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace HopperHelper.Components;

/// <summary>Extracts directional vectors from input geometry using the VectorExtraction library.</summary>
public class VectorExtractor() : GhComponentBase("Vector Extractor", "Vectors",
    "Extracts directional vectors from input geometry (tangent, normal, U/V directions)",
    "HopperHelper", "Extraction")
{
    /// <summary>Registers input parameters for the component.</summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Input geometry to extract vectors from (Surface, Curve, Mesh, Brep, etc.)",
            GH_ParamAccess.item);
    }

    /// <summary>Registers output parameters for the component.</summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddVectorParameter("Tangent Vectors", "T", "Tangent vectors from curves", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normal Vectors", "N", "Normal vectors from surfaces and meshes", GH_ParamAccess.list);
        pManager.AddVectorParameter("U Directions", "U", "U direction vectors from surfaces", GH_ParamAccess.list);
        pManager.AddVectorParameter("V Directions", "V", "V direction vectors from surfaces", GH_ParamAccess.list);
        pManager.AddPointParameter("Extraction Points", "Pts", "Points where vectors were extracted", GH_ParamAccess.list);
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

        GeometryBase geometry = geometryResult.Value!;

        // Extract tangent vectors using VectorExtraction library
        Result<Vector3d[]> tangentResult = VectorExtraction.ExtractTangentVectors(geometry);
        List<Vector3d> tangentVectors = tangentResult.Ok ? [.. tangentResult.Value!] : [];

        // Extract normal vectors using VectorExtraction library
        Result<Vector3d[]> normalResult = VectorExtraction.ExtractNormalVectors(geometry);
        List<Vector3d> normalVectors = normalResult.Ok ? [.. normalResult.Value!] : [];

        // Extract U direction vectors using VectorExtraction library
        Result<Vector3d[]> uDirectionResult = VectorExtraction.ExtractUDirectionVectors(geometry);
        List<Vector3d> uDirections = uDirectionResult.Ok ? [.. uDirectionResult.Value!] : [];

        // Extract V direction vectors using VectorExtraction library
        Result<Vector3d[]> vDirectionResult = VectorExtraction.ExtractVDirectionVectors(geometry);
        List<Vector3d> vDirections = vDirectionResult.Ok ? [.. vDirectionResult.Value!] : [];

        // Extract extraction points using VectorExtraction library
        Result<Point3d[]> pointsResult = VectorExtraction.ExtractVectorPoints(geometry);
        List<Point3d> extractionPoints = pointsResult.Ok ? [.. pointsResult.Value!] : [];

        // Report warnings if no vectors could be extracted
        if (tangentVectors.Count == 0 && normalVectors.Count == 0 &&
            uDirections.Count == 0 && vDirections.Count == 0)
        {
            ReportWarning("No directional vectors could be extracted from the input geometry");
        }

        // Set outputs
        DA.SetDataList(0, tangentVectors);
        DA.SetDataList(1, normalVectors);
        DA.SetDataList(2, uDirections);
        DA.SetDataList(3, vDirections);
        DA.SetDataList(4, extractionPoints);
    }

    /// <summary>Unique component GUID.</summary>
    public override Guid ComponentGuid => new("D4E5F6A7-B8C9-0123-DEFG-456789012345");

    /// <summary>Component exposure level.</summary>
    public override GH_Exposure Exposure => GH_Exposure.primary;

    /// <summary>Component icon (null for default).</summary>
    protected override Bitmap? Icon => null;
}

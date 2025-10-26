using System.Collections.Generic;
using System.Drawing;
using Arsenal.Core;
using Arsenal.Grasshopper.Components;
using Arsenal.Grasshopper.Conversion;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace HopperHelper.Utils;

/// <summary>
/// Base class for geometry extraction components that extract elements from geometry with numbered preview.
/// Consolidates common patterns for input parameters, validation, and preview rendering.
/// </summary>
public abstract class ExtractorBase<T>(
    string name,
    string nickname,
    string description,
    string category,
    string subcategory)
    : GhComponentBase(name, nickname, description, category, subcategory)
{
    private T[] _extractedElements = [];
    private bool _showLabels = true;

    /// <summary>Registers standard input parameters for geometry extraction components.</summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Input geometry to extract elements from",
            GH_ParamAccess.item);
        pManager.AddBooleanParameter("Disable Labels", "D",
            "Disable numbered labels in viewport",
            GH_ParamAccess.item, false);
        pManager[1].Optional = true;
    }

    /// <summary>Registers output parameters using the derived class's parameter definition.</summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(CreateOutputParameter());
    }

    /// <summary>Implements the standard geometry extraction workflow with error handling.</summary>
    protected override void GuardedSolve(IGH_DataAccess DA)
    {
        Result<IGH_GeometricGoo> gooResult = ParameterAccess.GetItem<IGH_GeometricGoo>(DA, 0);
        if (!ReportIfFailed(gooResult))
        {
            return;
        }

        Result<bool> disableLabelsResult = ParameterAccess.GetOptionalValue(DA, 1, false);
        if (!ReportIfFailed(disableLabelsResult))
        {
            return;
        }

        bool disableLabels = disableLabelsResult.Value;

        Result<GeometryBase> conversionResult = GeometryConversion.ToGeometryBase(gooResult.Value!);
        if (!ReportIfFailed(conversionResult))
        {
            return;
        }

        Result<T[]> extractionResult = ExtractElements(conversionResult.Value!);
        if (!ReportIfFailed(extractionResult))
        {
            return;
        }

        T[] elements = extractionResult.Value!;
        if (elements.Length == 0)
        {
            ReportWarning($"No {GetElementTypeName()} found in geometry");
            _extractedElements = [];
            _showLabels = !disableLabels;
            DA.SetDataList(0, new List<T>());
            return;
        }

        _extractedElements = elements;
        _showLabels = !disableLabels;
        DA.SetDataList(0, elements);
    }

    /// <summary>Draws numbered preview dots for extracted elements when labels are enabled.</summary>
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        if (!Hidden && _showLabels && _extractedElements.Length > 0)
        {
            DrawPreview(args, _extractedElements);
        }
    }

    /// <summary>Gets the clipping box for preview elements including margin for numbered dots.</summary>
    public override BoundingBox ClippingBox
    {
        get
        {
            if (Hidden || !_showLabels || _extractedElements.Length == 0)
            {
                return BoundingBox.Empty;
            }

            return CalculateClippingBox(_extractedElements);
        }
    }

    /// <summary>Abstract method to extract elements from geometry using appropriate ElementExtraction method.</summary>
    protected abstract Result<T[]> ExtractElements(GeometryBase geometry);

    /// <summary>Abstract method to create the appropriate output parameter for the element type.</summary>
    protected abstract IGH_Param CreateOutputParameter();

    /// <summary>Abstract method to get the element type name for error messages.</summary>
    protected abstract string GetElementTypeName();

    /// <summary>Abstract method to draw preview for the extracted elements.</summary>
    protected abstract Result DrawPreview(IGH_PreviewArgs args, T[] elements);

    /// <summary>Abstract method to calculate clipping box for the extracted elements.</summary>
    protected abstract BoundingBox CalculateClippingBox(T[] elements);

    /// <summary>Gets the component icon (null for default).</summary>
    protected override Bitmap? Icon => null;
}
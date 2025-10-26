using System;
using System.Collections.Generic;
using Arsenal.Core;
using Arsenal.Grasshopper.Components;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace HopperHelper.Components;

/// <summary>
/// Selects items from a list by index, supporting negative indices.
/// </summary>
public class ListSelector() : GhComponentBase("List Selector", "ListSel",
    "Select items from a list by index (supports negative indices)",
    "HopperHelper", "Utilities")
{
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("List", "L",
            "List to select from",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Indices", "I",
            "Indices to select (negative indices count from end)",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Selected Item", "S",
            "Selected item from the list",
            GH_ParamAccess.list);
    }

    protected override void GuardedSolve(IGH_DataAccess DA)
    {
        Result<List<IGH_Goo>> listResult = ParameterAccess.GetList<IGH_Goo>(DA, 0);
        if (!ReportIfFailed(listResult))
        {
            return;
        }

        List<int> indices = [];
        if (!DA.GetDataList(1, indices))
        {
            ReportWarning("No indices provided");
            return;
        }

        List<IGH_Goo> list = listResult.Value!;

        if (list.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "List is empty");
            return;
        }

        List<IGH_Goo> selected = [];

        foreach (int index in indices)
        {
            int actualIndex = index < 0 ? list.Count + index : index;

            if (actualIndex >= 0 && actualIndex < list.Count)
            {
                selected.Add(list[actualIndex]);
            }
            else
            {
                string indexStr = index < 0
                    ? $"{index} (resolved to {actualIndex})"
                    : index.ToString();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Index {indexStr} is out of range for list of {list.Count} items");
            }
        }

        DA.SetDataList(0, selected);
    }

    public override Guid ComponentGuid => new("A3B5C7D9-2E4F-6A8B-9C1D-3F5E7B9A1C2D");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override System.Drawing.Bitmap? Icon => null;
}

using System;
using Arsenal.Core;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Components;

/// <summary>
/// Base class providing document tolerance access and uniform error handling.
/// Parameter preview is automatic via DA.SetData(). For supplementary visualization, override SDK methods directly.
/// </summary>
public abstract class GhComponentBase(
    string name,
    string nickname,
    string description,
    string category,
    string subcategory)
    : GH_Component(name, nickname, description, category, subcategory)
{
    /// <summary>Sealed to enforce uniform error handling. Override GuardedSolve instead.</summary>
    protected sealed override void SolveInstance(IGH_DataAccess DA)
    {
        ArgumentNullException.ThrowIfNull(DA);

        try
        {
            GuardedSolve(DA);
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Component execution failed: {ex.Message}");
        }
    }

    /// <summary>Override for pre-solve initialization. Called before the first SolveInstance.</summary>
    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        OnBeforeSolve();
    }

    /// <summary>Override for post-solve cleanup. Called after SolveInstance completes.</summary>
    protected override void AfterSolveInstance()
    {
        OnAfterSolve();
        base.AfterSolveInstance();
    }

    /// <summary>Virtual method for derived classes to implement pre-solve logic.</summary>
    protected virtual void OnBeforeSolve() { }

    /// <summary>Virtual method for derived classes to implement post-solve cleanup.</summary>
    protected virtual void OnAfterSolve() { }

    /// <summary>Component solve logic with automatic exception handling.</summary>
    protected abstract void GuardedSolve(IGH_DataAccess DA);

    /// <summary>Helper for consistent Result pattern error reporting.</summary>
    protected bool ReportIfFailed<T>(Result<T> result, GH_RuntimeMessageLevel level = GH_RuntimeMessageLevel.Error)
    {
        if (result.Ok)
        {
            return true;
        }

        AddRuntimeMessage(level, result.Error ?? "Operation failed");
        return false;
    }

    /// <summary>Reports a warning message.</summary>
    protected void ReportWarning(string message)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
    }
}

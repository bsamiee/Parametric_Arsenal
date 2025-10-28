using System;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Document;
using Arsenal.Grasshopper.Parameters;
using Arsenal.Grasshopper.Runtime;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Components;

/// <summary>Base class for synchronous Grasshopper components using Result-based flows.</summary>
public abstract class ComponentBase : GH_Component
{
    private readonly ISolvePipeline _pipeline;
    private readonly IDocumentScopeProvider _documentScopeProvider;

    /// <summary>Initializes the component with optional pipeline and document scope provider.</summary>
    protected ComponentBase(
        string name,
        string nickname,
        string description,
        string category,
        string subcategory,
        ISolvePipeline? pipeline = null,
        IDocumentScopeProvider? documentScopeProvider = null)
        : base(name, nickname, description, category, subcategory)
    {
        _pipeline = pipeline ?? SolvePipeline.Default;
        _documentScopeProvider = documentScopeProvider ?? DocScopeProvider.Default;
    }

    /// <summary>Provides access to parameter metadata catalog for derived components.</summary>
    protected ParameterCatalog Parameters { get; } = ParameterCatalog.Instance;

    /// <summary>Sealed to enforce uniform orchestration and Result handling.</summary>
    protected sealed override void SolveInstance(IGH_DataAccess dataAccess)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);

        SolveContext context = CreateContext(dataAccess);
        Result solveResult = _pipeline.Execute(() => Execute(context));

        if (solveResult is { IsSuccess: false, Failure: not null })
        {
            HandleFailure(solveResult.Failure, context);
        }
    }

    /// <summary>Runs synchronous component logic. Derived classes set outputs directly.</summary>
    protected abstract Result Execute(SolveContext context);

    /// <summary>Optional hook before solving starts.</summary>
    protected virtual void OnBeforeSolve() { }

    /// <summary>Optional hook after solving completes.</summary>
    protected virtual void OnAfterSolve() { }

    /// <inheritdoc/>
    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        OnBeforeSolve();
    }

    /// <inheritdoc/>
    protected override void AfterSolveInstance()
    {
        OnAfterSolve();
        base.AfterSolveInstance();
    }

    /// <summary>Creates the solve context with tolerance and cancellation information.</summary>
    protected virtual SolveContext CreateContext(IGH_DataAccess dataAccess)
    {
        DocScope docScope = _documentScopeProvider.Resolve(this);
        GeoContext geoContext = GeoContext.FromDocument(docScope);
        return new SolveContext(this, dataAccess, docScope, geoContext);
    }

    /// <summary>Reports Result failures as Grasshopper runtime messages.</summary>
    protected virtual void HandleFailure(Failure failure, SolveContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, FormatFailure(failure));
    }

    /// <summary>Formats failure messages consistently.</summary>
    protected virtual string FormatFailure(Failure failure) =>
        $"{failure.Code}: {failure.Message}";

    /// <summary>Applies a Result&lt;T&gt; by invoking a handler on success or surfacing failure.</summary>
    protected bool ApplyResult<T>(Result<T> result, SolveContext context, Action<T> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            if (result.Value is null)
            {
                HandleFailure(new Failure("grasshopper.result.null", "Operation succeeded without producing a value."), context);
                return false;
            }

            onSuccess(result.Value);
            return true;
        }

        if (result.Failure is not null)
        {
            HandleFailure(result.Failure, context);
        }

        return false;
    }
}

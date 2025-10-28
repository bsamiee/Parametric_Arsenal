using System;
using System.Threading;
using System.Threading.Tasks;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Document;
using Arsenal.Grasshopper.Parameters;
using Arsenal.Grasshopper.Runtime;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Components;

/// <summary>Base class for task-capable components using Result-driven workflows.</summary>
/// <typeparam name="TWork">Captured input data passed to background execution.</typeparam>
/// <typeparam name="TResult">Computed output payload.</typeparam>
public abstract class TaskComponent<TWork, TResult> : GH_TaskCapableComponent<SolveWorkResult<TResult>>
{
    private readonly ISolvePipeline _pipeline;
    private readonly IDocumentScopeProvider _documentScopeProvider;

    /// <summary>Initializes the component.</summary>
    protected TaskComponent(
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

    /// <summary>Provides access to shared parameter metadata.</summary>
    protected ParameterCatalog Parameters { get; } = ParameterCatalog.Instance;

    /// <inheritdoc/>
    protected sealed override void SolveInstance(IGH_DataAccess dataAccess)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        SolveContext context = CreateContext(dataAccess);

        if (InPreSolve)
        {
            Result<TWork> preparation = _pipeline.Execute(() => PrepareWork(context));
            if (!preparation.IsSuccess || preparation.Value is null)
            {
                if (preparation.Failure is not null)
                {
                    HandleFailure(preparation.Failure, context);
                }

                return;
            }

            TaskList.EnsureCapacity(TaskList.Count + 1);
            TaskList.Add(ScheduleAsync(preparation.Value, context, CancelToken));
            return;
        }

        if (!GetSolveResults(dataAccess, out SolveWorkResult<TResult>? outcome) || outcome is null)
        {
            SolveWorkResult<TResult> fallback = InvokeSynchronously(context);
            HandleOutcome(context, fallback);
            return;
        }

        HandleOutcome(context, outcome);
    }

    /// <summary>Captures input data for background execution.</summary>
    protected abstract Result<TWork> PrepareWork(SolveContext context);

    /// <summary>Runs the computation on a background thread.</summary>
    protected abstract Task<Result<TResult>> RunWorkAsync(TWork work, SolveContext context,
        CancellationToken cancellationToken);

    /// <summary>Runs the computation synchronously when async scheduling is unavailable.</summary>
    protected abstract Result<TResult> RunWork(TWork work, SolveContext context);

    /// <summary>Applies the completed result to the Grasshopper outputs.</summary>
    protected abstract void ApplyResult(SolveContext context, TResult result);

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

    /// <summary>Creates the solve context with tolerance information.</summary>
    protected virtual SolveContext CreateContext(IGH_DataAccess dataAccess)
    {
        DocScope docScope = _documentScopeProvider.Resolve(this);
        GeoContext geoContext = GeoContext.FromDocument(docScope);
        return new SolveContext(this, dataAccess, docScope, geoContext, CancelToken);
    }

    /// <summary>Schedules asynchronous work and wraps the outcome.</summary>
    private async Task<SolveWorkResult<TResult>> ScheduleAsync(TWork work, SolveContext context,
        CancellationToken cancellationToken)
    {
        SolveContext cancellableContext = context.WithCancellation(cancellationToken);
        Result<TResult> result = await RunWorkAsync(work, cancellableContext, cancellationToken).ConfigureAwait(false);
        return new SolveWorkResult<TResult>(result);
    }

    /// <summary>Invokes synchronous execution when async results are unavailable.</summary>
    private SolveWorkResult<TResult> InvokeSynchronously(SolveContext context)
    {
        Result<TWork> preparation = _pipeline.Execute(() => PrepareWork(context));
        if (!preparation.IsSuccess || preparation.Value is null)
        {
            return preparation.Failure is not null
                ? new SolveWorkResult<TResult>(Result<TResult>.Fail(preparation.Failure))
                : new SolveWorkResult<TResult>(Result<TResult>.Fail(new Failure("grasshopper.task.invalidInput",
                    "Task preparation failed.")));
        }

        Result<TResult> result = _pipeline.Execute(() => RunWork(preparation.Value, context));
        return new SolveWorkResult<TResult>(result);
    }

    /// <summary>Handles a solve outcome by applying outputs or reporting failures.</summary>
    private void HandleOutcome(SolveContext context, SolveWorkResult<TResult>? outcome)
    {
        if (outcome is null)
        {
            HandleFailure(new Failure("grasshopper.task.noOutcome", "Background task returned no outcome."), context);
            return;
        }

        Result<TResult> result = outcome.Outcome;
        if (result.IsSuccess)
        {
            if (result.Value is null)
            {
                HandleFailure(new Failure("grasshopper.result.null", "Operation succeeded without producing a value."),
                    context);
                return;
            }

            ApplyResult(context, result.Value);
            return;
        }

        if (result.Failure is not null)
        {
            HandleFailure(result.Failure, context);
        }
    }

    /// <summary>Reports failures as Grasshopper runtime messages.</summary>
    protected virtual void HandleFailure(Failure failure, SolveContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, FormatFailure(failure));
    }

    /// <summary>Formats a failure message.</summary>
    protected virtual string FormatFailure(Failure failure) =>
        $"{failure.Code}: {failure.Message}";
}

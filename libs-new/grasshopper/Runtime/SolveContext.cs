using System;
using System.Threading;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Document;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Aggregates per-solve information for component execution.</summary>
public sealed record SolveContext
{
    private GeoContext? _geoContext;

    /// <summary>Initializes the solve context.</summary>
    public SolveContext(
        GH_Component component,
        IGH_DataAccess dataAccess,
        DocScope documentScope,
        GeoContext geoContext,
        CancellationToken cancellationToken = default)
    {
        Component = component ?? throw new ArgumentNullException(nameof(component));
        DataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        DocumentScope = documentScope ?? throw new ArgumentNullException(nameof(documentScope));
        _geoContext = geoContext ?? throw new ArgumentNullException(nameof(geoContext));
        CancellationToken = cancellationToken;
    }

    /// <summary>Grasshopper component associated with this context.</summary>
    public GH_Component Component { get; }

    /// <summary>Data access handle for reading inputs and writing outputs.</summary>
    public IGH_DataAccess DataAccess { get; }

    /// <summary>Document scope exposing tolerances and Rhino document details.</summary>
    public DocScope DocumentScope { get; }

    /// <summary>Cancellation token propagated to long-running operations.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Geometric context containing tolerance information.</summary>
    public GeoContext GeoContext => _geoContext ??= GeoContext.FromDocument(DocumentScope);

    /// <summary>Creates a new context with a custom cancellation token.</summary>
    public SolveContext WithCancellation(CancellationToken token) =>
        new(Component, DataAccess, DocumentScope, GeoContext, token);
}

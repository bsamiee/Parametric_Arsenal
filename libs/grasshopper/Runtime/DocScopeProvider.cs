using System;
using Arsenal.Rhino.Document;
using Grasshopper.Kernel;
using Rhino;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Provides Rhino document scope information for Grasshopper components.</summary>
public interface IDocumentScopeProvider
{
    /// <summary>Resolves a document scope for the specified component.</summary>
    DocScope Resolve(GH_Component component);
}

/// <summary>Default document scope provider using the active Rhino document when available.</summary>
public sealed class DocScopeProvider : IDocumentScopeProvider
{
    /// <summary>Gets the singleton default provider.</summary>
    public static DocScopeProvider Default { get; } = new();

    private DocScopeProvider()
    {
    }

    /// <inheritdoc/>
    public DocScope Resolve(GH_Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        RhinoDoc? document = component.OnPingDocument()?.RhinoDocument ?? RhinoDoc.ActiveDoc;
        return document is null
            ? DocScope.Detached()
            : DocScope.FromDocument(document);
    }
}

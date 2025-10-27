using Arsenal.Rhino.Document;

namespace Arsenal.Rhino.Context;

/// <summary>Geometric operation context with tolerance resolution.</summary>
public sealed record GeoContext
{
    private readonly double? _absoluteToleranceOverride;
    private readonly double? _angleToleranceOverride;

    private GeoContext(DocScope? docScope, double? absoluteToleranceOverride, double? angleToleranceOverride)
    {
        DocScope = docScope;
        _absoluteToleranceOverride = absoluteToleranceOverride;
        _angleToleranceOverride = angleToleranceOverride;
    }

    /// <summary>Document scope providing tolerance information.</summary>
    public DocScope? DocScope { get; }

    /// <summary>Absolute tolerance for geometric operations.</summary>
    public double AbsoluteTolerance =>
        _absoluteToleranceOverride
        ?? DocScope?.AbsoluteTolerance
        ?? DocScope.DefaultAbsoluteTolerance;

    /// <summary>Angle tolerance in radians for geometric operations.</summary>
    public double AngleToleranceRadians =>
        _angleToleranceOverride
        ?? DocScope?.AngleToleranceRadians
        ?? DocScope.DefaultAngleToleranceRadians;

    /// <summary>Whether context is associated with a document.</summary>
    public bool HasDocument => DocScope is not null;

    /// <summary>Creates a context from the specified document scope.</summary>
    public static GeoContext FromDocument(DocScope docScope) =>
        new(docScope, null, null);

    /// <summary>Creates a context with default tolerances.</summary>
    public static GeoContext Default() =>
        new(null, DocScope.DefaultAbsoluteTolerance, DocScope.DefaultAngleToleranceRadians);

    /// <summary>Creates a new context with the specified tolerance overrides.</summary>
    public GeoContext WithOverrides(double? absoluteTolerance = null, double? angleToleranceRadians = null) =>
        new(DocScope,
            absoluteTolerance ?? _absoluteToleranceOverride,
            angleToleranceRadians ?? _angleToleranceOverride);
}

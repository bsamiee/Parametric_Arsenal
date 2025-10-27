using Arsenal.Rhino.Document;

namespace Arsenal.Rhino.Context;

/// <summary>Operation-scoped context that resolves tolerances from the active document or explicit overrides.</summary>
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

    public DocScope? DocScope { get; }

    public double AbsoluteTolerance =>
        _absoluteToleranceOverride
        ?? DocScope?.AbsoluteTolerance
        ?? DocScope.DefaultAbsoluteTolerance;

    public double AngleToleranceRadians =>
        _angleToleranceOverride
        ?? DocScope?.AngleToleranceRadians
        ?? DocScope.DefaultAngleToleranceRadians;

    public bool HasDocument => DocScope is not null;

    public static GeoContext FromDocument(DocScope docScope) =>
        new(docScope, null, null);

    public static GeoContext Default() =>
        new(null, DocScope.DefaultAbsoluteTolerance, DocScope.DefaultAngleToleranceRadians);

    public GeoContext WithOverrides(double? absoluteTolerance = null, double? angleToleranceRadians = null) =>
        new(DocScope,
            absoluteTolerance ?? _absoluteToleranceOverride,
            angleToleranceRadians ?? _angleToleranceOverride);
}

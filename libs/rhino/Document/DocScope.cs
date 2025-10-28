using System;
using Rhino;

namespace Arsenal.Rhino.Document;

/// <summary>Rhino document scope with derived tolerances.</summary>
public sealed record DocScope
{
    /// <summary>Default absolute tolerance value in model units.</summary>
    public const double DefaultAbsoluteTolerance = 0.01;
    /// <summary>Default angle tolerance value in radians.</summary>
    public const double DefaultAngleToleranceRadians = Math.PI / 180.0;

    private DocScope(RhinoDoc? document, double absoluteTolerance, double angleToleranceRadians)
    {
        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance), absoluteTolerance, "Absolute tolerance must be a positive finite number.");
        }

        if (!double.IsFinite(angleToleranceRadians) || angleToleranceRadians <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(angleToleranceRadians), angleToleranceRadians, "Angle tolerance must be a positive finite number.");
        }

        Document = document;
        AbsoluteTolerance = absoluteTolerance;
        AngleToleranceRadians = angleToleranceRadians;
    }

    /// <summary>Rhino document associated with this scope.</summary>
    public RhinoDoc? Document { get; }

    /// <summary>Absolute tolerance for geometric operations.</summary>
    public double AbsoluteTolerance { get; }

    /// <summary>Angle tolerance in radians for geometric operations.</summary>
    public double AngleToleranceRadians { get; }

    /// <summary>Creates document scope from Rhino document.</summary>
    public static DocScope FromDocument(RhinoDoc document)
    {
        ArgumentNullException.ThrowIfNull(document);

        double abs = document.ModelAbsoluteTolerance > 0 ? document.ModelAbsoluteTolerance : DefaultAbsoluteTolerance;
        double angle = document.ModelAngleToleranceRadians > 0 ? document.ModelAngleToleranceRadians : DefaultAngleToleranceRadians;

        return new DocScope(document, abs, angle);
    }

    /// <summary>Creates detached document scope with specified tolerances.</summary>
    public static DocScope Detached(double absoluteTolerance = DefaultAbsoluteTolerance, double angleToleranceRadians = DefaultAngleToleranceRadians) =>
        new(null, absoluteTolerance, angleToleranceRadians);

    /// <summary>Creates new document scope with tolerance overrides.</summary>
    public DocScope WithTolerances(double? absoluteTolerance = null, double? angleToleranceRadians = null) =>
        new(Document,
            absoluteTolerance ?? AbsoluteTolerance,
            angleToleranceRadians ?? AngleToleranceRadians);
}

using System;
using Rhino;

namespace Arsenal.Rhino.Document;

/// <summary>Encapsulates a Rhino document and the tolerances derived from it.</summary>
public sealed record DocScope
{
    public const double DefaultAbsoluteTolerance = 0.01;
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

    public RhinoDoc? Document { get; }

    public double AbsoluteTolerance { get; }

    public double AngleToleranceRadians { get; }

    public static DocScope FromDocument(RhinoDoc document)
    {
        ArgumentNullException.ThrowIfNull(document);

        double abs = document.ModelAbsoluteTolerance > 0 ? document.ModelAbsoluteTolerance : DefaultAbsoluteTolerance;
        double angle = document.ModelAngleToleranceRadians > 0 ? document.ModelAngleToleranceRadians : DefaultAngleToleranceRadians;

        return new DocScope(document, abs, angle);
    }

    public static DocScope Detached(double absoluteTolerance = DefaultAbsoluteTolerance, double angleToleranceRadians = DefaultAngleToleranceRadians) =>
        new(null, absoluteTolerance, angleToleranceRadians);

    public DocScope WithTolerances(double? absoluteTolerance = null, double? angleToleranceRadians = null) =>
        new(Document,
            absoluteTolerance ?? AbsoluteTolerance,
            angleToleranceRadians ?? AngleToleranceRadians);
}

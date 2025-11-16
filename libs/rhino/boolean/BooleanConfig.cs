using System.Diagnostics.Contracts;

namespace Arsenal.Rhino.Boolean;

/// <summary>Boolean operation configuration constants and tolerance helpers.</summary>
[Pure]
internal static class BooleanConfig {
    internal const double DefaultToleranceFactor = 1.0;
    internal const double MinimumToleranceMultiplier = 1.0;
    internal const double MaximumToleranceMultiplier = 10.0;

    internal const int MinimumBrepFaces = 1;
    internal const int MinimumMeshFaces = 4;
    internal const int MinimumCurveSegments = 1;

    internal const int MaximumRegionCurves = 1000;
    internal const int MaximumSplitResults = 10000;

    internal const double BrepSplitDefaultTolerance = 0.001;
    internal const double MeshBooleanDefaultTolerance = 0.01;
    internal const double CurveRegionDefaultTolerance = 0.0001;
}

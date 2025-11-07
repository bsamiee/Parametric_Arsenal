namespace Arsenal.Rhino.Analysis;

/// <summary>Configuration constants for geometric analysis operations.</summary>
internal static class AnalysisConfig {
    /// <summary>Maximum number of discontinuities to track in curve analysis.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Default derivative order for analysis operations.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Curve frame sample count for perpendicular frame extraction.</summary>
    internal const int CurveFrameSampleCount = 5;
}

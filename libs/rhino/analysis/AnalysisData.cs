using System.Diagnostics.Contracts;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis method discriminator using readonly struct for compile-time safety and zero allocation.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Colocated with AnalysisData within 4-file limit constraint")]
public readonly struct AnalysisMethod : IEquatable<AnalysisMethod> {
    private readonly byte _value;
    private AnalysisMethod(byte value) => this._value = value;

    public static readonly AnalysisMethod Derivatives = new(1);
    public static readonly AnalysisMethod Frame = new(2);
    public static readonly AnalysisMethod Curvature = new(3);
    public static readonly AnalysisMethod Discontinuity = new(4);
    public static readonly AnalysisMethod Topology = new(5);
    public static readonly AnalysisMethod Proximity = new(6);
    public static readonly AnalysisMethod Metrics = new(7);
    public static readonly AnalysisMethod Domains = new(8);

    [Pure] public bool Equals(AnalysisMethod other) => this._value == other._value;
    [Pure] public override bool Equals(object? obj) => obj is AnalysisMethod other && this.Equals(other);
    [Pure] public override int GetHashCode() => this._value.GetHashCode();
    public static bool operator ==(AnalysisMethod left, AnalysisMethod right) => left.Equals(right);
    public static bool operator !=(AnalysisMethod left, AnalysisMethod right) => !left.Equals(right);
}

/// <summary>Dense analysis result using Result monad for optional fields eliminating null reference issues.</summary>
public sealed record AnalysisData(
    Point3d Point,
    Result<Vector3d[]> Derivatives,
    Result<Plane> Frame,
    Result<(double Gaussian, double Mean, double K1, double K2, Vector3d Dir1, Vector3d Dir2)> Curvature,
    Result<(double[] Parameters, Continuity[] Types)> Discontinuities,
    Result<((int Index, Point3d Location)[] Vertices, (int Index, Line Geometry)[] Edges, bool IsManifold, bool IsClosed)> Topology,
    Result<(Point3d Closest, double Distance)> Proximity,
    Result<(double Length, double Area, double Volume, Point3d Centroid)> Metrics,
    Result<Interval[]> Domains,
    (double? Curve, (double, double)? Surface, int? Mesh) Parameters) {
}

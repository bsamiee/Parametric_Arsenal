using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis result marker interface for polymorphic return discrimination.</summary>
public interface IAnalysisResult {
    public Point3d Location { get; }
}

/// <summary>Curve analysis result containing derivatives, curvature, frame data, discontinuities, and metrics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record CurveData(
    Point3d Location,
    Vector3d[] Derivatives,
    double Curvature,
    Plane Frame,
    Plane[] PerpendicularFrames,
    double Torsion,
    double[] DiscontinuityParameters,
    Continuity[] DiscontinuityTypes,
    double Length,
    Point3d Centroid) : IAnalysisResult {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Curve @ {this.Location} | κ={this.Curvature:F3} | L={this.Length:F3} | Disc={this.DiscontinuityParameters?.Length.ToString(CultureInfo.InvariantCulture) ?? "0"}");
}

/// <summary>Surface analysis result containing derivatives, principal curvatures, frame data, singularity detection, and metrics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record SurfaceData(
    Point3d Location,
    Vector3d[] Derivatives,
    double Gaussian,
    double Mean,
    double K1,
    double K2,
    Vector3d PrincipalDir1,
    Vector3d PrincipalDir2,
    Plane Frame,
    Vector3d Normal,
    bool AtSeam,
    bool AtSingularity,
    double Area,
    Point3d Centroid) : IAnalysisResult {
    [Pure]
    private string DebuggerDisplay => this.AtSingularity
        ? string.Create(CultureInfo.InvariantCulture, $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3} [singular]")
        : string.Create(CultureInfo.InvariantCulture, $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3}");
}

/// <summary>Brep analysis result containing surface evaluation, topology navigation, proximity data, and solid metrics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record BrepData(
    Point3d Location,
    Vector3d[] Derivatives,
    double Gaussian,
    double Mean,
    double K1,
    double K2,
    Vector3d PrincipalDir1,
    Vector3d PrincipalDir2,
    Plane Frame,
    Vector3d Normal,
    (int Index, Point3d Point)[] Vertices,
    (int Index, Line Geometry)[] Edges,
    bool IsManifold,
    bool IsSolid,
    Point3d ClosestPoint,
    double Distance,
    ComponentIndex Component,
    (double U, double V) SurfaceUV,
    double Area,
    double Volume,
    Point3d Centroid) : IAnalysisResult {
    [Pure]
    private string DebuggerDisplay => this.IsSolid && this.IsManifold
        ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [solid] [manifold]")
        : this.IsSolid
            ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [solid]")
            : this.IsManifold
                ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [manifold]")
                : string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}");
}

/// <summary>Mesh analysis result containing topology navigation, manifold state, and volume metrics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record MeshData(
    Point3d Location,
    Plane Frame,
    Vector3d Normal,
    (int Index, Point3d Point)[] TopologyVertices,
    (int Index, Line Geometry)[] TopologyEdges,
    bool IsManifold,
    bool IsClosed,
    double Area,
    double Volume) : IAnalysisResult {
    [Pure]
    private string DebuggerDisplay => this.IsClosed && this.IsManifold
        ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [closed] [manifold]")
        : this.IsClosed
            ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [closed]")
            : this.IsManifold
                ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [manifold]")
                : string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}");
}

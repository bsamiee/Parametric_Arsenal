using System.Diagnostics.Contracts;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

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
    /// <summary>Creates empty analysis with point only.</summary>
    [Pure]
    public static AnalysisData Empty(Point3d point) => new(
        point,
        ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.DerivativeComputationFailed),
        ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
        ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
        ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
        ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
        (null, null, null));
}

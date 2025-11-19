using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Metadata, constants, and dispatch tables for orientation operations.</summary>
[Pure]
internal static class OrientationConfig {
    /// <summary>Operation metadata bundling validation mode and operation name.</summary>
    internal sealed record OrientationOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Geometry type to validation mode dispatch for orientation operations.</summary>
    internal static readonly FrozenDictionary<Type, V> GeometryValidation =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point3d)] = V.None,
            [typeof(PointCloud)] = V.None,
        }.ToFrozenDictionary();

    /// <summary>Operation type to metadata dispatch for orientation operations.</summary>
    internal static readonly FrozenDictionary<Type, OrientationOperationMetadata> Operations =
        new Dictionary<Type, OrientationOperationMetadata> {
            [typeof(Orientation.ToPlane)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.ToPlane"),
            [typeof(Orientation.ToCanonical)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orientation.ToCanonical"),
            [typeof(Orientation.ToPoint)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.ToPoint"),
            [typeof(Orientation.ToVector)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.ToVector"),
            [typeof(Orientation.ToBestFit)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.ToBestFit"),
            [typeof(Orientation.Mirror)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.Mirror"),
            [typeof(Orientation.FlipDirection)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.FlipDirection"),
            [typeof(Orientation.ToCurveFrame)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Orientation.ToCurveFrame"),
            [typeof(Orientation.ToSurfaceFrame)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Orientation.ToSurfaceFrame"),
            [typeof(Orientation.Optimize)] = new(
                ValidationMode: V.Standard | V.Topology | V.BoundingBox | V.MassProperties,
                OperationName: "Orientation.Optimize"),
            [typeof(Orientation.ComputeRelative)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.ComputeRelative"),
            [typeof(Orientation.DetectPattern)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orientation.DetectPattern"),
        }.ToFrozenDictionary();

    /// <summary>Canonical mode to validation flags dispatch.</summary>
    internal static readonly FrozenDictionary<Type, V> CanonicalModeValidation =
        new Dictionary<Type, V> {
            [typeof(Orientation.WorldXY)] = V.Standard | V.BoundingBox,
            [typeof(Orientation.WorldYZ)] = V.Standard | V.BoundingBox,
            [typeof(Orientation.WorldXZ)] = V.Standard | V.BoundingBox,
            [typeof(Orientation.AreaCentroid)] = V.Standard | V.BoundingBox,
            [typeof(Orientation.VolumeCentroid)] = V.Standard | V.MassProperties,
        }.ToFrozenDictionary();

    /// <summary>Centroid mode to validation flags dispatch.</summary>
    internal static readonly FrozenDictionary<Type, V> CentroidModeValidation =
        new Dictionary<Type, V> {
            [typeof(Orientation.BoundingBoxCentroid)] = V.Standard | V.BoundingBox,
            [typeof(Orientation.MassCentroid)] = V.Standard | V.MassProperties,
        }.ToFrozenDictionary();

    /// <summary>Plane extractor dispatch by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Result<Plane>>> PlaneExtractors =
        new Dictionary<Type, Func<GeometryBase, Result<Plane>>> {
            [typeof(Curve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(NurbsCurve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(LineCurve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(ArcCurve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(PolyCurve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(PolylineCurve)] = static g => OrientationCompute.ExtractCurvePlane((Curve)g),
            [typeof(Surface)] = static g => OrientationCompute.ExtractSurfacePlane((Surface)g),
            [typeof(NurbsSurface)] = static g => OrientationCompute.ExtractSurfacePlane((Surface)g),
            [typeof(PlaneSurface)] = static g => OrientationCompute.ExtractSurfacePlane((Surface)g),
            [typeof(Brep)] = static g => OrientationCompute.ExtractBrepPlane((Brep)g),
            [typeof(Extrusion)] = static g => OrientationCompute.ExtractExtrusionPlane((Extrusion)g),
            [typeof(Mesh)] = static g => OrientationCompute.ExtractMeshPlane((Mesh)g),
            [typeof(PointCloud)] = static g => OrientationCompute.ExtractPointCloudPlane((PointCloud)g),
        }.ToFrozenDictionary();

    /// <summary>Tolerance and threshold constants for orientation analysis.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    internal const double LowProfileAspectRatio = 0.5;
    internal const double PatternAnomalyThreshold = 0.5;

    /// <summary>Canonical positioning score weights for optimization.</summary>
    internal const double OrientationScoreWeight1 = 0.4;
    internal const double OrientationScoreWeight2 = 0.4;
    internal const double OrientationScoreWeight3 = 0.2;

    /// <summary>Count and size thresholds for orientation operations.</summary>
    internal const int BestFitMinPoints = 3;
    internal const int PatternMinInstances = 3;
    internal const int RotationSymmetrySampleCount = 36;
    internal const int MaxDegeneracyDimensions = 3;
}

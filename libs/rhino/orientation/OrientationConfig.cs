using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Unified metadata, constants, and dispatch tables for orientation operations.</summary>
[Pure] internal static class OrientationConfig {
    /// <summary>Centroid mode validation modes.</summary>
    internal static readonly FrozenDictionary<Type, V> CentroidModeValidation =
        new Dictionary<Type, V> {
            [typeof(Orientation.BoundingBoxCentroid)] = V.BoundingBox,
            [typeof(Orientation.MassCentroid)] = V.MassProperties,
        }.ToFrozenDictionary();

    /// <summary>Canonical mode validation modes.</summary>
    internal static readonly FrozenDictionary<Type, V> CanonicalModeValidation =
        new Dictionary<Type, V> {
            [typeof(Orientation.WorldXY)] = V.BoundingBox,
            [typeof(Orientation.WorldYZ)] = V.BoundingBox,
            [typeof(Orientation.WorldXZ)] = V.BoundingBox,
            [typeof(Orientation.AreaCentroid)] = V.BoundingBox,
            [typeof(Orientation.VolumeCentroid)] = V.MassProperties,
        }.ToFrozenDictionary();

    /// <summary>Plane extraction dispatch table by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, PlaneExtractorMetadata> PlaneExtractors =
        new Dictionary<Type, PlaneExtractorMetadata> {
            [typeof(Curve)] = new(g => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane frame) && frame.IsValid
                ? ResultFactory.Create(value: frame)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)),
            [typeof(Surface)] = new(g => ((Surface)g).FrameAt(((Surface)g).Domain(0).Mid, ((Surface)g).Domain(1).Mid, out Plane frame) && frame.IsValid
                ? ResultFactory.Create(value: frame)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)),
            [typeof(Brep)] = new(ExtractBrepPlane),
            [typeof(Extrusion)] = new(ExtractExtrusionPlane),
            [typeof(Mesh)] = new(ExtractMeshPlane),
            [typeof(PointCloud)] = new(g => ((PointCloud)g).Count > 0
                ? ResultFactory.Create(value: new Plane(((PointCloud)g)[0].Location, Vector3d.ZAxis))
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)),
        }.ToFrozenDictionary();

    /// <summary>Singular unified operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, OrientationOperationMetadata> Operations =
        new Dictionary<Type, OrientationOperationMetadata> {
            [typeof(Orientation.ToPlane)] = new(V.Standard, "Orientation.ToPlane"),
            [typeof(Orientation.ToCanonical)] = new(V.Standard | V.BoundingBox, "Orientation.ToCanonical"),
            [typeof(Orientation.ToPoint)] = new(V.Standard, "Orientation.ToPoint"),
            [typeof(Orientation.ToVector)] = new(V.Standard | V.BoundingBox, "Orientation.ToVector"),
            [typeof(Orientation.ToBestFit)] = new(V.Standard, "Orientation.ToBestFit"),
            [typeof(Orientation.Mirror)] = new(V.Standard, "Orientation.Mirror"),
            [typeof(Orientation.FlipDirection)] = new(V.Standard, "Orientation.FlipDirection"),
            [typeof(Orientation.ToCurveFrame)] = new(V.Standard, "Orientation.ToCurveFrame"),
            [typeof(Orientation.ToSurfaceFrame)] = new(V.Standard | V.UVDomain, "Orientation.ToSurfaceFrame"),
        }.ToFrozenDictionary();

    /// <summary>Geometry-specific validation modes.</summary>
    internal static readonly FrozenDictionary<Type, V> GeometryValidation =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.BoundingBox,
            [typeof(NurbsSurface)] = V.Standard | V.BoundingBox | V.NurbsGeometry,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(PointCloud)] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Optimization operation metadata.</summary>
    internal static readonly OrientationOperationMetadata OptimizationMetadata = new(
        ValidationMode: V.Standard | V.Topology | V.BoundingBox | V.MassProperties,
        OperationName: "Orientation.Optimize");

    /// <summary>Unified operation metadata for all orientation transforms.</summary>
    internal sealed record OrientationOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Plane extractor metadata with validation and extraction function.</summary>
    internal sealed record PlaneExtractorMetadata(
        Func<GeometryBase, Result<Plane>> Extractor);

    /// <summary>Best-fit plane minimum point count.</summary>
    internal const int BestFitMinPoints = 3;

    /// <summary>Pattern detection minimum instance count.</summary>
    internal const int PatternMinInstances = 3;

    /// <summary>Optimization test plane configurations.</summary>
    internal const int MaxDegeneracyDimensions = 3;

    /// <summary>Rotation symmetry sample count for curve analysis.</summary>
    internal const int RotationSymmetrySampleCount = 36;

    /// <summary>Pattern anomaly detection threshold multiplier.</summary>
    internal const double PatternAnomalyThreshold = 0.5;

    /// <summary>Best-fit plane RMS residual threshold.</summary>
    internal const double BestFitResidualThreshold = 1e-3;

    /// <summary>Low-profile aspect ratio threshold for canonical scoring.</summary>
    internal const double LowProfileAspectRatio = 0.25;

    /// <summary>Canonical orientation scoring weights.</summary>
    internal const double OrientationScoreWeight1 = 0.4;
    internal const double OrientationScoreWeight2 = 0.4;
    internal const double OrientationScoreWeight3 = 0.2;

    private static Result<Plane> ExtractBrepPlane(GeometryBase geometry) {
        Brep brep = (Brep)geometry;
        Vector3d normal = brep.Faces.Count > 0 ? brep.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis;
        return brep.IsSolid
            ? ((Func<Result<Plane>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, normal)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))()
            : brep.SolidOrientation != BrepSolidOrientation.None
                ? ((Func<Result<Plane>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                    return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, normal)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))()
                : ResultFactory.Create(value: new Plane(brep.GetBoundingBox(accurate: false).Center, normal));
    }

    private static Result<Plane> ExtractExtrusionPlane(GeometryBase geometry) =>
        ((Extrusion)geometry) switch {
            Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(e);
                using LineCurve? path = e.PathLineCurve();
                return vmp is not null && path is not null
                    ? ResultFactory.Create(value: new Plane(vmp.Centroid, path.TangentAtStart))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
            Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(e);
                using LineCurve? path = e.PathLineCurve();
                return amp is not null && path is not null
                    ? ResultFactory.Create(value: new Plane(amp.Centroid, path.TangentAtStart))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
            Extrusion e => ((Func<Result<Plane>>)(() => {
                using LineCurve? path = e.PathLineCurve();
                return path is not null
                    ? ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
        };

    private static Result<Plane> ExtractMeshPlane(GeometryBase geometry) {
        Mesh mesh = (Mesh)geometry;
        Vector3d normal = mesh.Normals.Count > 0 ? mesh.Normals[0] : Vector3d.ZAxis;
        return mesh.IsClosed
            ? ((Func<Result<Plane>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
                return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, normal)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))()
            : ((Func<Result<Plane>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
                return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, normal)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))();
    }
}

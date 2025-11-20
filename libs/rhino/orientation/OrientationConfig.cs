using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Unified metadata, constants, and dispatch tables for orientation operations.</summary>
[Pure]
internal static class OrientationConfig {
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
        return (brep, normal) switch {
            var (b, n) when b.IsSolid => ComputeMassPlane(VolumeMassProperties.Compute(b), n),
            var (b, n) when b.SolidOrientation != BrepSolidOrientation.None => ComputeMassPlane(AreaMassProperties.Compute(b), n),
            var (b, n) => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, n)),
        };
    }

    private static Result<Plane> ExtractExtrusionPlane(GeometryBase geometry) =>
        ((Extrusion)geometry) switch {
            Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => { using LineCurve? path = e.PathLineCurve(); return ComputeMassPlane(VolumeMassProperties.Compute(e), path?.TangentAtStart ?? Vector3d.ZAxis); }))(),
            Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => { using LineCurve? path = e.PathLineCurve(); return ComputeMassPlane(AreaMassProperties.Compute(e), path?.TangentAtStart ?? Vector3d.ZAxis); }))(),
            Extrusion e => ((Func<Result<Plane>>)(() => {
                using LineCurve? path = e.PathLineCurve();
                return path is not null
                    ? ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
        };

    private static Result<Plane> ExtractMeshPlane(GeometryBase geometry) =>
        ((Mesh)geometry, ((Mesh)geometry).Normals.Count > 0 ? ((Mesh)geometry).Normals[0] : Vector3d.ZAxis) switch {
            (Mesh m, Vector3d n) when m.IsClosed => ComputeMassPlane(VolumeMassProperties.Compute(m), n),
            (Mesh m, Vector3d n) => ComputeMassPlane(AreaMassProperties.Compute(m), n),
        };

    private static Result<Plane> ComputeMassPlane(VolumeMassProperties? vmp, Vector3d normal) =>
        vmp is not null ? ((Func<Result<Plane>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: new Plane(c, normal)); }))() : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);

    private static Result<Plane> ComputeMassPlane(AreaMassProperties? amp, Vector3d normal) =>
        amp is not null ? ((Func<Result<Plane>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: new Plane(c, normal)); }))() : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
}

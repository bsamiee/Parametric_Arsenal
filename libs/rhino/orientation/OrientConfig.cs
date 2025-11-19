using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Validation modes, thresholds, and configuration for orientation operations.</summary>
[Pure]
internal static class OrientConfig {
    internal enum AlignmentOperationKind {
        Plane,
        CurveFrame,
        SurfaceFrame,
        WorldXY,
        WorldYZ,
        WorldXZ,
        BoundingBoxOrigin,
        VolumeOrigin,
        BoundingBoxPoint,
        MassPoint,
        Vector,
        BestFit,
        Mirror,
        Flip,
    }

    internal sealed record AlignmentOperationMetadata(
        AlignmentOperationKind Kind,
        string OperationName,
        V ValidationMode);

    internal sealed record OrientationAnalysisMetadata(
        string OperationName,
        V ValidationMode);

    internal sealed record PlaneExtractorMetadata(Func<object, Result<Plane>> Extractor);

    internal static readonly FrozenDictionary<Type, AlignmentOperationMetadata> AlignmentOperations =
        new Dictionary<Type, AlignmentOperationMetadata> {
            [typeof(Orient.PlaneAlignment)] = new(
                Kind: AlignmentOperationKind.Plane,
                OperationName: "Orient.Align.Plane",
                ValidationMode: V.Standard),
            [typeof(Orient.CurveFrameAlignment)] = new(
                Kind: AlignmentOperationKind.CurveFrame,
                OperationName: "Orient.Align.CurveFrame",
                ValidationMode: V.Standard),
            [typeof(Orient.SurfaceFrameAlignment)] = new(
                Kind: AlignmentOperationKind.SurfaceFrame,
                OperationName: "Orient.Align.SurfaceFrame",
                ValidationMode: V.Standard | V.UVDomain),
            [typeof(Orient.WorldXYAlignment)] = new(
                Kind: AlignmentOperationKind.WorldXY,
                OperationName: "Orient.Align.WorldXY",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.WorldYZAlignment)] = new(
                Kind: AlignmentOperationKind.WorldYZ,
                OperationName: "Orient.Align.WorldYZ",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.WorldXZAlignment)] = new(
                Kind: AlignmentOperationKind.WorldXZ,
                OperationName: "Orient.Align.WorldXZ",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.BoundingBoxOriginAlignment)] = new(
                Kind: AlignmentOperationKind.BoundingBoxOrigin,
                OperationName: "Orient.Align.BoundingOrigin",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.VolumeOriginAlignment)] = new(
                Kind: AlignmentOperationKind.VolumeOrigin,
                OperationName: "Orient.Align.VolumeOrigin",
                ValidationMode: V.MassProperties),
            [typeof(Orient.BoundingBoxPointAlignment)] = new(
                Kind: AlignmentOperationKind.BoundingBoxPoint,
                OperationName: "Orient.Align.BoundingPoint",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.MassPointAlignment)] = new(
                Kind: AlignmentOperationKind.MassPoint,
                OperationName: "Orient.Align.MassPoint",
                ValidationMode: V.MassProperties),
            [typeof(Orient.VectorAlignment)] = new(
                Kind: AlignmentOperationKind.Vector,
                OperationName: "Orient.Align.Vector",
                ValidationMode: V.BoundingBox),
            [typeof(Orient.BestFitAlignment)] = new(
                Kind: AlignmentOperationKind.BestFit,
                OperationName: "Orient.Align.BestFit",
                ValidationMode: V.Standard),
            [typeof(Orient.MirrorAlignment)] = new(
                Kind: AlignmentOperationKind.Mirror,
                OperationName: "Orient.Align.Mirror",
                ValidationMode: V.Standard),
            [typeof(Orient.FlipDirectionAlignment)] = new(
                Kind: AlignmentOperationKind.Flip,
                OperationName: "Orient.Align.Flip",
                ValidationMode: V.Standard),
        }.ToFrozenDictionary();

    internal static readonly OrientationAnalysisMetadata OptimizationMetadata = new(
        OperationName: "Orient.Optimize",
        ValidationMode: V.Standard | V.Topology | V.BoundingBox | V.MassProperties);

    internal static readonly OrientationAnalysisMetadata RelativeMetadata = new(
        OperationName: "Orient.Relative",
        ValidationMode: V.Standard | V.Topology);

    internal static readonly OrientationAnalysisMetadata PatternMetadata = new(
        OperationName: "Orient.Pattern",
        ValidationMode: V.None);

    internal static readonly FrozenDictionary<Type, PlaneExtractorMetadata> PlaneExtractors =
        new Dictionary<Type, PlaneExtractorMetadata> {
            [typeof(Curve)] = new(g => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane f) && f.IsValid
                ? ResultFactory.Create(value: f)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)),
            [typeof(Surface)] = new(g => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            }),
            [typeof(Brep)] = new(g => ((Brep)g) switch {
                Brep b when b.IsSolid => ((Func<Result<Plane>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(b);
                    return vmp is not null
                        ? ResultFactory.Create(value: new Plane(vmp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Plane>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(b);
                    return amp is not null
                        ? ResultFactory.Create(value: new Plane(amp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
                Brep b => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
            }),
            [typeof(Extrusion)] = new(g => ((Extrusion)g) switch {
                Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(e);
                    using LineCurve path = e.PathLineCurve();
                    return vmp is not null
                        ? ResultFactory.Create(value: new Plane(vmp.Centroid, path.TangentAtStart))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(e);
                    using LineCurve path = e.PathLineCurve();
                    return amp is not null
                        ? ResultFactory.Create(value: new Plane(amp.Centroid, path.TangentAtStart))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
                Extrusion e => ((Func<Result<Plane>>)(() => {
                    using LineCurve path = e.PathLineCurve();
                    return ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart));
                }))(),
            }),
            [typeof(Mesh)] = new(g => ((Mesh)g) switch {
                Mesh m when m.IsClosed => ((Func<Result<Plane>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(m);
                    return vmp is not null
                        ? ResultFactory.Create(value: new Plane(vmp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
                Mesh m => ((Func<Result<Plane>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(m);
                    return amp is not null
                        ? ResultFactory.Create(value: new Plane(amp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis))
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                }))(),
            }),
            [typeof(Point3d)] = new(g => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis))),
            [typeof(PointCloud)] = new(g => (PointCloud)g switch {
                PointCloud pc when pc.Count > 0 => ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            }),
        }.ToFrozenDictionary();

    /// <summary>Type-specific validation mode dispatch for orientation operations.</summary>
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

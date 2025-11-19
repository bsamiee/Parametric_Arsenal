using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Unified operation metadata, validation dispatch, and configuration for orientation operations.</summary>
[Pure]
internal static class OrientConfig {
    /// <summary>Operation metadata bundling validation mode, operation name, and buffer size.</summary>
    internal sealed record OrientOperationMetadata(
        V ValidationMode,
        string OperationName,
        int BufferSize);

    /// <summary>Canonical mode operation dispatch metadata keyed by mode type.</summary>
    internal static readonly FrozenDictionary<Type, OrientOperationMetadata> CanonicalModes =
        new Dictionary<Type, OrientOperationMetadata> {
            [typeof(Orient.WorldXYMode)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orient.Canonical.WorldXY",
                BufferSize: 1024),
            [typeof(Orient.WorldYZMode)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orient.Canonical.WorldYZ",
                BufferSize: 1024),
            [typeof(Orient.WorldXZMode)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orient.Canonical.WorldXZ",
                BufferSize: 1024),
            [typeof(Orient.AreaCentroidMode)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orient.Canonical.AreaCentroid",
                BufferSize: 1024),
            [typeof(Orient.VolumeCentroidMode)] = new(
                ValidationMode: V.Standard | V.MassProperties,
                OperationName: "Orient.Canonical.VolumeCentroid",
                BufferSize: 2048),
        }.ToFrozenDictionary();

    /// <summary>Orientation target operation dispatch metadata keyed by target type.</summary>
    internal static readonly FrozenDictionary<Type, OrientOperationMetadata> OrientTargets =
        new Dictionary<Type, OrientOperationMetadata> {
            [typeof(Orient.PlaneTarget)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orient.ToPlane",
                BufferSize: 1024),
            [typeof(Orient.PointTarget)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: "Orient.ToPoint",
                BufferSize: 1024),
            [typeof(Orient.MassPointTarget)] = new(
                ValidationMode: V.Standard | V.MassProperties,
                OperationName: "Orient.ToMassPoint",
                BufferSize: 2048),
            [typeof(Orient.VectorTarget)] = new(
                ValidationMode: V.Standard,
                OperationName: "Orient.ToVector",
                BufferSize: 1024),
            [typeof(Orient.CurveFrameTarget)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Orient.ToCurveFrame",
                BufferSize: 1024),
            [typeof(Orient.SurfaceFrameTarget)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Orient.ToSurfaceFrame",
                BufferSize: 1024),
        }.ToFrozenDictionary();

    /// <summary>Optimization criteria dispatch metadata keyed by criteria type.</summary>
    internal static readonly FrozenDictionary<Type, OrientOperationMetadata> OptimizationCriteria =
        new Dictionary<Type, OrientOperationMetadata> {
            [typeof(Orient.CompactCriteria)] = new(
                ValidationMode: V.Standard | V.Topology | V.BoundingBox,
                OperationName: "Orient.Optimize.Compact",
                BufferSize: 4096),
            [typeof(Orient.CentroidCriteria)] = new(
                ValidationMode: V.Standard | V.Topology | V.BoundingBox | V.MassProperties,
                OperationName: "Orient.Optimize.Centroid",
                BufferSize: 4096),
            [typeof(Orient.FlatnessCriteria)] = new(
                ValidationMode: V.Standard | V.Topology | V.BoundingBox,
                OperationName: "Orient.Optimize.Flatness",
                BufferSize: 4096),
            [typeof(Orient.CanonicalCriteria)] = new(
                ValidationMode: V.Standard | V.Topology | V.BoundingBox | V.MassProperties,
                OperationName: "Orient.Optimize.Canonical",
                BufferSize: 4096),
        }.ToFrozenDictionary();

    /// <summary>Operation type enum for geometry-specific metadata dispatch.</summary>
    internal enum OpType { ToPlane = 0, Mirror = 1, FlipDirection = 2, BestFit = 3 }

    /// <summary>Geometry-type-specific operation metadata for plane extraction and transformations.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), OrientOperationMetadata> GeometryOperations =
        new Dictionary<(Type, OpType), OrientOperationMetadata> {
            [(typeof(Curve), OpType.ToPlane)] = new(V.Standard | V.Degeneracy, "Orient.ToPlane.Curve", 1024),
            [(typeof(NurbsCurve), OpType.ToPlane)] = new(V.Standard | V.Degeneracy | V.NurbsGeometry, "Orient.ToPlane.NurbsCurve", 1024),
            [(typeof(Surface), OpType.ToPlane)] = new(V.Standard | V.UVDomain, "Orient.ToPlane.Surface", 1024),
            [(typeof(NurbsSurface), OpType.ToPlane)] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Orient.ToPlane.NurbsSurface", 1024),
            [(typeof(Brep), OpType.ToPlane)] = new(V.Standard | V.Topology, "Orient.ToPlane.Brep", 2048),
            [(typeof(Extrusion), OpType.ToPlane)] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Orient.ToPlane.Extrusion", 2048),
            [(typeof(Mesh), OpType.ToPlane)] = new(V.Standard | V.MeshSpecific, "Orient.ToPlane.Mesh", 2048),
            [(typeof(PointCloud), OpType.ToPlane)] = new(V.None, "Orient.ToPlane.PointCloud", 1024),
            [(typeof(Curve), OpType.Mirror)] = new(V.Standard | V.Degeneracy, "Orient.Mirror.Curve", 1024),
            [(typeof(Surface), OpType.Mirror)] = new(V.Standard | V.UVDomain, "Orient.Mirror.Surface", 1024),
            [(typeof(Brep), OpType.Mirror)] = new(V.Standard | V.Topology, "Orient.Mirror.Brep", 2048),
            [(typeof(Extrusion), OpType.Mirror)] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Orient.Mirror.Extrusion", 2048),
            [(typeof(Mesh), OpType.Mirror)] = new(V.Standard | V.MeshSpecific, "Orient.Mirror.Mesh", 2048),
            [(typeof(Curve), OpType.FlipDirection)] = new(V.Standard | V.Degeneracy, "Orient.Flip.Curve", 1024),
            [(typeof(Brep), OpType.FlipDirection)] = new(V.Standard | V.Topology, "Orient.Flip.Brep", 2048),
            [(typeof(Extrusion), OpType.FlipDirection)] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Orient.Flip.Extrusion", 2048),
            [(typeof(Mesh), OpType.FlipDirection)] = new(V.Standard | V.MeshSpecific, "Orient.Flip.Mesh", 2048),
            [(typeof(PointCloud), OpType.BestFit)] = new(V.None, "Orient.BestFit.PointCloud", 2048),
            [(typeof(Mesh), OpType.BestFit)] = new(V.Standard | V.MeshSpecific, "Orient.BestFit.Mesh", 2048),
        }.ToFrozenDictionary();

    /// <summary>Default validation modes for geometry types when specific operation metadata is unavailable.</summary>
    internal static readonly FrozenDictionary<Type, V> DefaultValidationModes =
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

    /// <summary>Best-fit plane extraction: minimum 3 points, RMS threshold 1e-3.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    internal const int BestFitMinPoints = 3;

    /// <summary>Optimization scoring weights: compact 0.4, centroid 0.4, profile 0.2.</summary>
    internal const double ScoreWeightCompact = 0.4;
    internal const double ScoreWeightCentroid = 0.4;
    internal const double ScoreWeightProfile = 0.2;
    internal const double LowProfileAspectRatio = 0.5;
    internal const int MaxDegeneracyDimensions = 3;

    /// <summary>Pattern detection: minimum 3 instances, anomaly threshold 0.5.</summary>
    internal const int PatternMinInstances = 3;
    internal const double PatternAnomalyThreshold = 0.5;
    internal const int RotationSymmetrySampleCount = 36;
}

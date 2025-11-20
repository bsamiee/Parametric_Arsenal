using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Unified metadata, constants, and dispatch tables for transformation operations.</summary>
[Pure]
internal static class TransformationConfig {
    /// <summary>Unified metadata for transform operations bundling validation, operation name, and constants.</summary>
    internal sealed record TransformOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Unified metadata for array operations.</summary>
    internal sealed record ArrayOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Unified metadata for morph operations.</summary>
    internal sealed record MorphOperationMetadata(
        V ValidationMode,
        string OperationName,
        double Tolerance);

    /// <summary>Transform operation metadata dispatch table.</summary>
    internal static readonly FrozenDictionary<Type, TransformOperationMetadata> TransformOperations =
        new Dictionary<Type, TransformOperationMetadata> {
            [typeof(Transformation.MatrixTransform)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.MatrixTransform"),
            [typeof(Transformation.UniformScale)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.UniformScale"),
            [typeof(Transformation.NonUniformScale)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.NonUniformScale"),
            [typeof(Transformation.AxisRotation)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.AxisRotation"),
            [typeof(Transformation.VectorRotation)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.VectorRotation"),
            [typeof(Transformation.MirrorTransform)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.MirrorTransform"),
            [typeof(Transformation.Translation)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Translation"),
            [typeof(Transformation.Shear)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Shear"),
            [typeof(Transformation.Projection)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Projection"),
            [typeof(Transformation.ChangeBasis)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.ChangeBasis"),
            [typeof(Transformation.PlaneToPlane)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.PlaneToPlane"),
        }.ToFrozenDictionary();

    /// <summary>Array operation metadata dispatch table.</summary>
    internal static readonly FrozenDictionary<Type, ArrayOperationMetadata> ArrayOperations =
        new Dictionary<Type, ArrayOperationMetadata> {
            [typeof(Transformation.RectangularArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.RectangularArray"),
            [typeof(Transformation.PolarArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.PolarArray"),
            [typeof(Transformation.LinearArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.LinearArray"),
            [typeof(Transformation.PathArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.PathArray"),
        }.ToFrozenDictionary();

    /// <summary>Morph operation metadata dispatch table.</summary>
    internal static readonly FrozenDictionary<Type, MorphOperationMetadata> MorphOperations =
        new Dictionary<Type, MorphOperationMetadata> {
            [typeof(Transformation.FlowMorph)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Transformation.FlowMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.TwistMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.TwistMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.BendMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.BendMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.TaperMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.TaperMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.StretchMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.StretchMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.SplopMorph)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Transformation.SplopMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.SporphMorph)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Transformation.SporphMorph",
                Tolerance: DefaultMorphTolerance),
            [typeof(Transformation.MaelstromMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.MaelstromMorph",
                Tolerance: DefaultMorphTolerance),
        }.ToFrozenDictionary();

    /// <summary>Geometry type validation mode mapping.</summary>
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
            [typeof(PlaneSurface)] = V.Standard | V.UVDomain,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point3d)] = V.None,
            [typeof(Point3d[])] = V.None,
            [typeof(PointCloud)] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Minimum scale factor to prevent singularity.</summary>
    internal const double MinScaleFactor = 1e-6;

    /// <summary>Maximum scale factor to prevent overflow.</summary>
    internal const double MaxScaleFactor = 1e6;

    /// <summary>Maximum array count to prevent memory exhaustion.</summary>
    internal const int MaxArrayCount = 10000;

    /// <summary>Angular tolerance multiplier for vector parallelism checks.</summary>
    internal const double AngleToleranceMultiplier = 10.0;

    /// <summary>Maximum twist angle in radians.</summary>
    internal const double MaxTwistAngle = RhinoMath.TwoPI * 10.0;

    /// <summary>Maximum bend angle in radians.</summary>
    internal const double MaxBendAngle = RhinoMath.TwoPI;

    /// <summary>Default tolerance for morph operations.</summary>
    internal const double DefaultMorphTolerance = 0.001;
}

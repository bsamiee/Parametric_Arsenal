using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Unified metadata, constants, and dispatch tables for transformation operations.</summary>
[Pure]
internal static class TransformationConfig {
    /// <summary>Transform operation metadata bundling validation and operation name.</summary>
    internal sealed record TransformOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Array operation metadata bundling validation, operation name, and constraints.</summary>
    internal sealed record ArrayOperationMetadata(
        V ValidationMode,
        string OperationName,
        int MaxCount);

    /// <summary>Morph operation metadata bundling validation and operation name.</summary>
    internal sealed record MorphOperationMetadata(
        V ValidationMode,
        string OperationName,
        double DefaultTolerance);

    /// <summary>Unified transform operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, TransformOperationMetadata> TransformOperations =
        new Dictionary<Type, TransformOperationMetadata> {
            [typeof(Transformation.Matrix)] = new(V.Standard, "Transformation.Matrix"),
            [typeof(Transformation.UniformScale)] = new(V.Standard, "Transformation.UniformScale"),
            [typeof(Transformation.NonUniformScale)] = new(V.Standard, "Transformation.NonUniformScale"),
            [typeof(Transformation.Rotation)] = new(V.Standard, "Transformation.Rotation"),
            [typeof(Transformation.RotationVectors)] = new(V.Standard, "Transformation.RotationVectors"),
            [typeof(Transformation.Mirror)] = new(V.Standard, "Transformation.Mirror"),
            [typeof(Transformation.Translation)] = new(V.Standard, "Transformation.Translation"),
            [typeof(Transformation.Shear)] = new(V.Standard, "Transformation.Shear"),
            [typeof(Transformation.Projection)] = new(V.Standard, "Transformation.Projection"),
            [typeof(Transformation.ChangeBasis)] = new(V.Standard, "Transformation.ChangeBasis"),
            [typeof(Transformation.PlaneToPlane)] = new(V.Standard, "Transformation.PlaneToPlane"),
        }.ToFrozenDictionary();

    /// <summary>Unified array operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, ArrayOperationMetadata> ArrayOperations =
        new Dictionary<Type, ArrayOperationMetadata> {
            [typeof(Transformation.RectangularArray)] = new(V.Standard, "Transformation.RectangularArray", MaxArrayCount),
            [typeof(Transformation.PolarArray)] = new(V.Standard, "Transformation.PolarArray", MaxArrayCount),
            [typeof(Transformation.LinearArray)] = new(V.Standard, "Transformation.LinearArray", MaxArrayCount),
            [typeof(Transformation.PathArray)] = new(V.Standard | V.Degeneracy, "Transformation.PathArray", MaxArrayCount),
        }.ToFrozenDictionary();

    /// <summary>Unified morph operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, MorphOperationMetadata> MorphOperations =
        new Dictionary<Type, MorphOperationMetadata> {
            [typeof(Transformation.Flow)] = new(V.Standard | V.Degeneracy, "Transformation.Flow", DefaultMorphTolerance),
            [typeof(Transformation.Twist)] = new(V.Standard, "Transformation.Twist", DefaultMorphTolerance),
            [typeof(Transformation.Bend)] = new(V.Standard, "Transformation.Bend", DefaultMorphTolerance),
            [typeof(Transformation.Taper)] = new(V.Standard, "Transformation.Taper", DefaultMorphTolerance),
            [typeof(Transformation.Stretch)] = new(V.Standard, "Transformation.Stretch", DefaultMorphTolerance),
            [typeof(Transformation.Splop)] = new(V.Standard | V.UVDomain, "Transformation.Splop", DefaultMorphTolerance),
            [typeof(Transformation.Sporph)] = new(V.Standard | V.UVDomain, "Transformation.Sporph", DefaultMorphTolerance),
            [typeof(Transformation.Maelstrom)] = new(V.Standard, "Transformation.Maelstrom", DefaultMorphTolerance),
        }.ToFrozenDictionary();

    /// <summary>Geometry-specific validation modes.</summary>
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

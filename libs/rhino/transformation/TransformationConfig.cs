using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Linq;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Unified metadata, validation, and constants for transformation operations.</summary>
internal static class TransformationConfig {
    /// <summary>Transform request metadata.</summary>
    internal static readonly FrozenDictionary<Type, TransformMetadata> Transformations =
        new Dictionary<Type, TransformMetadata> {
            [typeof(Transformation.MatrixTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.Matrix",
                Error: E.Geometry.Transformation.InvalidTransformMatrix),
            [typeof(Transformation.UniformScaleTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.UniformScale",
                Error: E.Geometry.Transformation.InvalidScaleFactor),
            [typeof(Transformation.NonUniformScaleTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.NonUniformScale",
                Error: E.Geometry.Transformation.InvalidScaleFactor),
            [typeof(Transformation.AxisRotationTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.AxisRotation",
                Error: E.Geometry.Transformation.InvalidRotationAxis),
            [typeof(Transformation.DirectionRotationTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.DirectionRotation",
                Error: E.Geometry.Transformation.InvalidRotationAxis),
            [typeof(Transformation.MirrorTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.Mirror",
                Error: E.Geometry.Transformation.InvalidMirrorPlane),
            [typeof(Transformation.TranslationTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.Translation",
                Error: E.Geometry.Transformation.InvalidTransformSpec),
            [typeof(Transformation.ShearTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.Shear",
                Error: E.Geometry.Transformation.InvalidShearParameters),
            [typeof(Transformation.ProjectionTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.Projection",
                Error: E.Geometry.Transformation.InvalidProjectionPlane),
            [typeof(Transformation.ChangeBasisTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.ChangeBasis",
                Error: E.Geometry.Transformation.InvalidBasisPlanes),
            [typeof(Transformation.PlaneToPlaneTransform)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Apply.PlaneToPlane",
                Error: E.Geometry.Transformation.InvalidBasisPlanes),
        }.ToFrozenDictionary();

    /// <summary>Array request metadata.</summary>
    internal static readonly FrozenDictionary<Type, ArrayMetadata> Arrays =
        new Dictionary<Type, ArrayMetadata> {
            [typeof(Transformation.RectangularArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Array.Rectangular"),
            [typeof(Transformation.PolarArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Array.Polar"),
            [typeof(Transformation.LinearArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Array.Linear"),
            [typeof(Transformation.PathArray)] = new(
                ValidationMode: V.None,
                OperationName: "Transformation.Array.Path"),
        }.ToFrozenDictionary();

    /// <summary>Morph request metadata.</summary>
    internal static readonly FrozenDictionary<Type, MorphMetadata> Morphs =
        new Dictionary<Type, MorphMetadata> {
            [typeof(Transformation.FlowMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Flow"),
            [typeof(Transformation.TwistMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Twist"),
            [typeof(Transformation.BendMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Bend"),
            [typeof(Transformation.TaperMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Taper"),
            [typeof(Transformation.StretchMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Stretch"),
            [typeof(Transformation.SplopMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Splop"),
            [typeof(Transformation.SporphMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Sporph"),
            [typeof(Transformation.MaelstromMorph)] = new(
                ValidationMode: V.Standard,
                OperationName: "Transformation.Morph.Maelstrom"),
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

    /// <summary>Get validation mode for geometry type with inheritance fallback.</summary>
    [Pure]
    internal static V GetGeometryValidation(Type geometryType) =>
        GeometryValidation.TryGetValue(geometryType, out V mode)
            ? mode
            : GeometryValidation.FirstOrDefault(kv => kv.Key.IsAssignableFrom(geometryType)).Value switch {
                V found => found,
                _ => V.Standard,
            };

    /// <summary>Transform metadata with validation and diagnostics name.</summary>
    internal sealed record TransformMetadata(
        V ValidationMode,
        string OperationName,
        SystemError Error);

    /// <summary>Array metadata with validation and diagnostics name.</summary>
    internal sealed record ArrayMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Morph metadata with validation and diagnostics name.</summary>
    internal sealed record MorphMetadata(
        V ValidationMode,
        string OperationName);
}

using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform validation modes, algorithmic constants, and operation metadata.</summary>
internal static class TransformationConfig {
    /// <summary>Transform operation metadata: validation mode and operation name.</summary>
    internal sealed record TransformOperationMetadata(V ValidationMode, string OperationName);

    /// <summary>Array operation metadata: validation mode, operation name, and maximum count.</summary>
    internal sealed record ArrayOperationMetadata(V ValidationMode, string OperationName, int MaxCount);

    /// <summary>Morph operation metadata: validation mode, operation name, and tolerance.</summary>
    internal sealed record MorphOperationMetadata(V ValidationMode, string OperationName, double Tolerance);

    /// <summary>Unified transform operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, TransformOperationMetadata> TransformOperations =
        new Dictionary<Type, TransformOperationMetadata> {
            [typeof(Transformation.MatrixTransform)] = new(V.Standard, "Transformation.MatrixTransform"),
            [typeof(Transformation.UniformScale)] = new(V.Standard, "Transformation.UniformScale"),
            [typeof(Transformation.NonUniformScale)] = new(V.Standard, "Transformation.NonUniformScale"),
            [typeof(Transformation.AxisRotation)] = new(V.Standard, "Transformation.AxisRotation"),
            [typeof(Transformation.VectorRotation)] = new(V.Standard, "Transformation.VectorRotation"),
            [typeof(Transformation.MirrorTransform)] = new(V.Standard, "Transformation.MirrorTransform"),
            [typeof(Transformation.Translation)] = new(V.Standard, "Transformation.Translation"),
            [typeof(Transformation.ShearTransform)] = new(V.Standard, "Transformation.ShearTransform"),
            [typeof(Transformation.ProjectionTransform)] = new(V.Standard, "Transformation.ProjectionTransform"),
            [typeof(Transformation.BasisChange)] = new(V.Standard, "Transformation.BasisChange"),
            [typeof(Transformation.PlaneTransform)] = new(V.Standard, "Transformation.PlaneTransform"),
        }.ToFrozenDictionary();

    /// <summary>Unified array operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, ArrayOperationMetadata> ArrayOperations =
        new Dictionary<Type, ArrayOperationMetadata> {
            [typeof(Transformation.RectangularArray)] = new(V.None, "Transformation.RectangularArray", MaxArrayCount),
            [typeof(Transformation.PolarArray)] = new(V.None, "Transformation.PolarArray", MaxArrayCount),
            [typeof(Transformation.LinearArray)] = new(V.None, "Transformation.LinearArray", MaxArrayCount),
            [typeof(Transformation.PathArray)] = new(V.Standard, "Transformation.PathArray", MaxArrayCount),
        }.ToFrozenDictionary();

    /// <summary>Unified morph operations dispatch table: operation type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, MorphOperationMetadata> MorphOperations =
        new Dictionary<Type, MorphOperationMetadata> {
            [typeof(Transformation.FlowMorph)] = new(V.Standard | V.Degeneracy, "Transformation.FlowMorph", DefaultMorphTolerance),
            [typeof(Transformation.TwistMorph)] = new(V.Standard, "Transformation.TwistMorph", DefaultMorphTolerance),
            [typeof(Transformation.BendMorph)] = new(V.Standard, "Transformation.BendMorph", DefaultMorphTolerance),
            [typeof(Transformation.TaperMorph)] = new(V.Standard, "Transformation.TaperMorph", DefaultMorphTolerance),
            [typeof(Transformation.StretchMorph)] = new(V.Standard, "Transformation.StretchMorph", DefaultMorphTolerance),
            [typeof(Transformation.SplopMorph)] = new(V.Standard | V.UVDomain, "Transformation.SplopMorph", DefaultMorphTolerance),
            [typeof(Transformation.SporphMorph)] = new(V.Standard | V.UVDomain, "Transformation.SporphMorph", DefaultMorphTolerance),
            [typeof(Transformation.MaelstromMorph)] = new(V.Standard, "Transformation.MaelstromMorph", DefaultMorphTolerance),
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
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static V GetValidationMode(Type geometryType) =>
        GeometryValidation.TryGetValue(geometryType, out V mode)
            ? mode
            : GeometryValidation
                .Where(kv => kv.Key.IsAssignableFrom(geometryType))
                .Aggregate(
                    ((V?)null, (Type?)null),
                    (best, kv) => best.Item2?.IsAssignableFrom(kv.Key) != false
                        ? (kv.Value, kv.Key)
                        : best)
                .Item1 ?? V.Standard;
}

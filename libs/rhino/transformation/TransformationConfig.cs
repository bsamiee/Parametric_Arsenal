using System;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Unified metadata, constants, and dispatch tables for transformation operations.</summary>
[Pure]
internal static class TransformationConfig {
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

    internal static readonly OperationMetadata ApplyOperation = new(
        ValidationMode: V.None,
        OperationName: "Transformation.Apply",
        Error: E.Geometry.Transformation.TransformApplicationFailed);

    internal static readonly FrozenDictionary<Type, TransformMetadata> TransformDispatch =
        new Dictionary<Type, TransformMetadata> {
            [typeof(Transformation.MatrixTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidTransformMatrix,
                Validate: (request, context) => request is Transformation.MatrixTransform transform
                    ? (transform.Matrix.IsValid && Math.Abs(transform.Matrix.Determinant) > context.AbsoluteTolerance, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Valid: {transform.Matrix.IsValid}, Det: {transform.Matrix.Determinant.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => ((Transformation.MatrixTransform)request).Matrix),
            [typeof(Transformation.UniformScaleTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidScaleFactor,
                Validate: (request, _) => request is Transformation.UniformScaleTransform scale
                    ? (scale.Factor >= MinScaleFactor && scale.Factor <= MaxScaleFactor, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Factor: {scale.Factor.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => Transform.Scale(((Transformation.UniformScaleTransform)request).Anchor, ((Transformation.UniformScaleTransform)request).Factor)),
            [typeof(Transformation.NonUniformScaleTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidScaleFactor,
                Validate: (request, _) => request is Transformation.NonUniformScaleTransform scale
                    ? (scale.Plane.IsValid
                        && scale.XScale >= MinScaleFactor && scale.XScale <= MaxScaleFactor
                        && scale.YScale >= MinScaleFactor && scale.YScale <= MaxScaleFactor
                        && scale.ZScale >= MinScaleFactor && scale.ZScale <= MaxScaleFactor,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Plane: {scale.Plane.IsValid}, X: {scale.XScale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Y: {scale.YScale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Z: {scale.ZScale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.NonUniformScaleTransform scale = (Transformation.NonUniformScaleTransform)request;
                    return Transform.Scale(scale.Plane, scale.XScale, scale.YScale, scale.ZScale);
                }),
            [typeof(Transformation.AxisRotationTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidRotationAxis,
                Validate: (request, context) => request is Transformation.AxisRotationTransform rotation
                    ? (rotation.Axis.Length > context.AbsoluteTolerance, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Axis: {rotation.Axis.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.AxisRotationTransform rotation = (Transformation.AxisRotationTransform)request;
                    return Transform.Rotation(rotation.AngleRadians, rotation.Axis, rotation.Center);
                }),
            [typeof(Transformation.VectorRotationTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidRotationAxis,
                Validate: (request, context) => request is Transformation.VectorRotationTransform rotation
                    ? (rotation.StartDirection.Length > context.AbsoluteTolerance && rotation.EndDirection.Length > context.AbsoluteTolerance, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Start: {rotation.StartDirection.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {rotation.EndDirection.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.VectorRotationTransform rotation = (Transformation.VectorRotationTransform)request;
                    return Transform.Rotation(rotation.StartDirection, rotation.EndDirection, rotation.Center);
                }),
            [typeof(Transformation.MirrorTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidMirrorPlane,
                Validate: (request, _) => request is Transformation.MirrorTransform mirror
                    ? (mirror.Plane.IsValid, string.Empty)
                    : (false, string.Empty),
                Build: request => Transform.Mirror(((Transformation.MirrorTransform)request).Plane)),
            [typeof(Transformation.TranslationTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidTransformSpec,
                Validate: (request, _) => request is Transformation.TranslationTransform translation
                    ? (translation.Motion.IsValid, string.Empty)
                    : (false, string.Empty),
                Build: request => Transform.Translation(((Transformation.TranslationTransform)request).Motion)),
            [typeof(Transformation.ShearTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidShearParameters,
                Validate: (request, context) => request is Transformation.ShearTransform shear
                    ? (shear.Plane.IsValid
                        && shear.Direction.Length > context.AbsoluteTolerance
                        && shear.Plane.ZAxis.IsParallelTo(shear.Direction, context.AngleToleranceRadians * AngleToleranceMultiplier) == 0,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Plane: {shear.Plane.IsValid}, Dir: {shear.Direction.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.ShearTransform shear = (Transformation.ShearTransform)request;
                    return Transform.Shear(shear.Plane, shear.Direction * Math.Tan(shear.AngleRadians), Vector3d.Zero, Vector3d.Zero);
                }),
            [typeof(Transformation.ProjectionTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidProjectionPlane,
                Validate: (request, _) => request is Transformation.ProjectionTransform projection
                    ? (projection.Plane.IsValid, string.Empty)
                    : (false, string.Empty),
                Build: request => Transform.PlanarProjection(((Transformation.ProjectionTransform)request).Plane)),
            [typeof(Transformation.ChangeBasisTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidBasisPlanes,
                Validate: (request, _) => request is Transformation.ChangeBasisTransform change
                    ? (change.From.IsValid && change.To.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"From: {change.From.IsValid}, To: {change.To.IsValid}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.ChangeBasisTransform change = (Transformation.ChangeBasisTransform)request;
                    return Transform.ChangeBasis(change.From, change.To);
                }),
            [typeof(Transformation.PlaneToPlaneTransform)] = new(
                Operation: ApplyOperation,
                Error: E.Geometry.Transformation.InvalidBasisPlanes,
                Validate: (request, _) => request is Transformation.PlaneToPlaneTransform change
                    ? (change.From.IsValid && change.To.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"From: {change.From.IsValid}, To: {change.To.IsValid}"))
                    : (false, string.Empty),
                Build: request => {
                    Transformation.PlaneToPlaneTransform change = (Transformation.PlaneToPlaneTransform)request;
                    return Transform.PlaneToPlane(change.From, change.To);
                }),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<Type, ArrayOperationMetadata> ArrayDispatch =
        new Dictionary<Type, ArrayOperationMetadata> {
            [typeof(Transformation.RectangularArray)] = new(
                Operation: new OperationMetadata(V.None, "Transformation.RectangularArray", E.Geometry.Transformation.InvalidArrayParameters),
                Validate: (request, context) => request is Transformation.RectangularArray array
                    ? (array.XCount > 0
                        && array.YCount > 0
                        && array.ZCount > 0
                        && array.XCount * array.YCount * array.ZCount <= MaxArrayCount
                        && Math.Abs(array.XSpacing) > context.AbsoluteTolerance
                        && Math.Abs(array.YSpacing) > context.AbsoluteTolerance
                        && (array.ZCount <= 1 || Math.Abs(array.ZSpacing) > context.AbsoluteTolerance),
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"XCount: {array.XCount}, YCount: {array.YCount}, ZCount: {array.ZCount}, Total: {(array.XCount * array.YCount * array.ZCount)}"))
                    : (false, string.Empty),
                Build: (request, context) => TransformationCompute.RectangularArray(
                    request: (Transformation.RectangularArray)request,
                    context: context)),
            [typeof(Transformation.PolarArray)] = new(
                Operation: new OperationMetadata(V.None, "Transformation.PolarArray", E.Geometry.Transformation.InvalidArrayParameters),
                Validate: (request, context) => request is Transformation.PolarArray array
                    ? (array.Count > 0
                        && array.Count <= MaxArrayCount
                        && array.Axis.Length > context.AbsoluteTolerance
                        && array.TotalAngle > 0.0
                        && array.TotalAngle <= RhinoMath.TwoPI,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Count: {array.Count}, Axis: {array.Axis.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Angle: {array.TotalAngle.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: (request, context) => TransformationCompute.PolarArray(
                    request: (Transformation.PolarArray)request,
                    context: context)),
            [typeof(Transformation.LinearArray)] = new(
                Operation: new OperationMetadata(V.None, "Transformation.LinearArray", E.Geometry.Transformation.InvalidArrayParameters),
                Validate: (request, context) => request is Transformation.LinearArray array
                    ? (array.Count > 0
                        && array.Count <= MaxArrayCount
                        && array.Direction.Length > context.AbsoluteTolerance
                        && Math.Abs(array.Spacing) > context.AbsoluteTolerance,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Count: {array.Count}, Direction: {array.Direction.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Spacing: {array.Spacing.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"))
                    : (false, string.Empty),
                Build: (request, context) => TransformationCompute.LinearArray(
                    request: (Transformation.LinearArray)request,
                    context: context)),
            [typeof(Transformation.PathArray)] = new(
                Operation: new OperationMetadata(V.None, "Transformation.PathArray", E.Geometry.Transformation.InvalidArrayParameters),
                Validate: (request, context) => request is Transformation.PathArray array
                    ? (array.Count > 0
                        && array.Count <= MaxArrayCount
                        && array.Path is not null
                        && array.Path.IsValid
                        && array.Path.GetLength() > context.AbsoluteTolerance,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Count: {array.Count}, Path: {array.Path?.IsValid ?? false}"))
                    : (false, string.Empty),
                Build: (request, context) => TransformationCompute.PathArray(
                    request: (Transformation.PathArray)request,
                    context: context)),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<Type, MorphMetadata> MorphDispatch =
        new Dictionary<Type, MorphMetadata> {
            [typeof(Transformation.FlowMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Flow", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidFlowCurves,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, _) => request is Transformation.FlowMorph morph
                    ? (morph.BaseCurve.IsValid && morph.TargetCurve.IsValid && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Base: {morph.BaseCurve.IsValid}, Target: {morph.TargetCurve.IsValid}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Flow(
                    geometry: geometry,
                    request: (Transformation.FlowMorph)request,
                    context: context)),
            [typeof(Transformation.TwistMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Twist", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidTwistParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, context) => request is Transformation.TwistMorph morph
                    ? (morph.Axis.IsValid && Math.Abs(morph.AngleRadians) <= MaxTwistAngle && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Axis: {morph.Axis.IsValid}, Angle: {morph.AngleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Twist(
                    geometry: geometry,
                    request: (Transformation.TwistMorph)request,
                    context: context)),
            [typeof(Transformation.BendMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Bend", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidBendParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, context) => request is Transformation.BendMorph morph
                    ? (morph.Axis.IsValid && Math.Abs(morph.AngleRadians) <= MaxBendAngle && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Spine: {morph.Axis.IsValid}, Angle: {morph.AngleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Bend(
                    geometry: geometry,
                    request: (Transformation.BendMorph)request,
                    context: context)),
            [typeof(Transformation.TaperMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Taper", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidTaperParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, _) => request is Transformation.TaperMorph morph
                    ? (morph.Axis.IsValid && morph.StartWidth >= MinScaleFactor && morph.EndWidth >= MinScaleFactor && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Axis: {morph.Axis.IsValid}, Start: {morph.StartWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {morph.EndWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Taper(
                    geometry: geometry,
                    request: (Transformation.TaperMorph)request,
                    context: context)),
            [typeof(Transformation.StretchMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Stretch", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidStretchParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, _) => request is Transformation.StretchMorph morph
                    ? (morph.Axis.IsValid && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Axis: {morph.Axis.IsValid}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Stretch(
                    geometry: geometry,
                    request: (Transformation.StretchMorph)request,
                    context: context)),
            [typeof(Transformation.SplopMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Splop", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidSplopParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, _) => request is Transformation.SplopMorph morph
                    ? (morph.BasePlane.IsValid && morph.TargetSurface.IsValid && morph.TargetPoint.IsValid && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Plane: {morph.BasePlane.IsValid}, Surface: {morph.TargetSurface.IsValid}, Point: {morph.TargetPoint.IsValid}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Splop(
                    geometry: geometry,
                    request: (Transformation.SplopMorph)request,
                    context: context)),
            [typeof(Transformation.SporphMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Sporph", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidSporphParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, _) => request is Transformation.SporphMorph morph
                    ? (morph.SourceSurface.IsValid && morph.TargetSurface.IsValid && geometry.IsValid, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Source: {morph.SourceSurface.IsValid}, Target: {morph.TargetSurface.IsValid}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Sporph(
                    geometry: geometry,
                    request: (Transformation.SporphMorph)request,
                    context: context)),
            [typeof(Transformation.MaelstromMorph)] = new(
                Operation: new OperationMetadata(V.Standard, "Transformation.Maelstrom", E.Geometry.Transformation.MorphApplicationFailed),
                ValidationError: E.Geometry.Transformation.InvalidMaelstromParameters,
                MorphabilityError: E.Geometry.Transformation.GeometryNotMorphable,
                Validate: (request, geometry, context) => request is Transformation.MaelstromMorph morph
                    ? (morph.Center.IsValid
                        && morph.Axis.IsValid
                        && morph.Radius > context.AbsoluteTolerance
                        && geometry.IsValid
                        && Math.Abs(morph.AngleRadians) <= RhinoMath.TwoPI,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Center: {morph.Center.IsValid}, Axis: {morph.Axis.IsValid}, Radius: {morph.Radius.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"))
                    : (false, string.Empty),
                Execute: (request, geometry, context) => TransformationCompute.Maelstrom(
                    geometry: geometry,
                    request: (Transformation.MaelstromMorph)request,
                    context: context)),
        }.ToFrozenDictionary();

    internal const double MinScaleFactor = 1e-6;
    internal const double MaxScaleFactor = 1e6;
    internal const int MaxArrayCount = 10000;
    internal const double AngleToleranceMultiplier = 10.0;
    internal const double MaxTwistAngle = RhinoMath.TwoPI * 10.0;
    internal const double MaxBendAngle = RhinoMath.TwoPI;
    internal const double DefaultMorphTolerance = 0.001;

    internal sealed record OperationMetadata(
        V ValidationMode,
        string OperationName,
        SystemError Error);

    internal sealed record TransformMetadata(
        OperationMetadata Operation,
        SystemError Error,
        Func<Transformation.TransformRequest, IGeometryContext, (bool Valid, string Context)> Validate,
        Func<Transformation.TransformRequest, Transform> Build);

    internal sealed record ArrayOperationMetadata(
        OperationMetadata Operation,
        Func<Transformation.ArrayRequest, IGeometryContext, (bool Valid, string Context)> Validate,
        Func<Transformation.ArrayRequest, IGeometryContext, ComputeOutcome<Transform[]>> Build);

    internal sealed record MorphMetadata(
        OperationMetadata Operation,
        SystemError ValidationError,
        SystemError MorphabilityError,
        Func<Transformation.MorphRequest, GeometryBase, IGeometryContext, (bool Valid, string Context)> Validate,
        Func<Transformation.MorphRequest, GeometryBase, IGeometryContext, ComputeOutcome<GeometryBase>> Execute);

    internal readonly record struct ComputeOutcome<T>(
        bool Success,
        T Value,
        string Context);

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

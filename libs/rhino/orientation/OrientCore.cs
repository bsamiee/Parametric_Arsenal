using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Orchestration layer for orientation operations with UnifiedOperation wiring.</summary>
[Pure]
internal static class OrientCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Align<T>(T geometry, Orient.AlignmentRequest request, IGeometryContext context) where T : GeometryBase =>
        (geometry, request) switch {
            (null, _) => ResultFactory.Create<T>(error: E.Validation.GeometryInvalid),
            (_, null) => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode.WithContext("Request cannot be null")),
            _ when !OrientConfig.AlignmentOperations.TryGetValue(request.GetType(), out OrientConfig.AlignmentOperationMetadata? metadata) =>
                ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode.WithContext(request.GetType().Name)),
            _ => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ExecuteAlignment(item, request, metadata)),
                    config: new OperationConfig<T, T> {
                        Context = context,
                        ValidationMode = ComposeValidation(geometry.GetType(), metadata.ValidationMode),
                        OperationName = metadata.OperationName,
                    })
                .Map(results => results[0]),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.OrientationOptimizationResult> Optimize(
        Brep brep,
        Orient.OrientationOptimizationRequest request,
        IGeometryContext context) =>
        (brep, request?.Criterion) switch {
            (null, _) => ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Validation.GeometryInvalid),
            (_, null) => ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Geometry.InvalidOrientationMode.WithContext("Criterion cannot be null")),
            _ => UnifiedOperation.Apply(
                    input: brep,
                    operation: (Func<Brep, Result<IReadOnlyList<Orient.OrientationOptimizationResult>>>)(item =>
                        OrientCompute.OptimizeOrientation(item, request.Criterion, context)
                            .Map(result => (IReadOnlyList<Orient.OrientationOptimizationResult>)[result,])),
                    config: new OperationConfig<Brep, Orient.OrientationOptimizationResult> {
                        Context = context,
                        ValidationMode = OrientConfig.OptimizationMetadata.ValidationMode,
                        OperationName = OrientConfig.OptimizationMetadata.OperationName,
                    })
                .Map(results => results[0]),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.RelativeOrientationResult> Relative(
        Orient.RelativeOrientationRequest request,
        IGeometryContext context) =>
        request switch {
            null => ResultFactory.Create<Orient.RelativeOrientationResult>(error: E.Geometry.InvalidOrientationMode.WithContext("Request cannot be null")),
            { Primary: null } => ResultFactory.Create<Orient.RelativeOrientationResult>(error: E.Validation.GeometryInvalid.WithContext("Primary geometry cannot be null")),
            { Secondary: null } => ResultFactory.Create<Orient.RelativeOrientationResult>(error: E.Validation.GeometryInvalid.WithContext("Secondary geometry cannot be null")),
            _ => ValidateGeometry(request.Secondary, context)
                .Bind(_ => UnifiedOperation.Apply(
                    input: request.Primary,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Orient.RelativeOrientationResult>>>)(primary =>
                        OrientCompute.ComputeRelative(primary, request.Secondary, context)
                            .Map(result => (IReadOnlyList<Orient.RelativeOrientationResult>)[result,])),
                    config: new OperationConfig<GeometryBase, Orient.RelativeOrientationResult> {
                        Context = context,
                        ValidationMode = OrientConfig.RelativeMetadata.ValidationMode,
                        OperationName = OrientConfig.RelativeMetadata.OperationName,
                    }))
                .Map(results => results[0]),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.PatternDetectionResult> Detect(
        Orient.PatternDetectionRequest request,
        IGeometryContext context) =>
        request switch {
            null => ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.InvalidOrientationMode.WithContext("Request cannot be null")),
            { Geometries: null } => ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.InvalidOrientationMode.WithContext("Geometry collection cannot be null")),
            _ => UnifiedOperation.Apply(
                    input: request.Geometries,
                    operation: (Func<GeometryBase[], Result<IReadOnlyList<Orient.PatternDetectionResult>>>)(items =>
                        OrientCompute.DetectPattern(items, context)
                            .Map(result => (IReadOnlyList<Orient.PatternDetectionResult>)[result,])),
                    config: new OperationConfig<GeometryBase[], Orient.PatternDetectionResult> {
                        Context = context,
                        ValidationMode = OrientConfig.PatternMetadata.ValidationMode,
                        OperationName = OrientConfig.PatternMetadata.OperationName,
                    })
                .Map(results => results[0]),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ExecuteAlignment<T>(
        T geometry,
        Orient.AlignmentRequest request,
        OrientConfig.AlignmentOperationMetadata metadata) where T : GeometryBase =>
        metadata.Kind switch {
            OrientConfig.AlignmentOperationKind.Plane => OrientCompute.AlignToPlane(geometry, ((Orient.PlaneAlignment)request).Target),
            OrientConfig.AlignmentOperationKind.CurveFrame => OrientCompute.AlignToCurveFrame(geometry, ((Orient.CurveFrameAlignment)request).Curve, ((Orient.CurveFrameAlignment)request).Parameter),
            OrientConfig.AlignmentOperationKind.SurfaceFrame => OrientCompute.AlignToSurfaceFrame(geometry, ((Orient.SurfaceFrameAlignment)request).Surface, ((Orient.SurfaceFrameAlignment)request).U, ((Orient.SurfaceFrameAlignment)request).V),
            OrientConfig.AlignmentOperationKind.WorldXY => OrientCompute.AlignToWorldPlane(geometry, Plane.WorldXY, Vector3d.XAxis, Vector3d.YAxis),
            OrientConfig.AlignmentOperationKind.WorldYZ => OrientCompute.AlignToWorldPlane(geometry, Plane.WorldYZ, Vector3d.YAxis, Vector3d.ZAxis),
            OrientConfig.AlignmentOperationKind.WorldXZ => OrientCompute.AlignToWorldPlane(geometry, new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis), Vector3d.XAxis, Vector3d.ZAxis),
            OrientConfig.AlignmentOperationKind.BoundingBoxOrigin => OrientCompute.AlignBoundingBoxToOrigin(geometry),
            OrientConfig.AlignmentOperationKind.VolumeOrigin => OrientCompute.AlignCentroidToOrigin(geometry),
            OrientConfig.AlignmentOperationKind.BoundingBoxPoint => OrientCompute.AlignBoundingBoxToPoint(geometry, ((Orient.BoundingBoxPointAlignment)request).Target),
            OrientConfig.AlignmentOperationKind.MassPoint => OrientCompute.AlignCentroidToPoint(geometry, ((Orient.MassPointAlignment)request).Target),
            OrientConfig.AlignmentOperationKind.Vector => OrientCompute.AlignVector(geometry, ((Orient.VectorAlignment)request).Source, ((Orient.VectorAlignment)request).Target, ((Orient.VectorAlignment)request).Anchor),
            OrientConfig.AlignmentOperationKind.BestFit => OrientCompute.AlignBestFit(geometry),
            OrientConfig.AlignmentOperationKind.Mirror => OrientCompute.MirrorGeometry(geometry, ((Orient.MirrorAlignment)request).Plane),
            OrientConfig.AlignmentOperationKind.Flip => OrientCompute.FlipGeometry(geometry),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationMode.WithContext(metadata.OperationName ?? "Orient.Align")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static V ComposeValidation(Type geometryType, V operationFlags) =>
        OrientConfig.GeometryValidation.TryGetValue(geometryType, out V baseFlags)
            ? baseFlags | operationFlags
            : V.Standard | operationFlags;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<GeometryBase> ValidateGeometry(GeometryBase geometry, IGeometryContext context) =>
        geometry is null
            ? ResultFactory.Create<GeometryBase>(error: E.Validation.GeometryInvalid)
            : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<GeometryBase>>>)(item => ResultFactory.Create(value: (IReadOnlyList<GeometryBase>)[item,])),
                    config: new OperationConfig<GeometryBase, GeometryBase> {
                        Context = context,
                        ValidationMode = ComposeValidation(geometry.GetType(), V.Standard),
                        OperationName = "Orient.Validate",
                    })
                .Map(results => results[0]);
}

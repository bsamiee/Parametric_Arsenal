using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform matrix construction, validation, and application.</summary>
[Pure]
internal static class TransformationCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ApplyTransform<T>(
        T geometry,
        Transformation.TransformRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        BuildTransform(request: request, context: context)
            .Bind(xform => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ApplyTransform(item: item, transform: xform)),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = TransformationConfig.GetValidationMode(typeof(T)),
                    OperationName = TransformationConfig.ApplyOperation.OperationName,
                    EnableDiagnostics = enableDiagnostics,
                }))
            .Map(result => result[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        Transformation.ArrayRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        TransformationConfig.ArrayDispatch.TryGetValue(request.GetType(), out TransformationConfig.ArrayOperationMetadata metadata)
            ? metadata.Validate(request, context) switch {
                (true, _) => WrapArrayResult(
                    metadata: metadata,
                    geometry: geometry,
                    request: request,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                (false, string ctx) => ResultFactory.Create<IReadOnlyList<T>>(error: metadata.Operation.Error.WithContext(ctx)),
            }
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Morph<T>(
        T geometry,
        Transformation.MorphRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        TransformationConfig.MorphDispatch.TryGetValue(request.GetType(), out TransformationConfig.MorphMetadata metadata)
            ? metadata.Validate(request, geometry, context) switch {
                (true, _) when !SpaceMorph.IsMorphable(geometry) => ResultFactory.Create<T>(error: metadata.MorphabilityError.WithContext(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Geometry: {typeof(T).Name}, Morph: {request.GetType().Name}"))),
                (true, _) => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ApplyMorph(
                        geometry: item,
                        request: request,
                        metadata: metadata,
                        context: context)),
                    config: new OperationConfig<T, T> {
                        Context = context,
                        ValidationMode = metadata.Operation.ValidationMode | TransformationConfig.GetValidationMode(typeof(T)),
                        OperationName = metadata.Operation.OperationName,
                        AccumulateErrors = false,
                        EnableDiagnostics = enableDiagnostics,
                    })
                    .Map(result => result[0]),
                (false, string ctx) => ResultFactory.Create<T>(error: metadata.ValidationError.WithContext(ctx)),
            }
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> WrapArrayResult<T>(
        TransformationConfig.ArrayOperationMetadata metadata,
        T geometry,
        Transformation.ArrayRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        TransformationConfig.ComputeOutcome<Transform[]> outcome = metadata.Build(
            request: request,
            context: context);

        return outcome.Success
            ? UnifiedOperation.Apply(
                input: outcome.Value,
                operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform => ApplyTransform(item: geometry, transform: xform)),
                config: new OperationConfig<IReadOnlyList<Transform>, T> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = false,
                    OperationName = metadata.Operation.OperationName,
                    EnableDiagnostics = enableDiagnostics,
                })
            : ResultFactory.Create<IReadOnlyList<T>>(error: metadata.Operation.Error.WithContext(outcome.Context));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyMorph<T>(
        T geometry,
        Transformation.MorphRequest request,
        TransformationConfig.MorphMetadata metadata,
        IGeometryContext context) where T : GeometryBase {
        TransformationConfig.ComputeOutcome<GeometryBase> outcome = metadata.Execute(
            request: request,
            geometry: geometry,
            context: context);

        return outcome.Success && outcome.Value is T cast
            ? ResultFactory.Create<IReadOnlyList<T>>(value: [cast,])
            : ResultFactory.Create<IReadOnlyList<T>>(error: metadata.Operation.Error.WithContext(outcome.Context));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildTransform(
        Transformation.TransformRequest request,
        IGeometryContext context) =>
        TransformationConfig.TransformDispatch.TryGetValue(request.GetType(), out TransformationConfig.TransformMetadata metadata)
            ? metadata.Validate(request, context) switch {
                (true, _) => ResultFactory.Create(value: metadata.Build(request)),
                (false, string ctx) => ResultFactory.Create<Transform>(error: metadata.Error.WithContext(ctx)),
            }
            : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        Transform transform) where T : GeometryBase {
        TransformationConfig.ComputeOutcome<IReadOnlyList<T>> outcome = TransformationCompute.ApplyTransform(
            item: item,
            transform: transform);

        return outcome.Success
            ? ResultFactory.Create(value: outcome.Value)
            : ResultFactory.Create<IReadOnlyList<T>>(error: TransformationConfig.ApplyOperation.Error.WithContext(outcome.Context));
    }
}

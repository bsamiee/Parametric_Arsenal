using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing with RhinoCommon RTree algorithms and monadic composition.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>RTree cache using weak references for automatic memory management and tree reuse across operations.</summary>
    internal static readonly ConditionalWeakTable<object, RTree> TreeCache = [];

    /// <summary>Closest point result containing location, distance, and optional parameter/component data.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ClosestPointData(
        Point3d Point,
        double Distance,
        double Parameter,
        ComponentIndex ComponentIndex);

    /// <summary>Performs spatial indexing operations using RhinoCommon RTree algorithms with type-based query dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        bool enableDiagnostics = false) where TInput : notnull where TQuery : notnull =>
        SpatialCore.OperationRegistry.TryGetValue((typeof(TInput), typeof(TQuery)), out (Func<object, RTree>? _, V mode, int bufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> execute) config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => config.execute(item, query, context, config.bufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{typeof(TQuery).Name}",
                    EnableDiagnostics = enableDiagnostics,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

    /// <summary>Computes closest point on geometry to test point with distance and parameter information.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClosestPointData> ClosestPoint<T>(
        T geometry,
        Point3d testPoint,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<ClosestPointData>>>)(item =>
                SpatialCore.ComputeClosestPoint(geometry: item, testPoint: testPoint, context: context)),
            config: new OperationConfig<T, ClosestPointData> {
                Context = context,
                ValidationMode = V.Standard,
                OperationName = $"Spatial.ClosestPoint.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            }).Map(r => r[0]);

    /// <summary>Computes minimum distance between two geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> Distance<T1, T2>(
        T1 geometry1,
        T2 geometry2,
        IGeometryContext context,
        bool enableDiagnostics = false) where T1 : GeometryBase where T2 : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry1,
            operation: (Func<T1, Result<IReadOnlyList<double>>>)(item =>
                SpatialCore.ComputeDistance(geometry1: item, geometry2: geometry2, context: context)),
            config: new OperationConfig<T1, double> {
                Context = context,
                ValidationMode = V.Standard,
                OperationName = $"Spatial.Distance.{typeof(T1).Name}.{typeof(T2).Name}",
                EnableDiagnostics = enableDiagnostics,
            }).Map(r => r[0]);
}

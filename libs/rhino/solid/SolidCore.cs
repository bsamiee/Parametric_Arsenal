using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Solid;

/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
[Pure]
internal static class SolidCore {
    /// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
    internal static readonly FrozenDictionary<(Type T1, Type T2, byte Op), (V Mode, Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> Executor)> OperationRegistry =
        new Dictionary<(Type T1, Type T2, byte Op), (V Mode, Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> Executor)> {
            [(typeof(Brep), typeof(Brep), SolidConfig.UnionOp)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), SolidConfig.IntersectionOp)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), SolidConfig.DifferenceOp)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), SolidConfig.SplitOp)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), SolidConfig.UnionOp)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), SolidConfig.IntersectionOp)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), SolidConfig.DifferenceOp)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Mesh), typeof(Mesh), SolidConfig.UnionOp)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), SolidConfig.IntersectionOp)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), SolidConfig.DifferenceOp)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), SolidConfig.SplitOp)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), SolidConfig.UnionOp)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), SolidConfig.IntersectionOp)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), SolidConfig.DifferenceOp)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
        }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> ExecuteOperation<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        byte operation,
        IGeometryContext context,
        Solid.SolidOptions options) where T1 : notnull where T2 : notnull =>
        OperationRegistry.TryGetValue(
            key: (typeof(T1), typeof(T2), operation),
            value: out (V ValidationMode, Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> Executor) config) switch {
            true => UnifiedOperation.Apply(
                input: geometryA,
                operation: (Func<T1, Result<IReadOnlyList<Solid.SolidOutput>>>)(itemA => config.Executor(
                    itemA,
                    geometryB,
                    operation,
                    options,
                    context)
                    .Map(output => (IReadOnlyList<Solid.SolidOutput>)[output])),
                config: new OperationConfig<T1, Solid.SolidOutput> {
                    Context = context,
                    ValidationMode = config.ValidationMode,
                    OperationName = $"Solid.{operation}.{typeof(T1).Name}.{typeof(T2).Name}",
                    EnableDiagnostics = false,
                })
                .Map(outputs => outputs.Count > 0 ? outputs[0] : Solid.SolidOutput.Empty),
            false => ResultFactory.Create<Solid.SolidOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext(
                    $"Operation: {operation}, Types: {typeof(T1).Name}, {typeof(T2).Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> MakeBrepExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepBoolean((Brep)a, (Brep)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> MakeBrepArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepArrayBoolean((Brep[])a, (Brep[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> MakeMeshExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshBoolean((Mesh)a, (Mesh)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, byte, Solid.SolidOptions, IGeometryContext, Result<Solid.SolidOutput>> MakeMeshArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshArrayBoolean((Mesh[])a, (Mesh[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Solid.SolidOutput> ExecuteBrepBoolean(
        Brep brepA,
        Brep brepB,
        byte operation,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        operation switch {
            SolidConfig.UnionOp => SolidCompute.BrepUnion([brepA, brepB,], options, context),
            SolidConfig.IntersectionOp => SolidCompute.BrepIntersection([brepA,], [brepB,], options, context),
            SolidConfig.DifferenceOp => SolidCompute.BrepDifference([brepA,], [brepB,], options, context),
            SolidConfig.SplitOp => SolidCompute.BrepSplit(brepA, brepB, options, context),
            _ => ResultFactory.Create<Solid.SolidOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Solid.SolidOutput> ExecuteBrepArrayBoolean(
        Brep[] brepsA,
        Brep[] brepsB,
        byte operation,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        operation switch {
            SolidConfig.UnionOp => SolidCompute.BrepUnion([.. brepsA, .. brepsB,], options, context),
            SolidConfig.IntersectionOp => SolidCompute.BrepIntersection(brepsA, brepsB, options, context),
            SolidConfig.DifferenceOp => SolidCompute.BrepDifference(brepsA, brepsB, options, context),
            _ => ResultFactory.Create<Solid.SolidOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep[] operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Solid.SolidOutput> ExecuteMeshBoolean(
        Mesh meshA,
        Mesh meshB,
        byte operation,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        operation switch {
            SolidConfig.UnionOp => SolidCompute.MeshUnion([meshA, meshB,], options, context),
            SolidConfig.IntersectionOp => SolidCompute.MeshIntersection([meshA,], [meshB,], options, context),
            SolidConfig.DifferenceOp => SolidCompute.MeshDifference([meshA,], [meshB,], options, context),
            SolidConfig.SplitOp => SolidCompute.MeshSplit([meshA,], [meshB,], options, context),
            _ => ResultFactory.Create<Solid.SolidOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Solid.SolidOutput> ExecuteMeshArrayBoolean(
        Mesh[] meshesA,
        Mesh[] meshesB,
        byte operation,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        operation switch {
            SolidConfig.UnionOp => SolidCompute.MeshUnion([.. meshesA, .. meshesB,], options, context),
            SolidConfig.IntersectionOp => SolidCompute.MeshIntersection(meshesA, meshesB, options, context),
            SolidConfig.DifferenceOp => SolidCompute.MeshDifference(meshesA, meshesB, options, context),
            _ => ResultFactory.Create<Solid.SolidOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh[] operation: {operation}")),
        };
}

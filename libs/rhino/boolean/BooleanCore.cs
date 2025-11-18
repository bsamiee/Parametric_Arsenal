using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Boolean;

/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
[Pure]
internal static class BooleanCore {
    /// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
    internal static readonly FrozenDictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> Executor)> OperationRegistry =
        new Dictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> Executor)> {
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Union)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Intersection)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Difference)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Split)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Union)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Intersection)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Difference)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Union)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Intersection)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Difference)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Split)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Union)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Intersection)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Difference)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
        }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeBrepExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepBoolean((Brep)a, (Brep)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeBrepArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepArrayBoolean((Brep[])a, (Brep[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeMeshExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshBoolean((Mesh)a, (Mesh)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeMeshArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshArrayBoolean((Mesh[])a, (Mesh[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteBrepBoolean(
        Brep brepA,
        Brep brepB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.BrepUnion([brepA, brepB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.BrepIntersection([brepA,], [brepB,], options, context),
            Boolean.OperationType.Difference => BooleanCompute.BrepDifference([brepA,], [brepB,], options, context),
            Boolean.OperationType.Split => BooleanCompute.BrepSplit(brepA, brepB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteBrepArrayBoolean(
        Brep[] brepsA,
        Brep[] brepsB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.BrepUnion([.. brepsA, .. brepsB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.BrepIntersection(brepsA, brepsB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.BrepDifference(brepsA, brepsB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep[] operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteMeshBoolean(
        Mesh meshA,
        Mesh meshB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.MeshUnion([meshA, meshB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.MeshIntersection([meshA,], [meshB,], options, context),
            Boolean.OperationType.Difference => BooleanCompute.MeshDifference([meshA,], [meshB,], options, context),
            Boolean.OperationType.Split => BooleanCompute.MeshSplit([meshA,], [meshB,], options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteMeshArrayBoolean(
        Mesh[] meshesA,
        Mesh[] meshesB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.MeshUnion([.. meshesA, .. meshesB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.MeshIntersection(meshesA, meshesB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.MeshDifference(meshesA, meshesB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh[] operation: {operation}")),
        };
}

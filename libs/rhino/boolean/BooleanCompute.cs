using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Boolean;

/// <summary>Dense boolean algorithm implementations wrapping RhinoCommon SDK.</summary>
[Pure]
internal static class BooleanCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepUnion(
        Brep[] breps,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanUnion(breps: breps, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Union returned null - verify input Breps are closed, valid, and have compatible tolerances")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Union produced empty result - Breps may not overlap or touch")),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps in result: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepIntersection(
        Brep[] firstSet,
        Brep[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanIntersection(firstSet: firstSet, secondSet: secondSet, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Intersection returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepDifference(
        Brep[] firstSet,
        Brep[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanDifference(
                    firstSet: firstSet,
                    secondSet: secondSet,
                    tolerance: tolerance,
                    manifoldOnly: options.ManifoldOnly) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Difference returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepSplit(
        Brep brep,
        Brep splitter,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : brep.Split(splitter, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Split returned null - verify Breps intersect")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [brep,],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepTrim(
        Brep target,
        Brep cutter,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : target.Trim(cutter, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.TrimFailed.WithContext("Trim returned null - verify Breps are valid and intersect")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [target,],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshUnion(
        Mesh[] meshes,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanUnion(meshes: meshes, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh union returned null - ensure meshes are closed and manifold")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh union produced empty result")),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshIntersection(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanIntersection(firstSet: firstSet, secondSet: secondSet) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh intersection returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshDifference(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanDifference(firstSet: firstSet, secondSet: secondSet) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh difference returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshSplit(
        Mesh[] meshes,
        Mesh[] cutters,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanSplit(meshes, cutters) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh split returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [.. meshes,],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();
}

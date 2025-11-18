using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Solid;

/// <summary>Dense boolean algorithm implementations wrapping RhinoCommon SDK.</summary>
[Pure]
internal static class SolidCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> BrepUnion(
        Brep[] breps,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanUnion(breps: breps, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Union returned null - verify input Breps are closed, valid, and have compatible tolerances")),
                    { Length: 0 } => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Union produced empty result - Breps may not overlap or touch")),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: results,
                                Meshes: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps in result: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: results,
                            Meshes: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> BrepIntersection(
        Brep[] firstSet,
        Brep[] secondSet,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanIntersection(firstSet: firstSet, secondSet: secondSet, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Intersection returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: results,
                                Meshes: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: results,
                            Meshes: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> BrepDifference(
        Brep[] firstSet,
        Brep[] secondSet,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanDifference(
                    firstSet: firstSet,
                    secondSet: secondSet,
                    tolerance: tolerance,
                    manifoldOnly: options.ManifoldOnly) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Difference returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: results,
                                Meshes: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: results,
                            Meshes: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> BrepSplit(
        Brep brep,
        Brep splitter,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : brep.Split(splitter, tolerance) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Split returned null - verify Breps intersect")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [brep,],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: results,
                                Meshes: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: results,
                            Meshes: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> BrepTrim(
        Brep target,
        Brep cutter,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : target.Trim(cutter, tolerance) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.TrimFailed.WithContext("Trim returned null - verify Breps are valid and intersect")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [target,],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: results,
                                Meshes: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: results,
                            Meshes: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> MeshUnion(
        Mesh[] meshes,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanUnion(meshes: meshes, tolerance: tolerance) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh union returned null - ensure meshes are closed and manifold")),
                    { Length: 0 } => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh union produced empty result")),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: [],
                                Meshes: results,
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: [],
                            Meshes: results,
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> MeshIntersection(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanIntersection(firstSet: firstSet, secondSet: secondSet) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh intersection returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: [],
                                Meshes: results,
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: [],
                            Meshes: results,
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> MeshDifference(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanDifference(firstSet: firstSet, secondSet: secondSet) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh difference returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [],
                        Meshes: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: [],
                                Meshes: results,
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: [],
                            Meshes: results,
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Solid.SolidOutput> MeshSplit(
        Mesh[] meshes,
        Mesh[] cutters,
        Solid.SolidOptions options,
        IGeometryContext context) =>
        ((Func<Result<Solid.SolidOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Solid.SolidOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanSplit(meshes, cutters) switch {
                    null => ResultFactory.Create<Solid.SolidOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh split returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Solid.SolidOutput(
                        Breps: [],
                        Meshes: [.. meshes,],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Solid.SolidOutput(
                                Breps: [],
                                Meshes: results,
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Solid.SolidOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Solid.SolidOutput(
                            Breps: [],
                            Meshes: results,
                            ToleranceUsed: tolerance)),
                };
        }))();
}

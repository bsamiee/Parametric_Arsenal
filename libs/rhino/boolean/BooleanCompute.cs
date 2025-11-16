using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Commands;
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
                : Brep.CreateBooleanUnion(breps, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Union returned null - verify input Breps are closed, valid, and have compatible tolerances")),
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
                : Brep.CreateBooleanIntersection(firstSet, secondSet, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Intersection returned null - verify Breps are closed and overlap")),
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
                        error: E.Geometry.InvalidGeometryType.WithContext("Difference returned null - verify Breps are closed and overlap")),
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
                        error: E.Geometry.InvalidGeometryType.WithContext("Split returned null - verify Breps intersect")),
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
    internal static Result<Boolean.BooleanOutput> MeshUnion(
        Mesh[] meshes,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            MeshBooleanOptions meshOptions = new() { Tolerance = tolerance, };

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanUnion(meshes, meshOptions, out Result result) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext($"Mesh union returned null (SDK result: {result}) - ensure meshes are closed and manifold")),
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
    internal static Result<Boolean.BooleanOutput> MeshIntersection(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            MeshBooleanOptions meshOptions = new() { Tolerance = tolerance, };

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanIntersection(firstSet, secondSet, meshOptions, out Result result) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext($"Mesh intersection returned null (SDK result: {result})")),
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
            MeshBooleanOptions meshOptions = new() { Tolerance = tolerance, };

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanDifference(firstSet, secondSet, meshOptions, out Result result) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext($"Mesh difference returned null (SDK result: {result})")),
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
            MeshBooleanOptions meshOptions = new() { Tolerance = tolerance, };

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanSplit(meshes, cutters, meshOptions, out Result result) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext($"Mesh split returned null (SDK result: {result})")),
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> CurveRegions(
        Curve[] curves,
        Plane plane,
        bool combineRegions,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;

            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Curve.CreateBooleanRegions(curves, plane, combineRegions, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Curve region extraction returned null - verify curves are planar and coplanar")),
                    CurveBooleanRegions regions when regions.RegionCount > 0 => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [.. Enumerable.Range(0, regions.RegionCount)
                            .SelectMany(i => regions.RegionCurves(i))
                            .Where(static c => c is not null),
                        ],
                        ToleranceUsed: tolerance)),
                    _ => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                };
        }))();
}

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

    /// <summary>Determines point containment within closed curves using winding number algorithm with tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<bool> Contains(Curve curve, Point3d point, Plane plane, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<bool>>>)(c =>
                (c.IsClosed && c.IsPlanar(minimumTolerance: context.AbsoluteTolerance)) switch {
                    true => c.Contains(testPoint: point, plane: plane, tolerance: context.AbsoluteTolerance) switch {
                        PointContainment.Inside => ResultFactory.Create(value: (IReadOnlyList<bool>)[true,]),
                        PointContainment.Outside => ResultFactory.Create(value: (IReadOnlyList<bool>)[false,]),
                        PointContainment.Coincident => ResultFactory.Create(value: (IReadOnlyList<bool>)[true,]),
                        _ => ResultFactory.Create<IReadOnlyList<bool>>(error: E.Spatial.ContainmentFailed),
                    },
                    false => ResultFactory.Create<IReadOnlyList<bool>>(error: E.Validation.CurveNotClosedOrPlanar),
                }),
            config: new OperationConfig<Curve, bool> {
                Context = context,
                ValidationMode = V.Standard | V.AreaCentroid,
            }).Map(r => r[0]);

    /// <summary>Determines point containment within brep using closest point and normal direction test.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<bool> Contains(Brep brep, Point3d point, IGeometryContext context, bool strictlyIn = true) =>
        UnifiedOperation.Apply(
            input: brep,
            operation: (Func<Brep, Result<IReadOnlyList<bool>>>)(b =>
                b.IsPointInside(point: point, tolerance: context.AbsoluteTolerance, strictlyIn: strictlyIn) switch {
                    true => ResultFactory.Create(value: (IReadOnlyList<bool>)[true,]),
                    false => ResultFactory.Create(value: (IReadOnlyList<bool>)[false,]),
                }),
            config: new OperationConfig<Brep, bool> {
                Context = context,
                ValidationMode = V.Standard | V.Topology,
            }).Map(r => r[0]);

    /// <summary>Determines point containment within mesh using ray casting with configurable strictness.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<bool> Contains(Mesh mesh, Point3d point, IGeometryContext context, bool strictlyIn = true) =>
        UnifiedOperation.Apply(
            input: mesh,
            operation: (Func<Mesh, Result<IReadOnlyList<bool>>>)(m =>
                (m.IsClosed && m.SolidOrientation() != 0) switch {
                    true => m.IsPointInside(point: point, tolerance: context.AbsoluteTolerance, strictlyIn: strictlyIn) switch {
                        true => ResultFactory.Create(value: (IReadOnlyList<bool>)[true,]),
                        false => ResultFactory.Create(value: (IReadOnlyList<bool>)[false,]),
                    },
                    false => ResultFactory.Create<IReadOnlyList<bool>>(error: E.Validation.MeshNotClosed),
                }),
            config: new OperationConfig<Mesh, bool> {
                Context = context,
                ValidationMode = V.Standard | V.MeshSpecific,
            }).Map(r => r[0]);

    /// <summary>Projects point to closest location on curve with parameter and distance output.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Point, double Parameter, double Distance)> ClosestPoint(Curve curve, Point3d point, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<(Point3d, double, double)>>>)(c =>
                c.ClosestPoint(testPoint: point, t: out double parameter) switch {
                    true => (c.PointAt(parameter), parameter, point.DistanceTo(c.PointAt(parameter))) switch {
                        (Point3d pt, double t, double d) => ResultFactory.Create(value: (IReadOnlyList<(Point3d, double, double)>)[(pt, t, d),]),
                    },
                    false => ResultFactory.Create<IReadOnlyList<(Point3d, double, double)>>(error: E.Spatial.ClosestPointFailed),
                }),
            config: new OperationConfig<Curve, (Point3d, double, double)> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);

    /// <summary>Projects point to closest location on surface with UV parameters and distance output.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Point, double U, double V, double Distance)> ClosestPoint(Surface surface, Point3d point, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: surface,
            operation: (Func<Surface, Result<IReadOnlyList<(Point3d, double, double, double)>>>)(s =>
                s.ClosestPoint(testPoint: point, u: out double u, v: out double v) switch {
                    true => (s.PointAt(u, v), u, v, point.DistanceTo(s.PointAt(u, v))) switch {
                        (Point3d pt, double uParam, double vParam, double d) => ResultFactory.Create(value: (IReadOnlyList<(Point3d, double, double, double)>)[(pt, uParam, vParam, d),]),
                    },
                    false => ResultFactory.Create<IReadOnlyList<(Point3d, double, double, double)>>(error: E.Spatial.ClosestPointFailed),
                }),
            config: new OperationConfig<Surface, (Point3d, double, double, double)> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);

    /// <summary>Projects point to closest location on brep with component identification and distance output.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Point, ComponentIndex Component, double Distance)> ClosestPoint(Brep brep, Point3d point, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: brep,
            operation: (Func<Brep, Result<IReadOnlyList<(Point3d, ComponentIndex, double)>>>)(b =>
                b.ClosestPoint(testPoint: point) switch {
                    Point3d pt when pt.IsValid => (pt, b.ClosestPoint(testPoint: point, maximumDistance: double.MaxValue, out ComponentIndex ci), point.DistanceTo(pt)) switch {
                        (Point3d p, Point3d _, ComponentIndex component, double d) => ResultFactory.Create(value: (IReadOnlyList<(Point3d, ComponentIndex, double)>)[(p, component, d),]),
                    },
                    _ => ResultFactory.Create<IReadOnlyList<(Point3d, ComponentIndex, double)>>(error: E.Spatial.ClosestPointFailed),
                }),
            config: new OperationConfig<Brep, (Point3d, ComponentIndex, double)> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);
}

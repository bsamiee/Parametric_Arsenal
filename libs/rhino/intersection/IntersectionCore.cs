using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using RhinoIntersect = Rhino.Geometry.Intersect.Intersection;

namespace Arsenal.Rhino.Intersection;

/// <summary>RhinoCommon intersection dispatch with FrozenDictionary resolution and unified metadata.</summary>
internal static class IntersectionCore {
    private static readonly Func<(bool, Curve[]?, Point3d[]?), Intersection.IntersectionResult> ArrayResultBuilder = static tuple => tuple switch {
        (true, { Length: > 0 } curves, { Length: > 0 } points) => new(points, curves, [], [], [], []),
        (true, { Length: > 0 } curves, _) => new([], curves, [], [], [], []),
        (true, _, { Length: > 0 } points) => new(points, [], [], [], [], []),
        (true, _, _) or (false, _, _) => Intersection.IntersectionResult.Empty,
    };

    private static readonly Func<CurveIntersections?, Curve, Intersection.IntersectionResult> IntersectionProcessor = static (results, source) => results switch {
        { Count: > 0 } => new(
            [.. results.Select(static e => e.PointA),],
            [.. results.Where(static e => e.IsOverlap).Select(e => source.Trim(e.OverlapA)).Where(static t => t is not null),],
            [.. results.Select(static e => e.ParameterA),],
            [.. results.Select(static e => e.ParameterB),],
            [],
            []),
        _ => Intersection.IntersectionResult.Empty,
    };

    private static readonly Func<int, Point3d, Point3d, double, double[]?, Intersection.IntersectionResult> TwoPointHandler = static (count, first, second, tolerance, parameters) =>
        (count, first.DistanceTo(second) > tolerance) switch {
            ( > 1, true) => new([first, second,], [], parameters ?? [], [], [], []),
            ( > 0, _) => new([first,], [], parameters is { Length: > 0 } ? [parameters[0],] : [], [], [], []),
            _ => Intersection.IntersectionResult.Empty,
        };

    private static readonly Func<int, Circle, Intersection.IntersectionResult> CircleHandler = static (type, circle) => type switch {
        1 => new([], [new ArcCurve(circle),], [], [], [], []),
        2 => new([circle.Center,], [], [], [], [], []),
        _ => Intersection.IntersectionResult.Empty,
    };

    private static readonly Func<Polyline[]?, Result<Intersection.IntersectionResult>> PolylineProcessor = static polylines => polylines switch {
        { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionResult([.. polylines.SelectMany(static p => p),], [], [], [], [], [.. polylines,])),
        null => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
        _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
    };

    private static readonly FrozenDictionary<(Type, Type), Func<object, object, double, Intersection.IntersectionMode, IGeometryContext, Result<Intersection.IntersectionResult>>> _executors =
        new Dictionary<(Type, Type), Func<object, object, double, Intersection.IntersectionMode, IGeometryContext, Result<Intersection.IntersectionResult>>> {
            [(typeof(Curve), typeof(Curve))] = static (first, second, tolerance, _, _) => {
                Curve curveA = (Curve)first;
                Curve curveB = (Curve)second;
                using CurveIntersections? intersections = ReferenceEquals(curveA, curveB) ? RhinoIntersect.CurveSelf(curveA, tolerance) : RhinoIntersect.CurveCurve(curveA, curveB, tolerance, tolerance);
                return ResultFactory.Create(value: IntersectionProcessor(intersections, curveA));
            },
            [(typeof(Curve), typeof(BrepFace))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.CurveBrepFace((Curve)first, (BrepFace)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Curve), typeof(Surface))] = static (first, second, tolerance, _, _) => { using CurveIntersections? i = RhinoIntersect.CurveSurface((Curve)first, (Surface)second, tolerance, overlapTolerance: tolerance); return ResultFactory.Create(value: IntersectionProcessor(i, (Curve)first)); },
            [(typeof(Curve), typeof(Plane))] = static (first, second, tolerance, _, _) => { using CurveIntersections? i = RhinoIntersect.CurvePlane((Curve)first, (Plane)second, tolerance); return ResultFactory.Create(value: IntersectionProcessor(i, (Curve)first)); },
            [(typeof(Curve), typeof(Line))] = static (first, second, tolerance, _, _) => { using CurveIntersections? i = RhinoIntersect.CurveLine((Curve)first, (Line)second, tolerance, overlapTolerance: tolerance); return ResultFactory.Create(value: IntersectionProcessor(i, (Curve)first)); },
            [(typeof(Curve), typeof(Brep))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.CurveBrep((Curve)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Brep), typeof(Brep))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.BrepBrep((Brep)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Brep), typeof(Plane))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.BrepPlane((Brep)first, (Plane)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Brep), typeof(Surface))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.BrepSurface((Brep)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Surface), typeof(Surface))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: ArrayResultBuilder((RhinoIntersect.SurfaceSurface((Surface)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            [(typeof(Mesh), typeof(Mesh))] = static (first, second, tolerance, _, _) => PolylineProcessor(RhinoIntersect.MeshMeshAccurate((Mesh)first, (Mesh)second, tolerance)),
            [(typeof(Mesh), typeof(Ray3d))] = static (first, second, _, _, _) => RhinoIntersect.MeshRay((Mesh)first, (Ray3d)second) is double d && d >= 0d ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Ray3d)second).PointAt(d),], [], [d,], [], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
            [(typeof(Mesh), typeof(Plane))] = static (first, second, _, _, _) => PolylineProcessor(RhinoIntersect.MeshPlane((Mesh)first, (Plane)second)),
            [(typeof(Mesh), typeof(Line))] = static (first, second, _, mode, _) => mode is Intersection.SortedMode ? (RhinoIntersect.MeshLineSorted((Mesh)first, (Line)second, out int[] ids) is Point3d[] pts && pts.Length > 0 ? ResultFactory.Create(value: new Intersection.IntersectionResult(pts, [], [], [], ids, [])) : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed)) : ResultFactory.Create(value: RhinoIntersect.MeshLine((Mesh)first, (Line)second) is Point3d[] p ? new Intersection.IntersectionResult(p, [], [], [], [], []) : Intersection.IntersectionResult.Empty),
            [(typeof(Mesh), typeof(PolylineCurve))] = static (first, second, _, mode, _) => mode is Intersection.SortedMode ? (RhinoIntersect.MeshPolylineSorted((Mesh)first, (PolylineCurve)second, out int[] ids) is Point3d[] pts && pts.Length > 0 ? ResultFactory.Create(value: new Intersection.IntersectionResult(pts, [], [], [], ids, [])) : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed)) : ResultFactory.Create(value: RhinoIntersect.MeshPolyline((Mesh)first, (PolylineCurve)second, out int[] idx) is Point3d[] p ? new Intersection.IntersectionResult(p, [], [], [], idx, []) : Intersection.IntersectionResult.Empty),
            [(typeof(Line), typeof(Line))] = static (first, second, tolerance, _, _) => RhinoIntersect.LineLine((Line)first, (Line)second, out double pa, out double pb, tolerance, finiteSegments: false) ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(pa),], [], [pa,], [pb,], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
            [(typeof(Line), typeof(BoundingBox))] = static (first, second, tolerance, _, _) => RhinoIntersect.LineBox((Line)first, (BoundingBox)second, tolerance, out Interval interval) ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(interval.Min), ((Line)first).PointAt(interval.Max),], [], [interval.Min, interval.Max,], [], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
            [(typeof(Line), typeof(Plane))] = static (first, second, _, _, _) => RhinoIntersect.LinePlane((Line)first, (Plane)second, out double p) ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(p),], [], [p,], [], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
            [(typeof(Line), typeof(Sphere))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: TwoPointHandler((int)RhinoIntersect.LineSphere((Line)first, (Sphere)second, out Point3d pa, out Point3d pb), pa, pb, tolerance, null)),
            [(typeof(Line), typeof(Cylinder))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: TwoPointHandler((int)RhinoIntersect.LineCylinder((Line)first, (Cylinder)second, out Point3d pa, out Point3d pb), pa, pb, tolerance, null)),
            [(typeof(Line), typeof(Circle))] = static (first, second, tolerance, _, _) => { int c = (int)RhinoIntersect.LineCircle((Line)first, (Circle)second, out double ta, out Point3d pa, out double tb, out Point3d pb); return ResultFactory.Create(value: TwoPointHandler(c, pa, pb, tolerance, c > 1 ? [ta, tb,] : c > 0 ? [ta,] : null)); },
            [(typeof(Plane), typeof(Plane))] = static (first, second, _, _, _) => RhinoIntersect.PlanePlane((Plane)first, (Plane)second, out Line line) ? ResultFactory.Create(value: new Intersection.IntersectionResult([], [new LineCurve(line),], [], [], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = static (first, second, _, _, _) => { (Plane a, Plane b) = (ValueTuple<Plane, Plane>)first; return RhinoIntersect.PlanePlanePlane(a, b, (Plane)second, out Point3d pt) ? ResultFactory.Create(value: new Intersection.IntersectionResult([pt,], [], [], [], [], [])) : ResultFactory.Create(value: Intersection.IntersectionResult.Empty); },
            [(typeof(Plane), typeof(Circle))] = static (first, second, _, _, _) => RhinoIntersect.PlaneCircle((Plane)first, (Circle)second, out double pa, out double pb) switch { PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersection.IntersectionResult([((Circle)second).PointAt(pa),], [], [], [pa,], [], [])), PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersection.IntersectionResult([((Circle)second).PointAt(pa), ((Circle)second).PointAt(pb),], [], [], [pa, pb,], [], [])), _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty), },
            [(typeof(Plane), typeof(Sphere))] = static (first, second, _, _, _) => ResultFactory.Create(value: CircleHandler((int)RhinoIntersect.PlaneSphere((Plane)first, (Sphere)second, out Circle c), c)),
            [(typeof(Plane), typeof(BoundingBox))] = static (first, second, _, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)first, (BoundingBox)second, out Polyline pl), pl) switch { (true, { Count: > 0 }) => ResultFactory.Create(value: new Intersection.IntersectionResult([.. pl,], [], [], [], [], [pl,])), _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty), },
            [(typeof(Sphere), typeof(Sphere))] = static (first, second, _, _, _) => ResultFactory.Create(value: CircleHandler((int)RhinoIntersect.SphereSphere((Sphere)first, (Sphere)second, out Circle c), c)),
            [(typeof(Circle), typeof(Circle))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: TwoPointHandler((int)RhinoIntersect.CircleCircle((Circle)first, (Circle)second, out Point3d pa, out Point3d pb), pa, pb, tolerance, null)),
            [(typeof(Arc), typeof(Arc))] = static (first, second, tolerance, _, _) => ResultFactory.Create(value: TwoPointHandler((int)RhinoIntersect.ArcArc((Arc)first, (Arc)second, out Point3d pa, out Point3d pb), pa, pb, tolerance, null)),
            [(typeof(Point3d[]), typeof(Brep[]))] = static (first, second, tolerance, mode, context) => mode is Intersection.ProjectionMode p && p.Direction.IsValid && p.Direction.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create<IEnumerable<Brep>>(value: (Brep[])second).TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, V.Standard | V.Topology,])).Map<Brep[]>(static v => [.. v,]).Bind(b => p.WithIndices ? ResultFactory.Create(value: new Intersection.IntersectionResult(RhinoIntersect.ProjectPointsToBrepsEx(b, (Point3d[])first, p.Direction, tolerance, out int[] idx), [], [], [], idx, [])) : ResultFactory.Create(value: new Intersection.IntersectionResult(RhinoIntersect.ProjectPointsToBreps(b, (Point3d[])first, p.Direction, tolerance), [], [], [], [], []))) : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidProjection.WithContext("ProjectionMode required")),
            [(typeof(Point3d[]), typeof(Mesh[]))] = static (first, second, tolerance, mode, context) => mode is Intersection.ProjectionMode p && p.Direction.IsValid && p.Direction.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create<IEnumerable<Mesh>>(value: (Mesh[])second).TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, V.MeshSpecific,])).Map<Mesh[]>(static v => [.. v,]).Bind(m => p.WithIndices ? ResultFactory.Create(value: new Intersection.IntersectionResult(RhinoIntersect.ProjectPointsToMeshesEx(m, (Point3d[])first, p.Direction, tolerance, out int[] idx), [], [], [], idx, [])) : ResultFactory.Create(value: new Intersection.IntersectionResult(RhinoIntersect.ProjectPointsToMeshes(m, (Point3d[])first, p.Direction, tolerance), [], [], [], [], []))) : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidProjection.WithContext("ProjectionMode required")),
            [(typeof(Ray3d), typeof(GeometryBase[]))] = static (first, second, _, mode, context) => mode is Intersection.ProjectionMode p && p.MaxHits > 0 ? ResultFactory.Create<IEnumerable<GeometryBase>>(value: (GeometryBase[])second).TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, V.None,])).Map<GeometryBase[]>(static v => [.. v,]).Map(g => new Intersection.IntersectionResult(RhinoIntersect.RayShoot((Ray3d)first, g, p.MaxHits), [], [], [], [], [])) : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidMaxHits.WithContext(mode is Intersection.ProjectionMode proj ? proj.MaxHits.ToString(CultureInfo.InvariantCulture) : "ProjectionMode required")),
        }.ToFrozenDictionary();

    [Pure]
    private static Result<(Func<object, object, double, Intersection.IntersectionMode, IGeometryContext, Result<Intersection.IntersectionResult>> Executor, IntersectionConfig.IntersectionOperationMetadata Metadata, bool Swapped)> ResolveExecutor(Type typeA, Type typeB) {
        Type[] getTypeChain(Type type) => [.. (type.BaseType is not null ? Enumerable.Repeat(type, 1).Concat(getTypeChain(type.BaseType)) : Enumerable.Repeat(type, 1)).Concat(type.GetInterfaces()).Distinct(),];
        (Type[] chainA, Type[] chainB) = (getTypeChain(typeA), getTypeChain(typeB));
        return chainA.SelectMany(a => chainB.Select(b => ((a, b), false))).Concat(chainB.SelectMany(a => chainA.Select(b => ((a, b), true))))
            .Select(c => (_executors.TryGetValue(c.Item1, out var e), IntersectionConfig.Operations.TryGetValue(c.Item1, out var m), c.Item1, c.Item2, e, m))
            .FirstOrDefault(x => x.Item1 && x.Item2) switch {
                (true, true, _, bool swapped, var executor, var metadata) => ResultFactory.Create(value: (executor!, metadata!, swapped)),
                _ => ResultFactory.Create<(Func<object, object, double, Intersection.IntersectionMode, IGeometryContext, Result<Intersection.IntersectionResult>>, IntersectionConfig.IntersectionOperationMetadata, bool)>(error: E.Geometry.UnsupportedIntersection.WithContext($"{typeA.Name} × {typeB.Name}")),
            };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.IntersectionResult> Execute<T1, T2>(T1 geometryA, T2 geometryB, IGeometryContext context, Intersection.IntersectionMode mode) where T1 : notnull where T2 : notnull {
        double tolerance = mode switch { Intersection.StandardMode s => s.Tolerance ?? context.AbsoluteTolerance, Intersection.SortedMode s => s.Tolerance ?? context.AbsoluteTolerance, Intersection.ProjectionMode p => p.Tolerance ?? context.AbsoluteTolerance, _ => context.AbsoluteTolerance, };
        return ResolveExecutor(typeof(T1), typeof(T2)).Bind(entry => {
            (V modeA, V modeB) = entry.Swapped ? (entry.Metadata.ModeB, entry.Metadata.ModeA) : (entry.Metadata.ModeA, entry.Metadata.ModeB);
            return UnifiedOperation.Apply(
                input: geometryA,
                operation: (Func<T1, Result<IReadOnlyList<Intersection.IntersectionResult>>>)(item => (modeA == V.None ? ResultFactory.Create(value: (object)item!) : ResultFactory.Create(value: (object)item!).Validate(args: [context, modeA,])).Bind(vA => (modeB == V.None ? ResultFactory.Create(value: (object)geometryB!) : ResultFactory.Create(value: (object)geometryB!).Validate(args: [context, modeB,])).Bind(vB => (entry.Swapped ? entry.Executor(vB, vA, tolerance, mode, context) : entry.Executor(vA, vB, tolerance, mode, context)).Map(o => entry.Swapped ? new Intersection.IntersectionResult(o.Points, o.Curves, o.ParametersB, o.ParametersA, o.FaceIndices, o.Sections) : o).Map(r => (IReadOnlyList<Intersection.IntersectionResult>)[r,])))),
                config: new OperationConfig<T1, Intersection.IntersectionResult> { Context = context, ValidationMode = V.None, OperationName = entry.Metadata.OperationName, AccumulateErrors = true, EnableDiagnostics = false, })
            .Map(static results => results.Count == 0 ? Intersection.IntersectionResult.Empty : new Intersection.IntersectionResult([.. results.SelectMany(static r => r.Points),], [.. results.SelectMany(static r => r.Curves),], [.. results.SelectMany(static r => r.ParametersA),], [.. results.SelectMany(static r => r.ParametersB),], [.. results.SelectMany(static r => r.FaceIndices),], [.. results.SelectMany(static r => r.Sections),]));
        });
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.ClassificationResult> Classify(Intersection.IntersectionResult result, GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context) =>
        IntersectionCompute.Classify(result: result, geomA: geometryA, geomB: geometryB, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.NearMissResult> FindNearMisses(GeometryBase geometryA, GeometryBase geometryB, double searchRadius, IGeometryContext context) =>
        IntersectionCompute.FindNearMisses(geomA: geometryA, geomB: geometryB, searchRadius: searchRadius, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.StabilityResult> AnalyzeStability(Intersection.IntersectionResult baseResult, GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context) =>
        IntersectionCompute.AnalyzeStability(geomA: geometryA, geomB: geometryB, baseResult: baseResult, context: context);
}

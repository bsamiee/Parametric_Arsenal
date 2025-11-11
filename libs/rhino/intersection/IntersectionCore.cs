using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using RhinoIntersect = Rhino.Geometry.Intersect.Intersection;

namespace Arsenal.Rhino.Intersection;

/// <summary>RhinoCommon intersection dispatch with FrozenDictionary resolution.</summary>
internal static class IntersectionCore {
    /// <summary>Intersection strategy metadata.</summary>
    internal readonly record struct IntersectionStrategy(
        Func<object, object, double, Intersect.IntersectionOptions, IGeometryContext, Result<Intersect.IntersectionOutput>> Executor,
        V ModeA,
        V ModeB);

    /// <summary>Builds intersection result from bool/arrays tuple using pattern matching discrimination.</summary>
    private static readonly Func<(bool, Curve[]?, Point3d[]?), Result<Intersect.IntersectionOutput>> ArrayResultBuilder = tuple => tuple switch {
        (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, curves, [], [], [], [])),
        (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], curves, [], [], [], [])),
        (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Processes CurveIntersections into output with points, overlap curves, and parameters.</summary>
    private static readonly Func<CurveIntersections?, Curve, Result<Intersect.IntersectionOutput>> IntersectionProcessor = (results, source) => results switch { { Count: > 0 }
        => ResultFactory.Create(value: new Intersect.IntersectionOutput(
        [.. from entry in results select entry.PointA],
        [.. from entry in results where entry.IsOverlap let trimmed = source.Trim(entry.OverlapA) where trimmed is not null select trimmed],
        [.. from entry in results select entry.ParameterA],
        [.. from entry in results select entry.ParameterB],
        [], [])),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Handles two-point intersection results with distance threshold validation and deduplication.</summary>
    private static readonly Func<int, Point3d, Point3d, double, double[]?, Result<Intersect.IntersectionOutput>> TwoPointHandler = (count, first, second, tolerance, parameters) =>
        count switch {
            > 1 when first.DistanceTo(second) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([first, second], [], parameters ?? [], [], [], [])),
            > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([first], [], parameters is { Length: > 0 } ? [parameters[0]] : [], [], [], [])),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

    /// <summary>Handles circle intersection results discriminating between arc curves and tangent points.</summary>
    private static readonly Func<int, Circle, Result<Intersect.IntersectionOutput>> CircleHandler = (type, circle) => (type, circle) switch {
        (1, Circle arc) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(arc)], [], [], [], [])),
        (2, Circle tangent) => ResultFactory.Create(value: new Intersect.IntersectionOutput([tangent.Center], [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Processes polyline arrays flattening points while preserving original polyline structures.</summary>
    private static readonly Func<Polyline[]?, Result<Intersect.IntersectionOutput>> PolylineProcessor = polylines => polylines switch { { Length: > 0 }
        => ResultFactory.Create(value: new Intersect.IntersectionOutput(
        [.. from polyline in polylines from point in polyline select point],
        [], [], [], [], [.. polylines])),
        null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Handles mesh intersections dispatching between sorted and unsorted RhinoCommon methods.</summary>
    private static readonly Func<Mesh, object, bool, (Func<Point3d[]?, int[]?, Result<Intersect.IntersectionOutput>>, Func<Point3d[]?, Result<Intersect.IntersectionOutput>>), Result<Intersect.IntersectionOutput>> MeshIntersectionHandler =
        (mesh, target, sorted, handlers) => sorted switch {
            true => target switch {
                Line line => handlers.Item1(RhinoIntersect.MeshLineSorted(mesh, line, out int[] ids), ids),
                PolylineCurve polyline => handlers.Item1(RhinoIntersect.MeshPolylineSorted(mesh, polyline, out int[] ids), ids),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
            false => target switch {
                Line line => handlers.Item2(RhinoIntersect.MeshLine(mesh, line)),
                PolylineCurve polyline when RhinoIntersect.MeshPolyline(mesh, polyline, out int[] ids) is Point3d[] points => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, [], [], [], ids, [])),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
        };

    /// <summary>Handles point projection to Breps or Meshes with direction validation and optional indices.</summary>
    private static readonly Func<Point3d[], object, Vector3d?, bool, double, IGeometryContext, V, Result<Intersect.IntersectionOutput>> ProjectionHandler = (points, targets, direction, withIndices, tolerance, context, validationMode) =>
        direction switch {
            Vector3d dir when dir.IsValid && dir.Length > RhinoMath.ZeroTolerance => (targets, withIndices) switch {
                (Brep[] breps, bool includeIndices) => ResultFactory.Create<IEnumerable<Brep>>(value: breps)
                    .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, validationMode,]))
                    .Map<Brep[]>(valid => [.. valid])
                    .Bind(validBreps => includeIndices
                        ? ResultFactory.Create(value: new Intersect.IntersectionOutput(
                            RhinoIntersect.ProjectPointsToBrepsEx(validBreps, points, dir, tolerance, out int[] indices), [], [], [], indices, []))
                        : ResultFactory.Create(value: new Intersect.IntersectionOutput(
                            RhinoIntersect.ProjectPointsToBreps(validBreps, points, dir, tolerance), [], [], [], [], []))),
                (Mesh[] meshes, bool includeIndices) => ResultFactory.Create<IEnumerable<Mesh>>(value: meshes)
                    .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, validationMode,]))
                    .Map<Mesh[]>(valid => [.. valid])
                    .Bind(validMeshes => includeIndices
                        ? ResultFactory.Create(value: new Intersect.IntersectionOutput(
                            RhinoIntersect.ProjectPointsToMeshesEx(validMeshes, points, dir, tolerance, out int[] indices), [], [], [], indices, []))
                        : ResultFactory.Create(value: new Intersect.IntersectionOutput(
                            RhinoIntersect.ProjectPointsToMeshes(validMeshes, points, dir, tolerance), [], [], [], [], []))),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection.WithContext(targets.GetType().Name)),
            },
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection.WithContext("null")),
        };

    /// <summary>FrozenDictionary mapping type pairs to intersection strategies with validation modes.</summary>
    private static readonly FrozenDictionary<(Type, Type), IntersectionStrategy> _strategies =
        new ((Type, Type) Key, Func<object, object, double, Intersect.IntersectionOptions, IGeometryContext, Result<Intersect.IntersectionOutput>> Executor)[] {
            ((typeof(Curve), typeof(Curve)), (first, second, tolerance, _, _) => {
                Curve curveA = (Curve)first;
                Curve curveB = (Curve)second;
                using CurveIntersections? intersections = ReferenceEquals(curveA, curveB)
                    ? RhinoIntersect.CurveSelf(curveA, tolerance)
                    : RhinoIntersect.CurveCurve(curveA, curveB, tolerance, tolerance);
                return IntersectionProcessor(intersections, curveA);
            }),
            ((typeof(Curve), typeof(BrepFace)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.CurveBrepFace((Curve)first, (BrepFace)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Curve), typeof(Surface)), (first, second, tolerance, _, _) => {
                using CurveIntersections? intersections = RhinoIntersect.CurveSurface((Curve)first, (Surface)second, tolerance, overlapTolerance: tolerance);
                return IntersectionProcessor(intersections, (Curve)first);
            }),
            ((typeof(Curve), typeof(Plane)), (first, second, tolerance, _, _) => {
                using CurveIntersections? intersections = RhinoIntersect.CurvePlane((Curve)first, (Plane)second, tolerance);
                return IntersectionProcessor(intersections, (Curve)first);
            }),
            ((typeof(Curve), typeof(Line)), (first, second, tolerance, _, _) => {
                using CurveIntersections? intersections = RhinoIntersect.CurveLine((Curve)first, (Line)second, tolerance, overlapTolerance: tolerance);
                return IntersectionProcessor(intersections, (Curve)first);
            }),
            ((typeof(Curve), typeof(Brep)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.CurveBrep((Curve)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Brep), typeof(Brep)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepBrep((Brep)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Brep), typeof(Plane)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepPlane((Brep)first, (Plane)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Brep), typeof(Surface)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepSurface((Brep)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Surface), typeof(Surface)), (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.SurfaceSurface((Surface)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points))),
            ((typeof(Mesh), typeof(Mesh)), (first, second, tolerance, options, _) => options.Sorted switch {
                true => PolylineProcessor(RhinoIntersect.MeshMeshAccurate((Mesh)first, (Mesh)second, tolerance)),
                false => RhinoIntersect.MeshMeshFast((Mesh)first, (Mesh)second) switch {
                    Line[] { Length: > 0 } segments => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from line in segments select line.From, .. from line in segments select line.To],
                        [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            }),
            ((typeof(Mesh), typeof(Ray3d)), (first, second, _, _, _) => RhinoIntersect.MeshRay((Mesh)first, (Ray3d)second) switch {
                double distance when distance >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Ray3d)second).PointAt(distance)], [], [distance], [], [], [])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            }),
            ((typeof(Mesh), typeof(Plane)), (first, second, _, _, _) => PolylineProcessor(RhinoIntersect.MeshPlane((Mesh)first, (Plane)second))),
            ((typeof(Mesh), typeof(Line)), (first, second, _, options, _) => MeshIntersectionHandler((Mesh)first, second, options.Sorted,
                ((points, indices) => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, [], [], [], indices ?? [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
                points => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                }))),
            ((typeof(Mesh), typeof(PolylineCurve)), (first, second, _, options, _) => MeshIntersectionHandler((Mesh)first, second, options.Sorted,
                ((points, indices) => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(points, [], [], [], indices ?? [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
                points => ResultFactory.Create(value: new Intersect.IntersectionOutput(points ?? [], [], [], [], [], []))))),
            ((typeof(Line), typeof(Line)), (first, second, tolerance, _, _) => RhinoIntersect.LineLine((Line)first, (Line)second, out double parameterA, out double parameterB, tolerance, finiteSegments: false)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)first).PointAt(parameterA)], [], [parameterA], [parameterB], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty)),
            ((typeof(Line), typeof(BoundingBox)), (first, second, tolerance, _, _) => RhinoIntersect.LineBox((Line)first, (BoundingBox)second, tolerance, out Interval interval)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)first).PointAt(interval.Min), ((Line)first).PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty)),
            ((typeof(Line), typeof(Plane)), (first, second, _, _, _) => RhinoIntersect.LinePlane((Line)first, (Plane)second, out double parameter)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)first).PointAt(parameter)], [], [parameter], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty)),
            ((typeof(Line), typeof(Sphere)), (first, second, tolerance, _, _) => {
                int count = (int)RhinoIntersect.LineSphere((Line)first, (Sphere)second, out Point3d pointA, out Point3d pointB);
                return TwoPointHandler(count, pointA, pointB, tolerance, null);
            }),
            ((typeof(Line), typeof(Cylinder)), (first, second, tolerance, _, _) => {
                int count = (int)RhinoIntersect.LineCylinder((Line)first, (Cylinder)second, out Point3d pointA, out Point3d pointB);
                return TwoPointHandler(count, pointA, pointB, tolerance, null);
            }),
            ((typeof(Line), typeof(Circle)), (first, second, tolerance, _, _) => {
                int count = (int)RhinoIntersect.LineCircle((Line)first, (Circle)second, out double parameterA, out Point3d pointA, out double parameterB, out Point3d pointB);
                return TwoPointHandler(count, pointA, pointB, tolerance, count > 1 ? [parameterA, parameterB] : count > 0 ? [parameterA] : null);
            }),
            ((typeof(Plane), typeof(Plane)), (first, second, _, _, _) => RhinoIntersect.PlanePlane((Plane)first, (Plane)second, out Line line)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty)),
            ((typeof(ValueTuple<Plane, Plane>), typeof(Plane)), (first, second, _, _, _) => {
                (Plane planeA, Plane planeB) = (ValueTuple<Plane, Plane>)first;
                return RhinoIntersect.PlanePlanePlane(planeA, planeB, (Plane)second, out Point3d point)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);
            }),
            ((typeof(Plane), typeof(Circle)), (first, second, _, _, _) => RhinoIntersect.PlaneCircle((Plane)first, (Circle)second, out double parameterA, out double parameterB) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)second).PointAt(parameterA)], [], [], [parameterA], [], [])),
                PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)second).PointAt(parameterA), ((Circle)second).PointAt(parameterB)], [], [], [parameterA, parameterB], [], [])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            }),
            ((typeof(Plane), typeof(Sphere)), (first, second, _, _, _) => CircleHandler((int)RhinoIntersect.PlaneSphere((Plane)first, (Sphere)second, out Circle circle), circle)),
            ((typeof(Plane), typeof(BoundingBox)), (first, second, _, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)first, (BoundingBox)second, out Polyline polyline), polyline) switch {
                (true, Polyline { Count: > 0 } pl) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from point in pl select point], [], [], [], [], [pl])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            }),
            ((typeof(Sphere), typeof(Sphere)), (first, second, _, _, _) => CircleHandler((int)RhinoIntersect.SphereSphere((Sphere)first, (Sphere)second, out Circle circle), circle)),
            ((typeof(Circle), typeof(Circle)), (first, second, tolerance, _, _) => {
                int count = (int)RhinoIntersect.CircleCircle((Circle)first, (Circle)second, out Point3d pointA, out Point3d pointB);
                return TwoPointHandler(count, pointA, pointB, tolerance, null);
            }),
            ((typeof(Arc), typeof(Arc)), (first, second, tolerance, _, _) => {
                int count = (int)RhinoIntersect.ArcArc((Arc)first, (Arc)second, out Point3d pointA, out Point3d pointB);
                return TwoPointHandler(count, pointA, pointB, tolerance, null);
            }),
            ((typeof(Point3d[]), typeof(Brep[])), (first, second, tolerance, options, context) => ProjectionHandler((Point3d[])first, second, options.ProjectionDirection, options.WithIndices, tolerance, context, V.Standard | V.Topology)),
            ((typeof(Point3d[]), typeof(Mesh[])), (first, second, tolerance, options, context) => ProjectionHandler((Point3d[])first, second, options.ProjectionDirection, options.WithIndices, tolerance, context, V.MeshSpecific)),
            ((typeof(Ray3d), typeof(GeometryBase[])), (first, second, _, options, context) => options.MaxHits switch {
                int hits when hits > 0 => ResultFactory.Create<IEnumerable<GeometryBase>>(value: (GeometryBase[])second)
                    .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, V.None,]))
                    .Map<GeometryBase[]>(valid => [.. valid])
                    .Map(validated => new Intersect.IntersectionOutput(RhinoIntersect.RayShoot((Ray3d)first, validated, hits), [], [], [], [], [])),
                int hits => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits.WithContext(hits.ToString(CultureInfo.InvariantCulture))),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
            }),
        }.ToFrozenDictionary(entry => entry.Key, entry => {
            (V ModeA, V ModeB) = IntersectionConfig.ValidationModes.TryGetValue(entry.Key, out (V ModeA, V ModeB) found)
                ? found
                : (V.None, V.None);
            return new IntersectionStrategy(entry.Executor, ModeA, ModeB);
        });

    /// <summary>Resolves intersection strategy for type pair using inheritance chain and interface traversal.</summary>
    [Pure]
    internal static Result<(IntersectionStrategy Strategy, bool Swapped)> ResolveStrategy(Type typeA, Type typeB) {
        List<Type> chainAList = [];
        for (Type? current = typeA; current is not null; current = current.BaseType) {
            chainAList.Add(current);
        }

        List<Type> chainBList = [];
        for (Type? current = typeB; current is not null; current = current.BaseType) {
            chainBList.Add(current);
        }

        Type[] chainA = [.. chainAList.Concat(typeA.GetInterfaces()).Distinct()];
        Type[] chainB = [.. chainBList.Concat(typeB.GetInterfaces()).Distinct()];

        return chainA.SelectMany(first => chainB.Select(second => ((first, second), false)))
            .Concat(chainB.SelectMany(first => chainA.Select(second => ((first, second), true))))
            .Select(candidate => {
                bool match = _strategies.TryGetValue(candidate.Item1, out IntersectionStrategy resolved);
                return (candidate.Item1, candidate.Item2, match, resolved);
            })
            .FirstOrDefault(match => match.match) switch {
                ((Type, Type) key, bool swapped, true, IntersectionStrategy strategy) => ResultFactory.Create(value: (strategy, swapped)),
                _ => ResultFactory.Create<(IntersectionStrategy, bool)>(error: E.Geometry.UnsupportedIntersection.WithContext($"{typeA.Name} × {typeB.Name}")),
            };
    }

    /// <summary>Normalizes intersection options validating tolerance and MaxHits with context defaults.</summary>
    [Pure]
    internal static Result<(double Tolerance, Intersect.IntersectionOptions Options)> NormalizeOptions(Intersect.IntersectionOptions options, IGeometryContext context) =>
        ResultFactory.Create(value: options)
            .Ensure(opt => !opt.Tolerance.HasValue || (RhinoMath.IsValidDouble(opt.Tolerance.Value) && opt.Tolerance.Value > RhinoMath.ZeroTolerance), E.Validation.ToleranceAbsoluteInvalid)
            .Ensure(opt => !opt.MaxHits.HasValue || opt.MaxHits.Value > 0, E.Geometry.InvalidMaxHits)
            .Map(opt => {
                double tolerance = opt.Tolerance ?? context.AbsoluteTolerance;
                return (tolerance, new Intersect.IntersectionOptions(tolerance, opt.ProjectionDirection, opt.MaxHits, opt.WithIndices, opt.Sorted));
            });

    /// <summary>Executes intersection with normalized options resolving strategy and validating inputs.</summary>
    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecuteWithOptions(object geometryA, object geometryB, IGeometryContext context, (double Tolerance, Intersect.IntersectionOptions Options) normalized) =>
        ResolveStrategy(geometryA.GetType(), geometryB.GetType())
            .Bind(entry => {
                (V modeA, V modeB) = entry.Swapped
                    ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                    : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                return (modeA == V.None
                        ? ResultFactory.Create(value: geometryA)
                        : ResultFactory.Create(value: geometryA).Validate(args: [context, modeA,]))
                    .Bind(validA => (modeB == V.None
                            ? ResultFactory.Create(value: geometryB)
                            : ResultFactory.Create(value: geometryB).Validate(args: [context, modeB,]))
                        .Bind(validB => (entry.Swapped
                            ? entry.Strategy.Executor(validB, validA, normalized.Tolerance, normalized.Options, context)
                            : entry.Strategy.Executor(validA, validB, normalized.Tolerance, normalized.Options, context))
                            .Map(output => entry.Swapped
                                ? new Intersect.IntersectionOutput(output.Points, output.Curves, output.ParametersB, output.ParametersA, output.FaceIndices, output.Sections)
                                : output)));
            });

    /// <summary>Executes intersection for typed geometry pair normalizing options before execution.</summary>
    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 geometryA, T2 geometryB, IGeometryContext context, Intersect.IntersectionOptions options) where T1 : notnull where T2 : notnull =>
        NormalizeOptions(options, context)
            .Bind(normalized => ExecuteWithOptions(geometryA, geometryB, context, normalized));
}

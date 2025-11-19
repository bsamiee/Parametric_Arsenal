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

/// <summary>Orchestration layer for intersection operations via UnifiedOperation.</summary>
[Pure]
internal static class IntersectionCore {
    /// <summary>Intersection strategy metadata.</summary>
    internal readonly record struct IntersectionStrategy(
        Func<object, object, double, Intersection.IntersectionSettings, IGeometryContext, Result<Intersection.IntersectionOutput>> Executor,
        V ModeA,
        V ModeB);

    /// <summary>Execute intersection request with algebraic dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.IntersectionOutput> ExecuteRequest(Intersection.Request request, IGeometryContext context) =>
        request switch {
            Intersection.General general => ExecuteGeneral(
                geometryA: general.GeometryA,
                geometryB: general.GeometryB,
                settings: general.Settings ?? new Intersection.IntersectionSettings(),
                context: context),
            Intersection.PointProjection projection => ExecutePointProjection(
                points: projection.Points,
                targets: projection.Targets,
                direction: projection.Direction,
                withIndices: projection.WithIndices,
                context: context),
            Intersection.RayShoot rayShoot => ExecuteRayShoot(
                ray: rayShoot.Ray,
                targets: rayShoot.Targets,
                maxHits: rayShoot.MaxHits,
                context: context),
            _ => ResultFactory.Create<Intersection.IntersectionOutput>(
                error: E.Geometry.UnsupportedIntersection.WithContext($"Unknown request type: {request.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Intersection.IntersectionOutput> ExecuteGeneral(
        object geometryA,
        object geometryB,
        Intersection.IntersectionSettings settings,
        IGeometryContext context) =>
        ResolveStrategy(geometryA.GetType(), geometryB.GetType())
            .Bind(entry => NormalizeSettings(settings: settings, context: context)
                .Bind(normalized => ExecuteWithSettings(
                    geometryA: geometryA,
                    geometryB: geometryB,
                    context: context,
                    normalized: normalized,
                    strategy: entry.Strategy,
                    swapped: entry.Swapped)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Intersection.IntersectionOutput> ExecutePointProjection(
        Point3d[] points,
        object targets,
        Vector3d? direction,
        bool withIndices,
        IGeometryContext context) =>
        direction switch {
            Vector3d dir when dir.IsValid && dir.Length > RhinoMath.ZeroTolerance => targets switch {
                Brep[] breps => IntersectionConfig.PairOperations.TryGetValue((typeof(Point3d[]), typeof(Brep[])), out IntersectionConfig.IntersectionPairMetadata? meta)
                    ? UnifiedOperation.Apply(
                        input: breps,
                        operation: (Func<Brep[], Result<IReadOnlyList<Intersection.IntersectionOutput>>>)(items =>
                            withIndices
                                ? ResultFactory.Create(value: (IReadOnlyList<Intersection.IntersectionOutput>)[
                                    new Intersection.IntersectionOutput(
                                        RhinoIntersect.ProjectPointsToBrepsEx(items, points, dir, context.AbsoluteTolerance, out int[] indices),
                                        [], [], [], indices, []),
                                ])
                                : ResultFactory.Create(value: (IReadOnlyList<Intersection.IntersectionOutput>)[
                                    new Intersection.IntersectionOutput(
                                        RhinoIntersect.ProjectPointsToBreps(items, points, dir, context.AbsoluteTolerance),
                                        [], [], [], [], []),
                                ])),
                        config: new OperationConfig<Brep[], Intersection.IntersectionOutput> {
                            Context = context,
                            ValidationMode = meta.ValidationModeB,
                            OperationName = meta.OperationName,
                        }).Map(static r => r[0])
                    : ResultFactory.Create<Intersection.IntersectionOutput>(
                        error: E.Geometry.InvalidProjection.WithContext("Missing metadata for Point3d[]/Brep[] projection")),
                Mesh[] meshes => IntersectionConfig.PairOperations.TryGetValue((typeof(Point3d[]), typeof(Mesh[])), out IntersectionConfig.IntersectionPairMetadata? meta)
                    ? UnifiedOperation.Apply(
                        input: meshes,
                        operation: (Func<Mesh[], Result<IReadOnlyList<Intersection.IntersectionOutput>>>)(items =>
                            withIndices
                                ? ResultFactory.Create(value: (IReadOnlyList<Intersection.IntersectionOutput>)[
                                    new Intersection.IntersectionOutput(
                                        RhinoIntersect.ProjectPointsToMeshesEx(items, points, dir, context.AbsoluteTolerance, out int[] indices),
                                        [], [], [], indices, []),
                                ])
                                : ResultFactory.Create(value: (IReadOnlyList<Intersection.IntersectionOutput>)[
                                    new Intersection.IntersectionOutput(
                                        RhinoIntersect.ProjectPointsToMeshes(items, points, dir, context.AbsoluteTolerance),
                                        [], [], [], [], []),
                                ])),
                        config: new OperationConfig<Mesh[], Intersection.IntersectionOutput> {
                            Context = context,
                            ValidationMode = meta.ValidationModeB,
                            OperationName = meta.OperationName,
                        }).Map(static r => r[0])
                    : ResultFactory.Create<Intersection.IntersectionOutput>(
                        error: E.Geometry.InvalidProjection.WithContext("Missing metadata for Point3d[]/Mesh[] projection")),
                _ => ResultFactory.Create<Intersection.IntersectionOutput>(
                    error: E.Geometry.InvalidProjection.WithContext(targets.GetType().Name)),
            },
            _ => ResultFactory.Create<Intersection.IntersectionOutput>(
                error: E.Geometry.InvalidProjection.WithContext("null or invalid direction")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Intersection.IntersectionOutput> ExecuteRayShoot(
        Ray3d ray,
        GeometryBase[] targets,
        int maxHits,
        IGeometryContext context) =>
        maxHits > 0
            ? IntersectionConfig.PairOperations.TryGetValue((typeof(Ray3d), typeof(GeometryBase[])), out IntersectionConfig.IntersectionPairMetadata? meta)
                ? UnifiedOperation.Apply(
                    input: targets,
                    operation: (Func<GeometryBase[], Result<IReadOnlyList<Intersection.IntersectionOutput>>>)(items =>
                        ResultFactory.Create(value: (IReadOnlyList<Intersection.IntersectionOutput>)[
                            new Intersection.IntersectionOutput(
                                RhinoIntersect.RayShoot(ray, items, maxHits),
                                [], [], [], [], []),
                        ])),
                    config: new OperationConfig<GeometryBase[], Intersection.IntersectionOutput> {
                        Context = context,
                        ValidationMode = meta.ValidationModeB,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0])
                : ResultFactory.Create<Intersection.IntersectionOutput>(
                    error: E.Geometry.InvalidProjection.WithContext("Missing metadata for Ray3d/GeometryBase[] intersection"))
            : ResultFactory.Create<Intersection.IntersectionOutput>(
                error: E.Geometry.InvalidMaxHits.WithContext(maxHits.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Builds intersection result from bool/arrays tuple using pattern matching discrimination.</summary>
    private static readonly Func<(bool, Curve[]?, Point3d[]?), Result<Intersection.IntersectionOutput>> ArrayResultBuilder = tuple => tuple switch {
        (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, curves, [], [], [], [])),
        (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersection.IntersectionOutput([], curves, [], [], [], [])),
        (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, [], [], [], [], [])),
        (true, _, _) or (false, _, _) => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
    };

    /// <summary>Processes CurveIntersections into output with points, overlap curves, and parameters.</summary>
    private static readonly Func<CurveIntersections?, Curve, Result<Intersection.IntersectionOutput>> IntersectionProcessor = (results, source) => results switch { { Count: > 0 } =>
        ResultFactory.Create(value: new Intersection.IntersectionOutput(
            [.. results.Select(entry => entry.PointA)],
            [.. results.Where(entry => entry.IsOverlap).Select(entry => source.Trim(entry.OverlapA)).Where(trimmed => trimmed is not null)],
            [.. results.Select(entry => entry.ParameterA)],
            [.. results.Select(entry => entry.ParameterB)],
            [], [])),
        _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
    };

    /// <summary>Handles two-point intersection results with distance threshold validation and deduplication.</summary>
    private static readonly Func<int, Point3d, Point3d, double, double[]?, Result<Intersection.IntersectionOutput>> TwoPointHandler = (count, first, second, tolerance, parameters) =>
        (count, first.DistanceTo(second) > tolerance) switch {
            ( > 1, true) => ResultFactory.Create(value: new Intersection.IntersectionOutput([first, second], [], parameters ?? [], [], [], [])),
            ( > 0, _) => ResultFactory.Create(value: new Intersection.IntersectionOutput([first], [], parameters is { Length: > 0 } ? [parameters[0]] : [], [], [], [])),
            _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
        };

    /// <summary>Handles circle intersection results discriminating between arc curves and tangent points.</summary>
    /// <remarks>ArcCurve instances in result are IDisposable and must be disposed by consumer.</remarks>
    private static readonly Func<int, Circle, Result<Intersection.IntersectionOutput>> CircleHandler = (type, circle) => (type, circle) switch {
        (1, Circle arc) => {
            ArcCurve arcCurve = new(arc);
            Curve nurbs = arcCurve.ToNurbsCurve();
            arcCurve.Dispose();
            return ResultFactory.Create(value: new Intersection.IntersectionOutput([], [nurbs,], [], [], [], []));
        },
        (2, Circle tangent) => ResultFactory.Create(value: new Intersection.IntersectionOutput([tangent.Center,], [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
    };

    /// <summary>Processes polyline arrays flattening points while preserving original polyline structures.</summary>
    private static readonly Func<Polyline[]?, Result<Intersection.IntersectionOutput>> PolylineProcessor = polylines
    => polylines switch { { Length: > 0 } nonNullPolylines => ResultFactory.Create(value: new Intersection.IntersectionOutput(
                              [.. nonNullPolylines.SelectMany(polyline => polyline)],
                              [], [], [], [], [.. nonNullPolylines])),
        null => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
        _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
    };

    /// <summary>Handles mesh intersections dispatching between sorted and unsorted RhinoCommon methods.</summary>
    private static readonly Func<Mesh, object, bool, (Func<Point3d[]?, int[]?, Result<Intersection.IntersectionOutput>>, Func<Point3d[]?, Result<Intersection.IntersectionOutput>>), Result<Intersection.IntersectionOutput>> MeshIntersectionHandler =
        (mesh, target, sorted, handlers) => sorted switch {
            true => target switch {
                Line line => handlers.Item1(RhinoIntersect.MeshLineSorted(mesh, line, out int[] ids), ids),
                PolylineCurve polyline => handlers.Item1(RhinoIntersect.MeshPolylineSorted(mesh, polyline, out int[] ids), ids),
                _ => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
            false => target switch {
                Line line => handlers.Item2(RhinoIntersect.MeshLine(mesh, line)),
                PolylineCurve polyline when RhinoIntersect.MeshPolyline(mesh, polyline, out int[] ids) is Point3d[] points => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, [], [], [], ids, [])),
                _ => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
        };

    /// <summary>FrozenDictionary mapping type pairs to intersection strategies with validation modes.</summary>
    private static readonly FrozenDictionary<(Type, Type), IntersectionStrategy> _strategies =
        new ((Type, Type) Key, Func<object, object, double, Intersection.IntersectionSettings, IGeometryContext, Result<Intersection.IntersectionOutput>> Executor)[] {
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
            ((typeof(Mesh), typeof(Mesh)), (first, second, tolerance, _, _) => PolylineProcessor(RhinoIntersect.MeshMeshAccurate((Mesh)first, (Mesh)second, tolerance))),
            ((typeof(Mesh), typeof(Ray3d)), (first, second, _, _, _) => RhinoIntersect.MeshRay((Mesh)first, (Ray3d)second) switch {
                double distance when distance >= 0d => ResultFactory.Create(value: new Intersection.IntersectionOutput([((Ray3d)second).PointAt(distance)], [], [distance], [], [], [])),
                _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
            }),
            ((typeof(Mesh), typeof(Plane)), (first, second, _, _, _) => PolylineProcessor(RhinoIntersect.MeshPlane((Mesh)first, (Plane)second))),
            ((typeof(Mesh), typeof(Line)), (first, second, _, settings, _) => MeshIntersectionHandler((Mesh)first, second, settings.Sorted,
                ((points, indices) => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, [], [], [], indices ?? [], [])),
                    _ => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
                points => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, [], [], [], [], [])),
                    null => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
                }))),
            ((typeof(Mesh), typeof(PolylineCurve)), (first, second, _, settings, _) => MeshIntersectionHandler((Mesh)first, second, settings.Sorted,
                ((points, indices) => points switch {
                    { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionOutput(points, [], [], [], indices ?? [], [])),
                    _ => ResultFactory.Create<Intersection.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
                points => ResultFactory.Create(value: new Intersection.IntersectionOutput(points ?? [], [], [], [], [], []))))),
            ((typeof(Line), typeof(Line)), (first, second, tolerance, _, _) => RhinoIntersect.LineLine((Line)first, (Line)second, out double parameterA, out double parameterB, tolerance, finiteSegments: false)
                ? ResultFactory.Create(value: new Intersection.IntersectionOutput([((Line)first).PointAt(parameterA)], [], [parameterA], [parameterB], [], []))
                : ResultFactory.Create(value: Intersection.IntersectionOutput.Empty)),
            ((typeof(Line), typeof(BoundingBox)), (first, second, tolerance, _, _) => RhinoIntersect.LineBox((Line)first, (BoundingBox)second, tolerance, out Interval interval)
                ? ResultFactory.Create(value: new Intersection.IntersectionOutput([((Line)first).PointAt(interval.Min), ((Line)first).PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                : ResultFactory.Create(value: Intersection.IntersectionOutput.Empty)),
            ((typeof(Line), typeof(Plane)), (first, second, _, _, _) => RhinoIntersect.LinePlane((Line)first, (Plane)second, out double parameter)
                ? ResultFactory.Create(value: new Intersection.IntersectionOutput([((Line)first).PointAt(parameter)], [], [parameter], [], [], []))
                : ResultFactory.Create(value: Intersection.IntersectionOutput.Empty)),
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
                ? ResultFactory.Create(value: new Intersection.IntersectionOutput([], [new LineCurve(line),], [], [], [], []))
                : ResultFactory.Create(value: Intersection.IntersectionOutput.Empty)),
            ((typeof(ValueTuple<Plane, Plane>), typeof(Plane)), (first, second, _, _, _) => {
                (Plane planeA, Plane planeB) = (ValueTuple<Plane, Plane>)first;
                return RhinoIntersect.PlanePlanePlane(planeA, planeB, (Plane)second, out Point3d point)
                    ? ResultFactory.Create(value: new Intersection.IntersectionOutput([point], [], [], [], [], []))
                    : ResultFactory.Create(value: Intersection.IntersectionOutput.Empty);
            }),
            ((typeof(Plane), typeof(Circle)), (first, second, _, _, _) => RhinoIntersect.PlaneCircle((Plane)first, (Circle)second, out double parameterA, out double parameterB) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersection.IntersectionOutput([((Circle)second).PointAt(parameterA)], [], [], [parameterA], [], [])),
                PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersection.IntersectionOutput([((Circle)second).PointAt(parameterA), ((Circle)second).PointAt(parameterB)], [], [], [parameterA, parameterB], [], [])),
                _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
            }),
            ((typeof(Plane), typeof(Sphere)), (first, second, _, _, _) => CircleHandler((int)RhinoIntersect.PlaneSphere((Plane)first, (Sphere)second, out Circle circle), circle)),
            ((typeof(Plane), typeof(BoundingBox)), (first, second, _, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)first, (BoundingBox)second, out Polyline polyline), polyline) switch {
                (true, Polyline { Count: > 0 } pl) => ResultFactory.Create(value: new Intersection.IntersectionOutput([.. from point in pl select point], [], [], [], [], [pl])),
                _ => ResultFactory.Create(value: Intersection.IntersectionOutput.Empty),
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
        }.ToFrozenDictionary(static entry => entry.Key, entry => {
            (V ModeA, V ModeB) = IntersectionConfig.PairOperations.TryGetValue(entry.Key, out IntersectionConfig.IntersectionPairMetadata? meta)
                ? (meta.ValidationModeA, meta.ValidationModeB)
                : (V.None, V.None);
            return new IntersectionStrategy(entry.Executor, ModeA, ModeB);
        });

    /// <summary>Resolves intersection strategy for type pair using inheritance chain and interface traversal.</summary>
    [Pure]
    internal static Result<(IntersectionStrategy Strategy, bool Swapped)> ResolveStrategy(Type typeA, Type typeB) {
        static Type[] getTypeChain(Type type) {
            List<Type> chain = [];
            for (Type? current = type; current is not null; current = current.BaseType) {
                chain.Add(current);
            }
            return [.. chain.Concat(type.GetInterfaces()).Distinct()];
        }

        (Type[] chainA, Type[] chainB) = (getTypeChain(typeA), getTypeChain(typeB));

        return chainA.SelectMany(first => chainB.Select(second => ((first, second), false)))
            .Concat(chainB.SelectMany(first => chainA.Select(second => ((first, second), true))))
            .Select(candidate => (_strategies.TryGetValue(candidate.Item1, out IntersectionStrategy resolved), candidate.Item1, candidate.Item2, resolved))
            .FirstOrDefault(match => match.Item1) switch {
                (true, (Type, Type) key, bool swapped, IntersectionStrategy strategy) => ResultFactory.Create(value: (strategy, swapped)),
                _ => ResultFactory.Create<(IntersectionStrategy, bool)>(error: E.Geometry.UnsupportedIntersection.WithContext($"{typeA.Name} × {typeB.Name}")),
            };
    }

    /// <summary>Normalizes intersection settings validating tolerance with context defaults.</summary>
    [Pure]
    internal static Result<(double Tolerance, Intersection.IntersectionSettings Settings)> NormalizeSettings(Intersection.IntersectionSettings settings, IGeometryContext context) =>
        ResultFactory.Create(value: settings)
            .Ensure(opt => !opt.Tolerance.HasValue || (RhinoMath.IsValidDouble(opt.Tolerance.Value) && opt.Tolerance.Value > RhinoMath.ZeroTolerance), E.Validation.ToleranceAbsoluteInvalid)
            .Map(opt => {
                double tolerance = opt.Tolerance ?? context.AbsoluteTolerance;
                return (tolerance, new Intersection.IntersectionSettings(tolerance, opt.Sorted));
            });

    /// <summary>Executes intersection with normalized settings resolving strategy and validating inputs.</summary>
    [Pure]
    internal static Result<Intersection.IntersectionOutput> ExecuteWithSettings(
        object geometryA,
        object geometryB,
        IGeometryContext context,
        (double Tolerance, Intersection.IntersectionSettings Settings) normalized,
        IntersectionStrategy strategy,
        bool swapped) {
        static Result<object> validate(object geometry, IGeometryContext ctx, V mode) =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        (V modeA, V modeB) = swapped
            ? (strategy.ModeB, strategy.ModeA)
            : (strategy.ModeA, strategy.ModeB);

        return validate(geometryA, context, modeA)
            .Bind(validA => validate(geometryB, context, modeB)
                .Bind(validB => (swapped
                    ? strategy.Executor(validB, validA, normalized.Tolerance, normalized.Settings, context)
                    : strategy.Executor(validA, validB, normalized.Tolerance, normalized.Settings, context))
                    .Map(output => swapped
                        ? new Intersection.IntersectionOutput(output.Points, output.Curves, output.ParametersB, output.ParametersA, output.FaceIndices, output.Sections)
                        : output)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.ClassificationResult> ExecuteClassification(
        Intersection.IntersectionOutput output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: geometryA,
            operation: (Func<GeometryBase, Result<IReadOnlyList<Intersection.ClassificationResult>>>)(geomA =>
                IntersectionCompute.Classify(output: output, geomA: geomA, geomB: geometryB, context: context)
                    .Map(tuple => (IReadOnlyList<Intersection.ClassificationResult>)[new Intersection.ClassificationResult(
                        Type: (Intersection.IntersectionType)tuple.Type,
                        ApproachAngles: tuple.ApproachAngles,
                        IsGrazing: tuple.IsGrazing,
                        BlendScore: tuple.BlendScore),])),
            config: new OperationConfig<GeometryBase, Intersection.ClassificationResult> {
                Context = context,
                ValidationMode = IntersectionConfig.ClassificationOperation.ValidationModeA,
                OperationName = IntersectionConfig.ClassificationOperation.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.NearMissResult> ExecuteNearMiss(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: geometryA,
            operation: (Func<GeometryBase, Result<IReadOnlyList<Intersection.NearMissResult>>>)(geomA =>
                IntersectionCompute.FindNearMisses(geomA: geomA, geomB: geometryB, searchRadius: searchRadius, context: context)
                    .Map(tuple => (IReadOnlyList<Intersection.NearMissResult>)[new Intersection.NearMissResult(
                        LocationsA: tuple.Item1,
                        LocationsB: tuple.Item2,
                        Distances: tuple.Item3),])),
            config: new OperationConfig<GeometryBase, Intersection.NearMissResult> {
                Context = context,
                ValidationMode = IntersectionConfig.NearMissOperation.ValidationModeA,
                OperationName = IntersectionConfig.NearMissOperation.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.StabilityResult> ExecuteStability(
        Intersection.IntersectionOutput baseIntersection,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: geometryA,
            operation: (Func<GeometryBase, Result<IReadOnlyList<Intersection.StabilityResult>>>)(geomA =>
                IntersectionCompute.AnalyzeStability(geomA: geomA, geomB: geometryB, baseOutput: baseIntersection, context: context)
                    .Map(tuple => (IReadOnlyList<Intersection.StabilityResult>)[new Intersection.StabilityResult(
                        StabilityScore: tuple.Score,
                        PerturbationSensitivity: tuple.Sensitivity,
                        UnstableFlags: tuple.UnstableFlags),])),
            config: new OperationConfig<GeometryBase, Intersection.StabilityResult> {
                Context = context,
                ValidationMode = IntersectionConfig.StabilityOperation.ValidationModeA,
                OperationName = IntersectionConfig.StabilityOperation.OperationName,
            }).Map(static r => r[0]);
}

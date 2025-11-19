using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
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

/// <summary>RhinoCommon intersection dispatch with FrozenDictionary resolution.</summary>
internal static class IntersectionCore {
    internal readonly record struct IntersectionExecutionOptions(
        double Tolerance,
        bool UseSortedEvaluation,
        bool IncludeIndices,
        Vector3d? ProjectionDirection,
        int? MaxHits);

    private sealed record IntersectionStrategy(
        Func<object, object, double, IntersectionExecutionOptions, IGeometryContext, Result<Intersection.IntersectionResult>> Executor,
        IntersectionConfig.IntersectionPairMetadata Metadata);

    private static readonly Func<(bool, Curve[]?, Point3d[]?), Result<Intersection.IntersectionResult>> ArrayResultBuilder = tuple => tuple switch {
        (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersection.IntersectionResult(points, curves, [], [], [], [])),
        (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersection.IntersectionResult([], curves, [], [], [], [])),
        (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersection.IntersectionResult(points, [], [], [], [], [])),
        (true, _, _) or (false, _, _) => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
    };

    private static readonly Func<CurveIntersections?, Curve, Result<Intersection.IntersectionResult>> IntersectionProcessor = (results, source) => results switch { { Count: > 0 } =>
        ResultFactory.Create(value: new Intersection.IntersectionResult(
            [.. results.Select(entry => entry.PointA)],
            [.. results.Where(entry => entry.IsOverlap).Select(entry => source.Trim(entry.OverlapA)).Where(trimmed => trimmed is not null)],
            [.. results.Select(entry => entry.ParameterA)],
            [.. results.Select(entry => entry.ParameterB)],
            [], [])),
        _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
    };

    private static readonly Func<int, Point3d, Point3d, double, double[]?, Result<Intersection.IntersectionResult>> TwoPointHandler = (count, first, second, tolerance, parameters) =>
        (count, first.DistanceTo(second) > tolerance) switch {
            ( > 1, true) => ResultFactory.Create(value: new Intersection.IntersectionResult([first, second], [], parameters ?? [], [], [], [])),
            ( > 0, _) => ResultFactory.Create(value: new Intersection.IntersectionResult([first], [], parameters is { Length: > 0 } ? [parameters[0]] : [], [], [], [])),
            _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
        };

    private static readonly Func<int, Circle, Result<Intersection.IntersectionResult>> CircleHandler = (type, circle) => (type, circle) switch {
        (1, Circle arc) => ResultFactory.Create(value: new Intersection.IntersectionResult([], [new ArcCurve(arc)], [], [], [], [])),
        (2, Circle tangent) => ResultFactory.Create(value: new Intersection.IntersectionResult([tangent.Center], [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
    };

    private static readonly Func<Polyline[]?, Result<Intersection.IntersectionResult>> PolylineProcessor = polylines => polylines switch {
        { Length: > 0 } nonNullPolylines => ResultFactory.Create(value: new Intersection.IntersectionResult(
            [.. nonNullPolylines.SelectMany(polyline => polyline)],
            [], [], [], [], [.. nonNullPolylines])),
        null => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
        _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
    };

    private static readonly Func<Mesh, object, IntersectionExecutionOptions, (Func<Point3d[]?, int[]?, Result<Intersection.IntersectionResult>>, Func<Point3d[]?, Result<Intersection.IntersectionResult>>), Result<Intersection.IntersectionResult>> MeshIntersectionHandler =
        (mesh, target, options, handlers) => options.UseSortedEvaluation switch {
            true => target switch {
                Line line => handlers.Item1(RhinoIntersect.MeshLineSorted(mesh, line, out int[] ids), ids),
                PolylineCurve polyline => handlers.Item1(RhinoIntersect.MeshPolylineSorted(mesh, polyline, out int[] ids), ids),
                _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
            },
            false => target switch {
                Line line => handlers.Item2(RhinoIntersect.MeshLine(mesh, line)),
                PolylineCurve polyline when RhinoIntersect.MeshPolyline(mesh, polyline, out int[] ids) is Point3d[] points => ResultFactory.Create(value: new Intersection.IntersectionResult(points, [], [], [], ids, [])),
                _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
            },
        };

    private static readonly Func<Point3d[], object, IntersectionExecutionOptions, double, IGeometryContext, V, Result<Intersection.IntersectionResult>> ProjectionHandler = (points, targets, options, tolerance, context, validationMode) =>
        options.ProjectionDirection is Vector3d dir && dir.IsValid && dir.Length > RhinoMath.ZeroTolerance ? (targets, options.IncludeIndices) switch {
            (Brep[] breps, bool includeIndices) => ResultFactory.Create<IEnumerable<Brep>>(value: breps)
                .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, validationMode,]))
                .Map<Brep[]>(valid => [.. valid])
                .Bind(validBreps => includeIndices
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult(
                        RhinoIntersect.ProjectPointsToBrepsEx(validBreps, points, dir, tolerance, out int[] indices), [], [], [], indices, []))
                    : ResultFactory.Create(value: new Intersection.IntersectionResult(
                        RhinoIntersect.ProjectPointsToBreps(validBreps, points, dir, tolerance), [], [], [], [], []))),
            (Mesh[] meshes, bool includeIndices) => ResultFactory.Create<IEnumerable<Mesh>>(value: meshes)
                .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, validationMode,]))
                .Map<Mesh[]>(valid => [.. valid])
                .Bind(validMeshes => includeIndices
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult(
                        RhinoIntersect.ProjectPointsToMeshesEx(validMeshes, points, dir, tolerance, out int[] indices), [], [], [], indices, []))
                    : ResultFactory.Create(value: new Intersection.IntersectionResult(
                        RhinoIntersect.ProjectPointsToMeshes(validMeshes, points, dir, tolerance), [], [], [], [], []))),
            _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidProjection.WithContext(targets.GetType().Name)),
        } : ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidProjection.WithContext("null"));

    private static readonly FrozenDictionary<(Type, Type), IntersectionStrategy> Strategies =
        new Dictionary<(Type, Type), IntersectionStrategy> {
            [(typeof(Curve), typeof(Curve))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    Curve curveA = (Curve)first;
                    Curve curveB = (Curve)second;
                    using CurveIntersections? intersections = ReferenceEquals(curveA, curveB)
                        ? RhinoIntersect.CurveSelf(curveA, tolerance)
                        : RhinoIntersect.CurveCurve(curveA, curveB, tolerance, tolerance);
                    return IntersectionProcessor(intersections, curveA);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(Curve))]),
            [(typeof(Curve), typeof(BrepFace))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.CurveBrepFace((Curve)first, (BrepFace)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(BrepFace))]),
            [(typeof(Curve), typeof(Surface))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    using CurveIntersections? intersections = RhinoIntersect.CurveSurface((Curve)first, (Surface)second, tolerance, overlapTolerance: tolerance);
                    return IntersectionProcessor(intersections, (Curve)first);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(Surface))]),
            [(typeof(Curve), typeof(Plane))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    using CurveIntersections? intersections = RhinoIntersect.CurvePlane((Curve)first, (Plane)second, tolerance);
                    return IntersectionProcessor(intersections, (Curve)first);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(Plane))]),
            [(typeof(Curve), typeof(Line))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    using CurveIntersections? intersections = RhinoIntersect.CurveLine((Curve)first, (Line)second, tolerance, overlapTolerance: tolerance);
                    return IntersectionProcessor(intersections, (Curve)first);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(Line))]),
            [(typeof(Curve), typeof(Brep))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.CurveBrep((Curve)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Curve), typeof(Brep))]),
            [(typeof(Brep), typeof(Brep))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepBrep((Brep)first, (Brep)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Brep), typeof(Brep))]),
            [(typeof(Brep), typeof(Plane))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepPlane((Brep)first, (Plane)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Brep), typeof(Plane))]),
            [(typeof(Brep), typeof(Surface))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.BrepSurface((Brep)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Brep), typeof(Surface))]),
            [(typeof(Surface), typeof(Surface))] = new(
                Executor: (first, second, tolerance, _, _) => ArrayResultBuilder((RhinoIntersect.SurfaceSurface((Surface)first, (Surface)second, tolerance, out Curve[] curves, out Point3d[] points), curves, points)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Surface), typeof(Surface))]),
            [(typeof(Mesh), typeof(Mesh))] = new(
                Executor: (first, second, tolerance, _, _) => PolylineProcessor(RhinoIntersect.MeshMeshAccurate((Mesh)first, (Mesh)second, tolerance)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Mesh), typeof(Mesh))]),
            [(typeof(Mesh), typeof(Ray3d))] = new(
                Executor: (first, second, _, _, _) => RhinoIntersect.MeshRay((Mesh)first, (Ray3d)second) switch {
                    double distance when distance >= 0d => ResultFactory.Create(value: new Intersection.IntersectionResult([((Ray3d)second).PointAt(distance)], [], [distance], [], [], [])),
                    _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Mesh), typeof(Ray3d))]),
            [(typeof(Mesh), typeof(Plane))] = new(
                Executor: (first, second, _, _, _) => PolylineProcessor(RhinoIntersect.MeshPlane((Mesh)first, (Plane)second)),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Mesh), typeof(Plane))]),
            [(typeof(Mesh), typeof(Line))] = new(
                Executor: (first, second, _, options, _) => MeshIntersectionHandler((Mesh)first, second, options,
                    ((points, indices) => points switch {
                        { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionResult(points, [], [], [], indices ?? [], [])),
                        _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
                    },
                    points => points switch {
                        { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionResult(points, [], [], [], [], [])),
                        null => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
                        _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                    })),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Mesh), typeof(Line))]),
            [(typeof(Mesh), typeof(PolylineCurve))] = new(
                Executor: (first, second, _, options, _) => MeshIntersectionHandler((Mesh)first, second, options,
                    ((points, indices) => points switch {
                        { Length: > 0 } => ResultFactory.Create(value: new Intersection.IntersectionResult(points, [], [], [], indices ?? [], [])),
                        _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.IntersectionFailed),
                    },
                    points => ResultFactory.Create(value: new Intersection.IntersectionResult(points ?? [], [], [], [], [], [])))),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Mesh), typeof(PolylineCurve))]),
            [(typeof(Line), typeof(Line))] = new(
                Executor: (first, second, tolerance, _, _) => RhinoIntersect.LineLine((Line)first, (Line)second, out double parameterA, out double parameterB, tolerance, finiteSegments: false)
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(parameterA)], [], [parameterA], [parameterB], [], []))
                    : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(Line))]),
            [(typeof(Line), typeof(BoundingBox))] = new(
                Executor: (first, second, tolerance, _, _) => RhinoIntersect.LineBox((Line)first, (BoundingBox)second, tolerance, out Interval interval)
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(interval.Min), ((Line)first).PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                    : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(BoundingBox))]),
            [(typeof(Line), typeof(Plane))] = new(
                Executor: (first, second, _, _, _) => RhinoIntersect.LinePlane((Line)first, (Plane)second, out double parameter)
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult([((Line)first).PointAt(parameter)], [], [parameter], [], [], []))
                    : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(Plane))]),
            [(typeof(Line), typeof(Sphere))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    int count = (int)RhinoIntersect.LineSphere((Line)first, (Sphere)second, out Point3d pointA, out Point3d pointB);
                    return TwoPointHandler(count, pointA, pointB, tolerance, null);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(Sphere))]),
            [(typeof(Line), typeof(Cylinder))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    int count = (int)RhinoIntersect.LineCylinder((Line)first, (Cylinder)second, out Point3d pointA, out Point3d pointB);
                    return TwoPointHandler(count, pointA, pointB, tolerance, null);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(Cylinder))]),
            [(typeof(Line), typeof(Circle))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    int count = (int)RhinoIntersect.LineCircle((Line)first, (Circle)second, out double parameterA, out Point3d pointA, out double parameterB, out Point3d pointB);
                    return TwoPointHandler(count, pointA, pointB, tolerance, count > 1 ? [parameterA, parameterB] : count > 0 ? [parameterA] : null);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Line), typeof(Circle))]),
            [(typeof(Plane), typeof(Plane))] = new(
                Executor: (first, second, _, _, _) => RhinoIntersect.PlanePlane((Plane)first, (Plane)second, out Line line)
                    ? ResultFactory.Create(value: new Intersection.IntersectionResult([], [new LineCurve(line)], [], [], [], []))
                    : ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Plane), typeof(Plane))]),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = new(
                Executor: (first, second, _, _, _) => {
                    (Plane planeA, Plane planeB) = (ValueTuple<Plane, Plane>)first;
                    return RhinoIntersect.PlanePlanePlane(planeA, planeB, (Plane)second, out Point3d point)
                        ? ResultFactory.Create(value: new Intersection.IntersectionResult([point], [], [], [], [], []))
                        : ResultFactory.Create(value: Intersection.IntersectionResult.Empty);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(ValueTuple<Plane, Plane>), typeof(Plane))]),
            [(typeof(Plane), typeof(Circle))] = new(
                Executor: (first, second, _, _, _) => RhinoIntersect.PlaneCircle((Plane)first, (Circle)second, out double parameterA, out double parameterB) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersection.IntersectionResult([((Circle)second).PointAt(parameterA)], [], [], [parameterA], [], [])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersection.IntersectionResult([((Circle)second).PointAt(parameterA), ((Circle)second).PointAt(parameterB)], [], [], [parameterA, parameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Plane), typeof(Circle))]),
            [(typeof(Plane), typeof(Sphere))] = new(
                Executor: (first, second, _, _, _) => CircleHandler((int)RhinoIntersect.PlaneSphere((Plane)first, (Sphere)second, out Circle circle), circle),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Plane), typeof(Sphere))]),
            [(typeof(Plane), typeof(BoundingBox))] = new(
                Executor: (first, second, _, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)first, (BoundingBox)second, out Polyline polyline), polyline) switch {
                    (true, Polyline { Count: > 0 } pl) => ResultFactory.Create(value: new Intersection.IntersectionResult([.. pl.Select(point => point)], [], [], [], [], [pl])),
                    _ => ResultFactory.Create(value: Intersection.IntersectionResult.Empty),
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Plane), typeof(BoundingBox))]),
            [(typeof(Sphere), typeof(Sphere))] = new(
                Executor: (first, second, _, _, _) => CircleHandler((int)RhinoIntersect.SphereSphere((Sphere)first, (Sphere)second, out Circle circle), circle),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Sphere), typeof(Sphere))]),
            [(typeof(Circle), typeof(Circle))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    int count = (int)RhinoIntersect.CircleCircle((Circle)first, (Circle)second, out Point3d pointA, out Point3d pointB);
                    return TwoPointHandler(count, pointA, pointB, tolerance, null);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Circle), typeof(Circle))]),
            [(typeof(Arc), typeof(Arc))] = new(
                Executor: (first, second, tolerance, _, _) => {
                    int count = (int)RhinoIntersect.ArcArc((Arc)first, (Arc)second, out Point3d pointA, out Point3d pointB);
                    return TwoPointHandler(count, pointA, pointB, tolerance, null);
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Arc), typeof(Arc))]),
            [(typeof(Point3d[]), typeof(Brep[]))] = new(
                Executor: (first, second, tolerance, options, context) => ProjectionHandler((Point3d[])first, second, options, tolerance, context, V.Standard | V.Topology),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Point3d[]), typeof(Brep[]))]),
            [(typeof(Point3d[]), typeof(Mesh[]))] = new(
                Executor: (first, second, tolerance, options, context) => ProjectionHandler((Point3d[])first, second, options, tolerance, context, V.MeshSpecific),
                Metadata: IntersectionConfig.PairMetadata[(typeof(Point3d[]), typeof(Mesh[]))]),
            [(typeof(Ray3d), typeof(GeometryBase[]))] = new(
                Executor: (first, second, _, options, context) => options.MaxHits switch {
                    int hits when hits > 0 => ResultFactory.Create<IEnumerable<GeometryBase>>(value: (GeometryBase[])second)
                        .TraverseElements(item => ResultFactory.Create(value: item).Validate(args: [context, V.None,]))
                        .Map<GeometryBase[]>(valid => [.. valid])
                        .Map(validated => new Intersection.IntersectionResult(RhinoIntersect.RayShoot((Ray3d)first, validated, hits), [], [], [], [], [])),
                    int hits => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidMaxHits.WithContext(hits.ToString(CultureInfo.InvariantCulture))),
                    _ => ResultFactory.Create<Intersection.IntersectionResult>(error: E.Geometry.InvalidMaxHits),
                },
                Metadata: IntersectionConfig.PairMetadata[(typeof(Ray3d), typeof(GeometryBase[]))]),
        }.ToFrozenDictionary();

    [Pure]
    internal static Result<(IntersectionStrategy Strategy, bool Swapped)> ResolveStrategy(Type typeA, Type typeB) {
        static Type[] GetTypeChain(Type type) {
            List<Type> chain = [];
            for (Type? current = type; current is not null; current = current.BaseType) {
                chain.Add(current);
            }
            return [.. chain.Concat(type.GetInterfaces()).Distinct()];
        }

        Type[] chainA = GetTypeChain(typeA);
        Type[] chainB = GetTypeChain(typeB);

        return chainA.SelectMany(first => chainB.Select(second => ((first, second), false)))
            .Concat(chainB.SelectMany(first => chainA.Select(second => ((first, second), true))))
            .Select(candidate => (Strategies.TryGetValue(candidate.Item1, out IntersectionStrategy resolved), candidate.Item1, candidate.Item2, resolved))
            .FirstOrDefault(match => match.Item1) switch {
                (true, (Type, Type) _, bool swapped, IntersectionStrategy strategy) => ResultFactory.Create(value: (strategy, swapped)),
                _ => ResultFactory.Create<(IntersectionStrategy, bool)>(error: E.Geometry.UnsupportedIntersection.WithContext($"{typeA.Name} × {typeB.Name}")),
            };
    }

    [Pure]
    internal static Result<IntersectionExecutionOptions> NormalizeRequest(Intersection.Request request, IGeometryContext context) {
        static Result<double> NormalizeTolerance(double? tolerance, double fallback) =>
            tolerance.HasValue
                ? RhinoMath.IsValidDouble(tolerance.Value) && tolerance.Value > RhinoMath.ZeroTolerance
                    ? ResultFactory.Create(value: tolerance.Value)
                    : ResultFactory.Create<double>(error: E.Validation.ToleranceAbsoluteInvalid)
                : ResultFactory.Create(value: fallback);

        return request switch {
            Intersection.Request.General general => NormalizeTolerance(general.Tolerance, context.AbsoluteTolerance)
                .Map(value => new IntersectionExecutionOptions(value, general.UseSortedMeshEvaluation, false, null, null)),
            Intersection.Request.PointProjection projection => NormalizeTolerance(projection.Tolerance, context.AbsoluteTolerance)
                .Ensure(_ => projection.Direction.IsValid && projection.Direction.Length > RhinoMath.ZeroTolerance, E.Geometry.InvalidProjection)
                .Map(value => new IntersectionExecutionOptions(value, false, projection.IncludeIndices, projection.Direction, null)),
            Intersection.Request.RayShoot ray => NormalizeTolerance(ray.Tolerance, context.AbsoluteTolerance)
                .Ensure(_ => ray.MaxHits > 0, E.Geometry.InvalidMaxHits)
                .Map(value => new IntersectionExecutionOptions(value, false, false, null, ray.MaxHits)),
            _ => ResultFactory.Create<IntersectionExecutionOptions>(error: E.Geometry.UnsupportedIntersection.WithContext(request.GetType().Name)),
        };
    }

    [Pure]
    internal static Result<Intersection.IntersectionResult> Execute<T1, T2>(T1 geometryA, T2 geometryB, IGeometryContext context, Intersection.Request request) where T1 : notnull where T2 : notnull =>
        NormalizeRequest(request, context)
            .Bind(options => ResolveStrategy(geometryA.GetType(), geometryB.GetType())
                .Bind(entry => ExecuteWithStrategy(geometryA, geometryB, context, options, entry)));

    private static Result<Intersection.IntersectionResult> ExecuteWithStrategy(
        object geometryA,
        object geometryB,
        IGeometryContext context,
        IntersectionExecutionOptions options,
        (IntersectionStrategy Strategy, bool Swapped) entry) {
        object first = entry.Swapped ? geometryB : geometryA;
        object second = entry.Swapped ? geometryA : geometryB;
        IntersectionConfig.IntersectionPairMetadata metadata = entry.Strategy.Metadata;
        V firstValidation = entry.Swapped ? metadata.SecondValidation : metadata.FirstValidation;
        V secondValidation = entry.Swapped ? metadata.FirstValidation : metadata.SecondValidation;

        return UnifiedOperation.Apply(
                input: first,
                operation: (Func<object, Result<IReadOnlyList<Intersection.IntersectionResult>>>)(primary =>
                    (secondValidation == V.None
                        ? ResultFactory.Create(value: second)
                        : ResultFactory.Create(value: second).Validate(args: [context, secondValidation,]))
                    .Bind(validSecondary => entry.Strategy.Executor(primary, validSecondary, options.Tolerance, options, context)
                        .Map(result => entry.Swapped
                            ? new Intersection.IntersectionResult(result.Points, result.Curves, result.ParametersB, result.ParametersA, result.FaceIndices, result.Sections)
                            : result)
                        .Map(result => (IReadOnlyList<Intersection.IntersectionResult>)[result,]))),
                config: new OperationConfig<object, Intersection.IntersectionResult> {
                    Context = context,
                    ValidationMode = firstValidation,
                    OperationName = metadata.OperationName,
                    AccumulateErrors = true,
                    EnableDiagnostics = false,
                })
            .Map(outputs => outputs.Count == 0
                ? Intersection.IntersectionResult.Empty
                : new Intersection.IntersectionResult(
                    [.. outputs.SelectMany(static output => output.Points)],
                    [.. outputs.SelectMany(static output => output.Curves)],
                    [.. outputs.SelectMany(static output => output.ParametersA)],
                    [.. outputs.SelectMany(static output => output.ParametersB)],
                    [.. outputs.SelectMany(static output => output.FaceIndices)],
                    [.. outputs.SelectMany(static output => output.Sections)]));
    }
}

using System.Collections.Frozen;
using System.Diagnostics.Contracts;
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

/// <summary>Polymorphic intersection engine with automatic type-based method detection.</summary>
public static class Intersect {
    /// <summary>Type-safe optional parameters for intersection operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOptions(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool WithIndices = false,
        bool Sorted = false);

    /// <summary>Unified intersection output with zero nullable fields.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }
    /// <summary>Performs intersection with automatic type detection, validation, and collection handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionOptions? options = null,
        bool enableDiagnostics = false) where T1 : notnull where T2 : notnull {
        IntersectionOptions opts = options ?? new();
        Type t1Type = typeof(T1);
        Type t2Type = typeof(T2);
        Type elementType = t1Type is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? t.GetGenericArguments()[0]
            : t1Type;
        ulong mode = IntersectionCore._validationConfig.TryGetValue((t1Type, t2Type), out ulong m1) ? m1
            : IntersectionCore._validationConfig.TryGetValue((elementType, t2Type), out ulong m2) ? m2
            : Modes.None;

        return UnifiedOperation.Apply(
            geometryA,
            (Func<object, Result<IReadOnlyList<IntersectionOutput>>>)(item =>
                IntersectionCore.ExecutePair(item, geometryB, context, opts)
                    .Map(r => (IReadOnlyList<IntersectionOutput>)[r])),
            new OperationConfig<object, IntersectionOutput> {
                Context = context,
                ValidationMode = mode,
                AccumulateErrors = true,
                OperationName = $"Intersect.{t1Type.Name}.{t2Type.Name}",
                EnableDiagnostics = enableDiagnostics,
            })
        .Map(outputs => outputs.Aggregate(IntersectionOutput.Empty, (acc, curr) => new IntersectionOutput(
            [.. acc.Points, .. curr.Points],
            [.. acc.Curves, .. curr.Curves],
            [.. acc.ParametersA, .. curr.ParametersA],
            [.. acc.ParametersB, .. curr.ParametersB],
            [.. acc.FaceIndices, .. curr.FaceIndices],
            [.. acc.Sections, .. curr.Sections])));
    }

    private static class IntersectionCore {
        internal static readonly FrozenDictionary<(Type, Type), ulong> _validationConfig =
            new Dictionary<(Type, Type), ulong> {
                [(typeof(Curve), typeof(Curve))] = Modes.Standard | Modes.Degeneracy,
                [(typeof(Curve), typeof(Surface))] = Modes.Standard,
                [(typeof(Curve), typeof(Brep))] = Modes.Standard | Modes.Topology,
                [(typeof(Curve), typeof(BrepFace))] = Modes.Standard | Modes.Topology,
                [(typeof(Curve), typeof(Plane))] = Modes.Standard | Modes.Degeneracy,
                [(typeof(Curve), typeof(Line))] = Modes.Standard | Modes.Degeneracy,
                [(typeof(Brep), typeof(Brep))] = Modes.Standard | Modes.Topology,
                [(typeof(Brep), typeof(Plane))] = Modes.Standard | Modes.Topology,
                [(typeof(Brep), typeof(Surface))] = Modes.Standard | Modes.Topology,
                [(typeof(Surface), typeof(Surface))] = Modes.Standard,
                [(typeof(Mesh), typeof(Mesh))] = Modes.MeshSpecific,
                [(typeof(Mesh), typeof(Ray3d))] = Modes.MeshSpecific,
                [(typeof(Mesh), typeof(Plane))] = Modes.MeshSpecific,
                [(typeof(Mesh), typeof(Line))] = Modes.MeshSpecific,
                [(typeof(Mesh), typeof(PolylineCurve))] = Modes.MeshSpecific,
                [(typeof(Line), typeof(Line))] = Modes.Standard,
                [(typeof(Line), typeof(BoundingBox))] = Modes.Standard,
                [(typeof(Line), typeof(Plane))] = Modes.Standard,
                [(typeof(Line), typeof(Sphere))] = Modes.Standard,
                [(typeof(Line), typeof(Cylinder))] = Modes.Standard,
                [(typeof(Line), typeof(Circle))] = Modes.Standard,
                [(typeof(Plane), typeof(Plane))] = Modes.Standard,
                [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = Modes.Standard,
                [(typeof(Plane), typeof(Circle))] = Modes.Standard,
                [(typeof(Plane), typeof(Sphere))] = Modes.Standard,
                [(typeof(Plane), typeof(BoundingBox))] = Modes.Standard,
                [(typeof(Sphere), typeof(Sphere))] = Modes.Standard,
                [(typeof(Circle), typeof(Circle))] = Modes.Standard,
                [(typeof(Arc), typeof(Arc))] = Modes.Standard,
                [(typeof(Point3d[]), typeof(Brep[]))] = Modes.Standard | Modes.Topology,
                [(typeof(Point3d[]), typeof(Mesh[]))] = Modes.MeshSpecific,
                [(typeof(Ray3d), typeof(GeometryBase[]))] = Modes.Standard,
            }.ToFrozenDictionary();

        [Pure]
#pragma warning disable MA0051 // Method too long - Large pattern matching switch with 30+ intersection type combinations cannot be meaningfully reduced without extraction
        internal static Result<IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, IntersectionOptions opts) where T1 : notnull where T2 : notnull {
#pragma warning restore MA0051
            static Result<IntersectionOutput> fromCurveIntersections(CurveIntersections? results, Curve? overlapSource) {
                if (results is null) {
                    return ResultFactory.Create(value: IntersectionOutput.Empty);
                }
#pragma warning disable IDISP007 // Don't dispose injected - CurveIntersections created by caller and owned by this method
                using (results) {
#pragma warning restore IDISP007
                    return results.Count > 0
                        ? ResultFactory.Create(value: new IntersectionOutput(
                            [.. from e in results select e.PointA],
                            overlapSource is not null ? [.. from e in results where e.IsOverlap let c = overlapSource.Trim(e.OverlapA) where c is not null select c] : [],
                            [.. from e in results select e.ParameterA],
                            [.. from e in results select e.ParameterB],
                            [], []))
                        : ResultFactory.Create(value: IntersectionOutput.Empty);
                }
            }

            static Result<IntersectionOutput> fromBrepIntersection(bool success, Curve[] curves, Point3d[] points) =>
                success switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionOutput(
                        [.. points],
                        curves.Length > 0 ? [.. curves] : [],
                        [], [], [], [])),
                    _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                };

            static Result<IntersectionOutput> fromCountedPoints(int count, Point3d p1, Point3d p2, double tolerance) =>
                count switch {
                    > 1 when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new IntersectionOutput(
                        [p1, p2], [], [], [], [], [])),
                    > 0 => ResultFactory.Create(value: new IntersectionOutput(
                        [p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                };

            static Result<IntersectionOutput> fromPolylines(Polyline[]? sections) =>
                sections switch { { Length: > 0 } => ResultFactory.Create(value: new IntersectionOutput(
                                      [.. from pl in sections from pt in pl select pt],
                                      [], [], [], [],
                                      [.. sections])),
                    null => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                    _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                };

            double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

            return (a, b, opts) switch {
                (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                    ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2202)),
                (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                    ResultFactory.Create(value: new IntersectionOutput(
                        [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] ids1)],
                        [], [], [], ids1.Length > 0 ? [.. ids1] : [], [])),
                (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                    ResultFactory.Create(value: new IntersectionOutput(
                        [.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)],
                        [], [], [], [], [])),
                (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                    ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2202)),
                (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                    ResultFactory.Create(value: new IntersectionOutput(
                        [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] ids2)],
                        [], [], [], ids2.Length > 0 ? [.. ids2] : [], [])),
                (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                    ResultFactory.Create(value: new IntersectionOutput(
                        [.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)],
                        [], [], [], [], [])),
                (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                    ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2204)),
                (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                    ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2205)),
                (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                    ResultFactory.Create(value: new IntersectionOutput(
                        [.. RhinoIntersect.RayShoot(ray, geoms, hits)],
                        [], [], [], [], [])),
                (Curve ca, Curve cb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                    fromCurveIntersections(RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca),
#pragma warning restore IDISP004
                (Curve ca, BrepFace bf, _) =>
                    fromBrepIntersection(RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] c2, out Point3d[] p2), c2, p2),
                (Curve ca, Surface sb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                    fromCurveIntersections(RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance), overlapSource: null),
#pragma warning restore IDISP004
                (Curve ca, Plane pb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                    fromCurveIntersections(RhinoIntersect.CurvePlane(ca, pb, tolerance), overlapSource: null),
#pragma warning restore IDISP004
                (Curve ca, Line lb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                    fromCurveIntersections(RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance), overlapSource: null),
#pragma warning restore IDISP004
                (Curve ca, Brep bb, _) =>
                    fromBrepIntersection(RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] c1, out Point3d[] p1), c1, p1),
                (Brep ba, Brep bb, _) =>
                    fromBrepIntersection(RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] c3, out Point3d[] p3), c3, p3),
                (Brep ba, Plane pb, _) =>
                    fromBrepIntersection(RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] c4, out Point3d[] p4), c4, p4),
                (Brep ba, Surface sb, _) =>
                    fromBrepIntersection(RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] c5, out Point3d[] p5), c5, p5),
                (Surface sa, Surface sb, _) =>
                    fromBrepIntersection(RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] c6, out Point3d[] p6), c6, p6),
                (Mesh ma, Mesh mb, _) when !opts.Sorted =>
                    RhinoIntersect.MeshMeshFast(ma, mb) switch {
                        Line[] { Length: > 0 } lines => ResultFactory.Create(value: new IntersectionOutput(
                            [.. from l in lines select l.From, .. from l in lines select l.To],
                            [], [], [], [], [])),
                        null => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Mesh ma, Mesh mb, { Sorted: true }) =>
                    fromPolylines(RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance)),
                (Mesh ma, Ray3d rb, _) =>
                    RhinoIntersect.MeshRay(ma, rb) switch {
                        double d when d >= 0d => ResultFactory.Create(value: new IntersectionOutput(
                            [rb.PointAt(d)], [], [d], [], [], [])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Mesh ma, Plane pb, _) =>
                    fromPolylines(RhinoIntersect.MeshPlane(ma, pb)),
                (Mesh ma, Line lb, _) when !opts.Sorted =>
                    RhinoIntersect.MeshLine(ma, lb) switch {
                        Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionOutput(
                            [.. points], [], [], [], [], [])),
                        null => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Mesh ma, Line lb, { Sorted: true }) =>
                    RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids3) switch {
                        Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionOutput(
                            [.. points], [], [], [], ids3.Length > 0 ? [.. ids3] : [], [])),
                        _ => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                    },
                (Mesh ma, PolylineCurve pc, { Sorted: false }) =>
                    RhinoIntersect.MeshPolyline(ma, pc, out int[] ids4) switch {
                        Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionOutput(
                            [.. points], [], [], [], ids4.Length > 0 ? [.. ids4] : [], [])),
                        _ => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                    },
                (Mesh ma, PolylineCurve pc, { Sorted: true }) =>
                    RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids5) switch {
                        Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionOutput(
                            [.. points], [], [], [], ids5.Length > 0 ? [.. ids5] : [], [])),
                        _ => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2201)),
                    },
                (Line la, Line lb, _) =>
                    RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tolerance, finiteSegments: false) switch {
                        true => ResultFactory.Create(value: new IntersectionOutput(
                            [la.PointAt(pa)], [], [pa], [pb], [], [])),
                        false => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Line la, BoundingBox boxb, _) =>
                    RhinoIntersect.LineBox(la, boxb, tolerance, out Interval interval) switch {
                        true => ResultFactory.Create(value: new IntersectionOutput(
                            [la.PointAt(interval.Min), la.PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], [])),
                        false => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Line la, Plane pb, _) =>
                    RhinoIntersect.LinePlane(la, pb, out double param) switch {
                        true => ResultFactory.Create(value: new IntersectionOutput(
                            [la.PointAt(param)], [], [param], [], [], [])),
                        false => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Line la, Sphere sb, _) =>
                    fromCountedPoints((int)RhinoIntersect.LineSphere(la, sb, out Point3d ps1, out Point3d ps2), ps1, ps2, tolerance),
                (Line la, Cylinder cylb, _) =>
                    fromCountedPoints((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d pc1, out Point3d pc2), pc1, pc2, tolerance),
                (Line la, Circle cb, _) =>
                    ((int)RhinoIntersect.LineCircle(la, cb, out double lct1, out Point3d lcp1, out double lct2, out Point3d lcp2)) switch {
                        > 1 when lcp1.DistanceTo(lcp2) > tolerance => ResultFactory.Create(value: new IntersectionOutput(
                            [lcp1, lcp2], [], [lct1, lct2], [], [], [])),
                        > 0 => ResultFactory.Create(value: new IntersectionOutput(
                            [lcp1], [], [lct1], [], [], [])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Plane pa, Plane pb, _) =>
                    RhinoIntersect.PlanePlane(pa, pb, out Line line) switch {
                        true => ResultFactory.Create(value: new IntersectionOutput(
                            [], [new LineCurve(line)], [], [], [], [])),
                        false => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                    RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point) switch {
                        true => ResultFactory.Create(value: new IntersectionOutput(
                            [point], [], [], [], [], [])),
                        false => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Plane pa, Circle cb, _) =>
                    RhinoIntersect.PlaneCircle(pa, cb, out double pct1, out double pct2) switch {
                        PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new IntersectionOutput(
                            [cb.PointAt(pct1)], [], [], [pct1], [], [])),
                        PlaneCircleIntersection.Secant => ResultFactory.Create(value: new IntersectionOutput(
                            [cb.PointAt(pct1), cb.PointAt(pct2)], [], [], [pct1, pct2], [], [])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Plane pa, Sphere sb, _) =>
                    ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle psc)) switch {
                        1 => ResultFactory.Create(value: new IntersectionOutput(
                            [], [new ArcCurve(psc)], [], [], [], [])),
                        2 => ResultFactory.Create(value: new IntersectionOutput(
                            [psc.Center], [], [], [], [], [])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Plane pa, BoundingBox boxb, _) =>
                    RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) switch {
                        true when poly?.Count > 0 => ResultFactory.Create(value: new IntersectionOutput(
                            [.. from pt in poly select pt], [], [], [], [], [poly])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Sphere sa, Sphere sb, _) =>
                    ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle ssc)) switch {
                        1 => ResultFactory.Create(value: new IntersectionOutput(
                            [], [new ArcCurve(ssc)], [], [], [], [])),
                        2 => ResultFactory.Create(value: new IntersectionOutput(
                            [ssc.Center], [], [], [], [], [])),
                        _ => ResultFactory.Create(value: IntersectionOutput.Empty),
                    },
                (Circle ca, Circle cb, _) =>
                    fromCountedPoints((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d ccp1, out Point3d ccp2), ccp1, ccp2, tolerance),
                (Arc aa, Arc ab, _) =>
                    fromCountedPoints((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aap1, out Point3d aap2), aap1, aap2, tolerance),
                _ => ResultFactory.Create<IntersectionOutput>(error: ErrorFactory.Create(code: 2200)),
            };
        }
    }
}

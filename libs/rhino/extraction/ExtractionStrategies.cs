using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Internal extraction algorithms with Rhino SDK geometry processing.</summary>
internal static class ExtractionStrategies {
    /// <summary>Extracts points using specified method with validation and error mapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count = null, double? length = null, bool includeEnds = true) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context,
                method switch {
                    ExtractionMethod.Analytical => ValidationMode.Standard | (geometry switch {
                        Brep => ValidationMode.MassProperties,
                        Curve or Surface => ValidationMode.AreaCentroid,
                        _ => ValidationMode.None,
                    }),
                    ExtractionMethod.Uniform => ValidationMode.Standard | ValidationMode.Degeneracy,
                    ExtractionMethod.Extremal => ValidationMode.BoundingBox,
                    ExtractionMethod.Quadrant => ValidationMode.Tolerance,
                    _ => ValidationMode.Standard,
                },
            ])
            .Bind(g => ExtractCore(g, method, context, count, length, includeEnds) switch {
                Point3d[] { Length: > 0 } result => ResultFactory.Create(value: (IReadOnlyList<Point3d>)result.AsReadOnly()),
                null => ResultFactory.Create<IReadOnlyList<Point3d>>(error: (method, count, length) switch {
                    (ExtractionMethod.Uniform, not null, _) => ExtractionErrors.Operation.InvalidCount,
                    (ExtractionMethod.Uniform, _, not null) => ExtractionErrors.Operation.InvalidLength,
                    _ => ExtractionErrors.Operation.InvalidMethod,
                }),
                [] => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
            });

    /// <summary>Core extraction logic with Rhino SDK geometry type dispatch.</summary>
    [Pure]
    private static Point3d[]? ExtractCore(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count, double? length, bool includeEnds) =>
        (method, geometry) switch {
            // UNIFORM - Rhino SDK validates parameters via null returns
            (ExtractionMethod.Uniform, Curve c) when count is int n =>
                c.DivideByCount(n, includeEnds)?.Select(c.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Curve c) when length is double len =>
                c.DivideByLength(len, includeEnds)?.Select(c.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Surface s) => [..
                from i in Enumerable.Range(0, count ?? 10)
                from j in Enumerable.Range(0, count ?? 10)
                let n = count ?? 10
                let paramU = (n, includeEnds, i) switch {
                    (1, _, _) => 0.5,
                    (_, true, var idx) => idx / (double)(n - 1),
                    (_, false, var idx) => (idx + 0.5) / n,
                }
                let paramV = (n, includeEnds, j) switch {
                    (1, _, _) => 0.5,
                    (_, true, var idx) => idx / (double)(n - 1),
                    (_, false, var idx) => (idx + 0.5) / n,
                }
                select s.PointAt(s.Domain(0).ParameterAt(paramU), s.Domain(1).ParameterAt(paramV)),
            ],
            // ANALYTICAL - Centroid and structural points via Rhino SDK
            (ExtractionMethod.Analytical, GeometryBase g) => [
                .. (g switch {
                    Brep b when VolumeMassProperties.Compute(b)?.Centroid is { IsValid: true } c => [c],
                    Curve c when AreaMassProperties.Compute(c)?.Centroid is { IsValid: true } ct => [ct],
                    Surface s when AreaMassProperties.Compute(s)?.Centroid is { IsValid: true } cs => [cs],
                    Mesh m when m.Vertices.Count > 0 && VolumeMassProperties.Compute(m)?.Centroid is { IsValid: true } mc => [mc],
                    PointCloud pc when pc.Count > 0 => [pc.GetPoints().Aggregate(Point3d.Origin, (a, p) => a + p) / pc.Count],
                    _ => Enumerable.Empty<Point3d>(),
                }),
                .. (g switch {
                    NurbsCurve nc => nc.Points.Select(p => p.Location),
                    NurbsSurface ns => from i in Enumerable.Range(0, ns.Points.CountU * ns.Points.CountV)
                                       select ns.Points.GetControlPoint(i / ns.Points.CountV, i % ns.Points.CountV).Location,
                    Curve c => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd],
                    Surface s => [s.PointAt(s.Domain(0).Min, s.Domain(1).Min),
                        s.PointAt(s.Domain(0).Max, s.Domain(1).Min),
                        s.PointAt(s.Domain(0).Max, s.Domain(1).Max),
                        s.PointAt(s.Domain(0).Min, s.Domain(1).Max),
                    ],
                    Brep b => b.Vertices.Select(v => v.Location),
                    Mesh m => m.Vertices.ToPoint3dArray().AsEnumerable(),
                    PointCloud pc => pc.GetPoints().AsEnumerable(),
                    _ => [],
                }),
            ],
            // EXTREMAL - Boundary points via Rhino SDK
            (ExtractionMethod.Extremal, Curve c) => [c.PointAtStart, c.PointAtEnd],
            (ExtractionMethod.Extremal, Surface s) when s.Domain(0) is Interval u && s.Domain(1) is Interval v =>
                [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max)],
            (ExtractionMethod.Extremal, GeometryBase g) => g.GetBoundingBox(accurate: true).GetCorners(),
            // QUADRANT - Shape-specific points via Rhino SDK
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetCircle(out Circle circle, context.AbsoluteTolerance) =>
                [.. Enumerable.Range(0, 4).Select(i => circle.PointAt(i * Math.PI / 2))],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) =>
                [e.Center + (e.Plane.XAxis * e.Radius1),
                    e.Center + (e.Plane.YAxis * e.Radius2),
                    e.Center - (e.Plane.XAxis * e.Radius1),
                    e.Center - (e.Plane.YAxis * e.Radius2),
                ],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetPolyline(out Polyline polyline) => [.. polyline],
            (ExtractionMethod.Quadrant, Curve c) when c.IsLinear(context.AbsoluteTolerance) => [c.PointAtStart, c.PointAtEnd],
            _ => null,
        };
}

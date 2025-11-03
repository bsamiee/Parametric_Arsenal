using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Internal extraction algorithms with geometry-specific strategy dispatch and Rhino SDK integration.</summary>
internal static class ExtractionStrategies {
    /// <summary>Dispatches extraction methods with inline validation and automatic null-to-error mapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count = null, double? length = null, bool includeEnds = true) =>
        ResultFactory.Create(value: geometry)
            .ValidateGeometry(context, method switch {
                ExtractionMethod.Analytical => ValidationMode.Standard | (geometry switch {
                    Brep => ValidationMode.MassProperties,
                    Curve or Surface => ValidationMode.AreaCentroid,
                    _ => ValidationMode.None,
                }),
                ExtractionMethod.Uniform => ValidationMode.Standard | ValidationMode.Degeneracy,
                ExtractionMethod.Extremal => ValidationMode.BoundingBox,
                ExtractionMethod.Quadrant => ValidationMode.Tolerance,
                _ => ValidationMode.Standard,
            })
            .Bind(g => ExtractCore(g, method, context, count, length, includeEnds) switch {
                Point3d[] result when result?.Length > 0 =>
                    ResultFactory.Create(value: (IReadOnlyList<Point3d>)result.AsReadOnly()),
                // Rhino SDK returns null for invalid parameters - map to appropriate errors
                null when method == ExtractionMethod.Uniform && count is not null =>
                    ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount),
                null when method == ExtractionMethod.Uniform && length is not null =>
                    ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength),
                [] => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
            });

    /// <summary>Core extraction logic with geometry type detection and method-specific algorithm selection.</summary>
    [Pure]
    private static Point3d[]? ExtractCore(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count, double? length, bool includeEnds) =>
        (method, geometry) switch {
            // UNIFORM - Let Rhino SDK validate parameters by returning null for invalid inputs
            (ExtractionMethod.Uniform, Curve c) when count is { } n =>
                c.DivideByCount(n, includeEnds)?.Select(c.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Curve c) when length is { } len =>
                c.DivideByLength(len, includeEnds)?.Select(c.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Surface s) => [..
                from i in Enumerable.Range(0, count ?? 10)
                from j in Enumerable.Range(0, count ?? 10)
                let n = count ?? 10
                let u = s.Domain(0).ParameterAt(includeEnds && n == 1 ? 0d : includeEnds && n > 1 ? i / (double)(n - 1) : (i + 0.5) / n)
                let v = s.Domain(1).ParameterAt(includeEnds && n == 1 ? 0d : includeEnds && n > 1 ? j / (double)(n - 1) : (j + 0.5) / n)
                select s.PointAt(u, v),],

            // ANALYTICAL - Centroid + structural points merged
            (ExtractionMethod.Analytical, var g) => [
                ..(g switch {
                    Brep b when VolumeMassProperties.Compute(b)?.Centroid is { IsValid: true } c => [c],
                    Curve c when AreaMassProperties.Compute(c)?.Centroid is { IsValid: true } ct => [ct],
                    Surface s when AreaMassProperties.Compute(s)?.Centroid is { IsValid: true } cs => [cs],
                    Mesh m when m.Vertices.Count > 0 => [m.Vertices.ToPoint3dArray().Aggregate(Point3d.Origin, (a, p) => a + p) / m.Vertices.Count],
                    PointCloud pc when pc.Count > 0 => [pc.GetPoints().Aggregate(Point3d.Origin, (a, p) => a + p) / pc.Count],
                    _ => Enumerable.Empty<Point3d>(),
                }),
                ..(g switch {
                    NurbsCurve nc => nc.Points.Select(p => p.Location),
                    NurbsSurface ns => from i in Enumerable.Range(0, ns.Points.CountU * ns.Points.CountV)
                                        select ns.Points.GetControlPoint(i / ns.Points.CountV, i % ns.Points.CountV).Location,
                    Curve c => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd],
                    Surface s => [ s.PointAt(s.Domain(0).Min, s.Domain(1).Min), s.PointAt(s.Domain(0).Max, s.Domain(1).Min),
                                        s.PointAt(s.Domain(0).Max, s.Domain(1).Max), s.PointAt(s.Domain(0).Min, s.Domain(1).Max), ],
                    Brep b => b.Vertices.Select(v => v.Location),
                    Mesh m => m.Vertices.ToPoint3dArray().AsEnumerable(),
                    PointCloud pc => pc.GetPoints().AsEnumerable(),
                    _ => [],
                }),],

            // EXTREMAL - Boundary extraction
            (ExtractionMethod.Extremal, Curve c) => [c.PointAtStart, c.PointAtEnd],
            (ExtractionMethod.Extremal, Surface s) when s.Domain(0) is var u && s.Domain(1) is var v =>
                [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max)],
            (ExtractionMethod.Extremal, var g) => g.GetBoundingBox(accurate: true).GetCorners(),

            // QUADRANT - Shape-specific extraction
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetCircle(out Circle circle, context.AbsoluteTolerance) =>
                [.. Enumerable.Range(0, 4).Select(i => circle.PointAt(i * Math.PI / 2))],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) =>
                [e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2),
                 e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetPolyline(out Polyline polyline) => [.. polyline],
            (ExtractionMethod.Quadrant, Curve c) when c.IsLinear(context.AbsoluteTolerance) => [c.PointAtStart, c.PointAtEnd],

            _ => null,
        };
}

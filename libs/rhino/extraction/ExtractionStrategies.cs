using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Internal extraction algorithms with Rhino SDK geometry processing.</summary>
internal static class ExtractionStrategies {
    private static readonly FrozenDictionary<(ExtractionMethod Method, Type GeometryType), ValidationMode> _validationConfig =
        new Dictionary<(ExtractionMethod, Type), ValidationMode> {
            [(ExtractionMethod.Uniform, typeof(GeometryBase))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(ExtractionMethod.Analytical, typeof(GeometryBase))] = ValidationMode.Standard,
            [(ExtractionMethod.Analytical, typeof(Brep))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(ExtractionMethod.Analytical, typeof(Curve))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(ExtractionMethod.Analytical, typeof(Surface))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(ExtractionMethod.Extremal, typeof(GeometryBase))] = ValidationMode.BoundingBox,
            [(ExtractionMethod.Quadrant, typeof(GeometryBase))] = ValidationMode.Tolerance,
            [(ExtractionMethod.EdgeMidpoints, typeof(GeometryBase))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Extrusion))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(SubD))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Mesh))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Polyline))] = ValidationMode.Standard | ValidationMode.Topology,
        }.ToFrozenDictionary();

    /// <summary>Extracts points using specified method with validation and error mapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count = null, double? length = null, bool includeEnds = true) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context,
                (new Type?[] { geometry.GetType(), geometry.GetType().BaseType, geometry.GetType().BaseType?.BaseType, typeof(GeometryBase), })
                    .OfType<Type>()
                    .Select((Type candidate) => _validationConfig.TryGetValue((method, candidate), out ValidationMode resolved)
                        ? resolved
                        : (ValidationMode?)null)
                    .FirstOrDefault((ValidationMode? value) => value.HasValue,
                        _validationConfig.GetValueOrDefault((method, typeof(GeometryBase)), ValidationMode.Standard))
                    .GetValueOrDefault(ValidationMode.Standard),
            ])
            .Bind(g => ExtractCore(g, method, context, count, length, includeEnds) switch {
                Point3d[] { Length: > 0 } result => ResultFactory.Create(value: (IReadOnlyList<Point3d>)result.AsReadOnly()),
                null => ResultFactory.Create<IReadOnlyList<Point3d>>(error: (method, count, length) switch {
                    (ExtractionMethod.Uniform, not null, _) => ExtractionErrors.Operation.InvalidCount,
                    (ExtractionMethod.Uniform, _, not null) => ExtractionErrors.Operation.InvalidLength,
                    _ => ExtractionErrors.Operation.InvalidMethod,
                }),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
            });

    /// <summary>Core extraction logic with Rhino SDK geometry type dispatch.</summary>
    [Pure]
    private static Point3d[]? ExtractCore(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count, double? length, bool includeEnds) =>
        method switch {
            ExtractionMethod.Uniform => (geometry, count, length) switch {
                (Curve curve, int uniformCount, null) => curve.DivideByCount(uniformCount, includeEnds)?.Select(curve.PointAt).ToArray(),
                (Curve curve, null, double uniformLength) => curve.DivideByLength(uniformLength, includeEnds)?.Select(curve.PointAt).ToArray(),
                (Surface surface, _, _) => [.. from uIndex in Enumerable.Range(0, count ?? 10)
                                               from vIndex in Enumerable.Range(0, count ?? 10)
                                               let density = count ?? 10
                                               let uParameter = density switch { 1 => 0.5, _ => includeEnds ? uIndex / (double)(density - 1) : (uIndex + 0.5) / density }
                                               let vParameter = density switch { 1 => 0.5, _ => includeEnds ? vIndex / (double)(density - 1) : (vIndex + 0.5) / density }
                                               select surface.PointAt(surface.Domain(0).ParameterAt(uParameter), surface.Domain(1).ParameterAt(vParameter)),
                ],
                _ => null,
            },
            ExtractionMethod.Analytical => (geometry switch {
                Extrusion extrusion when extrusion.ToBrep(true) is Brep brep => (GeometryBase)brep,
                SubD subD when subD.ToBrep() is Brep brep => (GeometryBase)brep,
                _ => geometry,
            }) switch {
                GeometryBase analytic => (analytic switch {
                    Brep brep => ((IEnumerable<Point3d>)(VolumeMassProperties.Compute(brep)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)brep.Vertices.Select((BrepVertex vertex) => vertex.Location)),
                    NurbsCurve nurbsCurve => ((IEnumerable<Point3d>)(AreaMassProperties.Compute(nurbsCurve)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)nurbsCurve.Points.Select((ControlPoint point) => point.Location)),
                    Curve curve => ((IEnumerable<Point3d>)(AreaMassProperties.Compute(curve)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)new Point3d[] { curve.PointAtStart, curve.PointAt(curve.Domain.ParameterAt(0.5)), curve.PointAtEnd }),
                    NurbsSurface nurbsSurface => ((IEnumerable<Point3d>)(AreaMassProperties.Compute(nurbsSurface)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)from index in Enumerable.Range(0, nurbsSurface.Points.CountU * nurbsSurface.Points.CountV)
                        select nurbsSurface.Points.GetControlPoint(index / nurbsSurface.Points.CountV, index % nurbsSurface.Points.CountV).Location),
                    Surface surface => ((IEnumerable<Point3d>)(AreaMassProperties.Compute(surface)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)new Point3d[] {
                        surface.PointAt(surface.Domain(0).Min, surface.Domain(1).Min),
                        surface.PointAt(surface.Domain(0).Max, surface.Domain(1).Min),
                        surface.PointAt(surface.Domain(0).Max, surface.Domain(1).Max),
                        surface.PointAt(surface.Domain(0).Min, surface.Domain(1).Max),
                    }),
                    Mesh mesh => ((IEnumerable<Point3d>)(mesh.Vertices.Count > 0 && VolumeMassProperties.Compute(mesh)?.Centroid is { IsValid: true } centroid ? new Point3d[] { centroid } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)mesh.Vertices.ToPoint3dArray().AsEnumerable()),
                    PointCloud cloud => ((IEnumerable<Point3d>)(cloud.Count > 0 ? new Point3d[] { cloud.GetPoints().Aggregate(Point3d.Origin, (Point3d sum, Point3d point) => sum + point) / cloud.Count } : Array.Empty<Point3d>()), (IEnumerable<Point3d>)cloud.GetPoints().AsEnumerable()),
                    GeometryBase unsupported => ((IEnumerable<Point3d>)Array.Empty<Point3d>(), (IEnumerable<Point3d>)Array.Empty<Point3d>()),
                }) switch {
                    (IEnumerable<Point3d> centroid, IEnumerable<Point3d> elements) => [.. centroid, .. elements],
                },
            },
            ExtractionMethod.Extremal => geometry switch {
                Curve curve => new Point3d[] { curve.PointAtStart, curve.PointAtEnd },
                Surface surface when surface.Domain(0) is Interval intervalU && surface.Domain(1) is Interval intervalV =>
                    new Point3d[] {
                        surface.PointAt(intervalU.Min, intervalV.Min),
                        surface.PointAt(intervalU.Max, intervalV.Min),
                        surface.PointAt(intervalU.Max, intervalV.Max),
                        surface.PointAt(intervalU.Min, intervalV.Max),
                    },
                GeometryBase geometryBase => geometryBase.GetBoundingBox(accurate: true).GetCorners(),
                _ => null,
            },
            ExtractionMethod.Quadrant => geometry switch {
                Curve curve when curve.TryGetCircle(out Circle circle, context.AbsoluteTolerance) =>
                    [.. Enumerable.Range(0, 4).Select((int index) => circle.PointAt(index * Math.PI / 2))],
                Curve curve when curve.TryGetEllipse(out Ellipse ellipse, context.AbsoluteTolerance) =>
                    new Point3d[] {
                        ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1),
                        ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2),
                        ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1),
                        ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2),
                    },
                Curve curve when curve.TryGetPolyline(out Polyline polyline) => [.. polyline],
                Curve curve when curve.IsLinear(context.AbsoluteTolerance) => new Point3d[] { curve.PointAtStart, curve.PointAtEnd },
                _ => null,
            },
            ExtractionMethod.EdgeMidpoints => (geometry switch {
                Extrusion extrusion when extrusion.ToBrep(true) is Brep brep => (GeometryBase)brep,
                SubD subD when subD.ToBrep() is Brep brep => (GeometryBase)brep,
                _ => geometry,
            }) switch {
                Brep brep => [.. brep.Edges.Select((BrepEdge edge) => edge.TryGetNormalizedLengthParameter(0.5, out double parameter)
                    ? edge.PointAt(parameter)
                    : edge.PointAt(edge.Domain.ParameterAt(0.5)))],
                Mesh mesh => [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                    .Select((int index) => mesh.TopologyEdges.EdgeLine(index))
                    .Where((Line line) => line.IsValid)
                    .Select((Line line) => line.PointAt(0.5))],
                Curve curve => (curve.DuplicateSegments(), curve.TryGetPolyline(out Polyline polyline)) switch {
                    (Curve[] segments, _) when segments.Length > 0 => [.. segments.Select((Curve segment) => segment.PointAtNormalizedLength(0.5))],
                    (_, true) => [.. polyline.GetSegments().Where((Line line) => line.IsValid).Select((Line line) => line.PointAt(0.5))],
                    _ => null,
                },
                Polyline polyline => [.. polyline.GetSegments().Where((Line line) => line.IsValid).Select((Line line) => line.PointAt(0.5))],
                _ => null,
            },
            _ => null,
        };
}

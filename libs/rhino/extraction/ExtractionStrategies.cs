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
                geometry.GetType() switch {
                    Type type when _validationConfig.TryGetValue((method, type), out ValidationMode mode0) => mode0,
                    Type type when type.BaseType is Type baseType &&
                        _validationConfig.TryGetValue((method, baseType), out ValidationMode mode1) => mode1,
                    Type type when type.BaseType is Type baseType1 && baseType1.BaseType is Type baseType2 &&
                        _validationConfig.TryGetValue((method, baseType2), out ValidationMode mode2) => mode2,
                    _ => _validationConfig.GetValueOrDefault((method, typeof(GeometryBase)), ValidationMode.Standard),
                },
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
        IGeometryContext context, int? count, double? length, bool includeEnds)
    {
        GeometryBase normalized = (method, geometry) switch {
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, Extrusion extrusion) when extrusion.ToBrep(true) is Brep brep => brep,
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, SubD subD) when subD.ToBrep() is Brep brep => brep,
            _ => geometry,
        };

        return (method, normalized) switch {
            (ExtractionMethod.Uniform, Curve curve) when count is int uniformCount =>
                curve.DivideByCount(uniformCount, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Curve curve) when length is double uniformLength =>
                curve.DivideByLength(uniformLength, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Surface surface) =>
                [.. from uIndex in Enumerable.Range(0, count ?? 10)
                    from vIndex in Enumerable.Range(0, count ?? 10)
                    let density = count ?? 10
                    let uParameter = density switch { 1 => 0.5, _ => includeEnds ? uIndex / (double)(density - 1) : (uIndex + 0.5) / density }
                    let vParameter = density switch { 1 => 0.5, _ => includeEnds ? vIndex / (double)(density - 1) : (vIndex + 0.5) / density }
                    select surface.PointAt(surface.Domain(0).ParameterAt(uParameter), surface.Domain(1).ParameterAt(vParameter))],
            (ExtractionMethod.Uniform, _) => null,
            (ExtractionMethod.Analytical, GeometryBase analytic) => [..
                (analytic switch {
                    Brep brep when VolumeMassProperties.Compute(brep)?.Centroid is { IsValid: true } centroid => [centroid],
                    Curve curve when AreaMassProperties.Compute(curve)?.Centroid is { IsValid: true } centroid => [centroid],
                    Surface surface when AreaMassProperties.Compute(surface)?.Centroid is { IsValid: true } centroid => [centroid],
                    Mesh mesh when mesh.Vertices.Count > 0 && VolumeMassProperties.Compute(mesh)?.Centroid is { IsValid: true } centroid => [centroid],
                    PointCloud cloud when cloud.Count > 0 => [cloud.GetPoints().Aggregate(Point3d.Origin, (Point3d sum, Point3d point) => sum + point) / cloud.Count],
                    _ => Enumerable.Empty<Point3d>(),
                }),
                .. (analytic switch {
                    NurbsCurve nurbsCurve => nurbsCurve.Points.Select(point => point.Location),
                    NurbsSurface nurbsSurface => from index in Enumerable.Range(0, nurbsSurface.Points.CountU * nurbsSurface.Points.CountV)
                        select nurbsSurface.Points.GetControlPoint(index / nurbsSurface.Points.CountV, index % nurbsSurface.Points.CountV).Location,
                    Curve curve => [curve.PointAtStart, curve.PointAt(curve.Domain.ParameterAt(0.5)), curve.PointAtEnd],
                    Surface surface => [surface.PointAt(surface.Domain(0).Min, surface.Domain(1).Min),
                        surface.PointAt(surface.Domain(0).Max, surface.Domain(1).Min),
                        surface.PointAt(surface.Domain(0).Max, surface.Domain(1).Max),
                        surface.PointAt(surface.Domain(0).Min, surface.Domain(1).Max)],
                    Brep brep => brep.Vertices.Select(vertex => vertex.Location),
                    Mesh mesh => mesh.Vertices.ToPoint3dArray().AsEnumerable(),
                    PointCloud cloud => cloud.GetPoints().AsEnumerable(),
                    _ => Array.Empty<Point3d>(),
                })],
            (ExtractionMethod.Extremal, Curve curve) => [curve.PointAtStart, curve.PointAtEnd],
            (ExtractionMethod.Extremal, Surface surface) when surface.Domain(0) is Interval intervalU && surface.Domain(1) is Interval intervalV =>
                [surface.PointAt(intervalU.Min, intervalV.Min), surface.PointAt(intervalU.Max, intervalV.Min), surface.PointAt(intervalU.Max, intervalV.Max), surface.PointAt(intervalU.Min, intervalV.Max)],
            (ExtractionMethod.Extremal, GeometryBase geometryBase) => geometryBase.GetBoundingBox(accurate: true).GetCorners(),
            (ExtractionMethod.Quadrant, Curve curve) when curve.TryGetCircle(out Circle circle, context.AbsoluteTolerance) =>
                [.. Enumerable.Range(0, 4).Select(index => circle.PointAt(index * Math.PI / 2))],
            (ExtractionMethod.Quadrant, Curve curve) when curve.TryGetEllipse(out Ellipse ellipse, context.AbsoluteTolerance) =>
                [ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1),
                    ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2),
                    ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1),
                    ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2)],
            (ExtractionMethod.Quadrant, Curve curve) when curve.TryGetPolyline(out Polyline polyline) => [.. polyline],
            (ExtractionMethod.Quadrant, Curve curve) when curve.IsLinear(context.AbsoluteTolerance) => [curve.PointAtStart, curve.PointAtEnd],
            (ExtractionMethod.Quadrant, _) => null,
            (ExtractionMethod.EdgeMidpoints, Brep brep) => [.. brep.Edges.Select(edge => edge.TryGetNormalizedLengthParameter(0.5, out double parameter)
                ? edge.PointAt(parameter)
                : edge.PointAt(edge.Domain.ParameterAt(0.5)))],
            (ExtractionMethod.EdgeMidpoints, Mesh mesh) => [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
                .Select(index => mesh.TopologyEdges.EdgeLine(index))
                .Where(line => line.IsValid)
                .Select(line => line.PointAt(0.5))],
            (ExtractionMethod.EdgeMidpoints, Curve curve) => curve.DuplicateSegments() switch {
                Curve[] segments when segments.Length > 0 => [.. segments.Select(segment => segment.PointAtNormalizedLength(0.5))],
                _ => curve.TryGetPolyline(out Polyline polyline)
                    ? [.. polyline.GetSegments().Where(line => line.IsValid).Select(line => line.PointAt(0.5))]
                    : null,
            },
            (ExtractionMethod.EdgeMidpoints, _) => null,
            _ => null,
        };
    }
}

using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic analysis engine with unified operation dispatch.</summary>
public static class AnalysisEngine {
    /// <summary>Analyzes geometry with method-based dispatch producing comprehensive evaluation data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<(
        Point3d Point,
        Vector3d[] Derivatives,
        (Plane Primary, Plane[]? Perpendicular, double[]? InflectionParams, double[]? MaxCurvatureParams, double? Torsion)? Frames,
        (double? Gaussian, double? Mean, double? K1, double? K2, Vector3d? Dir1, Vector3d? Dir2)? Curvature,
        (double[]? Parameters, Continuity[]? Types)? Discontinuities,
        ((int Index, Point3d Location)[]? Vertices, (int Index, Line Geometry)[]? Edges, bool? IsManifold, bool? IsClosed)? Topology,
        (Point3d? Closest, ComponentIndex? Component, double? Distance, (double, double)? SurfaceUV)? Proximity,
        (bool AtSeam, bool AtSingularity, Point3d[]? SeamPoints)? Singularities,
        (double? Length, double? Area, double? Volume, Point3d? Centroid)? Metrics,
        Interval[]? Domains,
        (double? Curve, (double, double)? Surface, int? Mesh, int DerivOrder) Params)>> Analyze<T>(
        T input,
        AnalysisMethod method,
        IGeometryContext context,
        (double? Curve, (double, double)? Surface, int? Mesh)? parameters = null,
        int derivativeOrder = 2) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (Func<object, Result<IReadOnlyList<(Point3d, Vector3d[],
                (Plane, Plane[]?, double[]?, double[]?, double?)?,
                (double?, double?, double?, double?, Vector3d?, Vector3d?)?,
                (double[]?, Continuity[]?)?,
                ((int, Point3d)[]?, (int, Line)[]?, bool?, bool?)?,
                (Point3d?, ComponentIndex?, double?, (double, double)?)?,
                (bool, bool, Point3d[]?)?,
                (double?, double?, double?, Point3d?)?,
                Interval[]?,
                (double?, (double, double)?, int?, int))>>>)(item =>
                AnalysisStrategies.Analyze(item, method, context, parameters, derivativeOrder)
                    .Map(results => (IReadOnlyList<(Point3d, Vector3d[],
                        (Plane, Plane[]?, double[]?, double[]?, double?)?,
                        (double?, double?, double?, double?, Vector3d?, Vector3d?)?,
                        (double[]?, Continuity[]?)?,
                        ((int, Point3d)[]?, (int, Line)[]?, bool?, bool?)?,
                        (Point3d?, ComponentIndex?, double?, (double, double)?)?,
                        (bool, bool, Point3d[]?)?,
                        (double?, double?, double?, Point3d?)?,
                        Interval[]?,
                        (double?, (double, double)?, int?, int))>)[.. results.Select(r =>
                        (r.Point, r.Derivatives, r.Frames, r.Curvature, r.Discontinuities, r.Topology, r.Proximity, r.Singularities, r.Metrics, r.Domains, r.Params)),
                        ])),
            new OperationConfig<object, (Point3d, Vector3d[],
                (Plane, Plane[]?, double[]?, double[]?, double?)?,
                (double?, double?, double?, double?, Vector3d?, Vector3d?)?,
                (double[]?, Continuity[]?)?,
                ((int, Point3d)[]?, (int, Line)[]?, bool?, bool?)?,
                (Point3d?, ComponentIndex?, double?, (double, double)?)?,
                (bool, bool, Point3d[]?)?,
                (double?, double?, double?, Point3d?)?,
                Interval[]?,
                (double?, (double, double)?, int?, int))> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = false,
                EnableCache = true,
            });
}

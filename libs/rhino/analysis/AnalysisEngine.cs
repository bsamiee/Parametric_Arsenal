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
    /// <summary>Analyzes geometry producing evaluation data with derivatives, curvature, and metrics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<(
        Point3d Point, Vector3d[] Derivatives, Plane Frame,
        (double? Gaussian, double? Mean, double? Min, double? Max, Vector3d? MinDir, Vector3d? MaxDir, Vector3d? Curve)? Curvature,
        (double? Length, double? Area, double? Volume)? Metrics,
        Interval[]? Domains,
        (Vector3d[]? TangentBasis, Vector3d? Normal, Plane? OrientationFrame)? Orientation,
        (double? CurveParam, (double, double)? SurfaceParams, int? MeshIndex, int DerivativeOrder) EvaluatedParams)>> Analyze<T>(
        T input,
        IGeometryContext context,
        double? curveParameter = null,
        (double, double)? surfaceParameters = null,
        int? meshIndex = null,
        int derivativeOrder = 2,
        bool includeMetrics = true,
        bool includeDomains = true,
        bool includeOrientation = true) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (Func<object, Result<IReadOnlyList<(Point3d, Vector3d[], Plane,
                (double?, double?, double?, double?, Vector3d?, Vector3d?, Vector3d?)?,
                (double?, double?, double?)?, Interval[]?,
                (Vector3d[]?, Vector3d?, Plane?)?,
                (double?, (double, double)?, int?, int))>>>)(item =>
                AnalysisStrategies.Analyze(item, context, curveParameter, surfaceParameters,
                    meshIndex, derivativeOrder, includeMetrics, includeDomains, includeOrientation)
                    .Map(results => (IReadOnlyList<(Point3d, Vector3d[], Plane,
                        (double?, double?, double?, double?, Vector3d?, Vector3d?, Vector3d?)?,
                        (double?, double?, double?)?, Interval[]?,
                        (Vector3d[]?, Vector3d?, Plane?)?,
                        (double?, (double, double)?, int?, int))>)[.. results.Select(r =>
                        (r.Point, r.Derivatives, r.Frame, r.Curvature, r.Metrics, r.Domains, r.Orientation, r.EvaluatedParams)),
                        ])),
            new OperationConfig<object, (Point3d, Vector3d[], Plane,
                (double?, double?, double?, double?, Vector3d?, Vector3d?, Vector3d?)?,
                (double?, double?, double?)?, Interval[]?,
                (Vector3d[]?, Vector3d?, Plane?)?,
                (double?, (double, double)?, int?, int))> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = false,
            });
}

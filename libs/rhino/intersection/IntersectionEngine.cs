using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with RhinoCommon Intersect SDK integration and unified operation dispatch.</summary>
public static class IntersectionEngine {
    /// <summary>Performs intersection operations using RhinoCommon Intersect algorithms with tolerance-aware geometry processing and batch operation support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(IReadOnlyList<Point3d> Points, IReadOnlyList<Curve>? Curves, IReadOnlyList<double>? ParametersA, IReadOnlyList<double>? ParametersB, IReadOnlyList<int>? FaceIndices, IReadOnlyList<Polyline>? Sections)> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null,
        Vector3d? projectionDirection = null,
        int? maxHitCount = null) where T1 : notnull where T2 : notnull =>
        (geometryA, geometryB) switch {
            (GeometryBase ga, GeometryBase gb) => IntersectionStrategies.Intersect(ga, gb, method, context, tolerance, projectionDirection, maxHitCount)
                .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
            (System.Collections.IEnumerable collA, GeometryBase gb) when collA is not string and collA is not GeometryBase =>
                UnifiedOperation.Apply(
                    collA.Cast<object>(),
                    (Func<object, Result<IntersectionResult>>)(item => IntersectionStrategies.Intersect(item, gb, method, context, tolerance, projectionDirection, maxHitCount)),
                    new OperationConfig<object, IntersectionResult> { Context = context, ValidationMode = ValidationMode.None, AccumulateErrors = true, })
                .Map(results => (
                    (IReadOnlyList<Point3d>)[.. results.SelectMany(r => r.Points)],
                    results.Any(r => r.Curves is not null) ? (IReadOnlyList<Curve>?)[.. results.SelectMany(r => r.Curves ?? [])] : null,
                    results.Any(r => r.ParametersA is not null) ? (IReadOnlyList<double>?)[.. results.SelectMany(r => r.ParametersA ?? [])] : null,
                    results.Any(r => r.ParametersB is not null) ? (IReadOnlyList<double>?)[.. results.SelectMany(r => r.ParametersB ?? [])] : null,
                    results.Any(r => r.FaceIndices is not null) ? (IReadOnlyList<int>?)[.. results.SelectMany(r => r.FaceIndices ?? [])] : null,
                    results.Any(r => r.Sections is not null) ? (IReadOnlyList<Polyline>?)[.. results.SelectMany(r => r.Sections ?? [])] : null)),
            (GeometryBase ga, System.Collections.IEnumerable collB) when collB is not string and collB is not GeometryBase =>
                UnifiedOperation.Apply(
                    collB.Cast<object>(),
                    (Func<object, Result<IntersectionResult>>)(item => IntersectionStrategies.Intersect(ga, item, method, context, tolerance, projectionDirection, maxHitCount)),
                    new OperationConfig<object, IntersectionResult> { Context = context, ValidationMode = ValidationMode.None, AccumulateErrors = true, })
                .Map(results => (
                    (IReadOnlyList<Point3d>)[.. results.SelectMany(r => r.Points)],
                    results.Any(r => r.Curves is not null) ? (IReadOnlyList<Curve>?)[.. results.SelectMany(r => r.Curves ?? [])] : null,
                    results.Any(r => r.ParametersA is not null) ? (IReadOnlyList<double>?)[.. results.SelectMany(r => r.ParametersA ?? [])] : null,
                    results.Any(r => r.ParametersB is not null) ? (IReadOnlyList<double>?)[.. results.SelectMany(r => r.ParametersB ?? [])] : null,
                    results.Any(r => r.FaceIndices is not null) ? (IReadOnlyList<int>?)[.. results.SelectMany(r => r.FaceIndices ?? [])] : null,
                    results.Any(r => r.Sections is not null) ? (IReadOnlyList<Polyline>?)[.. results.SelectMany(r => r.Sections ?? [])] : null)),
            (Point3d[] points, GeometryBase[] targets) when (method & (IntersectionMethod.ProjectPointsToBreps | IntersectionMethod.ProjectPointsToMeshes | IntersectionMethod.ProjectPointsToBrepsEx | IntersectionMethod.ProjectPointsToMeshesEx)) != IntersectionMethod.None =>
                IntersectionStrategies.Intersect(points, targets, method, context, tolerance, projectionDirection, maxHitCount)
                    .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
            _ => IntersectionStrategies.Intersect(geometryA, geometryB, method, context, tolerance, projectionDirection, maxHitCount)
                .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
        };
}

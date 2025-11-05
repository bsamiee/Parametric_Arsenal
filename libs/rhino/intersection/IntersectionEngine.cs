using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with RhinoCommon Intersect SDK integration.</summary>
public static class IntersectionEngine {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(IReadOnlyList<Point3d> Points, IReadOnlyList<Curve>? Curves, IReadOnlyList<double>? ParametersA, IReadOnlyList<double>? ParametersB, IReadOnlyList<int>? FaceIndices, IReadOnlyList<Polyline>? Sections)> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null) where T1 : notnull where T2 : notnull =>
        IntersectionStrategies.Intersect(geometryA, geometryB, method, context, tolerance)
            .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections));
}

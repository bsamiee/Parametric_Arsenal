using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection with automatic type-based dispatch.</summary>
public static class Intersect {
    /// <summary>Intersection options: tolerance, projection, output format.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOptions(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool WithIndices = false,
        bool Sorted = false);

    /// <summary>Intersection result: points, curves, parameters, indices.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Empty result with zero-length collections.</summary>
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }
    /// <summary>Type-detected intersection with validation and collection handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionOptions? options = null,
        bool enableDiagnostics = false) where T1 : notnull where T2 : notnull {
        IntersectionOptions opts = options ?? new();
        (Type t1, Type t2) = (typeof(T1), typeof(T2));

        return IntersectionCore.NormalizeOptions(opts, context)
            .Bind(normalized => UnifiedOperation.Apply(
                geometryA,
                (Func<object, Result<IReadOnlyList<IntersectionOutput>>>)(item => IntersectionCore.ExecuteWithOptions(
                        item,
                        (object)geometryB,
                        context,
                        normalized)
                    .Map(output => (IReadOnlyList<IntersectionOutput>)[output])),
                new OperationConfig<object, IntersectionOutput> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = true,
                    OperationName = $"Intersect.{t1.Name}.{t2.Name}",
                    EnableDiagnostics = enableDiagnostics,
                }))
        .Map(outputs => outputs.Count switch {
            0 => IntersectionOutput.Empty,
            _ => new IntersectionOutput(
                [.. outputs.SelectMany(output => output.Points)],
                [.. outputs.SelectMany(output => output.Curves)],
                [.. outputs.SelectMany(output => output.ParametersA)],
                [.. outputs.SelectMany(output => output.ParametersB)],
                [.. outputs.SelectMany(output => output.FaceIndices)],
                [.. outputs.SelectMany(output => output.Sections)]),
        });
    }

    /// <summary>Classify intersection: 0=tangent, 1=transverse, 2=unknown with approach angles.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> ClassifyIntersection(
        IntersectionOutput output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.Classify(output: output, geomA: geometryA, geomB: geometryB, context: context);

    /// <summary>Find near-misses within tolerance band: (pointsA[], pointsB[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] LocationsA, Point3d[] LocationsB, double[] Distances)> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCompute.FindNearMisses(geomA: geometryA, geomB: geometryB, searchRadius: searchRadius, context: context);

    /// <summary>Analyze stability via perturbation: (score, sensitivity, unstable flags[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double StabilityScore, double PerturbationSensitivity, bool[] UnstableFlags)> AnalyzeStability(
        IntersectionOutput baseIntersection,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.AnalyzeStability(geomA: geometryA, geomB: geometryB, baseOutput: baseIntersection, context: context);
}

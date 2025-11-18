using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with unified FrozenDictionary following SpatialCore pattern.</summary>
[Pure]
internal static class FieldsCore {
    /// <summary>Distance field operation metadata keyed by compile-time geometry type.</summary>
    internal static readonly FrozenDictionary<Type, (Func<GeometryBase, Fields.FieldSampling, IGeometryContext, Result<(Point3d[], double[])>> Execute, V ValidationMode)> DistanceOperations =
        new Dictionary<Type, (Func<GeometryBase, Fields.FieldSampling, IGeometryContext, Result<(Point3d[], double[])>>, V)> {
            [typeof(Mesh)] = (
                static (geometry, sampling, context) => ExecuteDistanceField(
                    (Mesh)geometry,
                    sampling,
                    context,
                    FieldsConfig.DistanceFieldMeshBuffer),
                V.Standard | V.MeshSpecific),
            [typeof(Brep)] = (
                static (geometry, sampling, context) => ExecuteDistanceField(
                    (Brep)geometry,
                    sampling,
                    context,
                    FieldsConfig.DistanceFieldBrepBuffer),
                V.Standard | V.Topology),
            [typeof(Curve)] = (
                static (geometry, sampling, context) => ExecuteDistanceField(
                    (Curve)geometry,
                    sampling,
                    context,
                    FieldsConfig.DistanceFieldCurveBuffer),
                V.Standard | V.Degeneracy),
            [typeof(Surface)] = (
                static (geometry, sampling, context) => ExecuteDistanceField(
                    (Surface)geometry,
                    sampling,
                    context,
                    FieldsConfig.DistanceFieldSurfaceBuffer),
                V.Standard | V.BoundingBox),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context) =>
        geometry switch {
            null => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null")),
            _ when !TryGetDistanceOperation(geometry.GetType(), out (Func<GeometryBase, Fields.FieldSampling, IGeometryContext, Result<(Point3d[], double[])>> Execute, V ValidationMode) entry) =>
                ResultFactory.Create<(Point3d[], double[])>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}")),
            _ => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<(Point3d[], double[])>>)(item => entry.Execute(item, sampling, context)),
                    config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = entry.ValidationMode,
                        OperationName = FieldsConfig.OperationNames.DistanceField,
                        EnableDiagnostics = false,
                    })
                .Bind(results => results.Count switch {
                    0 => ResultFactory.Create<(Point3d[], double[])>(
                        error: E.Geometry.UnsupportedAnalysis.WithContext("Distance field produced no samples")),
                    _ => ResultFactory.Create(value: results[0]),
                }),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetDistanceOperation(
        Type geometryType,
        out (Func<GeometryBase, Fields.FieldSampling, IGeometryContext, Result<(Point3d[], double[])>> Execute, V ValidationMode) entry) {
        Type? current = geometryType;
        while (current is not null) {
            if (DistanceOperations.TryGetValue(current, out entry)) {
                return true;
            }
            current = current.BaseType;
        }

        entry = default;
        return false;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        T geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context,
        int bufferSize) where T : GeometryBase =>
        ((Func<Result<(Point3d[], double[])>>)(() => {
            BoundingBox bounds = sampling.Bounds ?? geometry.GetBoundingBox(accurate: true);
            if (!bounds.IsValid) {
                return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
            }
            int resolution = sampling.Resolution;
            int totalSamples = resolution * resolution * resolution;
            int capacity = Math.Max(totalSamples, bufferSize);
            Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(capacity);
            double[] distances = ArrayPool<double>.Shared.Rent(capacity);
            try {
                Vector3d delta = (bounds.Max - bounds.Min) / (resolution - 1);
                int gridIndex = 0;
                for (int i = 0; i < resolution; i++) {
                    for (int j = 0; j < resolution; j++) {
                        for (int k = 0; k < resolution; k++) {
                            grid[gridIndex++] = new Point3d(bounds.Min.X + (i * delta.X), bounds.Min.Y + (j * delta.Y), bounds.Min.Z + (k * delta.Z));
                        }
                    }
                }
                for (int i = 0; i < totalSamples; i++) {
                    Point3d closest = geometry switch {
                        Mesh m => m.ClosestPoint(grid[i]),
                        Brep b => b.ClosestPoint(grid[i]),
                        Curve c => c.ClosestPoint(grid[i], out double t) ? c.PointAt(t) : grid[i],
                        Surface s => s.ClosestPoint(grid[i], out double u, out double v) ? s.PointAt(u, v) : grid[i],
                        _ => grid[i],
                    };
                    double unsignedDist = grid[i].DistanceTo(closest);
                    bool inside = geometry switch {
                        Brep brep => brep.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance * FieldsConfig.InsideOutsideToleranceMultiplier, strictlyIn: false),
                        Mesh mesh when mesh.IsClosed => mesh.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance, strictlyIn: false),
                        _ => false,
                    };
                    distances[i] = inside ? -unsignedDist : unsignedDist;
                }
                Point3d[] finalGrid = [.. grid[..totalSamples]];
                double[] finalDistances = [.. distances[..totalSamples]];
                return ResultFactory.Create(value: (Grid: finalGrid, Distances: finalDistances));
            } finally {
                ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
                ArrayPool<double>.Shared.Return(distances, clearArray: true);
            }
        }))();
}

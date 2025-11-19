using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with UnifiedOperation integration.</summary>
[Pure]
internal static class FieldsCore {
    private sealed record DistanceOperationMetadata(
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<IReadOnlyList<(Point3d[], double[])>>> Executor,
        FieldsConfig.DistanceFieldMetadata Metadata);

    private static readonly FrozenDictionary<Type, DistanceOperationMetadata> DistanceDispatch =
        FieldsConfig.DistanceFields
            .ToDictionary(
                keySelector: static entry => entry.Key,
                elementSelector: static entry => new DistanceOperationMetadata(
                    Executor: entry.Key == typeof(Mesh)
                        ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                        : entry.Key == typeof(Brep)
                            ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                            : entry.Key == typeof(Curve)
                                ? static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,])
                                : static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bufferSize, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result,]),
                    Metadata: entry.Value))
            .ToFrozenDictionary();

    [Pure]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        IGeometryContext context) =>
        geometry is null
            ? ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null"))
            : !DistanceDispatch.TryGetValue(geometry.GetType(), out DistanceOperationMetadata? metadata)
                ? ResultFactory.Create<(Point3d[], double[])>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"))
                : UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<(Point3d[], double[])>>>)(item => metadata.Executor(item, sampling, metadata.Metadata.BufferSize, context)),
                    config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = metadata.Metadata.ValidationMode,
                        OperationName = metadata.Metadata.OperationName,
                        EnableDiagnostics = false,
                    }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int bufferSize,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
        BoundingBox bounds = sampling.Bounds ?? typed.GetBoundingBox(accurate: true);
        if (!bounds.IsValid) {
            return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
        }
        int resolution = sampling.Resolution;
        int totalSamples = resolution * resolution * resolution;
        int actualBufferSize = Math.Max(totalSamples, bufferSize);
        Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(actualBufferSize);
        double[] distances = ArrayPool<double>.Shared.Rent(actualBufferSize);
        try {
            Vector3d delta = (bounds.Max - bounds.Min) / (resolution - 1);
            int gridIndex = 0;
            for (int i = 0; i < resolution; i++) {
                for (int j = 0; j < resolution; j++) {
                    for (int k = 0; k < resolution; k++) {
                        grid[gridIndex++] = new(bounds.Min.X + (i * delta.X), bounds.Min.Y + (j * delta.Y), bounds.Min.Z + (k * delta.Z));
                    }
                }
            }
            for (int i = 0; i < totalSamples; i++) {
                Point3d closest = typed switch {
                    Mesh m => m.ClosestPoint(grid[i]),
                    Brep b => b.ClosestPoint(grid[i]),
                    Curve c => c.ClosestPoint(grid[i], out double t) ? c.PointAt(t) : grid[i],
                    Surface s => s.ClosestPoint(grid[i], out double u, out double v) ? s.PointAt(u, v) : grid[i],
                    _ => grid[i],
                };
                double unsignedDist = grid[i].DistanceTo(closest);
                bool inside = typed switch {
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
    }
}

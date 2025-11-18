using System;
using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields orchestration layer bridging public API and compute implementations.</summary>
[Pure]
internal static class FieldsCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        Fields.FieldSpec spec,
        IGeometryContext context) where T : GeometryBase {
        Type runtimeType = geometry.GetType();
        return FieldsConfig.DistanceOperationMetadataByType.TryGetValue(runtimeType, out FieldsConfig.DistanceOperationMetadata metadata) switch {
            false => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {runtimeType.Name}")),
            true => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<(Point3d[], double[])>>)(item => ExecuteDistanceField(
                        geometry: item,
                        spec: spec,
                        context: context,
                        configuredBufferSize: metadata.BufferSize)),
                    config: new OperationConfig<T, (Point3d[], double[])> {
                        Context = context,
                        ValidationMode = metadata.ValidationMode,
                        OperationName = FieldsConfig.OperationNames.DistanceField,
                        EnableDiagnostics = false,
                    })
                .Bind(results => results.Count switch {
                    0 => ResultFactory.Create<(Point3d[], double[])>(
                        error: E.Geometry.UnsupportedAnalysis.WithContext("Distance field produced no samples")),
                    _ => ResultFactory.Create(value: results[0]),
                }),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        T geometry,
        Fields.FieldSpec spec,
        IGeometryContext context,
        int configuredBufferSize) where T : GeometryBase =>
        ((Func<Result<(Point3d[], double[])>>)(() => {
            BoundingBox bounds = spec.Bounds ?? geometry.GetBoundingBox(accurate: true);
            if (!bounds.IsValid) {
                return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
            }
            int resolution = spec.Resolution;
            int totalSamples = resolution * resolution * resolution;
            int bufferSize = Math.Max(totalSamples, configuredBufferSize);
            Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(bufferSize);
            double[] distances = ArrayPool<double>.Shared.Rent(bufferSize);
            try {
                Vector3d delta = (bounds.Max - bounds.Min) / (resolution - 1);
                int gridIndex = 0;
                for (int i = 0; i < resolution; i++) {
                    for (int j = 0; j < resolution; j++) {
                        for (int k = 0; k < resolution; k++) {
                            grid[gridIndex++] = new Point3d(
                                bounds.Min.X + (i * delta.X),
                                bounds.Min.Y + (j * delta.Y),
                                bounds.Min.Z + (k * delta.Z));
                        }
                    }
                }
                for (int i = 0; i < totalSamples; i++) {
                    Point3d closest = geometry switch {
                        Mesh mesh => mesh.ClosestPoint(grid[i]),
                        Brep brep => brep.ClosestPoint(grid[i]),
                        Curve curve => curve.ClosestPoint(grid[i], out double t) ? curve.PointAt(t) : grid[i],
                        Surface surface => surface.ClosestPoint(grid[i], out double u, out double v) ? surface.PointAt(u, v) : grid[i],
                        _ => grid[i],
                    };
                    double unsignedDist = grid[i].DistanceTo(closest);
                    bool inside = geometry switch {
                        Brep brepGeometry => brepGeometry.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance * FieldsConfig.InsideOutsideToleranceMultiplier, strictlyIn: false),
                        Mesh meshGeometry when meshGeometry.IsClosed => meshGeometry.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance, strictlyIn: false),
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

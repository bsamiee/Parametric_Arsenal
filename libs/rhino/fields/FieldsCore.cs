using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with unified FrozenDictionary following SpatialCore pattern.</summary>
[Pure]
internal static class FieldsCore {
    /// <summary>Unified operation dispatch table: (operation, geometry type) → (execute function, validation mode, buffer size, integration method).</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), (Func<object, Fields.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, V ValidationMode, int BufferSize, byte IntegrationMethod)> OperationRegistry =
        new Dictionary<(byte, Type), (Func<object, Fields.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>>, V, int, byte)> {
            [(FieldsConfig.OperationDistance, typeof(Mesh))] = (ExecuteDistanceField<Mesh>, V.Standard | V.MeshSpecific, 4096, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Brep))] = (ExecuteDistanceField<Brep>, V.Standard | V.Topology, 8192, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Curve))] = (ExecuteDistanceField<Curve>, V.Standard | V.Degeneracy, 2048, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Surface))] = (ExecuteDistanceField<Surface>, V.Standard | V.BoundingBox, 4096, FieldsConfig.IntegrationRK4),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        object geometry,
        Fields.FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        ((Func<Result<(Point3d[], double[])>>)(() => {
            T typed = (T)geometry;
            BoundingBox bounds = spec.Bounds ?? typed.GetBoundingBox(accurate: true);
            if (!bounds.IsValid) {
                return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
            }
            int resolution = spec.Resolution;
            int totalSamples = resolution * resolution * resolution;
            int bufferSize = OperationRegistry.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out (Func<object, Fields.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, V ValidationMode, int BufferSize, byte IntegrationMethod) config)
                ? Math.Max(totalSamples, config.BufferSize)
                : totalSamples;
            Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(bufferSize);
            double[] distances = ArrayPool<double>.Shared.Rent(bufferSize);
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
        }))();
}

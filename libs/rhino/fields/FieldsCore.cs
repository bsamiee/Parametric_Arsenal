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

/// <summary>Fields dispatch orchestration bridging public API with compute routines.</summary>
[Pure]
internal static class FieldsCore {
    private sealed record DistanceOperationEntry(V ValidationMode, int BufferSize);

    internal static readonly FrozenDictionary<Type, DistanceOperationEntry> DistanceOperations =
        new Dictionary<Type, DistanceOperationEntry> {
            [typeof(Mesh)] = new(V.Standard | V.MeshSpecific, 4096),
            [typeof(Brep)] = new(V.Standard | V.Topology, 8192),
            [typeof(Curve)] = new(V.Standard | V.Degeneracy, 2048),
            [typeof(Surface)] = new(V.Standard | V.BoundingBox, 4096),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField(
        GeometryBase geometry,
        Fields.FieldSpec spec,
        IGeometryContext context) =>
        DistanceOperations.TryGetValue(geometry.GetType(), out DistanceOperationEntry? metadata)
            ? UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<GeometryBase, Result<(Point3d[], double[])>>)(item => ExecuteDistanceField(
                    geometry: item,
                    spec: spec,
                    context: context,
                    baseBufferSize: metadata.BufferSize)),
                config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = FieldsConfig.DistanceOperationName,
                }).Map(result => result[0])
            : ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField(
        GeometryBase geometry,
        Fields.FieldSpec spec,
        IGeometryContext context,
        int baseBufferSize) =>
        ((Func<Result<(Point3d[], double[])>>)(() => {
            BoundingBox bounds = spec.Bounds ?? geometry.GetBoundingBox(accurate: true);
            if (!bounds.IsValid) {
                return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
            }
            int resolution = spec.Resolution;
            int totalSamples = resolution * resolution * resolution;
            int bufferSize = baseBufferSize > totalSamples ? baseBufferSize : totalSamples;
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
                        Curve curve when curve.ClosestPoint(grid[i], out double t) => curve.PointAt(t),
                        Surface surface when surface.ClosestPoint(grid[i], out double u, out double v) => surface.PointAt(u, v),
                        _ => grid[i],
                    };
                    double unsignedDistance = grid[i].DistanceTo(closest);
                    bool inside = geometry switch {
                        Brep brep => brep.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance * FieldsConfig.InsideOutsideToleranceMultiplier, strictlyIn: false),
                        Mesh mesh when mesh.IsClosed => mesh.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance, strictlyIn: false),
                        _ => false,
                    };
                    distances[i] = inside ? -unsignedDistance : unsignedDistance;
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

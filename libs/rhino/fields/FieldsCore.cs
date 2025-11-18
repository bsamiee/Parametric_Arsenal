using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry wired through UnifiedOperation.</summary>
[Pure]
internal static class FieldsCore {
    private sealed record DistanceOperationMetadata(
        Func<GeometryBase, Fields.FieldSampling, int, IGeometryContext, Result<(Point3d[], double[])>> Executor,
        V ValidationMode,
        string OperationName,
        int BufferSize);

    private static readonly FrozenDictionary<Type, DistanceOperationMetadata> DistanceDispatch =
        new Dictionary<Type, DistanceOperationMetadata> {
            [typeof(Mesh)] = new DistanceOperationMetadata(
                Executor: static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Mesh>(geometry, sampling, bufferSize, context),
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: FieldsConfig.OperationNames.MeshDistance,
                BufferSize: FieldsConfig.MeshDistanceBuffer),
            [typeof(Brep)] = new DistanceOperationMetadata(
                Executor: static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Brep>(geometry, sampling, bufferSize, context),
                ValidationMode: V.Standard | V.Topology,
                OperationName: FieldsConfig.OperationNames.BrepDistance,
                BufferSize: FieldsConfig.BrepDistanceBuffer),
            [typeof(Curve)] = new DistanceOperationMetadata(
                Executor: static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Curve>(geometry, sampling, bufferSize, context),
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: FieldsConfig.OperationNames.CurveDistance,
                BufferSize: FieldsConfig.CurveDistanceBuffer),
            [typeof(Surface)] = new DistanceOperationMetadata(
                Executor: static (geometry, sampling, bufferSize, context) => ExecuteDistanceField<Surface>(geometry, sampling, bufferSize, context),
                ValidationMode: V.Standard | V.BoundingBox,
                OperationName: FieldsConfig.OperationNames.SurfaceDistance,
                BufferSize: FieldsConfig.SurfaceDistanceBuffer),
        ,}.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Distances)> DistanceField(
        Fields.DistanceFieldRequest request,
        IGeometryContext context) =>
        request switch {
            null => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Request cannot be null")),
            { Geometry: null } => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Geometry cannot be null")),
            { Sampling: null } => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Sampling specification cannot be null")),
            _ => DispatchDistance(request: request, context: context),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[] Grid, double[] Distances)> DispatchDistance(
        Fields.DistanceFieldRequest request,
        IGeometryContext context) {
        GeometryBase geometry = request.Geometry;
        return DistanceDispatch.TryGetValue(geometry.GetType(), out DistanceOperationMetadata metadata)
            ? UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<GeometryBase, Result<(Point3d[], double[])>>)(item => metadata.Executor(item, request.Sampling, metadata.BufferSize, context)),
                config: new OperationConfig<GeometryBase, (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                }).Map(results => results[0])
            : ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {geometry.GetType().Name}"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        GeometryBase geometry,
        Fields.FieldSampling sampling,
        int preferredBuffer,
        IGeometryContext context) where T : GeometryBase =>
        ((Func<Result<(Point3d[], double[])>>)(() => {
            T typed = (T)geometry;
            BoundingBox bounds = sampling.Bounds ?? typed.GetBoundingBox(accurate: true);
            if (!bounds.IsValid) {
                return ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds);
            }

            int resolution = sampling.Resolution;
            int totalSamples = resolution * resolution * resolution;
            int bufferSize = Math.Max(totalSamples, preferredBuffer);
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
                    Point3d closest = typed switch {
                        Mesh mesh => mesh.ClosestPoint(grid[i]),
                        Brep brep => brep.ClosestPoint(grid[i]),
                        Curve curve => curve.ClosestPoint(grid[i], out double t) ? curve.PointAt(t) : grid[i],
                        Surface surface => surface.ClosestPoint(grid[i], out double u, out double v) ? surface.PointAt(u, v) : grid[i],
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

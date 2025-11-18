using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry with unified FrozenDictionary for distance field operations.</summary>
[Pure]
internal static class FieldsCore {
    /// <summary>Entry for distance field operations with execution function and validation mode.</summary>
    internal readonly record struct DistanceFieldEntry(
        Func<GeometryBase, Fields.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute,
        V ValidationMode,
        int BufferSize);

    /// <summary>Distance field operation dispatch table: geometry type → execution entry.</summary>
    internal static readonly FrozenDictionary<Type, DistanceFieldEntry> OperationRegistry =
        new Dictionary<Type, DistanceFieldEntry> {
            [typeof(Mesh)] = new(
                Execute: (geometry, spec, context) => ExecuteDistanceField<Mesh>((Mesh)geometry, spec, context),
                ValidationMode: V.Standard | V.MeshSpecific,
                BufferSize: 4096),
            [typeof(Brep)] = new(
                Execute: (geometry, spec, context) => ExecuteDistanceField<Brep>((Brep)geometry, spec, context),
                ValidationMode: V.Standard | V.Topology,
                BufferSize: 8192),
            [typeof(Curve)] = new(
                Execute: (geometry, spec, context) => ExecuteDistanceField<Curve>((Curve)geometry, spec, context),
                ValidationMode: V.Standard | V.Degeneracy,
                BufferSize: 2048),
            [typeof(Surface)] = new(
                Execute: (geometry, spec, context) => ExecuteDistanceField<Surface>((Surface)geometry, spec, context),
                ValidationMode: V.Standard | V.BoundingBox,
                BufferSize: 4096),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        T geometry,
        Fields.FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        OperationRegistry.TryGetValue(typeof(T), out DistanceFieldEntry entry) switch {
            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {typeof(T).Name}")),
            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                SystemError[] validationErrors = entry.ValidationMode != V.None
                    ? ValidationRules.GetOrCompileValidator(typeof(T), entry.ValidationMode)(geometry, context)
                    : [];
                return validationErrors.Length > 0
                    ? ResultFactory.Create<(Point3d[], double[])>(errors: validationErrors)
                    : ((Func<Result<(Point3d[], double[])>>)(() => {
                        BoundingBox bounds = spec.Bounds ?? geometry.GetBoundingBox(accurate: true);
                        return bounds.IsValid switch {
                            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds),
                            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                    int resolution = spec.Resolution;
                    int totalSamples = resolution * resolution * resolution;
                    int bufferSize = OperationRegistry.TryGetValue(typeof(T), out DistanceFieldEntry config)
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
                                    grid[gridIndex++] = new Point3d(
                                        bounds.Min.X + (i * delta.X),
                                        bounds.Min.Y + (j * delta.Y),
                                        bounds.Min.Z + (k * delta.Z));
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
                                Brep brep => brep.IsPointInside(
                                    grid[i],
                                    tolerance: context.AbsoluteTolerance * FieldsConfig.InsideOutsideToleranceMultiplier,
                                    strictlyIn: false),
                                Mesh mesh when mesh.IsClosed => mesh.IsPointInside(
                                    grid[i],
                                    tolerance: context.AbsoluteTolerance,
                                    strictlyIn: false),
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
                            }))(),
                        };
                    }))(),
            };
        }))(),
        };
}

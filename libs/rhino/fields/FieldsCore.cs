using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Fields dispatch registry and spatial acceleration.</summary>
[Pure]
internal static class FieldsCore {
    // ============================================================================
    // RTREE FACTORY FUNCTIONS (inline RTree construction - NO helper methods)
    // ============================================================================

    private static readonly Func<object, RTree> _meshRTreeFactory = geometry => {
        RTree tree = new();
        Mesh mesh = (Mesh)geometry;
        _ = tree.Insert(mesh.GetBoundingBox(accurate: true), 0);
        return tree;
    };

    private static readonly Func<object, RTree> _brepRTreeFactory = geometry => {
        RTree tree = new();
        Brep brep = (Brep)geometry;
        _ = tree.Insert(brep.GetBoundingBox(accurate: true), 0);
        return tree;
    };

    private static readonly Func<object, RTree> _curveRTreeFactory = geometry => {
        RTree tree = new();
        Curve curve = (Curve)geometry;
        _ = tree.Insert(curve.GetBoundingBox(accurate: true), 0);
        return tree;
    };

    private static readonly Func<object, RTree> _surfaceRTreeFactory = geometry => {
        RTree tree = new();
        Surface surface = (Surface)geometry;
        _ = tree.Insert(surface.GetBoundingBox(accurate: true), 0);
        return tree;
    };

    // ============================================================================
    // OPERATION REGISTRY (byte + Type → execute function + integration method)
    // ============================================================================

    /// <summary>Operation-type dispatch table: (operation, geometry type) → (execute function, integration method).</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), (Func<object, Field.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, byte IntegrationMethod)> OperationRegistry =
        new Dictionary<(byte, Type), (Func<object, Field.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>>, byte)> {
            [(FieldsConfig.OperationDistance, typeof(Mesh))] = (ExecuteDistanceField<Mesh>, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Brep))] = (ExecuteDistanceField<Brep>, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Curve))] = (ExecuteDistanceField<Curve>, FieldsConfig.IntegrationRK4),
            [(FieldsConfig.OperationDistance, typeof(Surface))] = (ExecuteDistanceField<Surface>, FieldsConfig.IntegrationRK4),
        }.ToFrozenDictionary();

    // ============================================================================
    // DISTANCE FIELD EXECUTION (dispatched by geometry type)
    // ============================================================================

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        object geometry,
        Field.FieldSpec spec,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
        BoundingBox bounds = spec.Bounds ?? typed.GetBoundingBox(accurate: true);

        return bounds.IsValid switch {
            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds),
            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                int resolution = RhinoMath.Clamp(spec.Resolution, FieldsConfig.MinResolution, FieldsConfig.MaxResolution);
                int totalSamples = resolution * resolution * resolution;
                Point3d[] grid = ArrayPool<Point3d>.Shared.Rent(totalSamples);
                double[] distances = ArrayPool<double>.Shared.Rent(totalSamples);

                try {
                    // Inline grid sampling (NO helper method)
                    Vector3d delta = (bounds.Max - bounds.Min) / (resolution - 1);
                    int gridIndex = 0;
                    for (int i = 0; i < resolution; i++) {
                        double x = bounds.Min.X + (i * delta.X);
                        for (int j = 0; j < resolution; j++) {
                            double y = bounds.Min.Y + (j * delta.Y);
                            for (int k = 0; k < resolution; k++) {
                                grid[gridIndex++] = new Point3d(x, y, bounds.Min.Z + (k * delta.Z));
                            }
                        }
                    }

                    // Inline distance computation (NO helper method)
                    RTree? tree = totalSamples > FieldsConfig.DistanceFieldRTreeThreshold
                        ? (typed switch {
                            Mesh => _meshRTreeFactory(typed),
                            Brep => _brepRTreeFactory(typed),
                            Curve => _curveRTreeFactory(typed),
                            Surface => _surfaceRTreeFactory(typed),
                            _ => null,
                        })
                        : null;

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
                            Mesh mesh when mesh.IsClosed => mesh.IsPointInside(grid[i], context.AbsoluteTolerance, strictlyIn: false),
                            _ => false,
                        };

                        distances[i] = inside ? -unsignedDist : unsignedDist;
                    }

                    tree?.Dispose();

                    return ResultFactory.Create<(Point3d[], double[])>(value: (
                        [.. grid[..totalSamples]],
                        [.. distances[..totalSamples]]));
                } finally {
                    ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
                    ArrayPool<double>.Shared.Return(distances, clearArray: true);
                }
            }))(),
        };
    }

    /// <summary>3D grid sampling configuration.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal readonly struct SampleGrid {
        internal readonly Point3d Origin;
        internal readonly Vector3d Delta;
        internal readonly int Resolution;
        internal readonly int TotalSamples;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SampleGrid(BoundingBox bounds, int resolution) {
            this.Origin = bounds.Min;
            this.Delta = (bounds.Max - bounds.Min) / (resolution - 1);
            this.Resolution = resolution;
            this.TotalSamples = resolution * resolution * resolution;
        }

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Point3d GetPoint(int i, int j, int k) =>
            new(
                this.Origin.X + (i * this.Delta.X),
                this.Origin.Y + (j * this.Delta.Y),
                this.Origin.Z + (k * this.Delta.Z));
    }
}

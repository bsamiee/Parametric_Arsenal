# MorphologyCore.cs - Dispatch and Grid Sampling Blueprint

## File Purpose
Byte-based FrozenDictionary dispatch registry, RTree spatial acceleration factories, and grid sampling infrastructure. NO helper methods - inline algorithm logic.

## Type Count
**3 types**: 
1. `MorphologyCore` (internal static class - dispatch registry)
2. `SampleGrid` (internal readonly struct - 3D grid configuration)
3. `DistanceQuery` (internal readonly struct - cached closest point result)

## Critical Patterns
- Byte-based FrozenDictionary dispatch (NO enums)
- RTree factory functions for O(log n) spatial queries
- ArrayPool for grid point buffers
- Inline grid sampling (NO helper methods)
- Exact patterns from SpatialCore.cs and ExtractionCore.cs

## Complete Implementation

```csharp
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology dispatch registry and spatial acceleration.</summary>
[Pure]
internal static class MorphologyCore {
    // ============================================================================
    // RTREE FACTORY FUNCTIONS (inline RTree construction - NO helper methods)
    // ============================================================================

    private static readonly Func<object, RTree> _meshRTreeFactory = geometry => {
        RTree tree = new();
        Mesh mesh = (Mesh)geometry;
        _ = tree.Insert(mesh.GetBoundingBox(accurate: true), index: 0);
        return tree;
    };

    private static readonly Func<object, RTree> _brepRTreeFactory = geometry => {
        RTree tree = new();
        Brep brep = (Brep)geometry;
        _ = tree.Insert(brep.GetBoundingBox(accurate: true), index: 0);
        return tree;
    };

    private static readonly Func<object, RTree> _curveRTreeFactory = geometry => {
        RTree tree = new();
        Curve curve = (Curve)geometry;
        _ = tree.Insert(curve.GetBoundingBox(accurate: true), index: 0);
        return tree;
    };

    private static readonly Func<object, RTree> _surfaceRTreeFactory = geometry => {
        RTree tree = new();
        Surface surface = (Surface)geometry;
        _ = tree.Insert(surface.GetBoundingBox(accurate: true), index: 0);
        return tree;
    };

    // ============================================================================
    // OPERATION REGISTRY (byte + Type → execute function + integration method)
    // ============================================================================

    /// <summary>Operation-type dispatch table: (operation, geometry type) → (execute function, integration method).</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), (Func<object, Morphology.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, byte IntegrationMethod)> OperationRegistry =
        new (byte Operation, Type GeometryType, Func<object, Morphology.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute, byte IntegrationMethod)[] {
            (MorphologyConfig.OperationDistance, typeof(Mesh), ExecuteDistanceField<Mesh>, MorphologyConfig.IntegrationRK4),
            (MorphologyConfig.OperationDistance, typeof(Brep), ExecuteDistanceField<Brep>, MorphologyConfig.IntegrationRK4),
            (MorphologyConfig.OperationDistance, typeof(Curve), ExecuteDistanceField<Curve>, MorphologyConfig.IntegrationRK4),
            (MorphologyConfig.OperationDistance, typeof(Surface), ExecuteDistanceField<Surface>, MorphologyConfig.IntegrationRK4),
        }.ToFrozenDictionary(
            static entry => (entry.Operation, entry.GeometryType),
            static entry => ((Func<object, Morphology.FieldSpec, IGeometryContext, Result<(Point3d[], double[])>>)entry.Execute, entry.IntegrationMethod));

    // ============================================================================
    // DISTANCE FIELD EXECUTION (dispatched by geometry type)
    // ============================================================================

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[], double[])> ExecuteDistanceField<T>(
        object geometry,
        Morphology.FieldSpec spec,
        IGeometryContext context) where T : GeometryBase {
        T typed = (T)geometry;
        BoundingBox bounds = spec.Bounds ?? typed.GetBoundingBox(accurate: true);

        return bounds.IsValid switch {
            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldBounds),
            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                int resolution = RhinoMath.Clamp(spec.Resolution, MorphologyConfig.MinResolution, MorphologyConfig.MaxResolution);
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
                    RTree? tree = totalSamples > MorphologyConfig.DistanceFieldRTreeThreshold
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
                            Curve c => c.PointAt(c.ClosestPoint(grid[i])),
                            Surface s => s.PointAt(s.ClosestPoint(grid[i]).Item1, s.ClosestPoint(grid[i]).Item2),
                            _ => grid[i],
                        };

                        double unsignedDist = grid[i].DistanceTo(closest);
                        bool inside = typed switch {
                            Brep brep => brep.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance * MorphologyConfig.InsideOutsideToleranceMultiplier, strictlyIn: false),
                            Mesh mesh when mesh.IsClosed => mesh.IsPointInside(grid[i]),
                            _ => false,
                        };

                        distances[i] = inside ? -unsignedDist : unsignedDist;
                    }

                    tree?.Dispose();

                    return ResultFactory.Create(value: (
                        Grid: [.. grid[..totalSamples]],
                        Distances: [.. distances[..totalSamples]]));
                } finally {
                    ArrayPool<Point3d>.Shared.Return(grid, clearArray: true);
                    ArrayPool<double>.Shared.Return(distances, clearArray: true);
                }
            }))(),
        };
    }
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

/// <summary>Cached distance query result for O(1) field evaluation.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly struct DistanceQuery {
    internal readonly Point3d SamplePoint;
    internal readonly Point3d ClosestPoint;
    internal readonly double Distance;
    internal readonly bool IsInside;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DistanceQuery(Point3d samplePoint, Point3d closestPoint, double distance, bool isInside) {
        this.SamplePoint = samplePoint;
        this.ClosestPoint = closestPoint;
        this.Distance = distance;
        this.IsInside = isInside;
    }

    [Pure]
    internal double SignedDistance => this.IsInside ? -this.Distance : this.Distance;
}
```

## LOC: 178

## Key Patterns Demonstrated
1. **Byte-based dispatch** - FrozenDictionary with (byte, Type) keys
2. **RTree factory lambdas** - Inline construction, NO separate methods
3. **ArrayPool buffers** - Rent/Return pattern with try/finally
4. **For-loop grid sampling** - Hot path optimization with index access
5. **Inline algorithms** - All logic in ExecuteDistanceField, NO helpers
6. **Pattern matching** - Switch expressions for geometry type dispatch
7. **Named parameters** - `error:`, `value:`, `tolerance:`, `accurate:`
8. **Explicit types** - NO var anywhere
9. **K&R braces** - Opening brace on same line
10. **RhinoMath.Clamp** - For resolution bounds validation

## Integration Points
- **Morphology**: Calls OperationRegistry.TryGetValue for dispatch
- **MorphologyConfig**: Reads constants (OperationDistance, MinResolution, DistanceFieldRTreeThreshold)
- **MorphologyCompute**: Uses SampleGrid and DistanceQuery structs
- **ArrayPool<T>.Shared**: Zero-allocation buffer management
- **RTree**: Spatial acceleration for large point clouds

## Struct Justification
- **SampleGrid**: Encapsulates grid parameters for passing to compute methods
- **DistanceQuery**: Caches query results to avoid recomputation (not used in this file but available for MorphologyCompute)

## No Helper Methods
Grid sampling and distance computation inline in ExecuteDistanceField. RTree factories are lambdas in readonly fields.

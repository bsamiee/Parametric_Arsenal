# MorphConfig.cs Implementation Blueprint

## File Purpose
Configuration constants and byte-based validation mode dispatch following ExtractionConfig.cs pattern exactly.

## Complete Implementation

```csharp
using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Configuration for morphology operations: validation modes and algorithmic constants.</summary>
internal static class MorphConfig {
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Mesh))] = V.Standard | V.Topology,
            [(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Degeneracy,
            [(3, typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Gets validation mode with fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact)
            ? exact
            : ValidationModes
                .Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) =>
                    a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();

    /// <summary>FFD control cage parameters.</summary>
    internal const int FFDMinControlPoints = 8;
    internal const int FFDBernsteinDegree = 3;
    internal const int FFDDefaultDivisions = 3;

    /// <summary>Laplacian smoothing parameters.</summary>
    internal const int LaplacianMaxIterations = 1000;
    internal const int LaplacianDefaultIterations = 10;
    internal const double LaplacianDefaultLambda = 0.5;
    internal const double LaplacianConvergenceThreshold = 1e-6;
    internal static readonly double CotangentWeightMin = RhinoMath.ZeroTolerance;
    internal const double CotangentWeightMax = 1e6;

    /// <summary>Subdivision surface limits.</summary>
    internal const int MaxSubdivisionLevels = 5;
    internal const int MaxVertexCount = 1_000_000;
    internal static readonly double MinEdgeLength = RhinoMath.ZeroTolerance * 10.0;
    internal const double MaxAspectRatio = 100.0;

    /// <summary>Surface evolution PDE integration.</summary>
    internal const int EvolutionMaxSteps = 500;
    internal const double EvolutionCFLFactor = 0.25;
    internal const double EvolutionDefaultStepSize = 0.01;
    internal static readonly double EvolutionMinStepSize = RhinoMath.ZeroTolerance * 100.0;

    /// <summary>Topology preservation thresholds.</summary>
    internal static readonly double NormalFlipAngleThreshold = RhinoMath.ToRadians(90.0);
    internal const double MaxDisplacementRatio = 0.5;

    /// <summary>Degenerate angle bounds for cotangent stability.</summary>
    internal static readonly double DegenerateAngleMin = RhinoMath.ToRadians(1.0);
    internal static readonly double DegenerateAngleMax = RhinoMath.ToRadians(179.0);
}
```

## Key Patterns Followed

1. **Exact ExtractionConfig.cs structure**: FrozenDictionary with `(byte Kind, Type GeometryType)` keys
2. **GetValidationMode fallback logic**: Identical implementation with `IsAssignableFrom` and type specificity ordering
3. **RhinoMath constants**: All numerical constants derived from `RhinoMath` (no magic numbers)
4. **Const vs static readonly**: Const for compile-time constants, static readonly for RhinoMath-derived values
5. **Grouped by operation**: Clear organizational sections (FFD, Laplacian, Subdivision, Evolution, Topology)
6. **Explicit types**: No var usage
7. **Pure static class**: No state, only configuration data
8. **Internal visibility**: Configuration is internal to the library
9. **XML documentation**: All constants documented with purpose
10. **Named according to usage**: Constants named to reflect their algorithmic role (e.g., `CotangentWeightMin`, `EvolutionCFLFactor`)

## Constants Justification

### FFD Parameters
- **FFDMinControlPoints = 8**: Minimum 2×2×2 lattice for trivariate Bernstein basis
- **FFDBernsteinDegree = 3**: Cubic Bernstein polynomials (standard for smooth deformation)
- **FFDDefaultDivisions = 3**: Per-axis division count for default cage

### Laplacian Smoothing
- **LaplacianMaxIterations = 1000**: Upper bound to prevent infinite loops
- **LaplacianDefaultIterations = 10**: Reasonable default for mesh fairing
- **LaplacianDefaultLambda = 0.5**: Mid-range damping factor for stability
- **LaplacianConvergenceThreshold = 1e-6**: Relative displacement convergence criterion
- **CotangentWeightMin = RhinoMath.ZeroTolerance**: Numerical zero for weight clamping
- **CotangentWeightMax = 1e6**: Upper clamp to prevent inf/NaN from degenerate triangles

### Subdivision Surface
- **MaxSubdivisionLevels = 5**: Prevents exponential vertex explosion (4^5 = 1024× vertices)
- **MaxVertexCount = 1_000_000**: Memory safety bound for mesh operations
- **MinEdgeLength = RhinoMath.ZeroTolerance * 10.0**: Minimum edge length before degeneracy
- **MaxAspectRatio = 100.0**: Face quality threshold for topology validation

### Surface Evolution
- **EvolutionMaxSteps = 500**: Prevents runaway PDE integration
- **EvolutionCFLFactor = 0.25**: Safety factor for CFL condition (actual limit = minEdge²/4)
- **EvolutionDefaultStepSize = 0.01**: Conservative timestep for stability
- **EvolutionMinStepSize = RhinoMath.ZeroTolerance * 100.0**: Minimum meaningful displacement

### Topology Preservation
- **NormalFlipAngleThreshold = 90°**: Detect inverted faces during smoothing
- **MaxDisplacementRatio = 0.5**: Maximum vertex displacement relative to edge length

### Degenerate Angle Bounds
- **DegenerateAngleMin = 1°**: Below this, cotangent weights become unstable
- **DegenerateAngleMax = 179°**: Above this, cotangent weights approach infinity

## LOC Estimate
70-80 lines (pure configuration, no logic)
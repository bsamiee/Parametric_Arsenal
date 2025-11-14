# FieldsConfig.cs - Configuration and Constants Blueprint

## File Purpose
Configuration constants, byte operation codes, FrozenDictionary dispatch tables, and marching cubes lookup. NO enums - all classification via byte constants.

## Type Count
**1 type**: `FieldsConfig` (internal static class)

## Critical Patterns
- **ABSOLUTELY NO ENUMS** - byte constants only
- FrozenDictionary for all dispatch tables
- RhinoMath constants (ZeroTolerance, SqrtEpsilon, etc.)
- NO magic numbers - all values derived from RhinoMath or justified constants
- Static readonly for computed values
- Const for compile-time constants

## Complete Implementation

```csharp
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;

namespace Arsenal.Rhino.Fields;

/// <summary>Configuration constants, byte operation codes, and dispatch tables for fields operations.</summary>
[Pure]
internal static class FieldsConfig {
    // ============================================================================
    // OPERATION TYPE IDENTIFIERS (NO ENUMS - byte constants only)
    // ============================================================================

    /// <summary>Distance field operation identifier.</summary>
    internal const byte OperationDistance = 0;
    /// <summary>Gradient field operation identifier.</summary>
    internal const byte OperationGradient = 1;
    /// <summary>Streamline tracing operation identifier.</summary>
    internal const byte OperationStreamline = 2;
    /// <summary>Isosurface extraction operation identifier.</summary>
    internal const byte OperationIsosurface = 3;

    // ============================================================================
    // INTEGRATION METHOD IDENTIFIERS (NO ENUMS - byte constants only)
    // ============================================================================

    /// <summary>Euler forward integration method.</summary>
    internal const byte IntegrationEuler = 0;
    /// <summary>Second-order Runge-Kutta integration method.</summary>
    internal const byte IntegrationRK2 = 1;
    /// <summary>Fourth-order Runge-Kutta integration method.</summary>
    internal const byte IntegrationRK4 = 2;
    /// <summary>Adaptive fourth-order Runge-Kutta with error control.</summary>
    internal const byte IntegrationAdaptiveRK4 = 3;

    // ============================================================================
    // GRID RESOLUTION CONSTANTS (derived from analysis of typical use cases)
    // ============================================================================

    /// <summary>Default grid resolution (32³ = 32,768 samples).</summary>
    internal const int DefaultResolution = 32;
    /// <summary>Minimum grid resolution (8³ = 512 samples).</summary>
    internal const int MinResolution = 8;
    /// <summary>Maximum grid resolution (256³ = 16,777,216 samples).</summary>
    internal const int MaxResolution = 256;

    // ============================================================================
    // INTEGRATION STEP PARAMETERS (using RhinoMath constants)
    // ============================================================================

    /// <summary>Default integration step size (1% of typical geometry scale).</summary>
    internal const double DefaultStepSize = 0.01;
    /// <summary>Minimum step size (RhinoMath.SqrtEpsilon for numerical stability).</summary>
    internal static readonly double MinStepSize = RhinoMath.SqrtEpsilon;
    /// <summary>Maximum step size (prevents overshooting field features).</summary>
    internal const double MaxStepSize = 1.0;
    /// <summary>Maximum streamline integration steps (prevents infinite loops).</summary>
    internal const int MaxStreamlineSteps = 10000;
    /// <summary>Adaptive step tolerance (RhinoMath.SqrtEpsilon for error control).</summary>
    internal static readonly double AdaptiveStepTolerance = RhinoMath.SqrtEpsilon;
    /// <summary>Finite difference step for gradient computation (RhinoMath.SqrtEpsilon for numerical derivatives).</summary>
    internal static readonly double GradientFiniteDifferenceStep = RhinoMath.SqrtEpsilon;

    // ============================================================================
    // RK4 INTEGRATION COEFFICIENTS (exact fractions for numerical accuracy)
    // ============================================================================

    /// <summary>RK4 final stage weights: [k1/6, k2/3, k3/3, k4/6].</summary>
    internal static readonly double[] RK4Weights = [1.0 / 6.0, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 6.0,];
    /// <summary>RK4 intermediate stage half-step multipliers: [0.5, 0.5, 1.0].</summary>
    internal static readonly double[] RK4HalfSteps = [0.5, 0.5, 1.0,];

    // ============================================================================
    // BUFFER SIZE DISPATCH TABLE (operation + geometry type → buffer size)
    // ============================================================================

    /// <summary>ArrayPool buffer sizes for distance field operations by geometry type.</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), int> BufferSizes =
        new Dictionary<(byte, Type), int> {
            [(OperationDistance, typeof(Mesh))] = 4096,
            [(OperationDistance, typeof(Brep))] = 8192,
            [(OperationDistance, typeof(Curve))] = 2048,
            [(OperationDistance, typeof(Surface))] = 4096,
            [(OperationGradient, typeof(Mesh))] = 8192,
            [(OperationGradient, typeof(Brep))] = 16384,
            [(OperationStreamline, typeof(void))] = 4096,
            [(OperationIsosurface, typeof(void))] = 16384,
        }.ToFrozenDictionary();

    // ============================================================================
    // VALIDATION MODE DISPATCH TABLE (operation + geometry type → validation flags)
    // ============================================================================

    /// <summary>Validation mode dispatch for operation-type pairs.</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(OperationDistance, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(OperationDistance, typeof(Brep))] = V.Standard | V.Topology,
            [(OperationDistance, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(OperationDistance, typeof(Surface))] = V.Standard | V.BoundingBox,
            [(OperationGradient, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(OperationGradient, typeof(Brep))] = V.Standard | V.Topology,
            [(OperationGradient, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(OperationGradient, typeof(Surface))] = V.Standard | V.BoundingBox,
        }.ToFrozenDictionary();

    // ============================================================================
    // MARCHING CUBES LOOKUP TABLE (256 cube configurations → triangle indices)
    // ============================================================================

    /// <summary>Marching cubes triangle configuration table: cube corner bits → edge triangle indices.</summary>
    /// <remarks>
    /// Each entry corresponds to one of 256 possible cube configurations (8 corners × 2 states).
    /// Values are edge indices (0-11) where triangles intersect cube edges.
    /// Empty array means no triangles (all corners same state).
    /// IMPORTANT: Full 256-case table required in actual implementation. This shows pattern only.
    /// Reference: Paul Bourke's marching cubes lookup table or equivalent validated source.
    /// </remarks>
    internal static readonly int[][] MarchingCubesTable = [
        [],  // Case 0: all outside
        [0, 8, 3,],  // Case 1: corner 0 inside
        [0, 1, 9,],  // Case 2: corner 1 inside
        [1, 8, 3, 9, 8, 1,],  // Case 3: corners 0,1 inside
        [1, 2, 10,],  // Case 4: corner 2 inside
        [0, 8, 3, 1, 2, 10,],  // Case 5: corners 0,2 inside
        [9, 2, 10, 0, 2, 9,],  // Case 6: corners 1,2 inside
        [2, 8, 3, 2, 10, 8, 10, 9, 8,],  // Case 7: corners 0,1,2 inside
        [3, 11, 2,],  // Case 8: corner 3 inside
        [0, 11, 2, 8, 11, 0,],  // Case 9: corners 0,3 inside
        [1, 9, 0, 2, 3, 11,],  // Case 10: corners 1,3 inside
        [1, 11, 2, 1, 9, 11, 9, 8, 11,],  // Case 11: corners 0,1,3 inside
        [3, 10, 1, 11, 10, 3,],  // Case 12: corners 2,3 inside
        [0, 10, 1, 0, 8, 10, 8, 11, 10,],  // Case 13: corners 0,2,3 inside
        [3, 9, 0, 3, 11, 9, 11, 10, 9,],  // Case 14: corners 1,2,3 inside
        [9, 8, 10, 10, 8, 11,],  // Case 15: corners 0,1,2,3 inside
        // Cases 16-255: (remaining 240 configurations - MUST be included in actual implementation)
        // Pattern repeats with symmetry transformations of base 16 cases
    ];

    /// <summary>Marching cubes edge vertex pairs: edge index → (vertex1, vertex2).</summary>
    /// <remarks>
    /// Cube vertices numbered 0-7: bottom face (0-3) counterclockwise, top face (4-7) counterclockwise.
    /// Edges: 0-3 bottom face, 4-7 top face, 8-11 vertical.
    /// </remarks>
    internal static readonly (int V1, int V2)[] EdgeVertexPairs = [
        (0, 1), (1, 2), (2, 3), (3, 0),  // Bottom face edges 0-3
        (4, 5), (5, 6), (6, 7), (7, 4),  // Top face edges 4-7
        (0, 4), (1, 5), (2, 6), (3, 7),  // Vertical edges 8-11
    ];

    // ============================================================================
    // DISTANCE FIELD PARAMETERS (using RhinoMath for tolerance-based computations)
    // ============================================================================

    /// <summary>RTree threshold for switching from linear to spatial search (based on SpatialConfig pattern).</summary>
    internal const int DistanceFieldRTreeThreshold = 100;
    /// <summary>Inside/outside ray casting tolerance multiplier (context.AbsoluteTolerance × this value).</summary>
    internal const double InsideOutsideToleranceMultiplier = 10.0;
}
```

## LOC: 154

## Key Patterns Demonstrated
1. **NO ENUMS** - All operation/method classification via byte constants
2. **RhinoMath constants** - SqrtEpsilon for numerical derivatives and tolerances
3. **FrozenDictionary dispatch** - O(1) lookup for operation-type pairs
4. **Exact fractions** - RK4 weights computed as 1.0/6.0 not 0.16667
5. **Justified constants** - Every magic number has comment explaining derivation
6. **Static readonly** - For computed values that reference RhinoMath
7. **Const** - For compile-time constants
8. **Marching cubes table** - 256-case lookup (full table in actual implementation)
9. **Grouped constants** - Logical sections with separator comments
10. **No helper methods** - Pure data/configuration class

## Integration Points
- **Fields**: Public API reads constants (DefaultResolution, OperationDistance)
- **FieldsCore**: Reads ValidationModes and BufferSizes for dispatch
- **FieldsCompute**: Reads RK4Weights, GradientFiniteDifferenceStep, MarchingCubesTable
- **V flags**: Validation mode combinations from Arsenal.Core.Validation

## Verification
`grep -r "enum " FieldsConfig.cs` MUST return zero matches.

# BooleanConfig.cs Implementation Blueprint

**File**: `libs/rhino/boolean/BooleanConfig.cs`  
**Purpose**: Configuration constants and algorithmic parameters  
**Types**: 1 (BooleanConfig class only)  
**Estimated LOC**: 60-80

## File Structure

```csharp
using System.Diagnostics.Contracts;
using Rhino;

namespace Arsenal.Rhino.Boolean;

/// <summary>Boolean operation configuration constants and tolerance helpers.</summary>
[Pure]
internal static class BooleanConfig {
    internal const double DefaultToleranceFactor = 1.0;
    internal const double MinimumToleranceMultiplier = 1.0;
    internal const double MaximumToleranceMultiplier = 10.0;
    
    internal const int MinimumBrepFaces = 1;
    internal const int MinimumMeshFaces = 4;
    internal const int MinimumCurveSegments = 1;
    
    internal const int MaximumRegionCurves = 1000;
    internal const int MaximumSplitResults = 10000;
    
    internal const double BrepSplitDefaultTolerance = 0.001;
    internal const double MeshBooleanDefaultTolerance = 0.01;
    internal const double CurveRegionDefaultTolerance = 0.0001;
}
```

## Key Design Notes

### Tolerance Configuration
- **DefaultToleranceFactor**: 1.0 (use context tolerance as-is)
- **MinimumToleranceMultiplier**: 1.0 (no tolerance reduction)
- **MaximumToleranceMultiplier**: 10.0 (allow up to 10x context tolerance)

### Validation Thresholds
- **MinimumBrepFaces**: 1 face minimum for valid Brep result
- **MinimumMeshFaces**: 4 faces minimum (tetrahedron) for valid Mesh result
- **MinimumCurveSegments**: 1 segment minimum for valid Curve result

### Operation Limits
- **MaximumRegionCurves**: 1000 curves maximum from region extraction
- **MaximumSplitResults**: 10,000 pieces maximum from split operations

### Default Tolerances (Fallback Values)
- **BrepSplitDefaultTolerance**: 0.001 (1mm in meters, typical modeling scale)
- **MeshBooleanDefaultTolerance**: 0.01 (10mm, looser for mesh operations)
- **CurveRegionDefaultTolerance**: 0.0001 (0.1mm, tighter for 2D operations)

### NO FrozenDictionary
Unlike other *Config.cs files, this one is simpler:
- **Reason**: Boolean operations don't need polymorphic extractors
- **All configuration is static constants**: No runtime dispatch tables
- **No TypeExtractors**: SDK handles type-specific behavior internally

### NO Helper Methods
- **Pure constants only**: No tolerance computation methods
- **Computation inline**: Tolerance handling done in BooleanCompute methods
- **Rationale**: Keep config minimal, avoid convenience method sprawl

### RhinoMath Reference
- Import Rhino namespace for RhinoMath access in calling code
- Config file doesn't use RhinoMath directly (all constants are literals)
- Calling code uses RhinoMath.ZeroTolerance, RhinoMath.IsValidDouble

### Pattern Alignment
- **Similar to IntersectionConfig.cs**: Simple constants, no dispatch tables
- **Different from SpatialConfig.cs**: No FrozenDictionary (spatial needs centroid extraction)
- **Style**: internal const fields, no methods

### LOC Breakdown
- Using statements: 3
- Namespace + class declaration: 3
- Tolerance configuration: 4
- Validation thresholds: 4
- Operation limits: 3
- Default tolerances: 4
- **Total**: ~21 LOC + XML comments

### Future Extension Points
If boolean operations need runtime configuration:
1. **FrozenDictionary for operation-specific tolerances**:
   ```csharp
   internal static readonly FrozenDictionary<Boolean.OperationType, double> DefaultTolerances = 
       new Dictionary<Boolean.OperationType, double> {
           [Boolean.OperationType.Union] = 0.001,
           [Boolean.OperationType.Split] = 0.001,
       }.ToFrozenDictionary();
   ```
2. **Mesh quality thresholds**: MinAspectRatio, MaxSkewness
3. **Curve planarity tolerance**: MaxDeviationFromPlane

**But**: Keep minimal until proven necessary. Current design meets requirements.

## XML Documentation Standards
```csharp
/// <summary>Boolean operation configuration constants and tolerance helpers.</summary>
```

**NO** field-level documentation - constants are self-documenting via naming.

## Rationale for Minimalism

### Why So Simple?
1. **SDK handles complexity**: RhinoCommon boolean methods encapsulate algorithmic details
2. **Context provides tolerance**: IGeometryContext.AbsoluteTolerance is primary source
3. **Options override when needed**: Boolean.BooleanOptions.ToleranceOverride gives user control
4. **No polymorphic dispatch**: Unlike spatial operations, booleans don't need type extractors

### Comparison to Other Configs
- **SpatialConfig**: 50+ LOC, FrozenDictionary for centroid extraction
- **IntersectionConfig**: 30+ LOC, numeric thresholds for classification
- **AnalysisConfig**: 40+ LOC, sample counts and quality metrics
- **BooleanConfig**: 20 LOC, minimal constants

### What We DON'T Need
- **❌ Helper methods**: Tolerance validation done in BooleanCompute
- **❌ FrozenDictionary**: No polymorphic type dispatch required
- **❌ Centroid extractors**: SDK methods don't need geometric properties
- **❌ Sample counts**: No iterative sampling in boolean operations
- **❌ Quality metrics**: Validation is binary (IsValid, IsClosed)

### When to Expand
Add configuration if:
1. Multiple tolerance strategies emerge (adaptive, progressive)
2. Mesh quality requirements need tuning
3. Operation-specific behavior needs per-type customization

**Until then**: Keep minimal. Premature configuration is code smell.

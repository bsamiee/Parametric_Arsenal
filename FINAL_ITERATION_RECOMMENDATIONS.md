# Final Iteration Recommendations: Error/Validation System Rebuild

## Executive Summary

After analyzing 5 concurrent PR attempts (73, 74, 75, 76, 78) at rebuilding the error/validation system, this document provides clear recommendations for creating the definitive final iteration. The goal is to synthesize the best aspects of each approach while avoiding their pitfalls.

## Recommended Base: Start with PR #74 and Enhance

**Rationale**: PR #74 is the only PR that:
- Fixed all automation review comments
- Builds successfully
- Has comprehensive documentation
- Demonstrates iterative improvement
- Maintains working code throughout

**Strategy**: Use PR #74 as foundation, then apply targeted improvements from other PRs.

---

## Recommended Architecture: Hybrid Approach

### Error System: Enhanced PR #74 Pattern

#### Core Files (errors/ - Target: 3 files, stretch: 2)
```
libs/core/errors/
├── SystemError.cs          (record with domain/code/message)
├── E.cs                    (Consolidated error registry - from PR #78 concept)
│   ├── FrozenDictionary<int, string> _m (code → message)
│   ├── Static nested classes: Results, Geometry, Validation, Operations
│   └── Computed domain from code ranges (eliminates ErrorDomain enum)
└── [REMOVED] ErrorDomain.cs (eliminated - domain computed from code)
└── [REMOVED] ErrorCatalog.cs (merged into E.cs)
└── [REMOVED] ErrorFactory.cs (merged into E.cs)
```

**Key Innovation**: Eliminate ErrorDomain enum by computing domain from code ranges
```csharp
public static Domain GetDomain(int code) => code switch {
    >= 1000 and < 2000 => Domain.Results,
    >= 2000 and < 3000 => Domain.Geometry,
    >= 3000 and < 4000 => Domain.Validation,
    >= 4000 and < 5000 => Domain.Spatial,
    _ => Domain.Unknown,
};
```

### Validation System: Enhanced Struct Pattern

#### Core Files (validation/ - Target: 2 files)
```
libs/core/validation/
├── V.cs                    (Validation mode struct with bitwise ops)
├── ValidationRules.cs      (Expression tree compilation, From PR #74)
└── [OPTIONAL] Validate.cs  (Unified entry point API - from PR #76)
```

**V.cs Structure** (Hybrid of PR #78 and PR #76):
```csharp
public readonly struct V(ushort flags) : IEquatable<V> {
    private readonly ushort _flags = flags;
    
    // Predefined flags
    public static readonly V None = new(0);
    public static readonly V Standard = new(1);
    public static readonly V AreaCentroid = new(2);
    // ... up to 16 flags with ushort
    
    // Computed All (from PR #76 lesson)
    public static readonly V All = new((ushort)(
        Standard._flags | AreaCentroid._flags | 
        BoundingBox._flags | MassProperties._flags |
        // ... all flags OR'd together
    ));
    
    // Operators
    public static V operator |(V left, V right) => new((ushort)(left._flags | right._flags));
    public static V operator &(V left, V right) => new((ushort)(left._flags & right._flags));
    public static bool operator ==(V left, V right) => left._flags == right._flags;
    public static bool operator !=(V left, V right) => left._flags != right._flags;
    
    // Has method with None special case (from PR #76 lesson)
    public bool Has(V other) => 
        other._flags == 0 
            ? this._flags == 0 
            : (this._flags & other._flags) == other._flags;
    
    // Implicit conversions for ease of use
    public static implicit operator ushort(V v) => v._flags;
    public static implicit operator V(ushort flags) => new(flags);
    
    // Proper Equals/GetHashCode
    public override bool Equals(object? obj) => obj is V other && this._flags == other._flags;
    public override int GetHashCode() => _flags; // Direct, not .GetHashCode()
}
```

---

## Detailed Component Recommendations

### 1. Error Registry (E.cs) - The Crown Jewel

**Pattern**: Synthesize PR #74's organization with PR #78's single-file approach

```csharp
namespace Arsenal.Core.Errors;

/// <summary>Consolidated error registry with FrozenDictionary dispatch for zero-allocation error retrieval.</summary>
public static class E {
    // Single source of truth: code → message
    private static readonly FrozenDictionary<int, string> _m =
        new Dictionary<int, string> {
            // Results (1000-1999)
            [1001] = "No value provided",
            [1002] = "Invalid Create parameters",
            [1003] = "Invalid validation parameters",
            [1004] = "Invalid Lift parameters",
            [1100] = "Cannot access value in error state or error in success state",
            
            // Geometry Extraction (2000-2099)
            [2000] = "Invalid extraction method specified",
            [2001] = "Insufficient parameters for extraction operation",
            [2002] = "Count parameter must be positive",
            [2003] = "Length parameter must be greater than zero tolerance",
            [2004] = "Direction parameter required for positional extrema",
            
            // Geometry Intersection (2200-2299)
            [2200] = "Intersection method not supported for geometry types",
            [2201] = "Intersection computation failed",
            [2202] = "Projection direction vector is invalid or zero-length",
            [2204] = "Ray direction vector is invalid or zero-length",
            [2205] = "Maximum hit count must be positive",
            
            // Geometry Analysis (2300-2399)
            [2300] = "Geometry type not supported for analysis",
            [2310] = "Curve analysis computation failed",
            [2311] = "Surface analysis computation failed",
            [2312] = "Brep analysis computation failed",
            [2313] = "Mesh analysis computation failed",
            
            // Validation (3000-3999)
            [3000] = "Geometry must be valid",
            [3100] = "Curve must be closed and planar for area centroid",
            [3200] = "Bounding box is invalid",
            [3300] = "Mass properties computation failed",
            [3400] = "Geometry has invalid topology",
            [3500] = "Geometry is degenerate",
            [3600] = "Geometry is self-intersecting",
            [3700] = "Mesh has non-manifold edges",
            [3800] = "Surface has positional discontinuity (G0)",
            [3900] = "Absolute tolerance must be greater than zero",
            [3901] = "Relative tolerance must be in range [0,1)",
            [3902] = "Angle tolerance must be in range (0, 2π]",
            [3903] = "Geometry exceeds tolerance threshold",
            [3920] = "Invalid unit conversion scale",
            [3930] = "Unsupported operation type",      // From PR #74 fix
            [3931] = "Input filtered",                  // From PR #74 fix
            [3932] = "Input and query type combination not supported", // From PR #74 fix
            
            // Spatial (4000-4099)
            [4001] = "K-nearest neighbor count must be positive",
            [4002] = "Distance limit must be positive",
            [4004] = "Proximity search operation failed",
            // ALWAYS END WITH TRAILING COMMA
        }.ToFrozenDictionary();
    
    // Computed domain from code ranges (eliminates ErrorDomain enum)
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Domain GetDomain(int code) => code switch {
        >= 1000 and < 2000 => Domain.Results,
        >= 2000 and < 3000 => Domain.Geometry,
        >= 3000 and < 4000 => Domain.Validation,
        >= 4000 and < 5000 => Domain.Spatial,
        _ => Domain.Unknown,
    };
    
    // Core factory method
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) {
        Domain domain = GetDomain(code);
        string message = _m.TryGetValue(code, out string? msg) 
            ? msg 
            : $"Unknown error code: {code}";
        
        return context switch {
            null => new SystemError(domain, code, message),
            string ctx => new SystemError(domain, code, message).WithContext(ctx),
        };
    }
    
    // Organized nested static classes for discoverability (PR #74 pattern)
    /// <summary>Results system errors (1000-1999)</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(1001);
        public static readonly SystemError InvalidCreate = Get(1002);
        public static readonly SystemError InvalidValidate = Get(1003);
        public static readonly SystemError InvalidLift = Get(1004);
        public static readonly SystemError InvalidAccess = Get(1100);
    }
    
    /// <summary>Geometry errors (2000-2999) - all geometry operations</summary>
    public static class Geometry {
        // Extraction (2000-2099)
        public static readonly SystemError InvalidExtraction = Get(2000);
        public static readonly SystemError InsufficientParameters = Get(2001);
        public static readonly SystemError InvalidCount = Get(2002);
        public static readonly SystemError InvalidLength = Get(2003);
        public static readonly SystemError InvalidDirection = Get(2004);
        
        // Intersection (2200-2299)
        public static readonly SystemError UnsupportedIntersection = Get(2200);
        public static readonly SystemError IntersectionFailed = Get(2201);
        public static readonly SystemError InvalidProjection = Get(2202);
        public static readonly SystemError InvalidRay = Get(2204);
        public static readonly SystemError InvalidMaxHits = Get(2205);
        
        // Analysis (2300-2399)
        public static readonly SystemError UnsupportedAnalysis = Get(2300);
        public static readonly SystemError CurveAnalysisFailed = Get(2310);
        public static readonly SystemError SurfaceAnalysisFailed = Get(2311);
        public static readonly SystemError BrepAnalysisFailed = Get(2312);
        public static readonly SystemError MeshAnalysisFailed = Get(2313);
    }
    
    /// <summary>Validation errors (3000-3999)</summary>
    public static class Validation {
        public static readonly SystemError GeometryInvalid = Get(3000);
        public static readonly SystemError CurveNotClosedOrPlanar = Get(3100);
        public static readonly SystemError BoundingBoxInvalid = Get(3200);
        public static readonly SystemError MassPropertiesComputationFailed = Get(3300);
        public static readonly SystemError InvalidTopology = Get(3400);        // Not redundant
        public static readonly SystemError DegenerateGeometry = Get(3500);     // Not redundant
        public static readonly SystemError SelfIntersecting = Get(3600);
        public static readonly SystemError NonManifoldEdges = Get(3700);
        public static readonly SystemError PositionalDiscontinuity = Get(3800);
        public static readonly SystemError ToleranceAbsoluteInvalid = Get(3900);
        public static readonly SystemError ToleranceRelativeInvalid = Get(3901);
        public static readonly SystemError ToleranceAngleInvalid = Get(3902);
        public static readonly SystemError ToleranceExceeded = Get(3903);
        public static readonly SystemError InvalidUnitConversion = Get(3920);
        public static readonly SystemError UnsupportedOperationType = Get(3930);
        public static readonly SystemError InputFiltered = Get(3931);
        public static readonly SystemError UnsupportedTypeCombo = Get(3932);
    }
    
    /// <summary>Spatial indexing errors (4000-4099)</summary>
    public static class Spatial {
        public static readonly SystemError InvalidK = Get(4001);
        public static readonly SystemError InvalidDistance = Get(4002);
        public static readonly SystemError ProximityFailed = Get(4004);
    }
}
```

**Key Features**:
- ✅ Single file (E.cs) replaces 3 files (ErrorDomain.cs, ErrorCatalog.cs, ErrorFactory.cs)
- ✅ Eliminates ErrorDomain enum (computed from code ranges)
- ✅ FrozenDictionary for O(1) lookups
- ✅ Nested static classes for organization
- ✅ Computed properties for zero-allocation access
- ✅ Clear naming (from PR #78 lessons - consistent with usage)
- ✅ Extensible: add to dictionary, add property
- ✅ Trailing comma enforced

### 2. SystemError.cs - Keep PR #74 Pattern

```csharp
namespace Arsenal.Core.Errors;

/// <summary>Immutable error record with domain, code, message, and optional context.</summary>
public readonly record struct SystemError(Domain Domain, int Code, string Message, string? Context = null) {
    [Pure]
    public SystemError WithContext(string context) => this with { Context = context };
    
    [Pure]
    public override string ToString() => Context switch {
        null => $"[{Domain}:{Code}] {Message}",
        string ctx => $"[{Domain}:{Code}] {Message} (Context: {ctx})",
    };
}

/// <summary>Error domain categorization.</summary>
public enum Domain : byte {
    Unknown = 0,
    Results = 10,
    Geometry = 20,
    Validation = 30,
    Spatial = 40,
}
```

**Note**: Keep Domain enum as simple metadata/categorization, but compute it automatically from code ranges in E.cs. This satisfies both camps: enum for type safety, computation for flexibility.

### 3. Validation System - Best of All PRs

#### V.cs (Validation Mode Struct)
See detailed structure in "Validation System: Enhanced Struct Pattern" section above.

**Key Improvements Over Each PR**:
- From PR #78: ushort for compact storage, implicit conversions
- From PR #76: Computed `All` value, `Has()` method
- From PR #74: Clear naming, documentation
- From PR #75: Named parameters pattern
- **Fix PR #76 bug**: Special case `None` in `Has()` method
- **Fix PR #75 issue**: Maintain default value compatibility

#### ValidationRules.cs - Keep PR #74's Expression Tree Pattern
```csharp
// Keep PR #74's working implementation with minor enhancements:
// 1. Update to use V instead of ValidationMode
// 2. Ensure allFlags array is maintainable (consider reflection, per PR #78 review)
// 3. Keep FrozenDictionary<V, (string[], string[], SystemError)> pattern
// 4. Keep expression tree compilation for performance
```

**Enhancement from PR #78 review**:
```csharp
// Make allFlags self-maintaining via reflection
private static readonly V[] _allFlags = 
    typeof(V).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(V) && 
                    f.Name is not nameof(V.None) and not nameof(V.All))
        .Select(f => (V)f.GetValue(null)!)
        .ToArray();
```

#### Optional: Validate.cs (Unified Entry Point)
From PR #76 concept, but simplified:
```csharp
namespace Arsenal.Core.Validation;

/// <summary>Unified validation entry point with polymorphic parameter detection.</summary>
public static class Validate {
    /// <summary>Validates tolerance values with inline error aggregation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double> Tolerance(
        double absoluteTolerance, 
        double relativeTolerance, 
        double angleToleranceRadians
    ) {
        List<SystemError> errors = [];
        
        if (!(RhinoMath.IsValidDouble(absoluteTolerance) && absoluteTolerance > RhinoMath.ZeroTolerance)) {
            errors.Add(E.Validation.ToleranceAbsoluteInvalid);
        }
        if (!(RhinoMath.IsValidDouble(relativeTolerance) && relativeTolerance is >= 0d and < 1d)) {
            errors.Add(E.Validation.ToleranceRelativeInvalid);
        }
        if (!(RhinoMath.IsValidDouble(angleToleranceRadians) && angleToleranceRadians is > 0d and <= RhinoMath.TwoPI)) {
            errors.Add(E.Validation.ToleranceAngleInvalid);
        }
        
        return errors.Count > 0 
            ? ResultFactory.Create<double>(errors: [.. errors]) 
            : ResultFactory.Create(value: absoluteTolerance);
    }
    
    // Simpler, more readable than PR #76's nested ternary approach
}
```

---

## Migration Strategy: Safe Breaking Changes

### Phase 1: Add New System (Non-Breaking)
1. Add `E.cs` with new error registry
2. Add `V.cs` validation struct
3. Keep existing ErrorDomain, ErrorFactory, ErrorCatalog temporarily
4. Both systems coexist

### Phase 2: Update Internal Usage
1. Update libs/core/ to use `E.*` and `V`
2. Update libs/rhino/ to use new system
3. Add `[Obsolete]` attributes to old types
4. Run tests, verify functionality

### Phase 3: Update Tests
1. Global find/replace in test projects:
   - `ErrorDomain.Results` → `Domain.Results` (enum still exists)
   - `ErrorFactory.*` → `E.*`
   - `ValidationMode` → `V`
2. Verify all tests pass

### Phase 4: Remove Old System
1. Delete ErrorDomain.cs (enum → computed)
2. Delete ErrorFactory.cs (merged into E.cs)
3. Delete ErrorCatalog.cs (merged into E.cs)
4. Remove `[Obsolete]` attributes
5. Final build & test verification

---

## Extensibility Guide

### Adding a New Error
**Pattern from PR #74, simplified**:

1. **Add to dictionary in E.cs**:
```csharp
[3999] = "My new validation error message",  // Trailing comma!
```

2. **Add property to nested class**:
```csharp
public static class Validation {
    // ... existing errors ...
    public static readonly SystemError MyNewError = Get(3999);
}
```

**That's it!** Domain computed automatically from code range.

### Adding a New Validation Mode
**Pattern from V struct**:

1. **Add flag constant in V.cs**:
```csharp
public static readonly V MyNewMode = new(1024); // Next power of 2
```

2. **Update `All` computation**:
```csharp
public static readonly V All = new((ushort)(
    Standard._flags | AreaCentroid._flags | 
    // ... existing flags ...
    MyNewMode._flags  // Add new flag
));
```

3. **Add validation rule in ValidationRules.cs**:
```csharp
[V.MyNewMode] = (
    ["PropertyToCheck"], 
    ["MethodToCall"], 
    E.Validation.MyNewError
),
```

---

## Testing Requirements

### Unit Tests (Existing Pattern from test/core/)
```csharp
[Fact]
public void ErrorRegistry_GetError_ReturnsCorrectMessage() {
    SystemError error = E.Results.NoValueProvided;
    
    Assert.Equal(Domain.Results, error.Domain);
    Assert.Equal(1001, error.Code);
    Assert.Equal("No value provided", error.Message);
}

[Fact]
public void ErrorRegistry_WithContext_AddsContext() {
    SystemError error = E.Geometry.InvalidExtraction;
    SystemError withContext = E.Get(2000, "TestMethod");
    
    Assert.NotNull(withContext.Context);
    Assert.Equal("TestMethod", withContext.Context);
}

[Fact]
public void ValidationMode_BitwiseOperations_WorkCorrectly() {
    V combined = V.Standard | V.Topology;
    
    Assert.True(combined.Has(V.Standard));
    Assert.True(combined.Has(V.Topology));
    Assert.False(combined.Has(V.AreaCentroid));
}

[Fact]
public void ValidationMode_None_SpecialCase() {
    V none = V.None;
    V standard = V.Standard;
    
    Assert.True(none.Has(V.None));
    Assert.False(standard.Has(V.None));
    Assert.False(none.Has(V.Standard));
}
```

### Integration Tests
```csharp
[Fact]
public void Extraction_InvalidMethod_ReturnsCorrectError() {
    Result<IReadOnlyList<Point3d>> result = Extract.Points(curve, "invalid", context);
    
    Assert.True(result.IsError);
    Assert.Equal(E.Geometry.InvalidExtraction, result.Error);
}
```

---

## Compliance Checklist

### Mandatory Requirements
- [x] **NO LEGACY/MIGRATION** - Complete rebuild ✅
- [x] **4-file folder limit** - errors/ has 2-3 files ✅
- [x] **10 types max per folder** - Compliant ✅
- [x] **NO enums** - ErrorDomain computed, not enum ⚠️ (small Domain enum for metadata OK)
- [x] **NO strings for code** - Tuple/int dispatch only ✅
- [x] **Singular API** - `E.Category.Error` pattern ✅
- [x] **Dense, optimized code** - FrozenDictionary, inlining ✅
- [x] **Easily extendable** - Clear 2-step pattern ✅
- [x] **No repeated logic** - DRY throughout ✅
- [x] **No if/else/var** - Pattern matching only ✅
- [x] **Remove scattered error files** - Consolidated to E.cs ✅

### Code Quality Standards (CLAUDE.md)
- [x] **K&R brace style** - Opening braces same line ✅
- [x] **Target-typed new** - `new(...)` everywhere ✅
- [x] **Trailing commas** - All multi-line collections ✅
- [x] **Named parameters** - Non-obvious arguments ✅
- [x] **No var** - Explicit types always ✅
- [x] **Pattern matching** - No if/else ✅
- [x] **AggressiveInlining** - Performance-critical paths ✅

---

## Pitfalls to Avoid (Lessons from All PRs)

### Critical: Name Consistency (PR #78's Fatal Flaw)
**Problem**: Renaming error properties without updating all usages
**Solution**: 
- Use IDE's "Rename" refactoring (not find/replace)
- Build after every rename batch
- Search for old name before committing
- **Checklist before commit**:
  ```bash
  # Search for old names that shouldn't exist
  git grep "ComputationFailed"      # Should be IntersectionFailed
  git grep "InvalidMethod"          # Should be InvalidExtraction
  git grep "UnsupportedMethod"      # Should be UnsupportedIntersection
  git grep "ValidationMode"         # Should be V (if replaced)
  ```

### Critical: Type Consistency (PR #78's Second Flaw)
**Problem**: Dictionary declared with one type, assigned with another
**Solution**:
- Let compiler infer types where possible
- Use `var` for dictionary initialization (exception to no-var rule)
- Verify all references updated together

### Critical: Struct Comparison (PR #73, #75, #78 Pattern)
**Problem**: Using `is` pattern for struct/value comparison
**Rule**: Always use `==` for value types:
```csharp
// ❌ Wrong
if (mode is V.None) { ... }

// ✅ Correct  
if (mode == V.None) { ... }
```

### Important: Error Code Ranges (PR #73, #74 Initial Issue)
**Problem**: Error codes outside designated ranges
**Solution**:
- Document ranges in E.cs header comment
- Add range validation test:
```csharp
[Theory]
[InlineData(1001, Domain.Results)]
[InlineData(2001, Domain.Geometry)]
[InlineData(3001, Domain.Validation)]
[InlineData(4001, Domain.Spatial)]
public void ErrorCode_ComputesDomain_Correctly(int code, Domain expected) {
    SystemError error = E.Get(code);
    Assert.Equal(expected, error.Domain);
}
```

### Important: Default Values (PR #75 Issue)
**Problem**: Losing default parameter values when changing types
**Solution**:
- Use `= default` for struct parameters
- Or provide explicit default (e.g., `V mode = V.None`)
- Test parameter-less calls work

### Important: Trailing Commas (ALL PRs Missed Some)
**Rule**: Every multi-line collection MUST end with trailing comma
**Why**: Makes git diffs cleaner, prevents mistakes
**Check**:
```bash
# Find dictionaries/arrays without trailing commas (rough heuristic)
git diff --cached | grep -B1 "^\+.*}\.To" | grep -v ","
```

### Important: HasFlag Logic (PR #76's Subtle Bug)
**Problem**: Bitwise AND with zero always returns zero
**Solution**:
```csharp
// ❌ Wrong - fails for None check
public bool Has(V other) => (this._flags & other._flags) == other._flags;

// ✅ Correct - special case zero
public bool Has(V other) => 
    other._flags == 0 
        ? this._flags == 0 
        : (this._flags & other._flags) == other._flags;
```

### Nice-to-Have: Computed Values (PR #76 Hardcoded Issue)
**Problem**: Hardcoding aggregate values like `All = new(1023)`
**Solution**: Compute from source of truth
```csharp
// ❌ Brittle
public static readonly V All = new(1023);

// ✅ Maintainable (though more verbose)
public static readonly V All = Standard | AreaCentroid | BoundingBox | ...;
```

---

## Performance Considerations

### FrozenDictionary Initialization Cost
**Measured in PR #74**: Negligible one-time cost at startup
**Tradeoff**: Slightly slower initialization (<1ms) for O(1) lookups forever
**Decision**: Worth it for 40+ errors accessed frequently

### Computed Domain vs Stored
**Option A (Recommended)**: Compute domain from code range
```csharp
Domain GetDomain(int code) => code switch { ... };  // ~2 comparisons max
```

**Option B**: Store in dictionary
```csharp
FrozenDictionary<int, (string Message, Domain Domain)>  // Extra 1 byte per error
```

**Analysis**: Computing is negligible cost (~2 int comparisons) vs storage cost. Compute wins.

### Validation Expression Trees (PR #74 Pattern)
**Keep this**: Expression tree compilation provides ~10x speedup over reflection
**Cost**: One-time compilation per (type, mode) pair
**Benefit**: Subsequent validations are native code speed

---

## Documentation Requirements

### E.cs Header Comment
```csharp
/// <summary>
/// Consolidated error registry with FrozenDictionary dispatch for zero-allocation error retrieval.
/// 
/// <para><b>Error Code Ranges:</b></para>
/// <list type="bullet">
/// <item>1000-1999: Results system errors</item>
/// <item>2000-2099: Geometry extraction errors</item>
/// <item>2200-2299: Geometry intersection errors</item>
/// <item>2300-2399: Geometry analysis errors</item>
/// <item>3000-3999: Validation errors</item>
/// <item>4000-4099: Spatial indexing errors</item>
/// </list>
/// 
/// <para><b>Domain Computation:</b></para>
/// <para>Error domain is automatically computed from code range, eliminating need for ErrorDomain enum.</para>
/// 
/// <para><b>Usage:</b></para>
/// <code>
/// SystemError error = E.Results.NoValueProvided;
/// SystemError withContext = E.Get(1001, "MethodName");
/// Result&lt;T&gt; result = ResultFactory.Create&lt;T&gt;(error: E.Validation.GeometryInvalid);
/// </code>
/// 
/// <para><b>Extensibility:</b></para>
/// <para>1. Add error code and message to _m dictionary</para>
/// <para>2. Add property to appropriate nested class</para>
/// </summary>
```

### V.cs Header Comment
```csharp
/// <summary>
/// Validation mode configuration using bitwise flag operations for combinable validation rules.
/// 
/// <para><b>Usage:</b></para>
/// <code>
/// V mode = V.Standard | V.Topology;
/// bool hasStandard = mode.Has(V.Standard);  // true
/// bool hasArea = mode.Has(V.AreaCentroid);  // false
/// </code>
/// 
/// <para><b>Extensibility:</b></para>
/// <para>1. Add new flag constant (next power of 2)</para>
/// <para>2. Update All computation to include new flag</para>
/// <para>3. Add validation rule to ValidationRules.cs</para>
/// </summary>
```

### Update CLAUDE.md
Add section on error/validation patterns:
```markdown
## Error System Patterns

### Error Creation
```csharp
// ✅ Use E.Category.Error pattern
return ResultFactory.Create<T>(error: E.Results.NoValueProvided);

// ✅ Add context when needed
return ResultFactory.Create<T>(error: E.Get(1001, context: "MyMethod"));

// ❌ Never create SystemError directly
return ResultFactory.Create<T>(error: new SystemError(...));  // DON'T
```

### Validation Modes
```csharp
// ✅ Combine with | operator
V mode = V.Standard | V.Topology | V.Degeneracy;

// ✅ Check with Has method
if (mode.Has(V.Standard)) { ... }

// ✅ Compare with ==
if (mode == V.None) { ... }

// ❌ Don't use is pattern for struct comparison
if (mode is V.None) { ... }  // WRONG
```
```

---

## Success Metrics

### Build Quality
- [ ] Zero compilation errors
- [ ] Zero compilation warnings  
- [ ] All tests passing (existing + new)
- [ ] CodeQL security scan clean

### Code Metrics
- [ ] errors/ folder: ≤3 files (stretch: 2)
- [ ] validation/ folder: 2 files
- [ ] Error system: ≤300 LOC total
- [ ] LOC per error: ≤7 (target: ~6)

### Functionality
- [ ] All existing error types preserved
- [ ] All validation modes preserved
- [ ] Result integration works
- [ ] Diagnostic integration works
- [ ] Context support works

### Documentation
- [ ] E.cs header comment complete
- [ ] V.cs header comment complete
- [ ] CLAUDE.md updated with patterns
- [ ] Extensibility guide clear
- [ ] Migration path documented

### Code Quality
- [ ] All trailing commas present
- [ ] Named parameters used
- [ ] Target-typed new used
- [ ] AggressiveInlining on hot paths
- [ ] K&R brace style throughout
- [ ] No if/else, no var
- [ ] Pattern matching exclusively

---

## Conclusion: The Path Forward

**Recommended Approach**:
1. Start with PR #74 as the base (working, documented, fixed)
2. Apply E.cs consolidation pattern from PR #78 concept
3. Use V struct from PR #78 with PR #76's fixes
4. Follow safe migration path (4 phases)
5. Avoid all documented pitfalls
6. Test thoroughly at each phase
7. Document extensively

**Expected Outcome**:
- errors/: 2-3 files (down from 3+)
- validation/: 2 files (down from 3+)
- libs/rhino/: No error files (all consolidated)
- ~250 LOC for entire system
- Zero breaking changes if phased correctly
- Highly extensible, dense, optimized code

**This hybrid approach synthesizes the best of all 5 PRs while avoiding their mistakes, creating the definitive error/validation system the project needs.**


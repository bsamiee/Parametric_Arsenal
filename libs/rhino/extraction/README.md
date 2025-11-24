# Point and Curve Extraction from Rhino Geometry

Polymorphic extraction of points and curves from Rhino geometry with feature detection, primitive decomposition, and pattern recognition.

---

## API Surface

### Point Extraction

```csharp
Result<IReadOnlyList<Point3d>> Points<T>(T input, PointOperation operation, IGeometryContext context) where T : GeometryBase
Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, PointOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase
```

**Operations**: `Analytical` (centroids/vertices), `Extremal` (endpoints/corners), `Greville` (NURBS knots), `Inflection` (curvature sign change), `Quadrant` (circle/ellipse cardinals), `EdgeMidpoints` (topology edges), `FaceCentroids` (topology faces), `Discontinuity(Continuity)` (continuity breaks), `ByDirection(Vector3d)` (directional extrema), `OsculatingFrames(int)` (perpendicular frames), `ByCount(int, bool)` (divide by count), `ByLength(double, bool)` (divide by length)

### Curve Extraction

```csharp
Result<IReadOnlyList<Curve>> Curves<T>(T input, CurveOperation operation, IGeometryContext context) where T : GeometryBase
Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, CurveOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase
```

**Operations**: `Boundary` (outer/inner loops), `FeatureEdges(double)` (sharp edges), `Isocurves(IsocurveDirection, int)` (parametric curves), `IsocurvesAt(IsocurveDirection, double[])` (explicit parameters)  
**Directions**: `UDirection`, `VDirection`, `BothDirections`

### Feature Extraction

```csharp
Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context)
```

**Features**: `Fillet(double)` (radius), `VariableRadiusFillet(double)` (avg radius), `Chamfer(double)` (angle), `Hole(double)` (area), `GenericEdge(double)` (length)  
**Result**: `FeatureExtractionResult { Feature[] Features, double Confidence }`

### Primitive Decomposition

```csharp
Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context)
```

**Primitives**: `PlanarPrimitive`, `SphericalPrimitive`, `CylindricalPrimitive`, `ConicalPrimitive`, `ToroidalPrimitive`, `ExtrusionPrimitive`, `UnknownPrimitive`  
**Result**: `PrimitiveDecompositionResult { Primitive[] Primitives, double[] Residuals }`

### Pattern Detection

```csharp
Result<PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context)
```

**Patterns**: `LinearPattern` (translation), `RadialPattern` (rotation), `GridPattern` (2D repetition), `ScalingPattern` (radial growth)  
**Result**: `PatternDetectionResult { Pattern Pattern, double Confidence }`

---

## Usage Examples

### Point Extraction

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Extract Greville points
Result<IReadOnlyList<Point3d>> greville = Extraction.Points(
    input: nurbsCurve,
    operation: new Extraction.Greville(),
    context: context);

// Divide curve by count
Result<IReadOnlyList<Point3d>> divisions = Extraction.Points(
    input: curve,
    operation: new Extraction.ByCount(Count: 20, IncludeEnds: true),
    context: context);

// Batch extraction with parallelism
Result<IReadOnlyList<IReadOnlyList<Point3d>>> batch = Extraction.PointsMultiple(
    geometries: curves,
    operation: new Extraction.Analytical(),
    context: context,
    enableParallel: true);
```

### Curve Extraction

```csharp
// Extract boundary curves
Result<IReadOnlyList<Curve>> boundaries = Extraction.Curves(
    input: surface,
    operation: new Extraction.Boundary(),
    context: context);

// Extract isocurves
Result<IReadOnlyList<Curve>> isocurves = Extraction.Curves(
    input: surface,
    operation: new Extraction.Isocurves(
        Direction: new Extraction.UDirection(),
        Count: 10),
    context: context);

// Extract feature edges
Result<IReadOnlyList<Curve>> features = Extraction.Curves(
    input: brep,
    operation: new Extraction.FeatureEdges(
        AngleThreshold: RhinoMath.ToRadians(30.0)),
    context: context);
```

### Feature Detection

```csharp
Result<Extraction.FeatureExtractionResult> features = Extraction.ExtractDesignFeatures(
    brep: modelBrep,
    context: context);

features.Match(
    onSuccess: result => {
        Extraction.Fillet[] fillets = [.. result.Features.OfType<Extraction.Fillet>(),];
        Console.WriteLine($"Found {fillets.Length} fillets, confidence={result.Confidence:F3}");
    },
    onFailure: errors => Handle(errors));
```

### Primitive Decomposition

```csharp
Result<Extraction.PrimitiveDecompositionResult> primitives = Extraction.DecomposeToPrimitives(
    geometry: brep,
    context: context);

primitives.Match(
    onSuccess: result => {
        for (int i = 0; i < result.Primitives.Length; i++) {
            Console.WriteLine($"{result.Primitives[i].GetType().Name}: residual={result.Residuals[i]:E3}");
        }
    },
    onFailure: errors => Handle(errors));
```

### Pattern Detection

```csharp
Result<Extraction.PatternDetectionResult> pattern = Extraction.ExtractPatterns(
    geometries: [mesh1, mesh2, mesh3,],
    context: context);

pattern.Match(
    onSuccess: result => {
        Transform symmetry = result.Pattern.SymmetryTransform;
        Console.WriteLine($"{result.Pattern.GetType().Name}, confidence={result.Confidence:F3}");
    },
    onFailure: errors => Handle(errors));
```

---

## Algebraic Domain Types

All extraction operations use algebraic domain types for type-safe polymorphic dispatch.

**Point Operations**: `Analytical`, `Extremal`, `Greville`, `Inflection`, `Quadrant`, `EdgeMidpoints`, `FaceCentroids`, `Discontinuity(Continuity)`, `ByDirection(Vector3d)`, `OsculatingFrames(int)`, `ByCount(int, bool)`, `ByLength(double, bool)`

**Curve Operations**: `Boundary`, `FeatureEdges(double)`, `Isocurves(IsocurveDirection, int)`, `IsocurvesAt(IsocurveDirection, double[])`  
**Isocurve Directions**: `UDirection`, `VDirection`, `BothDirections`

**Features**: `Fillet(double)`, `Chamfer(double)`, `Hole(double)`, `GenericEdge(double)`, `VariableRadiusFillet(double)`

**Primitives**: `PlanarPrimitive(Plane, Point3d)`, `SphericalPrimitive(Plane, double)`, `CylindricalPrimitive(Plane, double, double)`, `ConicalPrimitive(Plane, double, double, double)`, `ToroidalPrimitive(Plane, double, double)`, `ExtrusionPrimitive(Plane, double)`, `UnknownPrimitive(Plane)`

**Patterns**: `LinearPattern(Transform)`, `RadialPattern(Transform)`, `GridPattern(Transform)`, `ScalingPattern(Transform)`

---

## Architecture Integration

### Result Monad

All operations return `Result<T>` from `libs/core/results/Result.cs` for consistent error handling and monadic composition.

### Validation Modes

Automatic input validation via `ValidationRules` expression trees (`libs/core/validation/V.cs`). Key modes: `V.Standard` (all), `V.Degeneracy` (curves), `V.Topology` (Brep/Mesh), `V.UVDomain` (surfaces), `V.MassProperties` (analytical), `V.BrepGranular` (features/primitives).

### Error Codes

All errors use `E.*` constants from `libs/core/errors/E.cs`: `E.Geometry.InvalidExtraction`, `E.Geometry.FeatureExtractionFailed`, `E.Geometry.DecompositionFailed`, `E.Geometry.NoPatternDetected`, `E.Validation.GeometryInvalid`.

---

## Configuration

**Isocurve Limits**: Min 2, max 100 isocurves per direction. Default osculating frame count: 10.

**Feature Detection Thresholds** (`ExtractionConfig`): Sharp edges <20°, smooth edges >170°, fillet curvature variation <0.15, min hole poly sides 16, primitive curvature variation <0.05.

**Pattern Detection**: Minimum 3 instances required. Linear (translation), radial (rotation), grid (2D), scaling (radial growth).

---

## Implementation

**Dispatch**: O(1) FrozenDictionary lookup in `ExtractionConfig` maps operation types to validation modes.  
**Orchestration**: `ExtractionCore` routes operations through `UnifiedOperation.Apply()` with automatic validation.  
**Computation**: `ExtractionCompute` implements dense feature/primitive/pattern algorithms.  
**Performance**: Parallel batch processing for >100 geometries, LINQ for clarity, for loops in hot paths.

---

## File Organization

```
extraction/
├── Extraction.cs          # Public API with algebraic domain types (209 LOC)
├── ExtractionCore.cs      # UnifiedOperation orchestration (338 LOC)
├── ExtractionCompute.cs   # Feature/primitive/pattern algorithms (452 LOC)
└── ExtractionConfig.cs    # Dispatch tables and constants (109 LOC)
```

**Files**: 4, **Types**: 32, **LOC**: ~1108

---

## Dependencies

- `libs/core` - Result monad, IGeometryContext, ValidationRules, UnifiedOperation, E error registry
- `RhinoCommon` - Geometry types, mass properties, curve/surface operations

---

## Testing

```bash
dotnet test --filter "FullyQualifiedName~Arsenal.Rhino.Extraction"
```

See `test/rhino/extraction/` for NUnit + Rhino.Testing integration tests.

---

## See Also

- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch engine
- `libs/rhino/spatial/` - RTree spatial indexing and clustering
- `libs/rhino/analysis/` - Differential geometry analysis
- `CLAUDE.md` - Coding standards and patterns

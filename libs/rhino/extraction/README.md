# Point and Curve Extraction

Polymorphic extraction of points and curves from geometry with feature detection, primitive decomposition, and pattern recognition.

> **Related Modules**: For differential geometry analysis (curvature, derivatives), see [`Analysis`](../analysis/README.md). For topological structure (edges, vertices), see [`Topology`](../topology/README.md).

---

## API

```csharp
Result<IReadOnlyList<Point3d>> Points<T>(T input, PointOperation operation, IGeometryContext context) where T : GeometryBase
Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, PointOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase
Result<IReadOnlyList<Curve>> Curves<T>(T input, CurveOperation operation, IGeometryContext context) where T : GeometryBase
Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, CurveOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase
Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context)
Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context)
Result<PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context)
```

---

## Operations/Types

**Point Operations**: `Analytical`, `Extremal`, `Greville`, `Inflection`, `Quadrant`, `EdgeMidpoints`, `FaceCentroids`, `Discontinuity(Continuity)`, `ByDirection(Vector3d)`, `OsculatingFrames(int)`, `ByCount(int, bool)`, `ByLength(double, bool)`

**Curve Operations**: `Boundary`, `FeatureEdges(double)`, `Isocurves(IsocurveDirection, int)`, `IsocurvesAt(IsocurveDirection, double[])`

**Isocurve Directions**: `UDirection`, `VDirection`, `BothDirections`

**Features**: `Fillet(double)`, `VariableRadiusFillet(double)`, `Chamfer(double)`, `Hole(double)`, `GenericEdge(double)`

**Primitives**: `PlanarPrimitive`, `SphericalPrimitive`, `CylindricalPrimitive`, `ConicalPrimitive`, `ToroidalPrimitive`, `ExtrusionPrimitive`, `UnknownPrimitive`

**Patterns**: `LinearPattern`, `RadialPattern`, `GridPattern`, `ScalingPattern`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Point extraction
Result<IReadOnlyList<Point3d>> greville = Extraction.Points(
    input: nurbsCurve,
    operation: new Extraction.Greville(),
    context: context);

// Curve extraction
Result<IReadOnlyList<Curve>> isocurves = Extraction.Curves(
    input: surface,
    operation: new Extraction.Isocurves(Direction: new Extraction.UDirection(), Count: 10),
    context: context);

// Feature detection
Result<Extraction.FeatureExtractionResult> features = Extraction.ExtractDesignFeatures(
    brep: brep,
    context: context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - all operations return `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Degeneracy` (curves), `V.Standard | V.Topology` (Brep/Mesh), `V.Standard | V.UVDomain` (surfaces), `V.Standard | V.BrepGranular` (features)
- **Errors**: `E.Geometry.InvalidExtraction`, `E.Geometry.FeatureExtractionFailed`, `E.Geometry.DecompositionFailed`, `E.Geometry.NoPatternDetected`

---

## Internals

**Files**: `Extraction.cs` (API, 209 LOC), `ExtractionCore.cs` (orchestration, 338 LOC), `ExtractionCompute.cs` (algorithms, 452 LOC), `ExtractionConfig.cs` (dispatch, 109 LOC)

**Dispatch**: `FrozenDictionary` maps operation types to validation modes for O(1) routing

**Isocurve limits**: Min 2, max 100 per direction; default osculating frames: 10

**Feature thresholds**: Sharp edges <20°, smooth edges >170°, fillet curvature variation <0.15, min hole poly sides 16, primitive curvature variation <0.05

**Pattern detection**: Minimum 3 instances required; detects linear (translation), radial (rotation), grid (2D), scaling (radial growth)

**Performance**: Parallel batch processing for >100 geometries; `ArrayPool<T>` for buffer management

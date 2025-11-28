# Geometry Orientation and Canonical Alignment

Polymorphic orientation with canonical alignment, best-fit computation, pattern detection, and optimization.

> **Related Module**: For basic transforms (mirror, translate, rotate, scale), see [`Transformation`](../transformation/README.md). Use `Orientation` for derived transforms computed from geometry analysis (best-fit planes, canonical positioning, relative orientation between geometries).

---

## API

```csharp
Result<T> Execute<T>(T geometry, Operation operation, IGeometryContext context) where T : GeometryBase
Result<OptimizationResult> OptimizeOrientation(Brep brep, OptimizationCriteria criteria, IGeometryContext context)
Result<RelativeOrientationResult> ComputeRelativeOrientation(GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context)
Result<PatternDetectionResult> DetectAndAlign(GeometryBase[] geometries, IGeometryContext context)
```

---

## Operations/Types

**Operations**: `ToPlane(Plane)`, `ToBestFit`, `Mirror(Plane)`, `FlipDirection`, `ToCanonical(CanonicalMode)`, `ToPoint(Point3d, CentroidMode)`, `ToCurveFrame(Curve, double)`, `ToSurfaceFrame(Surface, double, double)`, `ToVector(Vector3d, Vector3d?, Point3d?)`

**CanonicalMode**: `WorldXY`, `WorldYZ`, `WorldXZ`, `AreaCentroid`, `VolumeCentroid`

**CentroidMode**: `BoundingBoxCentroid`, `MassCentroid`

**OptimizationCriteria**: `CompactCriteria`, `CenteredCriteria`, `FlatnessCriteria`, `CanonicalCriteria`

**SymmetryType**: `NoSymmetry`, `MirrorSymmetry`, `RotationalSymmetry`

**RelationshipType**: `Parallel`, `Perpendicular`, `Oblique`

**PatternType**: `LinearPattern`, `RadialPattern`, `NoPattern`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Canonical alignment to world XY
Result<Surface> canonical = Orientation.Execute(
    geometry: surface,
    operation: new Orientation.ToCanonical(Mode: new Orientation.WorldXY()),
    context: context);

// Centroid to origin
Result<Brep> centered = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToPoint(Target: Point3d.Origin, CentroidType: new Orientation.MassCentroid()),
    context: context);

// Optimization
Result<Orientation.OptimizationResult> compact = Orientation.OptimizeOrientation(
    brep: brep,
    criteria: new Orientation.CompactCriteria(),
    context: context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Degeneracy` (curves), `V.Standard | V.BoundingBox` (surfaces), `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes)
- **Errors**: `E.Geometry.InvalidGeometry`, `E.Geometry.UnsupportedGeometryType`, `E.Geometry.FrameExtractionFailed`, `E.Geometry.InvalidPlane`

---

## Internals

**Files**: `Orientation.cs` (API, 173 LOC), `OrientationCore.cs` (dispatch), `OrientationCompute.cs` (algorithms), `OrientationConfig.cs` (config, 168 LOC)

**Dispatch**: `FrozenDictionary<Type, OrientationOperationMetadata>` and geometry-type validation modes

**Best-fit plane**: PCA with minimum 3 points; RMS residual threshold 1e-3

**Pattern detection**: Minimum 3 instances; anomaly threshold 0.5× deviation

**Optimization scoring**: Weights 0.4 (compactness) + 0.4 (centering) + 0.2 (flatness); low-profile threshold 0.25

**Symmetry detection**: 36 rotation samples for curve analysis

**Performance**: O(1) dispatch; O(n) PCA for n points; O(n²) pairwise pattern detection

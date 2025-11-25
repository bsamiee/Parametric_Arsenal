# Geometry Intersection and Analysis

Polymorphic geometry intersection with classification, near-miss detection, and stability analysis.

---

## API

```csharp
Result<IntersectionOutput> Execute(Request request, IGeometryContext context)
Result<ClassificationResult> Classify(IntersectionOutput output, GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context)
Result<NearMissResult> FindNearMisses(GeometryBase geometryA, GeometryBase geometryB, double searchRadius, IGeometryContext context)
Result<StabilityResult> AnalyzeStability(IntersectionOutput baseIntersection, GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context)
```

---

## Operations/Types

**Request Types**: `General(object, object, IntersectionSettings?)`, `PointProjection(Point3d[], object, Vector3d?, bool)`, `RayShoot(Ray3d, GeometryBase[], int)`

**IntersectionType**: `Tangent`, `Transverse`, `Unknown`

**IntersectionOutput**: `Points`, `Curves`, `ParametersA`, `ParametersB`, `FaceIndices`, `Sections`

**ClassificationResult**: `Type`, `ApproachAngles`, `IsGrazing`, `BlendScore`

**NearMissResult**: `LocationsA`, `LocationsB`, `Distances`

**StabilityResult**: `StabilityScore`, `PerturbationSensitivity`, `UnstableFlags`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// General intersection
Result<Intersection.IntersectionOutput> result = Intersection.Execute(
    request: new Intersection.General(GeometryA: curve1, GeometryB: curve2),
    context: context);

// Point projection
Result<Intersection.IntersectionOutput> projection = Intersection.Execute(
    request: new Intersection.PointProjection(Points: points, Targets: breps, Direction: Vector3d.ZAxis, WithIndices: true),
    context: context);

// Classification
Result<Intersection.ClassificationResult> classification = Intersection.Classify(
    output: intersectionOutput, geometryA: curve, geometryB: surface, context: context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: Per geometry pair from dispatch table (`V.Standard | V.Degeneracy`, `V.Standard | V.Topology`, `V.MeshSpecific`, etc.)
- **Errors**: `E.Geometry.UnsupportedIntersection`, `E.Geometry.IntersectionFailed`, `E.Geometry.InvalidProjection`, `E.Geometry.ClassificationFailed`, `E.Geometry.InvalidSearchRadius`

---

## Internals

**Files**: `Intersection.cs` (API, 144 LOC), `IntersectionCore.cs` (dispatch, 359 LOC), `IntersectionCompute.cs` (algorithms, 272 LOC), `IntersectionConfig.cs` (config, 117 LOC)

**Supported pairs**: 38 geometry combinations including Curve×Curve, Curve×Surface, Curve×Brep, Brep×Brep, Mesh×Mesh, Mesh×Ray3d, Line×Sphere, Plane×Plane, etc.

**Classification thresholds**: Tangent <5°, grazing <15°; blend scores: tangent 1.0, perpendicular 0.5

**Near-miss detection**: Tolerance ×10 multiplier; curve samples 3 min, Brep samples 8 min, max 1000

**Stability analysis**: 8 spherical perturbation samples, 0.1% perturbation factor, golden ratio distribution

**Performance**: O(1) FrozenDictionary dispatch; automatic type swapping for symmetric pairs

**Curves disposal**: Consumer must dispose `Curve` instances in `IntersectionOutput.Curves`

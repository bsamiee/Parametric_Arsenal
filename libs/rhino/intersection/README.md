# Geometry Intersection and Analysis Operations

Polymorphic geometry intersection with classification, near-miss detection, and stability analysis for parametric design workflows.

---

## API Surface

### Intersection Execution

```csharp
Result<IntersectionOutput> Execute(Request request, IGeometryContext context)
```

**Request Types**:
- `General(object, object, IntersectionSettings?)` - Geometry pair intersection
- `PointProjection(Point3d[], object, Vector3d?, bool)` - Point projection to Brep/Mesh collections
- `RayShoot(Ray3d, GeometryBase[], int)` - Ray shooting with hit limit

### Intersection Classification

```csharp
Result<ClassificationResult> Classify(
    IntersectionOutput output,
    GeometryBase geometryA,
    GeometryBase geometryB,
    IGeometryContext context)
```

**Output**: `ClassificationResult(IntersectionType, double[], bool, double)` with type (tangent/transverse/unknown), approach angles, grazing flag, blend quality score.

### Near-Miss Detection

```csharp
Result<NearMissResult> FindNearMisses(
    GeometryBase geometryA,
    GeometryBase geometryB,
    double searchRadius,
    IGeometryContext context)
```

**Output**: `NearMissResult(Point3d[], Point3d[], double[])` with locations on A/B and distances within tolerance band.

### Stability Analysis

```csharp
Result<StabilityResult> AnalyzeStability(
    IntersectionOutput baseIntersection,
    GeometryBase geometryA,
    GeometryBase geometryB,
    IGeometryContext context)
```

**Output**: `StabilityResult(double, double, bool[])` with stability score, perturbation sensitivity, unstable flags.

---

## Supported Geometry Pairs

### Curve-Curve Intersections
- `Curve × Curve` - General curve intersection with overlap detection
- `NurbsCurve × NurbsCurve` - NURBS-specific intersection
- `PolyCurve × Curve` - Polycurve segment intersection

### Curve-Surface Intersections
- `Curve × Surface` - General surface intersection
- `Curve × NurbsSurface` - NURBS surface intersection
- `Curve × Brep` - Brep face intersection with topology
- `Curve × BrepFace` - Single face intersection
- `Curve × Extrusion` - Extrusion intersection
- `Curve × Plane` - Planar section
- `Curve × Line` - Line intersection with overlap

### Surface-Surface Intersections
- `Brep × Brep` - Full Brep intersection with curves/points
- `Brep × Surface` - Brep with surface
- `Brep × Plane` - Planar section curves
- `Surface × Surface` - General surface intersection
- `NurbsSurface × NurbsSurface` - NURBS-NURBS intersection
- `Extrusion × Extrusion` - Extrusion pair intersection

### Mesh Intersections
- `Mesh × Mesh` - Accurate mesh-mesh with polylines
- `Mesh × Ray3d` - Ray intersection with distance
- `Mesh × Plane` - Mesh section polylines
- `Mesh × Line` - Line intersection (sorted/unsorted)
- `Mesh × PolylineCurve` - Polyline intersection

### Primitive Intersections
- `Line × Line` - Line-line intersection
- `Line × Plane` - Line-plane intersection
- `Line × BoundingBox` - Box intersection
- `Line × Sphere` - Sphere intersection
- `Line × Cylinder` - Cylinder intersection
- `Line × Circle` - Circle intersection
- `Plane × Plane` - Plane-plane intersection line
- `Plane × Circle` - Circle-plane intersection
- `Plane × Sphere` - Sphere-plane intersection
- `Plane × BoundingBox` - Box-plane intersection
- `Sphere × Sphere` - Sphere-sphere intersection
- `Circle × Circle` - Circle-circle intersection
- `Arc × Arc` - Arc-arc intersection

### Projection Operations
- `Point3d[] → Brep[]` - Point projection to Breps with optional direction/indices
- `Point3d[] → Mesh[]` - Point projection to Meshes with optional direction/indices

### Ray Shooting
- `Ray3d → GeometryBase[]` - Ray shooting through collection with max hits

---

## Usage Examples

### General Intersection

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Curve-Curve intersection
Result<Intersection.IntersectionOutput> result = Intersection.Execute(
    request: new Intersection.General(
        GeometryA: curve1,
        GeometryB: curve2,
        Settings: new Intersection.IntersectionSettings(
            Tolerance: 0.001,
            Sorted: false)),
    context: context);

result.Match(
    onSuccess: output => {
        Console.WriteLine($"Points: {output.Points.Count}, Curves: {output.Curves.Count}");
        foreach (Curve overlapCurve in output.Curves) {
            // Curves are IDisposable - consumer must dispose
            overlapCurve.Dispose();
        }
    },
    onFailure: error => Handle(error));

// Brep-Plane section
Result<Intersection.IntersectionOutput> section = Intersection.Execute(
    request: new Intersection.General(brep, plane),
    context: context);
```

### Point Projection

```csharp
// Project points to Brep collection with direction
Result<Intersection.IntersectionOutput> projection = Intersection.Execute(
    request: new Intersection.PointProjection(
        Points: [point1, point2, point3,],
        Targets: breps,
        Direction: new Vector3d(0, 0, -1),
        WithIndices: true),
    context: context);

projection.Match(
    onSuccess: output => {
        for (int i = 0; i < output.Points.Count; i++) {
            Point3d projectedPoint = output.Points[i];
            int brepIndex = output.FaceIndices[i];
            Console.WriteLine($"Point {i} → Brep[{brepIndex}] at {projectedPoint}");
        }
    },
    onFailure: error => Handle(error));

// Mesh projection without direction (closest point)
Result<Intersection.IntersectionOutput> meshProjection = Intersection.Execute(
    request: new Intersection.PointProjection(
        Points: points,
        Targets: meshes,
        Direction: null,
        WithIndices: false),
    context: context);
```

### Ray Shooting

```csharp
Ray3d ray = new(origin: new Point3d(0, 0, 10), direction: new Vector3d(0, 0, -1));

Result<Intersection.IntersectionOutput> hits = Intersection.Execute(
    request: new Intersection.RayShoot(
        Ray: ray,
        Targets: geometries,
        MaxHits: 5),
    context: context);

hits.Match(
    onSuccess: output => {
        foreach (Point3d hitPoint in output.Points) {
            Console.WriteLine($"Hit at {hitPoint}");
        }
    },
    onFailure: error => Handle(error));
```

### Intersection Classification

```csharp
Result<Intersection.IntersectionOutput> intersection = Intersection.Execute(
    request: new Intersection.General(curve, surface),
    context: context);

Result<Intersection.ClassificationResult> classification = intersection.Bind(output =>
    Intersection.Classify(
        output: output,
        geometryA: curve,
        geometryB: surface,
        context: context));

classification.Match(
    onSuccess: result => {
        string typeStr = result.Type switch {
            Intersection.IntersectionType.Tangent => "Tangent",
            Intersection.IntersectionType.Transverse => "Transverse",
            Intersection.IntersectionType.Unknown => "Unknown",
            _ => "Unrecognized",
        };
        Console.WriteLine($"{typeStr}, Grazing={result.IsGrazing}, Blend={result.BlendScore:F3}");
        Console.WriteLine($"Approach angles: {string.Join(", ", result.ApproachAngles.Select(a => $"{RhinoMath.ToDegrees(a):F1}°"))}");
    },
    onFailure: error => Handle(error));
```

### Near-Miss Detection

```csharp
// Find near-misses within tolerance band
Result<Intersection.NearMissResult> nearMisses = Intersection.FindNearMisses(
    geometryA: curve1,
    geometryB: curve2,
    searchRadius: 0.5,
    context: context);

nearMisses.Match(
    onSuccess: result => {
        for (int i = 0; i < result.LocationsA.Length; i++) {
            Console.WriteLine($"Near-miss {i}: A={result.LocationsA[i]}, B={result.LocationsB[i]}, Distance={result.Distances[i]:F6}");
        }
    },
    onFailure: error => Handle(error));
```

### Stability Analysis

```csharp
Result<Intersection.IntersectionOutput> intersection = Intersection.Execute(
    request: new Intersection.General(curve, surface),
    context: context);

Result<Intersection.StabilityResult> stability = intersection.Bind(output =>
    Intersection.AnalyzeStability(
        baseIntersection: output,
        geometryA: curve,
        geometryB: surface,
        context: context));

stability.Match(
    onSuccess: result => {
        Console.WriteLine($"Stability: {result.StabilityScore:F3}, Sensitivity: {result.PerturbationSensitivity:F3}");
        int unstableCount = result.UnstableFlags.Count(static flag => flag);
        Console.WriteLine($"Unstable points: {unstableCount}/{result.UnstableFlags.Length}");
    },
    onFailure: error => Handle(error));
```

---

## Algebraic Domain Types

### Request Hierarchy

Base type enabling type-safe polymorphic dispatch for intersection operations.

```csharp
abstract record Request;

sealed record General(
    object GeometryA,
    object GeometryB,
    IntersectionSettings? Settings = null) : Request;

sealed record PointProjection(
    Point3d[] Points,
    object Targets,
    Vector3d? Direction = null,
    bool WithIndices = false) : Request;

sealed record RayShoot(
    Ray3d Ray,
    GeometryBase[] Targets,
    int MaxHits = 1) : Request;
```

### IntersectionType Hierarchy

Discriminated union for intersection classification with singleton instances.

```csharp
abstract record IntersectionType;

sealed record Tangent : IntersectionType {
    static Tangent Instance { get; }  // Singleton
}

sealed record Transverse : IntersectionType {
    static Transverse Instance { get; }  // Singleton
}

sealed record Unknown : IntersectionType {
    static Unknown Instance { get; }  // Singleton
}
```

### Configuration Types

```csharp
readonly record struct IntersectionSettings(
    double? Tolerance = null,
    bool Sorted = false);
```

### Result Types

```csharp
readonly record struct IntersectionOutput(
    IReadOnlyList<Point3d> Points,
    IReadOnlyList<Curve> Curves,
    IReadOnlyList<double> ParametersA,
    IReadOnlyList<double> ParametersB,
    IReadOnlyList<int> FaceIndices,
    IReadOnlyList<Polyline> Sections) {
    static readonly IntersectionOutput Empty;
}

sealed record ClassificationResult(
    IntersectionType Type,
    double[] ApproachAngles,
    bool IsGrazing,
    double BlendScore);

sealed record NearMissResult(
    Point3d[] LocationsA,
    Point3d[] LocationsB,
    double[] Distances);

sealed record StabilityResult(
    double StabilityScore,
    double PerturbationSensitivity,
    bool[] UnstableFlags);
```

---

## Architecture Integration

### Result Monad

All operations return `Result<T>` from `libs/core/results/Result.cs` for consistent error handling and monadic composition.

```csharp
Result<Intersection.IntersectionOutput> result = Intersection.Execute(request, context);

result
    .Bind(output => Intersection.Classify(output, geomA, geomB, context))
    .Match(
        onSuccess: classification => Process(classification),
        onFailure: error => Handle(error));
```

### IGeometryContext

Provides tolerance settings and RhinoDoc integration. See `libs/core/context/IGeometryContext.cs`.

```csharp
IGeometryContext context = new GeometryContext(
    doc: RhinoDoc.ActiveDoc,
    absoluteTolerance: 0.001,
    angleTolerance: 0.01);
```

### Validation Modes

Operations automatically validate inputs using `ValidationRules` expression trees. See `libs/core/validation/V.cs`.

- `V.None` - Point arrays, Ray3d
- `V.Standard` - Lines, planes, primitives
- `V.Standard | V.Degeneracy` - Curves, polycurves
- `V.Standard | V.NurbsGeometry` - NURBS curves/surfaces
- `V.Standard | V.UVDomain` - Surfaces
- `V.Standard | V.Topology` - Breps, BrepFaces
- `V.Standard | V.ExtrusionGeometry` - Extrusions
- `V.MeshSpecific` - Meshes

### Error Codes

All errors use `E.*` constants from `libs/core/errors/E.cs`.

- `E.Geometry.UnsupportedIntersection` - Geometry pair not supported
- `E.Geometry.IntersectionFailed` - Intersection computation failed
- `E.Geometry.InvalidProjection` - Invalid projection parameters
- `E.Geometry.InvalidMaxHits` - Invalid max hits parameter
- `E.Geometry.ClassificationFailed` - Classification analysis failed
- `E.Geometry.InsufficientIntersectionData` - Insufficient data for classification
- `E.Geometry.InvalidSearchRadius` - Invalid near-miss search radius
- `E.Validation.GeometryInvalid` - Input geometry validation failed

---

## Configuration Constants

### Classification Thresholds

```csharp
TangentAngleThreshold = 5° (0.0873 rad)    // Tangent vs transverse threshold
GrazingAngleThreshold = 15° (0.2618 rad)   // Grazing intersection threshold
```

### Blend Quality Scores

```csharp
TangentBlendScore = 1.0                     // Curve-curve tangent
PerpendicularBlendScore = 0.5               // Curve-curve transverse
CurveSurfaceTangentBlendScore = 0.8         // Curve-surface tangent
CurveSurfacePerpendicularBlendScore = 0.4   // Curve-surface perpendicular
```

### Near-Miss Detection

```csharp
NearMissToleranceMultiplier = 10.0          // Minimum distance = tolerance × 10
MinCurveNearMissSamples = 3                 // Minimum samples per curve
MinBrepNearMissSamples = 8                  // Minimum samples per Brep
MaxNearMissSamples = 1000                   // Maximum sample budget
MinSamplesPerFace = 3                       // Minimum samples per Brep face
```

### Stability Analysis

```csharp
StabilityPerturbationFactor = 0.001         // Perturbation magnitude (0.1%)
StabilitySampleCount = 8                    // Spherical perturbation samples
UnstableCountDeltaThreshold = 1.0           // Unstable if point count changes
```

---

## Implementation Notes

### Performance

- **FrozenDictionary dispatch**: O(1) type pair lookup in `IntersectionConfig.PairOperations`
- **UnifiedOperation**: Automatic validation and error handling via `libs/core/operations`
- **Hot paths**: Direct RhinoCommon API calls, minimal allocations
- **Curve disposals**: Consumer must dispose `Curve` instances in `IntersectionOutput.Curves`

### Dispatch Strategy

Type-based dispatch via `IntersectionCore._strategies` frozen dictionary:
- Keys: `(Type GeometryA, Type GeometryB)`
- Values: `IntersectionStrategy(Executor, V ModeA, V ModeB)`
- Automatic type swapping for symmetric operations (e.g., `Surface × Curve` → `Curve × Surface`)

### Classification Algorithm

**Curve-Curve**: Circular mean of tangent approach angles
- Tangent: Average angle <5° or >(180°-5°)
- Transverse: Average angle ≥5° and ≤(180°-5°)
- Grazing: Any angle <15° or >(180°-15°)

**Curve-Surface**: Angle between curve tangent and surface normal
- Tangent: Average deviation from parallel <5°
- Transverse: Average deviation ≥5°
- Grazing: Any angle <15° from parallel

### Near-Miss Sampling

**Curve pairs**: Bidirectional closest point search with adaptive sampling (length/radius)  
**Curve-Surface**: Curve sampling with surface projection  
**Brep pairs**: Area-weighted face sampling with UV parameter distribution

### Stability Analysis

Spherical perturbation sampling around each intersection point:
1. Generate 8 perturbation directions using golden ratio spherical distribution
2. Perturb geometries by 0.1% of characteristic size
3. Recompute intersection for each perturbation
4. Score stability: `1.0 - (stddev of point counts / mean point count)`
5. Flag points unstable if any perturbation changes point count

---

## File Organization

```
intersection/
├── Intersection.cs          # Public API with algebraic domain types (144 LOC)
├── IntersectionCore.cs      # UnifiedOperation orchestration (359 LOC)
├── IntersectionCompute.cs   # Classification/near-miss/stability algorithms (272 LOC)
└── IntersectionConfig.cs    # FrozenDictionary dispatch tables and constants (117 LOC)
```

**Files**: 4 (✓ within limit)  
**Types**: 10 (Request hierarchy + IntersectionType hierarchy + result types)  
**LOC**: ~892 total

---

## Dependencies

- `libs/core/results` - Result monad, ResultFactory
- `libs/core/context` - IGeometryContext, GeometryContext
- `libs/core/validation` - V flags, ValidationRules
- `libs/core/errors` - E error registry, SystemError
- `libs/core/operations` - UnifiedOperation, OperationConfig
- `RhinoCommon` - Geometry types, Rhino.Geometry.Intersect namespace

---

## Testing

See `test/rhino/intersection/` for NUnit + Rhino.Testing integration tests.

```bash
dotnet test --filter "FullyQualifiedName~Arsenal.Rhino.Intersection"
```

---

## See Also

- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch engine
- `libs/core/validation/ValidationRules.cs` - Expression tree validation
- `libs/rhino/spatial/` - RTree spatial indexing
- `libs/rhino/extraction/` - Point and curve extraction
- `libs/rhino/analysis/` - Differential geometry analysis
- `CLAUDE.md` - Complete coding standards and architectural patterns

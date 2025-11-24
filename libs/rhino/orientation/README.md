# Geometry Orientation and Canonical Alignment

Polymorphic orientation operations with canonical alignment, best-fit computation, pattern detection, and optimization for RhinoCommon geometry.

---

## API Surface

### Primary Operations

```csharp
Result<T> Execute<T>(T geometry, Operation operation, IGeometryContext context) where T : GeometryBase
Result<OptimizationResult> OptimizeOrientation(Brep brep, OptimizationCriteria criteria, IGeometryContext context)
Result<RelativeOrientationResult> ComputeRelativeOrientation(GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context)
Result<PatternDetectionResult> DetectAndAlign(GeometryBase[] geometries, IGeometryContext context)
```

### Operation Types

**Basic Alignment**:
- `ToPlane(Plane Target)` - Align from source plane to target plane
- `ToBestFit` - Orient to best-fit plane via PCA
- `ToCanonical(CanonicalMode Mode)` - Canonical world orientation
- `ToPoint(Point3d Target, CentroidMode CentroidType)` - Translate centroid to point

**Frame-Based**:
- `ToCurveFrame(Curve Curve, double Parameter)` - Orient to curve frame at t
- `ToSurfaceFrame(Surface Surface, double U, double V)` - Orient to surface frame at UV
- `ToVector(Vector3d Target, Vector3d? Source, Point3d? Anchor)` - Rotate to align vectors

**Transformations**:
- `Mirror(Plane MirrorPlane)` - Mirror across plane
- `FlipDirection` - Flip curve direction or surface/mesh normals

### Canonical Modes

Algebraic hierarchy for world-aligned positioning:

- `WorldXY` - Align bounding box center to XY plane
- `WorldYZ` - Align bounding box center to YZ plane
- `WorldXZ` - Align bounding box center to XZ plane
- `AreaCentroid` - Translate area centroid to world origin
- `VolumeCentroid` - Translate volume centroid to world origin

### Centroid Modes

Algebraic hierarchy for centroid computation:

- `BoundingBoxCentroid` - Use bounding box center
- `MassCentroid` - Use mass properties centroid (area/volume)

### Optimization Criteria

Algebraic hierarchy for orientation optimization:

- `CompactCriteria` - Minimize bounding box diagonal
- `CenteredCriteria` - Center centroid on XY plane
- `FlatnessCriteria` - Maximize flatness (degenerate dimensions)
- `CanonicalCriteria` - Canonical positioning with multiple factors

### Analysis Result Types

**OptimizationResult**:
```csharp
sealed record OptimizationResult(
    Transform OptimalTransform,
    double Score,
    OptimizationCriteria[] CriteriaSatisfied)
```

**RelativeOrientationResult**:
```csharp
sealed record RelativeOrientationResult(
    Transform RelativeTransform,
    double Twist,
    double Tilt,
    SymmetryType Symmetry,
    RelationshipType Relationship)
{
    double TwistDegrees { get; }  // Radians → degrees
    double TiltDegrees { get; }   // Radians → degrees
}
```

**PatternDetectionResult**:
```csharp
sealed record PatternDetectionResult(
    PatternType Pattern,
    Transform[] IdealTransforms,
    int[] Anomalies,
    double Deviation)
```

### Classification Types

**SymmetryType**: `NoSymmetry`, `MirrorSymmetry`, `RotationalSymmetry`  
**RelationshipType**: `Parallel`, `Perpendicular`, `Oblique`  
**PatternType**: `LinearPattern`, `RadialPattern`, `NoPattern`

---

## Usage Examples

### Basic Alignment

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Align to target plane
Result<Brep> aligned = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToPlane(Target: Plane.WorldXY),
    context: context);

// Best-fit plane via PCA
Result<Mesh> bestFit = Orientation.Execute(
    geometry: mesh,
    operation: new Orientation.ToBestFit(),
    context: context);

// Canonical alignment to world XY
Result<Surface> canonical = Orientation.Execute(
    geometry: surface,
    operation: new Orientation.ToCanonical(Mode: new Orientation.WorldXY()),
    context: context);
```

### Centroid Operations

```csharp
// Translate area centroid to origin
Result<Brep> centered = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToPoint(
        Target: Point3d.Origin,
        CentroidType: new Orientation.MassCentroid()),
    context: context);

// Volume centroid to specific point
Result<Brep> positioned = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToPoint(
        Target: new Point3d(100, 50, 0),
        CentroidType: new Orientation.MassCentroid()),
    context: context);
```

### Frame-Based Orientation

```csharp
// Orient to curve frame at parameter
Result<Brep> curveAligned = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToCurveFrame(
        Curve: railCurve,
        Parameter: 0.5),
    context: context);

// Orient to surface frame at UV
Result<Mesh> surfaceAligned = Orientation.Execute(
    geometry: mesh,
    operation: new Orientation.ToSurfaceFrame(
        Surface: targetSurface,
        U: 0.5,
        V: 0.5),
    context: context);
```

### Vector Alignment

```csharp
// Rotate to align vectors (auto-detect source)
Result<Curve> rotated = Orientation.Execute(
    geometry: curve,
    operation: new Orientation.ToVector(
        Target: Vector3d.ZAxis,
        Source: null,
        Anchor: null),
    context: context);

// Explicit source and anchor
Result<Brep> aligned = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToVector(
        Target: new Vector3d(1, 1, 0),
        Source: Vector3d.XAxis,
        Anchor: Point3d.Origin),
    context: context);
```

### Mirror and Flip

```csharp
// Mirror across plane
Result<Surface> mirrored = Orientation.Execute(
    geometry: surface,
    operation: new Orientation.Mirror(MirrorPlane: Plane.WorldYZ),
    context: context);

// Flip curve direction or surface normals
Result<Curve> flipped = Orientation.Execute(
    geometry: curve,
    operation: new Orientation.FlipDirection(),
    context: context);
```

### Optimization

```csharp
// Minimize bounding box diagonal
Result<OptimizationResult> compact = Orientation.OptimizeOrientation(
    brep: brep,
    criteria: new Orientation.CompactCriteria(),
    context: context);

OptimizationResult result = compact.Match(
    onSuccess: r => r,
    onFailure: _ => throw new InvalidOperationException());

Brep optimized = brep.Duplicate() as Brep ?? throw new InvalidOperationException();
_ = optimized.Transform(result.OptimalTransform);

// Canonical optimization (multi-factor)
Result<OptimizationResult> canonical = Orientation.OptimizeOrientation(
    brep: brep,
    criteria: new Orientation.CanonicalCriteria(),
    context: context);
```

### Relative Orientation Analysis

```csharp
// Compute relative orientation between geometries
Result<RelativeOrientationResult> relative = Orientation.ComputeRelativeOrientation(
    geometryA: brep1,
    geometryB: brep2,
    context: context);

RelativeOrientationResult analysis = relative.Match(
    onSuccess: r => r,
    onFailure: _ => throw new InvalidOperationException());

double twistDegrees = analysis.TwistDegrees;
double tiltDegrees = analysis.TiltDegrees;
SymmetryType symmetry = analysis.Symmetry;  // NoSymmetry, MirrorSymmetry, RotationalSymmetry
RelationshipType relationship = analysis.Relationship;  // Parallel, Perpendicular, Oblique
```

### Pattern Detection

```csharp
// Detect and align pattern in geometry array
Result<PatternDetectionResult> pattern = Orientation.DetectAndAlign(
    geometries: brepArray,
    context: context);

PatternDetectionResult detection = pattern.Match(
    onSuccess: r => r,
    onFailure: _ => throw new InvalidOperationException());

PatternType patternType = detection.Pattern;  // LinearPattern, RadialPattern, NoPattern
Transform[] idealTransforms = detection.IdealTransforms;
int[] anomalyIndices = detection.Anomalies;
double deviation = detection.Deviation;
```

---

## Integration with libs/core

### Result Monad

All operations return `Result<T>` for composable error handling:

```csharp
Result<Brep> processed = Orientation.Execute(
    geometry: brep,
    operation: new Orientation.ToCanonical(Mode: new Orientation.WorldXY()),
    context: context)
    .Bind(oriented => Orientation.Execute(
        geometry: oriented,
        operation: new Orientation.Mirror(MirrorPlane: Plane.WorldYZ),
        context: context))
    .Map(mirrored => {
        Brep copy = mirrored.Duplicate() as Brep ?? throw new InvalidOperationException();
        _ = copy.Flip();
        return copy;
    });
```

### Validation

Operations use `libs/core/validation/ValidationRules.cs` with mode-specific validation:

- **Standard**: `V.Standard` - Validity, degeneracy, null checks
- **Bounding box**: `V.BoundingBox` - Valid bounding box required
- **Mass properties**: `V.MassProperties` - Area/volume centroid computation
- **Topology**: `V.Topology` - Manifold edges, valid face connectivity
- **UV domain**: `V.UVDomain` - Surface UV parameter validation

### Error Handling

Orientation errors use `libs/core/errors/E.cs` constants:

- `E.Geometry.InvalidGeometry` - Invalid geometry input
- `E.Geometry.UnsupportedGeometryType` - Operation not supported for type
- `E.Geometry.FrameExtractionFailed` - Cannot extract plane/frame
- `E.Geometry.InvalidPlane` - Invalid target plane
- `E.Validation.InvalidParameter` - Invalid parameter value
- `E.Validation.OutOfRange` - Parameter out of valid range

---

## Implementation Architecture

### File Organization

**Orientation.cs** (174 LOC): Public API with algebraic domain types  
**OrientationCore.cs**: Dispatch engine and core execution  
**OrientationCompute.cs**: Orientation computation algorithms  
**OrientationConfig.cs** (169 LOC): FrozenDictionary dispatch tables, validation metadata

### Dispatch Pattern

Operations use `FrozenDictionary` dispatch with `libs/core/operations/UnifiedOperation.cs` integration:

```csharp
// From OrientationConfig.cs
internal static readonly FrozenDictionary<Type, OrientationOperationMetadata> Operations =
    new Dictionary<Type, OrientationOperationMetadata> {
        [typeof(Orientation.ToPlane)] = new(V.Standard, "Orientation.ToPlane"),
        [typeof(Orientation.ToCanonical)] = new(V.Standard | V.BoundingBox, "Orientation.ToCanonical"),
        [typeof(Orientation.ToPoint)] = new(V.Standard, "Orientation.ToPoint"),
        [typeof(Orientation.ToVector)] = new(V.Standard | V.BoundingBox, "Orientation.ToVector"),
    }.ToFrozenDictionary();
```

### Validation Strategy

Type-specific validation modes ensure correctness:

```csharp
// From OrientationConfig.cs
internal static readonly FrozenDictionary<Type, V> GeometryValidation =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
        [typeof(Surface)] = V.Standard | V.BoundingBox,
        [typeof(Brep)] = V.Standard | V.Topology,
        [typeof(Mesh)] = V.Standard | V.MeshSpecific,
    }.ToFrozenDictionary();
```

---

## Performance Characteristics

- **Execute operations**: O(1) dispatch via FrozenDictionary, O(geometry complexity) transform
- **Best-fit plane**: O(n) PCA computation for n control points
- **Optimization**: O(k × geometry complexity) for k candidate orientations
- **Pattern detection**: O(n²) pairwise comparison for n geometries
- **Relative orientation**: O(geometry complexity) for plane extraction and comparison

---

## Supported Geometry Types

All operations support polymorphic dispatch across:

- `Curve`, `NurbsCurve`, `PolyCurve`, `LineCurve`, `ArcCurve`
- `Surface`, `NurbsSurface`, `PlaneSurface`
- `Brep`, `Extrusion`
- `Mesh`
- `PointCloud`

Operation-specific support verified at runtime via validation rules.

---

## Related Libraries

- `libs/core/results/` - Result monad and error handling
- `libs/core/validation/` - ValidationRules expression trees
- `libs/core/operations/` - UnifiedOperation dispatch engine
- `libs/core/context/` - IGeometryContext and tolerance management
- `libs/core/errors/` - Centralized error registry (E.*)
- `libs/rhino/extraction/` - Point/curve extraction for frame computation

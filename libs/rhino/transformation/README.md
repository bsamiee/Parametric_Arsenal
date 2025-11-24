# Affine Transforms, Arrays, and Deformations

Unified polymorphic dispatch for affine transformations, array operations, and SpaceMorph deformations with algebraic operation types.

---

## API Overview

### Core Methods

```csharp
// Transform operations
Result<T> Apply<T>(T geometry, TransformOperation operation, IGeometryContext context, bool enableDiagnostics = false) where T : GeometryBase
Result<T> Mirror<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase
Result<T> Project<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase
Result<T> Translate<T>(T geometry, Vector3d motion, IGeometryContext context) where T : GeometryBase
Result<T> Translate<T>(T geometry, Point3d start, Point3d end, IGeometryContext context) where T : GeometryBase
Result<T> Scale<T>(T geometry, Point3d anchor, double factor, IGeometryContext context) where T : GeometryBase
Result<T> Scale<T>(T geometry, Plane plane, double xScale, double yScale, double zScale, IGeometryContext context) where T : GeometryBase
Result<T> Rotate<T>(T geometry, double angleRadians, Vector3d axis, Point3d center, IGeometryContext context) where T : GeometryBase
Result<T> Rotate<T>(T geometry, Vector3d startDirection, Vector3d endDirection, Point3d center, IGeometryContext context) where T : GeometryBase
Result<T> ChangeBasis<T>(T geometry, Plane fromPlane, Plane toPlane, IGeometryContext context) where T : GeometryBase
Result<T> PlaneToPlane<T>(T geometry, Plane fromPlane, Plane toPlane, IGeometryContext context) where T : GeometryBase
Result<T> Shear<T>(T geometry, Plane plane, Vector3d direction, double angle, IGeometryContext context) where T : GeometryBase
Result<T> Compound<T>(T geometry, TransformOperation[] operations, IGeometryContext context) where T : GeometryBase
Result<T> Blend<T>(T geometry, TransformOperation first, TransformOperation second, double blendFactor, IGeometryContext context) where T : GeometryBase
Result<T> Interpolate<T>(T geometry, TransformOperation start, TransformOperation end, double parameter, IGeometryContext context) where T : GeometryBase

// Array operations
Result<IReadOnlyList<T>> ApplyArray<T>(T geometry, ArrayOperation operation, IGeometryContext context, bool enableDiagnostics = false) where T : GeometryBase

// Morph operations
Result<T> Morph<T>(T geometry, MorphOperation operation, IGeometryContext context) where T : GeometryBase

// Decomposition
Result<DecomposedTransform> Decompose(Transform matrix, IGeometryContext context)
```

---

## Quick Examples

### Basic Transforms

```csharp
// Mirror, project, translate
Result<Curve> mirrored = Transformation.Mirror(curve, Plane.WorldXY, context);
Result<Mesh> projected = Transformation.Project(mesh, Plane.WorldXY, context);
Result<Surface> moved = Transformation.Translate(surface, Point3d.Origin, new Point3d(5, 5, 0), context);

// Scale, rotate, shear
Result<Brep> scaled = Transformation.Scale(brep, Point3d.Origin, factor: 2.0, context);
Result<Mesh> rotated = Transformation.Rotate(mesh, Math.PI / 4, Vector3d.ZAxis, Point3d.Origin, context);
Result<Mesh> sheared = Transformation.Shear(mesh, Plane.WorldXY, Vector3d.XAxis, Math.PI / 6, context);

// Basis changes
Result<Curve> rebased = Transformation.ChangeBasis(curve, Plane.WorldXY, customPlane, context);
Result<Brep> oriented = Transformation.PlaneToPlane(brep, Plane.WorldXY, Plane.WorldYZ, context);
```

### Arrays

```csharp
// Linear, polar, rectangular, path
Result<IReadOnlyList<Point3d>> linear = Transformation.ApplyArray(
    point, new Transformation.LinearArray(Vector3d.XAxis, Count: 10, Spacing: 5.0), context);

Result<IReadOnlyList<Curve>> polar = Transformation.ApplyArray(
    curve, new Transformation.PolarArray(Point3d.Origin, Vector3d.ZAxis, Count: 12, TotalAngleRadians: Math.PI * 2), context);

Result<IReadOnlyList<Mesh>> grid = Transformation.ApplyArray(
    mesh, new Transformation.RectangularArray(XCount: 5, YCount: 3, ZCount: 2, XSpacing: 10.0, YSpacing: 8.0, ZSpacing: 6.0), context);

Result<IReadOnlyList<Surface>> pathArray = Transformation.ApplyArray(
    surface, new Transformation.PathArray(guideCurve, Count: 20, OrientToPath: true), context);
```

### Morphs

```csharp
// Stretch, bend, twist, taper
Result<Mesh> stretched = Transformation.Morph(mesh, new Transformation.StretchMorph(axis), context);
Result<Curve> bent = Transformation.Morph(curve, new Transformation.BendMorph(axis, AngleRadians: Math.PI / 2), context);
Result<Surface> twisted = Transformation.Morph(surface, new Transformation.TwistMorph(axis, Math.PI, Infinite: false), context);
Result<Brep> tapered = Transformation.Morph(brep, new Transformation.TaperMorph(axis, StartWidth: 10.0, EndWidth: 2.0), context);

// Flow, splop, sporph, maelstrom
Result<Mesh> flowed = Transformation.Morph(mesh, new Transformation.FlowMorph(baseCurve, targetCurve, PreserveStructure: true), context);
Result<Curve> splopped = Transformation.Morph(curve, new Transformation.SplopMorph(Plane.WorldXY, targetSurface, targetPoint), context);
Result<Brep> sporphed = Transformation.Morph(brep, new Transformation.SporphMorph(sourceSurface, targetSurface, PreserveStructure: false), context);
Result<Mesh> vortex = Transformation.Morph(mesh, new Transformation.MaelstromMorph(axis, Radius: 5.0, AngleRadians: Math.PI * 2), context);
```

### Advanced Compositions

```csharp
// Compound: sequential composition
Result<Curve> compound = Transformation.Compound(curve, [
    new Transformation.Translation(Motion: new Vector3d(10, 0, 0)),
    new Transformation.AxisRotation(Math.PI / 4, Vector3d.ZAxis, Point3d.Origin),
    new Transformation.UniformScale(Point3d.Origin, Factor: 2.0),
], context);

// Blend: weighted interpolation between two transforms
Result<Surface> blended = Transformation.Blend(
    surface,
    first: new Transformation.Translation(Motion: Vector3d.XAxis),
    second: new Transformation.UniformScale(Point3d.Origin, Factor: 2.0),
    blendFactor: 0.5,
    context);

// Interpolate: parameter t ∈ [0,1] between start and end
Result<Mesh> interpolated = Transformation.Interpolate(
    mesh,
    start: new Transformation.Translation(Motion: Vector3d.Zero),
    end: new Transformation.Translation(Motion: new Vector3d(10, 10, 0)),
    parameter: 0.75,
    context);
```

### Decomposition

```csharp
Transform matrix = Transform.Translation(new Vector3d(10, 0, 0))
    * Transform.Rotation(Math.PI / 4, Vector3d.ZAxis, Point3d.Origin)
    * Transform.Scale(Point3d.Origin, 2.0);

Result<Transformation.DecomposedTransform> trs = Transformation.Decompose(matrix, context);
trs.Match(
    onSuccess: d => Console.WriteLine($"T: {d.Translation}, R: {d.Rotation}, S: {d.Scale}, Orthogonal: {d.IsOrthogonal}"),
    onFailure: error => Console.WriteLine($"Failed: {error}"));
```

---

## Algebraic Domain Types

### Transform Operation Hierarchy

```csharp
public abstract record TransformOperation;

// Basic affine transforms
public sealed record MirrorTransform(Plane Plane) : TransformOperation;
public sealed record Translation(Vector3d Motion) : TransformOperation;
public sealed record ProjectionTransform(Plane Plane) : TransformOperation;
public sealed record UniformScale(Point3d Anchor, double Factor) : TransformOperation;
public sealed record NonUniformScale(Plane Plane, double XScale, double YScale, double ZScale) : TransformOperation;
public sealed record AxisRotation(double AngleRadians, Vector3d Axis, Point3d Center) : TransformOperation;
public sealed record VectorRotation(Vector3d Start, Vector3d End, Point3d Center) : TransformOperation;
public sealed record BasisChange(Plane From, Plane To) : TransformOperation;
public sealed record PlaneTransform(Plane From, Plane To) : TransformOperation;
public sealed record ShearTransform(Plane Plane, Vector3d Direction, double AngleRadians) : TransformOperation;
public sealed record MatrixTransform(Transform Value) : TransformOperation;

// Advanced compositions
public sealed record CompoundTransform(TransformOperation[] Operations) : TransformOperation;
public sealed record BlendedTransform(TransformOperation First, TransformOperation Second, double BlendFactor) : TransformOperation;
public sealed record InterpolatedTransform(TransformOperation Start, TransformOperation End, double Parameter) : TransformOperation;
```

### Array Operation Hierarchy

```csharp
public abstract record ArrayOperation;

public sealed record LinearArray(Vector3d Direction, int Count, double Spacing) : ArrayOperation;
public sealed record PolarArray(Point3d Center, Vector3d Axis, int Count, double TotalAngleRadians) : ArrayOperation;
public sealed record PathArray(Curve Path, int Count, bool OrientToPath) : ArrayOperation;
public sealed record RectangularArray(int XCount, int YCount, int ZCount, double XSpacing, double YSpacing, double ZSpacing) : ArrayOperation;
```

### Morph Operation Hierarchy

```csharp
public abstract record MorphOperation;

public sealed record StretchMorph(Line Axis) : MorphOperation;
public sealed record BendMorph(Line Axis, double AngleRadians) : MorphOperation;
public sealed record TwistMorph(Line Axis, double AngleRadians, bool Infinite) : MorphOperation;
public sealed record TaperMorph(Line Axis, double StartWidth, double EndWidth) : MorphOperation;
public sealed record FlowMorph(Curve BaseCurve, Curve TargetCurve, bool PreserveStructure) : MorphOperation;
public sealed record SplopMorph(Plane BasePlane, Surface TargetSurface, Point3d TargetPoint) : MorphOperation;
public sealed record MaelstromMorph(Line Axis, double Radius, double AngleRadians) : MorphOperation;
public sealed record SporphMorph(Surface SourceSurface, Surface TargetSurface, bool PreserveStructure) : MorphOperation;
```

### Result Type

```csharp
public sealed record DecomposedTransform(
    Vector3d Translation,
    Quaternion Rotation,
    Vector3d Scale,
    Transform Residual,
    bool IsOrthogonal,
    double OrthogonalityError);
```

---

## libs/core Integration

### Result Monad

All operations return `Result<T>` for composable error handling. Reference: `libs/core/results/Result.cs`

```csharp
Result<Surface> result = Transformation.Mirror(surface, Plane.WorldXY, context)
    .Bind(s => Transformation.Scale(s, Point3d.Origin, 2.0, context))
    .Match(
        onSuccess: transformed => UseTransformed(transformed),
        onFailure: error => HandleError(error));
```

### IGeometryContext

All operations require `IGeometryContext` for tolerance resolution. Reference: `libs/core/context/IGeometryContext.cs`

```csharp
IGeometryContext context = new GeometryContext(
    absoluteTolerance: 0.001,
    angleTolerance: 0.017453,
    unitSystem: UnitSystem.Millimeters);
```

### Validation Modes

Operations use `V` bitwise flags from `libs/core/validation/V.cs`:
- `V.None` - No validation
- `V.Standard` - Basic validity
- `V.Degeneracy` - Degenerate geometry checks
- `V.NurbsGeometry` - NURBS validation
- `V.Topology` - Topology checks
- Combine: `V.Standard | V.Degeneracy`

### Error Handling

Errors use `E.*` constants from `libs/core/errors/E.cs`:
- `E.Geometry.InvalidTransform` - Invalid matrix
- `E.Geometry.InvalidCount` - Invalid array count
- `E.Geometry.InvalidScale` - Scale out of range [1e-6, 1e6]
- `E.Geometry.InvalidAngle` - Angle exceeds limits
- `E.Validation.GeometryInvalid` - Failed validation
- `E.Validation.ToleranceExceeded` - Decomposition tolerance exceeded

---

## Configuration

From `libs/rhino/transformation/TransformationConfig.cs`:

```csharp
MinScaleFactor = 1e-6           // Minimum scale
MaxScaleFactor = 1e6            // Maximum scale
MaxArrayCount = 10000           // Maximum array elements
MaxTwistAngle = 62.83           // 10 full revolutions
MaxBendAngle = 6.28             // 1 full revolution
DefaultMorphTolerance = 0.001   // 1mm tolerance
MaxCompoundDepth = 100          // Maximum compound nesting
OrthogonalityTolerance = 1e-6   // Decomposition orthogonality
ShearDetectionThreshold = 0.01  // Shear detection ratio
MaxNewtonSchulzIterations = 10  // Polar decomposition iterations
```

---

## Architecture

### Dispatch Tables

FrozenDictionary dispatch for O(1) operation routing:
- `TransformOperations` - Transform metadata with validation modes
- `ArrayOperations` - Array metadata with max counts
- `MorphOperations` - Morph metadata with tolerances
- `GeometryValidation` - Geometry-type validation mode mapping

### Performance

- Transform: O(n) where n = control point count
- Array: O(k × n) where k = array count
- Morph: O(n log n) typical
- Decompose: O(1) simple, O(i) shear (i = iterations)

### Validation

Per-geometry-type validation modes:
- Curves: `V.Standard | V.Degeneracy`
- Surfaces: `V.Standard | V.UVDomain`
- Breps: `V.Standard | V.Topology`
- Meshes: `V.Standard | V.MeshSpecific`

### Algebraic Guarantees

- Immutable records
- Pure functions (`[Pure]` attribute)
- `IReadOnlyList<T>` results
- Explicit error handling via Result monad

---

## File Structure

```
libs/rhino/transformation/
├── Transformation.cs           # Public API (34 operation types, convenience methods)
├── TransformationCore.cs       # Execution engine with FrozenDictionary dispatch
├── TransformationCompute.cs    # Morph operations, TRS decomposition algorithms
└── TransformationConfig.cs     # Constants, dispatch tables, validation mappings
```

**Limits**: 4 files (at max), 34 types, ~200 LOC/file average

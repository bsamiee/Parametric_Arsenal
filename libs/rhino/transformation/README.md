# Affine Transforms, Arrays, and Deformations

Unified polymorphic dispatch for affine transformations, array operations, and SpaceMorph deformations.

> **Related Module**: For derived transforms computed from geometry analysis (best-fit planes, canonical positioning, relative orientation), see [`Orientation`](../orientation/README.md). Use `Transformation` when you know the specific transform to apply (matrix, scale, rotate, mirror).

---

## API

```csharp
Result<T> Apply<T>(T geometry, TransformOperation operation, IGeometryContext context, bool enableDiagnostics = false) where T : GeometryBase
Result<IReadOnlyList<T>> ApplyArray<T>(T geometry, ArrayOperation operation, IGeometryContext context, bool enableDiagnostics = false) where T : GeometryBase
Result<T> Morph<T>(T geometry, MorphOperation operation, IGeometryContext context) where T : GeometryBase
Result<DecomposedTransform> Decompose(Transform matrix, IGeometryContext context)
```

---

## Operations/Types

**TransformOperation**: `MirrorTransform`, `Translation`, `ProjectionTransform`, `BasisChange`, `PlaneTransform`, `MatrixTransform`, `UniformScale`, `NonUniformScale`, `AxisRotation`, `VectorRotation`, `ShearTransform`, `CompoundTransform`, `BlendedTransform`, `InterpolatedTransform`

**ArrayOperation**: `LinearArray(Vector3d, int, double)`, `PolarArray(Point3d, Vector3d, int, double)`, `PathArray(Curve, int, bool)`, `RectangularArray(int, int, int, double, double, double)`

**MorphOperation**: `StretchMorph`, `BendMorph`, `TwistMorph`, `TaperMorph`, `FlowMorph`, `SplopMorph`, `SporphMorph`, `MaelstromMorph`

**DecomposedTransform**: `Translation`, `Rotation` (Quaternion), `Scale`, `Residual`, `IsOrthogonal`, `OrthogonalityError`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Transform operations via algebraic types
Result<Brep> mirrored = Transformation.Apply(brep, new Transformation.MirrorTransform(Plane.WorldXY), context);
Result<Mesh> rotated = Transformation.Apply(mesh, new Transformation.AxisRotation(Math.PI / 4, Vector3d.ZAxis, Point3d.Origin), context);
Result<Curve> translated = Transformation.Apply(curve, new Transformation.Translation(Vector3d.XAxis * 10), context);
Result<Surface> scaled = Transformation.Apply(surface, new Transformation.UniformScale(Point3d.Origin, 2.0), context);

// Basis and plane transforms
Result<Brep> basisChanged = Transformation.Apply(brep, new Transformation.BasisChange(Plane.WorldXY, Plane.WorldYZ), context);
Result<Mesh> planeToPlane = Transformation.Apply(mesh, new Transformation.PlaneTransform(sourcePlane, targetPlane), context);

// Compound, blended, and interpolated transforms
Result<Curve> compound = Transformation.Apply(
    curve,
    new Transformation.CompoundTransform([
        new Transformation.Translation(Vector3d.XAxis),
        new Transformation.AxisRotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin),
    ]),
    context);

Result<Surface> blended = Transformation.Apply(
    surface,
    new Transformation.BlendedTransform(
        new Transformation.Translation(Vector3d.XAxis),
        new Transformation.Translation(Vector3d.YAxis),
        BlendFactor: 0.5),
    context);

// Array operations
Result<IReadOnlyList<Curve>> polar = Transformation.ApplyArray(
    curve,
    new Transformation.PolarArray(Point3d.Origin, Vector3d.ZAxis, Count: 12, TotalAngleRadians: Math.PI * 2),
    context);

Result<IReadOnlyList<Brep>> linear = Transformation.ApplyArray(
    brep,
    new Transformation.LinearArray(Vector3d.XAxis, Count: 5, Spacing: 10.0),
    context);

// Morph operations
Result<Surface> twisted = Transformation.Morph(
    surface,
    new Transformation.TwistMorph(axis, AngleRadians: Math.PI, Infinite: false),
    context);

Result<Brep> bent = Transformation.Morph(
    brep,
    new Transformation.BendMorph(axis, AngleRadians: Math.PI / 4),
    context);

// Decomposition
Result<Transformation.DecomposedTransform> trs = Transformation.Decompose(matrix, context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Degeneracy` (curves), `V.Standard | V.UVDomain` (surfaces), `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes)
- **Errors**: `E.Geometry.InvalidTransform`, `E.Geometry.InvalidCount`, `E.Geometry.InvalidScale`, `E.Geometry.InvalidAngle`, `E.Validation.GeometryInvalid`

---

## Internals

**Files**: `Transformation.cs` (API, ~150 LOC), `TransformationCore.cs` (dispatch), `TransformationCompute.cs` (morph/decompose), `TransformationConfig.cs` (config, 131 LOC)

**Dispatch**: Three `FrozenDictionary` tables: `TransformOperations`, `ArrayOperations`, `MorphOperations`

**Limits**: Scale [1e-6, 1e6], array max 10000, twist max 10 revolutions, bend max 1 revolution, compound max depth 100

**Morph tolerances**: Default 0.001; flow/sporph preserve structure option

**Decomposition**: Direct TRS for orthogonal matrices; Newton-Schulz iteration (max 10) for shear; orthogonality tolerance 1e-6

**Performance**: O(n) transforms where n = control points; O(k×n) arrays; O(n log n) typical morphs

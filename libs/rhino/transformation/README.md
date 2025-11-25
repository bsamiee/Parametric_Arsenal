# Affine Transforms, Arrays, and Deformations

Unified polymorphic dispatch for affine transformations, array operations, and SpaceMorph deformations.

---

## API

```csharp
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

// Transform operations
Result<Brep> mirrored = Transformation.Mirror(brep, Plane.WorldXY, context);
Result<Mesh> rotated = Transformation.Rotate(mesh, Math.PI / 4, Vector3d.ZAxis, Point3d.Origin, context);

// Array operations
Result<IReadOnlyList<Curve>> polar = Transformation.ApplyArray(
    curve,
    new Transformation.PolarArray(Point3d.Origin, Vector3d.ZAxis, Count: 12, TotalAngleRadians: Math.PI * 2),
    context);

// Morph operations
Result<Surface> twisted = Transformation.Morph(
    surface,
    new Transformation.TwistMorph(axis, AngleRadians: Math.PI, Infinite: false),
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

**Files**: `Transformation.cs` (API, 280 LOC), `TransformationCore.cs` (dispatch), `TransformationCompute.cs` (morph/decompose), `TransformationConfig.cs` (config, 131 LOC)

**Dispatch**: Three `FrozenDictionary` tables: `TransformOperations`, `ArrayOperations`, `MorphOperations`

**Limits**: Scale [1e-6, 1e6], array max 10000, twist max 10 revolutions, bend max 1 revolution, compound max depth 100

**Morph tolerances**: Default 0.001; flow/sporph preserve structure option

**Decomposition**: Direct TRS for orthogonal matrices; Newton-Schulz iteration (max 10) for shear; orthogonality tolerance 1e-6

**Performance**: O(n) transforms where n = control points; O(k×n) arrays; O(n log n) typical morphs

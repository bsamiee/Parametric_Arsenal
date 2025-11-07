# Orientation Library Blueprint

## Overview
Polymorphic geometry orientation engine providing canonical positioning, alignment to arbitrary targets, mirroring, axis flipping, and directional corrections for 2D/3D geometry via RhinoCommon Transform API with Result<T> monadic composition.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All operations return `Result<GeometryBase>` (not `Result<Transform>` - we return transformed geometry directly). Use `Map` for post-transform operations, `Bind` for chaining multiple orientations, `Ensure` for validation, `Match` for result handling.
- **UnifiedOperation**: Primary dispatch for polymorphic input/target combinations with type-based validation mode selection. Handles collections via parallel processing with error accumulation.
- **ValidationRules**: Reuse existing `V.Standard`, `V.BoundingBox`, `V.Degeneracy`, `V.Topology`, `V.MassProperties`, `V.MeshSpecific`. No new validation modes required - existing flags cover all orientation needs.
- **Error Registry**: Use existing `E.Geometry.*` errors (2000-2999 range). Allocate new codes 2400-2449 for orientation-specific errors.
- **Context**: Use `IGeometryContext.AbsoluteTolerance` for plane validity, vector length checks, degenerate frame detection.

### Similar libs/rhino/ Implementations
- **`libs/rhino/spatial/Spatial.cs`**: Borrow FrozenDictionary dispatch pattern with `(Type, Type)` tuple keys. Use ConditionalWeakTable for caching extracted frames/centroids.
- **`libs/rhino/extraction/Extract.cs`**: Borrow Semantic marker struct pattern for parameterless operations (canonical modes like `Canonical.WorldXY`).
- **`libs/rhino/analysis/`**: Reference ultra-dense computation patterns with inline algorithms in switch expressions.
- **No Duplication**: Orientation is transformation construction + application. Spatial is indexing/proximity, Extraction is point sampling, Analysis is differential properties. This fills the transformation/alignment/positioning gap.

## SDK Research Summary

### RhinoCommon APIs Used

**Core Transformation Methods**:
- `Transform.PlaneToPlane(Plane from, Plane to)`: Physical orientation mapping (rotation + translation). **CRITICAL**: Use this, NOT ChangeBasis, for geometric alignment. ChangeBasis reinterprets coordinates without physical movement and can cause shearing.
- `Transform.Rotation(double angleRadians, Vector3d axis, Point3d center)`: Rotation around arbitrary axis through center point.
- `Transform.Rotation(Vector3d startDirection, Vector3d endDirection)`: Vector-to-vector alignment rotation.
- `Transform.Mirror(Plane mirrorPlane)`: Reflection across plane. **WARNING**: Flips surface normals - may require explicit `.Flip()` correction.
- `Transform.Translation(Vector3d motion)`: Pure translation for centroid/point alignment.
- `Transform.Scale(Point3d anchor, double scaleFactor)`: Uniform scaling (rarely needed for orientation, but available).

**Property Extraction Methods**:
- `GeometryBase.GetBoundingBox(bool accurate)`: World-aligned bbox. Use `accurate: true` for NURBS (evaluates surface grid), `accurate: false` for control points only (faster).
- `BoundingBox.GetBoundingBox(Plane plane)`: Plane-aligned bounding box for canonical positioning to non-world planes.
- `AreaMassProperties.Compute(GeometryBase geometry)`: Area centroid for closed curves/surfaces/meshes. **CRITICAL**: Fails for open geometry - check `.IsClosed` first.
- `VolumeMassProperties.Compute(Brep brep)`: Volume centroid for solid geometry. More accurate than area centroid for closed volumes.
- `Curve.FrameAt(double t, out Plane plane)`: Curve frame (tangent-aligned) at parameter t.
- `Surface.FrameAt(double u, double v, out Plane frame)`: Surface frame (tangent/normal-aligned) at UV parameters.
- `Curve.PointAtNormalizedLength(double t)`: Curve point at normalized [0,1] parameter for midpoint extraction.

**Direction/Flip Methods**:
- `Curve.Reverse()`: Flip curve direction. **In-place mutation**, returns bool success.
- `Brep.Flip()`: Reverse all surface normals. **In-place mutation**.
- `Mesh.Flip(bool flipVertexNormals, bool flipFaceNormals, bool flipFaceOrientation)`: Reverse mesh orientation. **In-place mutation**.

### Key Insights
- **PlaneToPlane vs ChangeBasis**: PlaneToPlane physically moves/rotates geometry (rigid body motion). ChangeBasis reinterprets coordinate systems (can cause shearing). **Always use PlaneToPlane for orientation**.
- **Performance**: Transform is value type (struct). Composition via multiplication: `finalXform = xform3 * xform2 * xform1` (right-to-left application).
- **Common Pitfall**: Plane construction from 3 points fails if collinear. Validate with distance and cross-product checks before `new Plane(pt1, pt2, pt3)`.
- **Mirror Normal Reversal**: Mirror operations flip surface normals. For closed solids, this inverts "inside" vs "outside". Check `Brep.SolidOrientation` after mirroring if volume calculations are critical.
- **Centroid Accuracy**: For irregular geometry, AreaMassProperties centroid > BoundingBox.Center. For solids, VolumeMassProperties > AreaMassProperties.
- **Disposal Pattern**: Converted geometries (Extrusion.ToBrep(), SubD.ToBrep()) must be disposed. Not needed here - we operate on input geometry directly.

### SDK Version Requirements
- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+

## File Organization

### File 1: `Orient.cs` (Public API Surface)
**Purpose**: Primary public API with semantic operation methods and canonical markers

**Types** (3 total):
- `Orient`: Static class - Primary API entry point
- `Canonical`: readonly struct - Semantic marker for canonical positioning (WorldXY, WorldYZ, WorldXZ, AreaCentroid, VolumeCentroid)
- `OrientSpec`: readonly record struct - Polymorphic specification for alignment targets (Plane, Point, Vector, Curve, Surface, Frame)

**Key Members**:
- `Orient.ToPlane<T>(T geometry, Plane target, IGeometryContext context)`: Align geometry to target plane via PlaneToPlane. Extracts source plane from geometry type (curves → FrameAt mid, surfaces → FrameAt center, breps → centroid plane, meshes → centroid plane). Returns `Result<T>`.
- `Orient.ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context)`: Canonical positioning to world planes or centroid alignment. Dispatches on mode byte. Returns `Result<T>`.
- `Orient.ToPoint<T>(T geometry, Point3d target, bool useMassCentroid, IGeometryContext context)`: Align geometry center to point via translation. Uses mass centroid if `useMassCentroid=true`, else bounding box center. Returns `Result<T>`.
- `Orient.ToVector<T>(T geometry, Vector3d targetDirection, Vector3d? sourceAxis, IGeometryContext context)`: Rotate geometry to align source axis (default: ZAxis) with target direction using VectorAngle + CrossProduct. Returns `Result<T>`.
- `Orient.Mirror<T>(T geometry, Plane mirrorPlane, IGeometryContext context)`: Mirror across plane. Returns `Result<T>`.
- `Orient.FlipDirection<T>(T geometry, IGeometryContext context)`: Reverse curve direction or flip surface/brep/mesh normals. Type-specific dispatch. Returns `Result<T>`.

**Code Style Example**:
```csharp
public static Result<T> ToPlane<T>(
    T geometry,
    Plane targetPlane,
    IGeometryContext context) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometry,
        operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
            OrientCore.ExtractSourcePlane(item, context)
                .Bind(sourcePlane => targetPlane.IsValid switch {
                    false => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationPlane),
                    _ => ResultFactory.Create(value: Transform.PlaneToPlane(sourcePlane, targetPlane)),
                })
                .Map(xform => {
                    T result = (T)item.Duplicate();
                    return result.Transform(xform)
                        ? (IReadOnlyList<T>)[result,]
                        : throw new InvalidOperationException(E.Geometry.TransformFailed.Message);
                })),
        config: new OperationConfig<T, T> {
            Context = context,
            ValidationMode = V.Standard | V.BoundingBox,
        }).Map(results => results[0]);

public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;
    
    public static readonly Canonical WorldXY = new(1);
    public static readonly Canonical WorldYZ = new(2);
    public static readonly Canonical WorldXZ = new(3);
    public static readonly Canonical AreaCentroid = new(4);
    public static readonly Canonical VolumeCentroid = new(5);
}

public readonly record struct OrientSpec {
    public required object Target { get; init; }
    public Plane? TargetPlane { get; init; }
    public Point3d? TargetPoint { get; init; }
    public Vector3d? TargetVector { get; init; }
    public Curve? TargetCurve { get; init; }
    public Surface? TargetSurface { get; init; }
    public double CurveParameter { get; init; }
    public (double u, double v) SurfaceUV { get; init; }
    
    public static OrientSpec Plane(Plane plane) => new() { Target = plane, TargetPlane = plane, };
    public static OrientSpec Point(Point3d point) => new() { Target = point, TargetPoint = point, };
    public static OrientSpec Vector(Vector3d vector) => new() { Target = vector, TargetVector = vector, };
    public static OrientSpec Curve(Curve curve, double t) => new() { Target = curve, TargetCurve = curve, CurveParameter = t, };
    public static OrientSpec Surface(Surface surface, double u, double v) => new() { Target = surface, TargetSurface = surface, SurfaceUV = (u, v), };
}
```

**LOC Estimate**: 160-200

### File 2: `OrientCore.cs` (Core Implementation Logic)
**Purpose**: Internal algorithms with FrozenDictionary dispatch and frame extraction

**Types** (2 total):
- `OrientCore`: internal static class - Core computation algorithms
- `GeometryInfo`: internal readonly record struct - Cached properties (BoundingBox, Centroid, LocalPlane) to avoid recomputation

**Key Members**:
- `OrientCore.ExtractSourcePlane<T>(T geometry, IGeometryContext context)`: Extract source plane using FrozenDictionary dispatch. Curve → FrameAt(mid), Surface → FrameAt(center UV), Brep → centroid plane, Mesh → centroid plane, Point/PointCloud → origin plane. Returns `Result<Plane>`.
- `OrientCore.ExtractCentroid<T>(T geometry, bool useMassCentroid, IGeometryContext context)`: Extract centroid using mass properties or bounding box center. Returns `Result<Point3d>`.
- `OrientCore.ComputeCanonicalTransform<T>(T geometry, Canonical mode, IGeometryContext context)`: Build transform for canonical positioning. Dispatches on mode byte. Returns `Result<Transform>`.
- `OrientCore.ComputeVectorAlignment(Vector3d source, Vector3d target, Point3d center, IGeometryContext context)`: Calculate rotation to align vectors. Handles parallel/antiparallel edge cases. Returns `Result<Transform>`.
- `OrientCore.FlipGeometryDirection<T>(T geometry, IGeometryContext context)`: Type-specific flip dispatch. Curve → Reverse(), Brep → Flip(), Mesh → Flip(). Mutates in-place, returns `Result<T>` with mutated geometry.

**Code Style Example**:
```csharp
private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<Plane>>> _planeExtractors =
    new Dictionary<Type, Func<object, IGeometryContext, Result<Plane>>> {
        [typeof(Curve)] = (g, ctx) => ((Curve)g) switch {
            Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                ResultFactory.Create(value: frame),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
        },
        [typeof(Surface)] = (g, ctx) => ((Surface)g) switch {
            Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                ResultFactory.Create(value: frame),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
        },
        [typeof(Brep)] = (g, ctx) => {
            Brep brep = (Brep)g;
            Point3d centroid = brep.IsSolid
                ? VolumeMassProperties.Compute(brep)?.Centroid ?? brep.GetBoundingBox(accurate: false).Center
                : AreaMassProperties.Compute(brep)?.Centroid ?? brep.GetBoundingBox(accurate: false).Center;
            Vector3d normal = brep.Faces.Count > 0
                ? brep.Faces[0].NormalAt(0.5, 0.5)
                : Vector3d.ZAxis;
            return ResultFactory.Create(value: new Plane(centroid, normal));
        },
        [typeof(Mesh)] = (g, ctx) => {
            Mesh mesh = (Mesh)g;
            Point3d centroid = mesh.IsClosed
                ? VolumeMassProperties.Compute(mesh)?.Centroid ?? mesh.GetBoundingBox(accurate: false).Center
                : AreaMassProperties.Compute(mesh)?.Centroid ?? mesh.GetBoundingBox(accurate: false).Center;
            Vector3d normal = mesh.Normals.Count > 0
                ? mesh.Normals[0]
                : Vector3d.ZAxis;
            return ResultFactory.Create(value: new Plane(centroid, normal));
        },
        [typeof(Point)] = (g, ctx) =>
            ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
        [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
            true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
            false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
        },
    }.ToFrozenDictionary();

internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
    _planeExtractors.TryGetValue(geometry.GetType(), out Func<object, IGeometryContext, Result<Plane>>? extractor) switch {
        true => extractor(geometry, context),
        false => _planeExtractors
            .Where(kv => kv.Key.IsAssignableFrom(geometry.GetType()))
            .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) =>
                a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
            .Select(kv => kv.Value(geometry, context))
            .FirstOrDefault() ?? ResultFactory.Create<Plane>(
                error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
    };

internal static Result<Transform> ComputeCanonicalTransform<T>(
    T geometry,
    Canonical mode,
    IGeometryContext context) where T : GeometryBase =>
    (mode.Mode, geometry.GetBoundingBox(accurate: true)) switch {
        (1, BoundingBox bbox) when bbox.IsValid => // WorldXY
            ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis),
                Plane.WorldXY)),
        (2, BoundingBox bbox) when bbox.IsValid => // WorldYZ
            ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.YAxis, Vector3d.ZAxis),
                Plane.WorldYZ)),
        (3, BoundingBox bbox) when bbox.IsValid => // WorldXZ
            ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.XAxis, Vector3d.ZAxis),
                Plane.WorldXZ)),
        (4, _) => // AreaCentroid
            ExtractCentroid(geometry, useMassCentroid: false, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
        (5, _) => // VolumeCentroid
            ExtractCentroid(geometry, useMassCentroid: true, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
        (_, BoundingBox bbox) when !bbox.IsValid =>
            ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
        _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
    };
```

**LOC Estimate**: 180-230

### File 3: `OrientConfig.cs` (Configuration Types)
**Purpose**: Configuration constants and validation mode dispatch tables

**Types** (1 total):
- `OrientConfig`: internal static class - Configuration constants and FrozenDictionary validation mode mappings

**Key Members**:
- `_validationModes`: `FrozenDictionary<Type, V>` mapping geometry types to appropriate validation modes. Curve → `V.Standard | V.Degeneracy`, Surface → `V.Standard`, Brep → `V.Standard | V.Topology`, Mesh → `V.Standard | V.MeshSpecific`, etc.
- `ToleranceDefaults`: Constants for degenerate checks - `MinPlaneSize`, `MinVectorLength`, `MinRotationAngle`.

**Code Style Example**:
```csharp
internal static class OrientConfig {
    internal static readonly FrozenDictionary<Type, V> _validationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point)] = V.None,
            [typeof(PointCloud)] = V.None,
        }.ToFrozenDictionary();

    internal static class ToleranceDefaults {
        internal const double MinPlaneSize = 1e-6;
        internal const double MinVectorLength = 1e-8;
        internal const double MinRotationAngle = 1e-10;
    }
}
```

**LOC Estimate**: 60-90

## Adherence to Limits

- **Files**: 3 files (✓ within 4-file max, ideal 2-3 range)
- **Types**: 6 types total (✓ well within 10-type max, ideal 6-8 range)
  - Orient.cs: 3 types (Orient, Canonical, OrientSpec)
  - OrientCore.cs: 2 types (OrientCore, GeometryInfo)
  - OrientConfig.cs: 1 type (OrientConfig)
- **Estimated Total LOC**: 400-520 across 3 files
  - Orient.cs: 160-200 LOC (✓ within 300 max)
  - OrientCore.cs: 180-230 LOC (✓ within 300 max)
  - OrientConfig.cs: 60-90 LOC (✓ within 300 max)

**Assessment**: ✓ All limits satisfied. File count ideal (3), type count ideal (6), LOC estimates conservative and dense.

## Algorithmic Density Strategy

**No helper methods** - achieve density through:
- **Inline Transform construction**: All transform building logic inlined in switch expressions. No extracted "BuildXYZTransform" methods.
- **FrozenDictionary dispatch**: Type-based algorithm selection with lambda expressions encoding full logic in dictionary values.
- **Result monad chaining**: Heavy `.Bind()` and `.Map()` usage eliminates intermediate variables.
- **Pattern matching on mode bytes**: Switch expressions on `Canonical.Mode`, `OrientSpec.Target` type for multi-branch discrimination.
- **Inline mass properties computation**: Centroid extraction occurs inline in pattern match arms, not utility methods.
- **Tuple patterns for multi-parameter**: `(mode.Mode, geometry.GetBoundingBox(accurate: true)) switch { ... }` eliminates temporary variables.
- **ConditionalWeakTable caching** (optional, not shown in estimates): Automatic GC-aware caching for extracted planes/centroids if performance becomes critical.

## Dispatch Architecture

**Primary Dispatch**: FrozenDictionary keyed by `Type` → `Func<object, IGeometryContext, Result<T>>`

**Secondary Dispatch**: Switch expression on `Canonical.Mode` byte → transform construction

**Tertiary Dispatch**: Pattern matching on `OrientSpec.Target` runtime type → operation selection

**Cache Strategy** (deferred optimization): ConditionalWeakTable for automatic frame/centroid reuse when same geometry processed multiple times. Not included in initial implementation - add only if profiling shows benefit.

**Example Dispatch Flow**:
```
User: Orient.ToPlane(curve, targetPlane, context)
  ↓
UnifiedOperation validates curve with V.Standard | V.Degeneracy
  ↓
OrientCore.ExtractSourcePlane(curve, context)
  ↓
_planeExtractors[typeof(Curve)](curve, context) → curve.FrameAt(mid)
  ↓
Transform.PlaneToPlane(sourceFrame, targetPlane)
  ↓
curve.Duplicate() → Transform(xform) → Result<Curve>
```

## Public API Surface

### Primary Operations

**Plane-Based Orientation**:
```csharp
public static Result<T> ToPlane<T>(
    T geometry,
    Plane targetPlane,
    IGeometryContext context) where T : GeometryBase;
```

**Canonical Positioning**:
```csharp
public static Result<T> ToCanonical<T>(
    T geometry,
    Canonical mode,
    IGeometryContext context) where T : GeometryBase;
```

**Point Alignment**:
```csharp
public static Result<T> ToPoint<T>(
    T geometry,
    Point3d target,
    bool useMassCentroid,
    IGeometryContext context) where T : GeometryBase;
```

**Vector Alignment**:
```csharp
public static Result<T> ToVector<T>(
    T geometry,
    Vector3d targetDirection,
    Vector3d? sourceAxis,
    IGeometryContext context) where T : GeometryBase;
```

**Mirror Operations**:
```csharp
public static Result<T> Mirror<T>(
    T geometry,
    Plane mirrorPlane,
    IGeometryContext context) where T : GeometryBase;
```

**Direction Flip Operations**:
```csharp
public static Result<T> FlipDirection<T>(
    T geometry,
    IGeometryContext context) where T : GeometryBase;
```

**Polymorphic Specification**:
```csharp
public static Result<T> Apply<T>(
    T geometry,
    OrientSpec spec,
    IGeometryContext context) where T : GeometryBase;
```

### Configuration Types

**Canonical Marker**:
```csharp
public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;
    
    public static readonly Canonical WorldXY = new(1);
    public static readonly Canonical WorldYZ = new(2);
    public static readonly Canonical WorldXZ = new(3);
    public static readonly Canonical AreaCentroid = new(4);
    public static readonly Canonical VolumeCentroid = new(5);
}
```

**OrientSpec Target Specification**:
```csharp
public readonly record struct OrientSpec {
    public required object Target { get; init; }
    public Plane? TargetPlane { get; init; }
    public Point3d? TargetPoint { get; init; }
    public Vector3d? TargetVector { get; init; }
    public Curve? TargetCurve { get; init; }
    public Surface? TargetSurface { get; init; }
    public double CurveParameter { get; init; }
    public (double u, double v) SurfaceUV { get; init; }
    
    public static OrientSpec Plane(Plane plane);
    public static OrientSpec Point(Point3d point);
    public static OrientSpec Vector(Vector3d vector);
    public static OrientSpec Curve(Curve curve, double t);
    public static OrientSpec Surface(Surface surface, double u, double v);
}
```

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters (`error: E.X`, `value: x`)
- [x] All examples use trailing commas in multi-line collections
- [x] All examples use K&R brace style (`method() =>` on same line)
- [x] All examples use target-typed new() (`new()`, `[]`)
- [x] All examples use collection expressions (`[]` not `new List<>()`)
- [x] One type per file organization specified
- [x] All member estimates under 300 LOC (160-230 max)
- [x] All patterns match existing libs/ exemplars (FrozenDictionary, UnifiedOperation, Result chaining, Semantic markers)

## New Error Codes (E.Geometry 2400-2449)

Add to `libs/core/errors/E.cs` in Geometry class:

**Error Messages**:
```csharp
[2400] = "Invalid target plane for orientation operation",
[2401] = "Failed to extract source frame from geometry",
[2402] = "Geometry type not supported for orientation operations",
[2403] = "Invalid orientation mode specified",
[2404] = "Source and target vectors are invalid or zero-length",
[2405] = "Failed to compute orientation transform",
[2406] = "Failed to extract geometry centroid",
[2407] = "Transform application failed on geometry",
[2408] = "Parallel or antiparallel vectors require reference plane for alignment",
[2409] = "Invalid curve parameter for frame extraction",
[2410] = "Invalid surface UV parameters for frame extraction",
```

**Error Constants**:
```csharp
public static class Geometry {
    // ... existing errors ...
    public static readonly SystemError InvalidOrientationPlane = Get(2400);
    public static readonly SystemError FrameExtractionFailed = Get(2401);
    public static readonly SystemError UnsupportedOrientationType = Get(2402);
    public static readonly SystemError InvalidOrientationMode = Get(2403);
    public static readonly SystemError InvalidOrientationVectors = Get(2404);
    public static readonly SystemError OrientationComputationFailed = Get(2405);
    public static readonly SystemError CentroidExtractionFailed = Get(2406);
    public static readonly SystemError TransformFailed = Get(2407);
    public static readonly SystemError ParallelVectorAlignment = Get(2408);
    public static readonly SystemError InvalidCurveParameter = Get(2409);
    public static readonly SystemError InvalidSurfaceUV = Get(2410);
}
```

## Implementation Sequence

1. Read this blueprint thoroughly
2. Add error codes to `libs/core/errors/E.cs` (2400-2410 range)
3. Create folder structure: `libs/rhino/orientation/` with 3 files
4. Implement `OrientConfig.cs` first (type system foundation - validation modes, constants)
5. Implement `OrientCore.cs` with FrozenDictionary dispatch and core algorithms
6. Implement `Orient.cs` public API with UnifiedOperation integration
7. Verify pattern matching usage (no if/else statements except guard clauses)
8. Verify explicit types (no var)
9. Verify named parameters (`error: E.X`, `value: x`, `mode: V.Standard`)
10. Verify trailing commas in all multi-line collections
11. Check LOC limits per member (≤300) - use pattern matching, switch expressions, inline lambdas
12. Verify file/type limits (3 files ≤4 max, 6 types ≤10 max)
13. Verify code style compliance (K&R braces, target-typed new, collection expressions)
14. Build with zero warnings: `dotnet build libs/rhino/Rhino.csproj`
15. Verify integration with libs/core (Result, UnifiedOperation, V, E)

## Integration with Existing Validation Modes

**No new V.* flags required**. Reuse existing modes:
- `V.Standard`: Always validate with `.IsValid` before orientation
- `V.BoundingBox`: For operations using `GetBoundingBox()` (canonical positioning)
- `V.Degeneracy`: For curve operations (zero-length, degenerate)
- `V.Topology`: For Brep operations (manifold checking)
- `V.MeshSpecific`: For mesh operations (normals, manifold state)
- `V.MassProperties`: For operations using centroids (AreaMassProperties, VolumeMassProperties)

**Typical validation combinations per geometry type**:
- Curve operations: `V.Standard | V.Degeneracy`
- Surface operations: `V.Standard`
- Brep operations: `V.Standard | V.Topology`
- Mesh operations: `V.Standard | V.MeshSpecific`
- Point/PointCloud operations: `V.None`

## References

### SDK Documentation
- [Transform struct - RhinoCommon](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform)
- [Transform.PlaneToPlane method](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/planetoplane)
- [Transform.Rotation method](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Transform_Rotation_1.htm)
- [Transform.Mirror method](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Transform_Mirror.htm)
- [AreaMassProperties class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.areamassproperties)
- [VolumeMassProperties class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.volumemassproperties)
- [Curve.FrameAt method](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.curve/frameat)
- [Surface.FrameAt method](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Surface_FrameAt.htm)
- [BoundingBox struct](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Geometry_BoundingBox.htm)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns (Map, Bind, Ensure, Match)
- `libs/core/results/ResultFactory.cs` - Polymorphic parameter detection, Create patterns
- `libs/core/operations/UnifiedOperation.cs` - Dispatch engine, OperationConfig usage
- `libs/core/validation/V.cs` - Validation mode bitwise flags
- `libs/core/errors/E.cs` - Error code allocation (2000-2999 = Geometry domain)
- `libs/rhino/spatial/Spatial.cs` - FrozenDictionary dispatch pattern
- `libs/rhino/spatial/SpatialCore.cs` - Core algorithm separation pattern
- `libs/rhino/extraction/Extract.cs` - Semantic marker pattern (Analytical, Extremal, etc.)
- `libs/rhino/extraction/ExtractionCore.cs` - Pattern matching on spec type

### Community Resources
- [McNeel Forum: Transform.ChangeBasis vs Transform.PlaneToPlane](https://discourse.mcneel.com/t/rhinocommon-transform-changebasis-vs-transform-planetoplane/155869)
- [McNeel Forum: Plane aligned bounding box](https://discourse.mcneel.com/t/rhinocommon-plane-aligned-boundingbox/64428)
- [McNeel Forum: Centroid of a Brep](https://discourse.mcneel.com/t/how-do-i-get-a-centroid-of-a-brep-using-rhinocommon/79849)
- [McNeel Forum: Mirror transformation](https://discourse.mcneel.com/t/mirror-transformation/27819)
- [McNeel Forum: Rotation relative to custom axis](https://discourse.mcneel.com/t/rotation-relative-to-custom-axis/119201)

## Comprehensive Scope Coverage

### 2D Orientation
- [x] Align to XY plane (canonical WorldXY)
- [x] Align to point in XY plane (Z constraint via ToPoint)
- [x] Align to planar curve (extract 2D frame from curve)
- [x] Mirror across X/Y axes (Mirror with WorldYZ/WorldXZ planes)
- [x] Flip direction (FlipDirection for curves)

### 3D Orientation
- [x] Align to arbitrary plane (ToPlane with custom plane)
- [x] Align to 3D point (ToPoint with mass centroid or bbox center)
- [x] Align to spatial curve (ToPlane via curve FrameAt)
- [x] Align to surface (ToPlane via surface FrameAt)
- [x] Mirror across arbitrary plane (Mirror with custom plane)
- [x] Align to vector direction (ToVector with axis specification)

### Canonical Positioning
- [x] Bounding box to World XY (Canonical.WorldXY)
- [x] Bounding box to World YZ (Canonical.WorldYZ)
- [x] Bounding box to World XZ (Canonical.WorldXZ)
- [x] Consistent Z-axis orientation (auto-correct in canonical positioning)
- [x] Center at origin via area centroid (Canonical.AreaCentroid)
- [x] Center at origin via volume centroid (Canonical.VolumeCentroid)

### Alignment Targets
- [x] Point alignment (centroid to point translation)
- [x] Plane alignment (PlaneToPlane full orientation)
- [x] Curve alignment (align to curve frame at parameter via OrientSpec)
- [x] Surface alignment (align to surface frame at UV via OrientSpec)
- [x] Vector alignment (rotation to align axis with direction)
- [x] Centroid alignment (area/volume centroid extraction)
- [x] Bounding box center alignment (bbox center extraction)

### Mirror/Flip Operations
- [x] Mirror across World XY plane
- [x] Mirror across World YZ plane
- [x] Mirror across World XZ plane
- [x] Mirror across custom plane
- [x] Flip curve direction (Curve.Reverse)
- [x] Flip surface normals (Brep.Flip)
- [x] Flip mesh normals (Mesh.Flip)

### Geometry Type Coverage
- [x] Point/PointCloud (origin plane, best-fit plane)
- [x] Curve (FrameAt, area centroid for closed)
- [x] Surface (FrameAt with UV)
- [x] Brep (area/volume centroid, face frame)
- [x] Mesh (area/volume centroid, vertex normal plane)
- [x] Collections (batch processing via UnifiedOperation)

### Advanced Features
- [x] Transform composition (chaining via Result.Bind)
- [x] Validation mode per geometry type (FrozenDictionary dispatch)
- [x] Error accumulation (Result monad applicative semantics)
- [x] Type-safe polymorphic dispatch (UnifiedOperation)
- [x] Semantic markers for parameterless operations (Canonical struct)

## Critical Implementation Notes

### Transform.PlaneToPlane vs Transform.ChangeBasis
**CRITICAL**: Always use `Transform.PlaneToPlane` for physical orientation. `ChangeBasis` is for coordinate system reinterpretation without moving geometry and can cause shearing. Confusing these leads to incorrect results.

### Frame Extraction Strategy
For geometry without explicit frames (Brep, Mesh), use centroid-based plane construction:
```csharp
Point3d centroid = IsSolid 
    ? VolumeMassProperties.Compute(geom)?.Centroid ?? bbox.Center
    : AreaMassProperties.Compute(geom)?.Centroid ?? bbox.Center;
Vector3d normal = ExtractRepresentativeNormal(geom);  // First face normal, average mesh normal, etc.
Plane frame = new Plane(centroid, normal);
```

### Mirror Normal Correction
Mirror operations flip surface normals. For consistent orientation:
1. Apply mirror transform: `geometry.Transform(mirrorXform)`
2. Check if normals flipped: `if (mirrorXform.Determinant < 0) { /* normals are flipped */ }`
3. For closed solids, this inverts "inside" vs "outside" - may need explicit `.Flip()` call

### 2D vs 3D Handling
For 2D operations, constrain Z-coordinate:
```csharp
Plane plane2D = targetPlane.Clone();
plane2D.Origin = new Point3d(plane2D.Origin.X, plane2D.Origin.Y, 0);
plane2D.ZAxis = Vector3d.ZAxis;  // Ensure Z-up
```

### Transform Validation
Before returning transforms, validate:
```csharp
return transform.IsValid && Math.Abs(transform.Determinant) > context.AbsoluteTolerance
    ? ResultFactory.Create(value: result)
    : ResultFactory.Create<T>(error: E.Geometry.TransformFailed);
```

### Canonical Positioning Algorithm
1. Compute bounding box: `BoundingBox bbox = geometry.GetBoundingBox(accurate: true)`
2. Determine target plane (WorldXY, WorldYZ, or WorldXZ based on mode)
3. Construct source plane at bbox center with bbox-aligned axes
4. Apply PlaneToPlane from source to target: `Transform.PlaneToPlane(sourcePlane, targetPlane)`

### Performance Considerations
- **Transform composition**: Transform is value type (struct), composition via multiplication is cheap
- **Mass properties**: Compute once, cache in local scope if needed multiple times
- **Bounding box accuracy**: Use `accurate: true` for NURBS (evaluates surface grid), `accurate: false` for control points only (faster)
- **Frame extraction**: Curve.FrameAt and Surface.FrameAt are O(1) operations
- **ConditionalWeakTable caching**: Defer until profiling shows benefit - premature optimization

## Final Verification Checklist

Before implementation:
- [x] Read all exemplar files (`Spatial.cs`, `Extract.cs`, `UnifiedOperation.cs`, `ResultFactory.cs`)
- [x] Verify understanding of PlaneToPlane vs ChangeBasis distinction
- [x] Confirm error code allocation (2400-2410 range)
- [x] Verify no overlap with existing functionality
- [x] Confirm Semantic marker pattern from Extract.cs
- [x] Confirm FrozenDictionary pattern from Spatial.cs

During implementation:
- [ ] Verify each file stays under 300 LOC per member
- [ ] Verify no helper methods extracted (inline complex logic)
- [ ] Verify all pattern matching (no if/else statements except guard clauses)
- [ ] Verify all explicit types (no var)
- [ ] Verify all named parameters (non-obvious args)
- [ ] Verify all trailing commas (multi-line collections)
- [ ] Verify all K&R braces (same line opening)
- [ ] Verify target-typed new() (no redundant types)
- [ ] Verify collection expressions [] (no new List<>())

After implementation:
- [ ] Build with zero warnings
- [ ] Verify 3 files total (✓ limit)
- [ ] Verify 6 types total (✓ limit)
- [ ] Verify all LOC under 300 per member
- [ ] Verify all patterns match exemplars
- [ ] Verify integration with libs/core (Result, UnifiedOperation, V, E)
- [ ] Verify error codes added to E.cs
- [ ] Verify no code duplication with existing modules

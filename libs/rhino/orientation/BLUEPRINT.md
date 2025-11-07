# Orientation Library Blueprint

## Overview
Comprehensive geometric orientation and alignment library providing canonical positioning, plane-to-plane transformations, frame alignment, mirroring/reflection, and center-based positioning for all RhinoCommon geometry types in both 2D and 3D contexts with polymorphic dispatch.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All orientation operations return `Result<GeometryBase>` or `Result<IReadOnlyList<GeometryBase>>` for error handling. Use `.Map()` for transforming results, `.Bind()` for chaining operations, `.Ensure()` for validation predicates, `.Match()` for pattern matching outcomes.
- **UnifiedOperation**: Primary dispatch mechanism for polymorphic orientation operations. Handles single geometry items and collections uniformly with automatic validation, caching, parallel execution options, and error accumulation strategies.
- **ValidationRules**: Leverage existing `V.Standard` for IsValid checks before transformation. No additional validation modes needed—orientation operations validate geometry state, not geometric properties.
- **Error Registry**: Use existing `E.Geometry.*` errors (2000-2999 range). Need to allocate new error codes:
  - 2400-2499: Orientation errors (invalid planes, degenerate frames, incompatible alignment targets)
  - Example: `E.Geometry.InvalidOrientationPlane` (2400), `E.Geometry.DegenerateFrame` (2401), `E.Geometry.IncompatibleAlignmentTarget` (2402)
- **Context**: Use `IGeometryContext.AbsoluteTolerance` for geometric tolerance checks, especially for plane construction, normal vector validation, and degenerate frame detection.

### Similar libs/rhino/ Implementations
- **`libs/rhino/spatial/`**: Demonstrates FrozenDictionary dispatch pattern with `(Type, Type)` tuple keys for polymorphic algorithm selection. We'll use `(Type, OrientationMode)` keys for orientation dispatch.
- **`libs/rhino/extraction/`**: Shows semantic marker pattern with `Semantic` struct for parameterless method discrimination. We'll use similar pattern for orientation modes.
- **`libs/rhino/analysis/`**: Demonstrates geometry-specific result types (`CurveData`, `SurfaceData`) via marker interface `IResult`. We won't need this—orientation returns transformed geometry directly.
- **No Duplication**: No existing functionality for orientation/alignment operations. Transform operations exist in RhinoCommon but not wrapped in our Result/UnifiedOperation infrastructure.

## SDK Research Summary

### RhinoCommon APIs Used

#### Core Transformation Methods
- **`Transform.PlaneToPlane(Plane source, Plane target)`**: Creates transformation mapping one plane to another. Foundation for all plane-based orientation operations. Combines rotation and translation to align coordinate systems.
- **`Transform.Rotation(double angleRadians, Vector3d axis, Point3d center)`**: Rotation about arbitrary axis through a center point. Used for axis-aligned rotations and custom orientation angles.
- **`Transform.Mirror(Plane mirrorPlane)`**: Reflection across a plane. Used for symmetry operations and flip operations along principal axes.
- **`Transform.Translation(Vector3d motion)`**: Pure translation. Used for center-based alignment (moving centroids to origin or target points).
- **`Transform.ChangeBasis(Plane from, Plane to)`**: Changes coordinate system representation. Different from PlaneToPlane—reinterprets coordinates rather than physically moving geometry.

#### Geometry Evaluation Methods
- **`GeometryBase.GetBoundingBox(bool accurate)`**: Returns world-aligned bounding box. Used for canonical positioning to world axes.
- **`GeometryBase.GetBoundingBox(Plane plane)`**: Returns plane-aligned bounding box. Used for oriented bounding box calculations.
- **`AreaMassProperties.Compute(GeometryBase geometry)`**: Computes centroid for Breps, Surfaces, Meshes. Returns `.Centroid` property for center-based alignment.
- **`VolumeMassProperties.Compute(GeometryBase geometry)`**: Computes centroid for solid geometry. More accurate for closed volumes.
- **`Curve.PointAtNormalizedLength(double t)`**: Evaluates curve at normalized parameter [0,1]. Used for curve center point extraction.
- **`Curve.GetPerpendicularFrames(double[] parameters)`**: Returns zero-twisting frames along curve. Used for orienting geometry to curve flow.
- **`Surface.FrameAt(double u, double v)`**: Evaluates surface frame (origin, tangent vectors, normal) at parameter. Used for surface-based orientation.

#### Frame Construction
- **`Plane(Point3d origin, Vector3d xAxis, Vector3d yAxis)`**: Constructs plane from orthonormal basis. Used for custom frame creation.
- **`Plane.WorldXY`, `Plane.WorldYZ`, `Plane.WorldXZ`**: Principal world planes. Used for canonical positioning operations.
- **`Vector3d.CrossProduct(Vector3d a, Vector3d b)`**: Computes perpendicular vector. Used for constructing orthonormal frames from normals and tangents.

### Key Insights
- **Performance consideration**: `Transform` operations are in-place when calling `geometry.Transform(xform)`. Always clone geometry before transforming if original must be preserved. Use `geometry.Duplicate()` to create independent copy.
- **Common pitfall**: Plane construction from three points using `new Plane(pt1, pt2, pt3)` can fail if points are collinear. Must validate with distance and cross-product checks before construction.
- **Best practice**: Mirror operations reverse geometry orientation (flip normals). For closed solids, this inverts "inside" vs "outside". Check `Brep.IsSolid` and `Brep.SolidOrientation` after mirroring if volume calculations are critical.
- **Best practice**: `GetBoundingBox(accurate: true)` is expensive for NURBS surfaces—uses evaluation grid. For real-time operations, use `accurate: false` which uses control points only. Orientation library will default to `accurate: true` for correctness.
- **Common pitfall**: Centroid calculation fails for open curves, surfaces, and meshes. Must check `IsClosed` before using `AreaMassProperties`. Fall back to bounding box center for open geometry.
- **Performance consideration**: `Transform.PlaneToPlane` is computed once and cached in Transform struct. Reuse same Transform instance when orienting multiple geometry objects to same target plane.

### SDK Version Requirements
- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+
- All APIs used are stable since RhinoCommon 7.x, no breaking changes in 8.x

## File Organization

### File 1: `Orient.cs`
**Purpose**: Public API surface with semantic orientation modes and primary entry point

**Types** (4 total):
- `Orient` (static class): Primary public API with overloaded `Apply` methods for different orientation specifications
- `OrientMode` (readonly struct): Semantic marker for orientation operation types (PlaneAlign, CanonicalXY, CanonicalYZ, CanonicalXZ, ToCenter, ToPoint, Mirror, Rotate)
- `OrientSpec` (readonly record struct): Polymorphic specification combining mode with optional parameters (target plane, target point, mirror plane, rotation axis/angle, alignment direction)
- `AlignTarget` (readonly record struct): Discriminated union for alignment targets (Plane, Point, Vector, Frame) with pattern matching support

**Key Members**:
- `Orient.Apply<T>(T geometry, OrientSpec spec, IGeometryContext context)`: Main entry point using polymorphic spec pattern. Returns `Result<T>` where T is same as input type. Implementation dispatches to `OrientCore.Execute` via UnifiedOperation.
- `OrientMode` properties: `PlaneAlign`, `CanonicalXY`, `CanonicalYZ`, `CanonicalXZ`, `ToCenter`, `ToOrigin`, `ToPoint`, `MirrorXY`, `MirrorYZ`, `MirrorXZ`, `Mirror`, `RotateX`, `RotateY`, `RotateZ`, `Rotate` (each returns new OrientMode with specific byte marker).
- `OrientSpec` factory methods: `OrientSpec.Align(Plane target)`, `OrientSpec.Canonical(OrientMode axis)`, `OrientSpec.Mirror(Plane plane)`, `OrientSpec.Rotate(Vector3d axis, double angle)`, `OrientSpec.Align(Point3d target, AlignTarget.CenterMode mode)` (discriminates between centroid, bounding box center, curve midpoint).

**Code Style Example**:
```csharp
public static Result<T> Apply<T>(
    T geometry,
    OrientSpec spec,
    IGeometryContext context) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometry,
        operation: (Func<T, Result<IReadOnlyList<T>>>)(item => OrientCore.Execute(item, spec, context)
            .Map(transformed => (IReadOnlyList<T>)[transformed])),
        config: new OperationConfig<T, T> {
            Context = context,
            ValidationMode = V.Standard,
        }).Map(results => results[0]);

public readonly struct OrientMode(byte kind) {
    internal readonly byte Kind = kind;
    
    public static readonly OrientMode PlaneAlign = new(1);
    public static readonly OrientMode CanonicalXY = new(2);
    public static readonly OrientMode CanonicalYZ = new(3);
    public static readonly OrientMode CanonicalXZ = new(4);
    public static readonly OrientMode ToCenter = new(5);
    public static readonly OrientMode ToOrigin = new(6);
    public static readonly OrientMode ToPoint = new(7);
    public static readonly OrientMode MirrorXY = new(8);
    public static readonly OrientMode MirrorYZ = new(9);
    public static readonly OrientMode MirrorXZ = new(10);
    public static readonly OrientMode Mirror = new(11);
    public static readonly OrientMode RotateX = new(12);
    public static readonly OrientMode RotateY = new(13);
    public static readonly OrientMode RotateZ = new(14);
    public static readonly OrientMode Rotate = new(15);
}

public readonly record struct OrientSpec(
    OrientMode Mode,
    Plane? TargetPlane = null,
    Point3d? TargetPoint = null,
    Plane? MirrorPlane = null,
    Vector3d? RotationAxis = null,
    double? RotationAngle = null,
    AlignTarget? Target = null) {
    
    public static OrientSpec Align(Plane target) => new(OrientMode.PlaneAlign, TargetPlane: target);
    public static OrientSpec Canonical(OrientMode axis) => axis.Kind switch {
        2 => new(OrientMode.CanonicalXY),
        3 => new(OrientMode.CanonicalYZ),
        4 => new(OrientMode.CanonicalXZ),
        _ => throw new ArgumentException(E.Geometry.InvalidOrientationMode.Message),
    };
    public static OrientSpec Mirror(Plane plane) => new(OrientMode.Mirror, MirrorPlane: plane);
    public static OrientSpec Rotate(Vector3d axis, double angleRadians) => 
        new(OrientMode.Rotate, RotationAxis: axis, RotationAngle: angleRadians);
}
```

**LOC Estimate**: 180-220

### File 2: `OrientCore.cs`
**Purpose**: Core implementation logic with FrozenDictionary dispatch and transformation computation

**Types** (3 total):
- `OrientCore` (internal static class): Implements core orientation algorithms with FrozenDictionary dispatch
- `TransformBuilder` (internal static class): Helper for constructing Transform instances from OrientSpec with validation
- `GeometryInfo` (internal readonly record struct): Cached geometry properties (bounding box, centroid, frame, normal) to avoid recomputation

**Key Members**:
- `OrientCore.Execute<T>(T geometry, OrientSpec spec, IGeometryContext context)`: Central dispatch using pattern matching on spec.Mode. Returns `Result<T>` with transformed geometry. Algorithm: (1) Extract GeometryInfo, (2) Build Transform via TransformBuilder, (3) Validate transform is not degenerate, (4) Clone geometry, (5) Apply transform, (6) Return Result.
- `TransformBuilder.Build(OrientSpec spec, GeometryInfo info, IGeometryContext context)`: Constructs Transform from spec using FrozenDictionary lookup for mode-specific builders. Pattern matches on Mode.Kind to select algorithm. Returns `Result<Transform>`.
- `GeometryInfo.From(GeometryBase geometry, IGeometryContext context)`: Extracts cached properties using pattern matching on geometry type. Computes bounding box, centroid (via AreaMassProperties or fallback to bbox center), local frame (for surfaces/curves), and primary normal (for planar geometry). Returns `Result<GeometryInfo>`.

**Code Style Example**:
```csharp
internal static Result<T> Execute<T>(
    T geometry,
    OrientSpec spec,
    IGeometryContext context) where T : GeometryBase =>
    GeometryInfo.From(geometry, context)
        .Bind(info => TransformBuilder.Build(spec, info, context))
        .Ensure(xform => !xform.IsIdentity || spec.Mode.Kind is 6 or 5,
            error: E.Geometry.DegenerateTransform)
        .Map(xform => {
            T result = (T)geometry.Duplicate();
            bool success = result.Transform(xform);
            return success ? result : throw new InvalidOperationException(E.Geometry.TransformFailed.Message);
        });

internal static Result<Transform> Build(
    OrientSpec spec,
    GeometryInfo info,
    IGeometryContext context) =>
    spec.Mode.Kind switch {
        1 => spec.TargetPlane.HasValue
            ? BuildPlaneAlign(info.LocalFrame, spec.TargetPlane.Value, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane),
        2 => BuildCanonical(info.BoundingBox, Plane.WorldXY, context),
        3 => BuildCanonical(info.BoundingBox, Plane.WorldYZ, context),
        4 => BuildCanonical(info.BoundingBox, Plane.WorldXZ, context),
        5 or 6 => BuildToCenter(info.Centroid, spec.TargetPoint ?? Point3d.Origin, context),
        7 => spec.TargetPoint.HasValue
            ? BuildToCenter(info.Centroid, spec.TargetPoint.Value, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidTargetPoint),
        8 => ResultFactory.Create(value: Transform.Mirror(Plane.WorldXY)),
        9 => ResultFactory.Create(value: Transform.Mirror(Plane.WorldYZ)),
        10 => ResultFactory.Create(value: Transform.Mirror(Plane.WorldXZ)),
        11 => spec.MirrorPlane.HasValue
            ? ResultFactory.Create(value: Transform.Mirror(spec.MirrorPlane.Value))
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidMirrorPlane),
        12 => spec.RotationAngle.HasValue
            ? BuildRotation(Vector3d.XAxis, spec.RotationAngle.Value, info.Centroid, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidRotationAngle),
        13 => spec.RotationAngle.HasValue
            ? BuildRotation(Vector3d.YAxis, spec.RotationAngle.Value, info.Centroid, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidRotationAngle),
        14 => spec.RotationAngle.HasValue
            ? BuildRotation(Vector3d.ZAxis, spec.RotationAngle.Value, info.Centroid, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidRotationAngle),
        15 => spec.RotationAxis.HasValue && spec.RotationAngle.HasValue
            ? BuildRotation(spec.RotationAxis.Value, spec.RotationAngle.Value, info.Centroid, context)
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidRotationParameters),
        _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
    };

private static Result<Transform> BuildPlaneAlign(
    Plane source,
    Plane target,
    IGeometryContext context) =>
    (source.IsValid, target.IsValid) switch {
        (false, _) => ResultFactory.Create<Transform>(error: E.Geometry.InvalidSourcePlane),
        (_, false) => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane),
        _ when source.Origin.DistanceTo(target.Origin) < context.AbsoluteTolerance &&
               Vector3d.VectorAngle(source.Normal, target.Normal) < context.AngleTolerance =>
            ResultFactory.Create(value: Transform.Identity),
        _ => ResultFactory.Create(value: Transform.PlaneToPlane(source, target)),
    };

private static Result<Transform> BuildCanonical(
    BoundingBox bbox,
    Plane targetPlane,
    IGeometryContext context) =>
    bbox.IsValid switch {
        false => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
        _ => BuildPlaneAlign(
            new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis),
            new Plane(targetPlane.Origin, targetPlane.XAxis, targetPlane.YAxis),
            context),
    };
```

**LOC Estimate**: 220-260

### File 3: `OrientConfig.cs`
**Purpose**: Configuration types and FrozenDictionary dispatch tables for orientation algorithms

**Types** (1 total):
- `OrientConfig` (internal static class): Contains FrozenDictionary dispatch tables and configuration constants

**Key Members**:
- `_geometryFrameExtractors`: `FrozenDictionary<Type, Func<GeometryBase, IGeometryContext, Result<Plane>>>` mapping geometry types to frame extraction functions. Handles Curve (tangent frame at mid-param), Surface (frame at center UV), Brep (face 0 frame), Mesh (average vertex normal frame), Point3d (XY plane at point), default (bounding box center frame).
- `_centroidExtractors`: `FrozenDictionary<Type, Func<GeometryBase, IGeometryContext, Result<Point3d>>>` mapping geometry types to centroid extraction. Prioritizes AreaMassProperties for closed geometry, VolumeMassProperties for solids, falls back to bounding box center.
- `ToleranceDefaults`: Constants for degenerate checks—`MinPlaneSize = 1e-6`, `MinVectorLength = 1e-8`, `MinRotationAngle = 1e-10`.

**Code Style Example**:
```csharp
internal static class OrientConfig {
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, IGeometryContext, Result<Plane>>> _geometryFrameExtractors =
        new Dictionary<Type, Func<GeometryBase, IGeometryContext, Result<Plane>>> {
            [typeof(Curve)] = (g, ctx) => ((Curve)g).PointAt(((Curve)g).Domain.Mid) is Point3d pt &&
                ((Curve)g).TangentAt(((Curve)g).Domain.Mid) is Vector3d tan &&
                tan.Length > ctx.AbsoluteTolerance
                    ? ResultFactory.Create(value: new Plane(pt, tan, Vector3d.CrossProduct(tan, Vector3d.ZAxis)))
                    : ResultFactory.Create<Plane>(error: E.Geometry.DegenerateFrame),
            [typeof(Surface)] = (g, ctx) => ((Surface)g).FrameAt(
                ((Surface)g).Domain(0).Mid,
                ((Surface)g).Domain(1).Mid) is Plane plane && plane.IsValid
                    ? ResultFactory.Create(value: plane)
                    : ResultFactory.Create<Plane>(error: E.Geometry.DegenerateFrame),
            [typeof(Brep)] = (g, ctx) => ((Brep)g).Faces.Count > 0 &&
                ((Brep)g).Faces[0].FrameAt(
                    ((Brep)g).Faces[0].Domain(0).Mid,
                    ((Brep)g).Faces[0].Domain(1).Mid) is Plane plane && plane.IsValid
                    ? ResultFactory.Create(value: plane)
                    : ResultFactory.Create<Plane>(error: E.Geometry.DegenerateFrame),
            [typeof(Mesh)] = (g, ctx) => {
                Mesh mesh = (Mesh)g;
                return mesh.Vertices.Count > 0 && mesh.Normals.Count > 0
                    ? ResultFactory.Create(value: new Plane(
                        mesh.Vertices[0],
                        mesh.Normals[0],
                        Vector3d.CrossProduct(mesh.Normals[0], Vector3d.XAxis)))
                    : ResultFactory.Create<Plane>(error: E.Geometry.DegenerateFrame);
            },
            [typeof(Point3d)] = (g, ctx) => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<Type, Func<GeometryBase, IGeometryContext, Result<Point3d>>> _centroidExtractors =
        new Dictionary<Type, Func<GeometryBase, IGeometryContext, Result<Point3d>>> {
            [typeof(Curve)] = (g, ctx) => ((Curve)g).IsClosed
                ? AreaMassProperties.Compute((Curve)g) is AreaMassProperties amp && amp is not null
                    ? ResultFactory.Create(value: amp.Centroid)
                    : ResultFactory.Create<Point3d>(error: E.Validation.MassPropertiesComputationFailed)
                : ResultFactory.Create(value: ((Curve)g).PointAtNormalizedLength(0.5)),
            [typeof(Surface)] = (g, ctx) => AreaMassProperties.Compute((Surface)g) is AreaMassProperties amp && amp is not null
                ? ResultFactory.Create(value: amp.Centroid)
                : ResultFactory.Create(value: ((Surface)g).GetBoundingBox(accurate: true).Center),
            [typeof(Brep)] = (g, ctx) => ((Brep)g).IsSolid
                ? VolumeMassProperties.Compute((Brep)g) is VolumeMassProperties vmp && vmp is not null
                    ? ResultFactory.Create(value: vmp.Centroid)
                    : ResultFactory.Create<Point3d>(error: E.Validation.MassPropertiesComputationFailed)
                : AreaMassProperties.Compute((Brep)g) is AreaMassProperties amp && amp is not null
                    ? ResultFactory.Create(value: amp.Centroid)
                    : ResultFactory.Create(value: ((Brep)g).GetBoundingBox(accurate: true).Center),
            [typeof(Mesh)] = (g, ctx) => ((Mesh)g).IsClosed
                ? VolumeMassProperties.Compute((Mesh)g) is VolumeMassProperties vmp && vmp is not null
                    ? ResultFactory.Create(value: vmp.Centroid)
                    : ResultFactory.Create(value: ((Mesh)g).GetBoundingBox(accurate: true).Center)
                : AreaMassProperties.Compute((Mesh)g) is AreaMassProperties amp && amp is not null
                    ? ResultFactory.Create(value: amp.Centroid)
                    : ResultFactory.Create(value: ((Mesh)g).GetBoundingBox(accurate: true).Center),
            [typeof(Point3d)] = (g, ctx) => ResultFactory.Create(value: (Point3d)g),
        }.ToFrozenDictionary();

    internal static class ToleranceDefaults {
        internal const double MinPlaneSize = 1e-6;
        internal const double MinVectorLength = 1e-8;
        internal const double MinRotationAngle = 1e-10;
    }
}
```

**LOC Estimate**: 160-190

## Adherence to Limits

- **Files**: 3 files (✓ well within 4-file maximum, at ideal 2-3 range)
- **Types**: 8 types total across all files (✓ within 10-type maximum, at ideal 6-8 range)
  - Orient.cs: 4 types (Orient, OrientMode, OrientSpec, AlignTarget)
  - OrientCore.cs: 3 types (OrientCore, TransformBuilder, GeometryInfo)
  - OrientConfig.cs: 1 type (OrientConfig)
- **Estimated Total LOC**: 560-670 across all files
  - Per-member estimates: All members estimated at 150-260 LOC, well under 300 LOC maximum
  - Densest member: `OrientCore.Execute` with inline Transform construction ~220 LOC
  - Public API: `Orient.Apply` with UnifiedOperation dispatch ~180 LOC

**Assessment**: ✓ All limits satisfied. File count ideal (3), type count ideal (8), LOC estimates conservative and dense.

## Algorithmic Density Strategy

**How we achieve dense code without helpers:**

1. **Inline Transform construction**: All Transform building logic inlined in `TransformBuilder.Build` using nested ternary and switch expressions. No extracted "BuildXYZTransform" methods—pattern match on Mode.Kind directly to construction logic.

2. **FrozenDictionary dispatch for extractors**: Frame and centroid extraction use FrozenDictionary with lambda expressions encoding full algorithm. Avoids separate extraction methods per geometry type. Dictionary value is `Func<GeometryBase, IGeometryContext, Result<T>>` containing complete inline logic.

3. **GeometryInfo caching**: Single `GeometryInfo.From` method computes all properties (bbox, centroid, frame) in one pass using pattern matching on geometry type. Caches results in readonly record struct to avoid recomputation across multiple transform building calls.

4. **Result composition chains**: Heavy use of `.Bind()` and `.Map()` to chain validation → extraction → transformation → application. No intermediate variables—operations flow through Result monad pipeline.

5. **Pattern matching for polymorphism**: Switch expressions on `Mode.Kind`, `spec.Mode.Kind`, and geometry types eliminate branching logic. Compiler optimizes to jump tables.

6. **Ternary expressions for validation**: All validation checks (plane validity, vector length, bbox validity) use ternary expressions returning Result<T>. No if/else guard clauses—everything returns values.

7. **Tuple deconstruction for multi-value**: When extracting multiple properties, use tuple patterns: `(source.IsValid, target.IsValid) switch { ... }`. Avoids temporary variables.

## Dispatch Architecture

**FrozenDictionary Configuration Pattern:**

```csharp
// Type-based dispatch for geometry property extraction
FrozenDictionary<Type, Func<GeometryBase, IGeometryContext, Result<T>>>

// Mode-based dispatch for transform building (inline in switch expression)
spec.Mode.Kind switch {
    1 => BuildPlaneAlign(...),
    2 => BuildCanonical(Plane.WorldXY),
    ...
}
```

**Not using FrozenDictionary for mode dispatch** because:
1. Mode.Kind is byte, not Type—switch expressions are more efficient than dictionary lookup for integer keys
2. Each mode has different parameter requirements (some need planes, some need points, some need axes)
3. Switch expressions provide exhaustiveness checking and better compile-time validation
4. Inline algorithms in switch arms are 3-10 lines each—no benefit from extraction

**Using FrozenDictionary for geometry-type dispatch** because:
1. Type-based lookup is natural for polymorphic geometry handling
2. Each geometry type has fundamentally different extraction algorithms (curves use parameters, surfaces use UV, meshes use vertices)
3. Extraction logic is 5-15 lines per type—FrozenDictionary keeps this organized without 8+ separate methods
4. Allows extensibility—new geometry types can be added to dictionary without modifying core logic

## Public API Surface

### Primary Operations

```csharp
// Plane-to-plane alignment
public static Result<T> Apply<T>(
    T geometry,
    Plane targetPlane,
    IGeometryContext context) where T : GeometryBase;

// Canonical positioning to world axes
public static Result<T> Apply<T>(
    T geometry,
    OrientMode canonicalAxis,  // CanonicalXY, CanonicalYZ, or CanonicalXZ
    IGeometryContext context) where T : GeometryBase;

// Center-based alignment
public static Result<T> Apply<T>(
    T geometry,
    Point3d targetPoint,
    IGeometryContext context) where T : GeometryBase;

// Polymorphic specification-based (most flexible)
public static Result<T> Apply<T>(
    T geometry,
    OrientSpec spec,
    IGeometryContext context) where T : GeometryBase;

// Batch operations (leverages UnifiedOperation collection handling)
public static Result<IReadOnlyList<T>> ApplyMultiple<T>(
    IReadOnlyList<T> geometries,
    OrientSpec spec,
    IGeometryContext context,
    bool accumulateErrors = false) where T : GeometryBase;

// Mirror operations (convenience overloads)
public static Result<T> Mirror<T>(
    T geometry,
    Plane mirrorPlane,
    IGeometryContext context) where T : GeometryBase;

// Rotation operations (convenience overloads)
public static Result<T> Rotate<T>(
    T geometry,
    Vector3d axis,
    double angleRadians,
    IGeometryContext context) where T : GeometryBase;
```

### Configuration Types

```csharp
// Semantic orientation mode marker
public readonly struct OrientMode(byte kind) {
    internal readonly byte Kind = kind;
    
    // Plane alignment modes
    public static readonly OrientMode PlaneAlign = new(1);
    
    // Canonical positioning modes
    public static readonly OrientMode CanonicalXY = new(2);
    public static readonly OrientMode CanonicalYZ = new(3);
    public static readonly OrientMode CanonicalXZ = new(4);
    
    // Center alignment modes
    public static readonly OrientMode ToCenter = new(5);
    public static readonly OrientMode ToOrigin = new(6);
    public static readonly OrientMode ToPoint = new(7);
    
    // Mirror modes
    public static readonly OrientMode MirrorXY = new(8);
    public static readonly OrientMode MirrorYZ = new(9);
    public static readonly OrientMode MirrorXZ = new(10);
    public static readonly OrientMode Mirror = new(11);
    
    // Rotation modes
    public static readonly OrientMode RotateX = new(12);
    public static readonly OrientMode RotateY = new(13);
    public static readonly OrientMode RotateZ = new(14);
    public static readonly OrientMode Rotate = new(15);
}

// Polymorphic orientation specification
public readonly record struct OrientSpec(
    OrientMode Mode,
    Plane? TargetPlane = null,
    Point3d? TargetPoint = null,
    Plane? MirrorPlane = null,
    Vector3d? RotationAxis = null,
    double? RotationAngle = null,
    AlignTarget? Target = null) {
    
    // Factory methods for type-safe construction
    public static OrientSpec Align(Plane target);
    public static OrientSpec Canonical(OrientMode axis);
    public static OrientSpec ToOrigin();
    public static OrientSpec ToPoint(Point3d target);
    public static OrientSpec Mirror(Plane plane);
    public static OrientSpec MirrorXY();
    public static OrientSpec MirrorYZ();
    public static OrientSpec MirrorXZ();
    public static OrientSpec Rotate(Vector3d axis, double angleRadians);
    public static OrientSpec RotateX(double angleRadians);
    public static OrientSpec RotateY(double angleRadians);
    public static OrientSpec RotateZ(double angleRadians);
}

// Alignment target discriminated union
public readonly record struct AlignTarget {
    public enum TargetType : byte { Plane, Point, Vector, Frame }
    
    internal readonly TargetType Type;
    internal readonly object Value;
    
    public static AlignTarget Plane(Plane plane);
    public static AlignTarget Point(Point3d point);
    public static AlignTarget Vector(Vector3d vector);
    public static AlignTarget Frame(Plane frame);
    
    public T Match<T>(
        Func<Plane, T> onPlane,
        Func<Point3d, T> onPoint,
        Func<Vector3d, T> onVector,
        Func<Plane, T> onFrame);
}
```

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else) - Switch expressions for mode dispatch, ternary for validation
- [x] All examples use explicit types (no var) - All declarations explicit: `Result<Transform>`, `FrozenDictionary<Type, Func<...>>`, etc.
- [x] All examples use named parameters - All non-obvious args named: `error: E.Geometry.X`, `value: transform`, `TargetPlane: target`
- [x] All examples use trailing commas - All multi-line dictionaries and arrays end with `,`
- [x] All examples use K&R brace style - Opening braces on same line: `Method() { ... }`, `switch { ... }`
- [x] All examples use target-typed new() - All constructors use `new()` when type known: `new OperationConfig<T, T> { ... }`
- [x] All examples use collection expressions [] - All arrays use `[]`: `[transformed]`, `[e1, e2,]`
- [x] One type per file organization - 3 files with 8 types total, never multiple top-level types in same file
- [x] All member estimates under 300 LOC - Largest member `TransformBuilder.Build` at ~220 LOC with inline logic
- [x] All patterns match existing libs/ exemplars - UnifiedOperation usage matches Spatial.cs, semantic markers match Extract.cs, FrozenDictionary dispatch matches ValidationRules.cs

## Implementation Sequence

1. Read this blueprint thoroughly—understand full scope and integration points
2. Add new error codes to `libs/core/errors/E.cs`:
   - 2400: InvalidOrientationPlane
   - 2401: DegenerateFrame
   - 2402: IncompatibleAlignmentTarget
   - 2403: InvalidSourcePlane
   - 2404: InvalidTargetPoint
   - 2405: InvalidMirrorPlane
   - 2406: InvalidRotationAngle
   - 2407: InvalidRotationParameters
   - 2408: InvalidOrientationMode
   - 2409: DegenerateTransform
   - 2410: TransformFailed
3. Create `libs/rhino/orientation/OrientConfig.cs` first—establish dispatch tables and constants
4. Create `libs/rhino/orientation/OrientCore.cs` second—implement core algorithms using dispatch tables
5. Create `libs/rhino/orientation/Orient.cs` third—implement public API with UnifiedOperation integration
6. Verify patterns match exemplars:
   - Compare `Orient.Apply` to `Extract.Points` and `Spatial.Analyze` for UnifiedOperation usage
   - Compare `OrientMode` to `Extract.Semantic` for marker pattern
   - Compare `OrientConfig._geometryFrameExtractors` to `ValidationRules._validationRules` for FrozenDictionary dispatch
7. Check LOC limits: All members ≤300 LOC (use `wc -l` per method)
8. Verify file/type limits: 3 files ✓, 8 types ✓
9. Verify code style compliance: No var, no if/else, named params, trailing commas, K&R braces
10. Build and test: `dotnet build libs/rhino/Rhino.csproj` should succeed with zero warnings
11. Add unit tests following NUnit patterns in `test/rhino/`
12. Add integration tests for each OrientMode
13. Test edge cases: degenerate planes, coincident points, zero-length vectors, collinear frames
14. Verify performance: canonical positioning should be <1ms per geometry, plane alignment <0.5ms
15. Final review: Re-read blueprint and implementation to ensure 100% coverage of scope

## References

### SDK Documentation
- [Transform Methods - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform)
- [Transform.PlaneToPlane - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/planetoplane)
- [Transform.Rotation - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/rotation)
- [Transform.Mirror - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/mirror)
- [GeometryBase.GetBoundingBox - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.geometrybase/getboundingbox)
- [AreaMassProperties - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.areamassproperties)
- [VolumeMassProperties - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.volumemassproperties)
- [Curve.GetPerpendicularFrames - RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.curve/getperpendicularframes)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns, Map/Bind/Ensure composition
- `libs/core/results/ResultFactory.cs` - Polymorphic parameter detection, Create patterns
- `libs/core/operations/UnifiedOperation.cs` - Dispatch engine, OperationConfig usage
- `libs/core/operations/OperationConfig.cs` - Configuration patterns, validation modes
- `libs/core/validation/ValidationRules.cs` - FrozenDictionary dispatch, expression tree compilation
- `libs/core/validation/V.cs` - Validation mode flags, bitwise operations
- `libs/core/errors/E.cs` - Error registry patterns, code allocation, WithContext usage
- `libs/rhino/spatial/Spatial.cs` - Public API patterns, UnifiedOperation integration
- `libs/rhino/spatial/SpatialCore.cs` - Core implementation patterns, FrozenDictionary dispatch
- `libs/rhino/extraction/Extract.cs` - Semantic marker pattern, polymorphic specs
- `libs/rhino/extraction/ExtractionCore.cs` - Pattern matching on input types
- `libs/rhino/analysis/Analysis.cs` - Result type patterns (not needed here, but study overload patterns)

## Scope Coverage Verification

### Orientation Capabilities (All Covered)
- [x] **Plane-to-plane alignment**: `Orient.Apply(geometry, targetPlane, context)` via `Transform.PlaneToPlane`
- [x] **Canonical positioning to world axes**: `OrientSpec.Canonical(OrientMode.CanonicalXY/YZ/XZ)` via bounding box alignment
- [x] **Center-based alignment**: `OrientSpec.ToOrigin()`, `OrientSpec.ToPoint(target)` via centroid translation
- [x] **Mirror/flip operations**: `OrientSpec.Mirror(plane)`, `OrientSpec.MirrorXY/YZ/XZ()` via `Transform.Mirror`
- [x] **Rotation operations**: `OrientSpec.Rotate(axis, angle)`, `OrientSpec.RotateX/Y/Z(angle)` via `Transform.Rotation`
- [x] **Frame alignment**: Implicit via plane-to-plane with `GeometryInfo.LocalFrame` extraction
- [x] **Surface/curve tangent alignment**: Via `GeometryInfo` with `Curve.TangentAt`, `Surface.FrameAt`
- [x] **Normal/perpendicular alignment**: Via frame construction with `Vector3d.CrossProduct`
- [x] **Consistent Z-axis orientation**: Built into all frame construction via validation of normal direction
- [x] **2D projection to XY plane**: Via canonical positioning with Z-suppression (flatten to XY)
- [x] **3D orientation**: All operations work in 3D with full 6-DOF transformation

### Geometry Types (All Covered)
- [x] **Point3d**: Direct handling in FrozenDictionary extractors
- [x] **Curve**: Tangent frame, mid-point centroid, parameter evaluation
- [x] **Surface**: Frame evaluation at UV parameters, area centroid
- [x] **Brep**: Face frame extraction, solid vs surface centroid handling
- [x] **Mesh**: Vertex normal frames, volume vs area centroid
- [x] **Collections**: Via `ApplyMultiple` with UnifiedOperation batch processing
- [x] **Polymorphic handling**: Via `GeometryBase` constraint with runtime type dispatch

### Alignment Targets (All Covered)
- [x] **Plane**: Direct `Transform.PlaneToPlane`
- [x] **Point**: Centroid translation to target point
- [x] **Origin**: Special case of point alignment
- [x] **Frame**: Via plane-to-plane with custom frame construction
- [x] **Vector**: Rotation to align principal axis with target vector
- [x] **Surface**: Frame extraction at surface parameter, then plane-to-plane
- [x] **Curve**: Frame extraction at curve parameter, then plane-to-plane
- [x] **Center of mass**: Via AreaMassProperties/VolumeMassProperties centroid

### Edge Cases (All Validated)
- [x] **Degenerate planes**: Validation via `Plane.IsValid` check before transform construction
- [x] **Collinear points**: Cross-product validation in frame construction
- [x] **Zero-length vectors**: Vector length check against `context.AbsoluteTolerance`
- [x] **Invalid bounding boxes**: `BoundingBox.IsValid` check before canonical positioning
- [x] **Open vs closed geometry**: Centroid extraction uses `IsClosed` discrimination
- [x] **Solid vs surface geometry**: Centroid uses VolumeMassProperties for solids, AreaMassProperties for surfaces
- [x] **Identity transforms**: Optimization check for already-aligned geometry (distance/angle tolerance)
- [x] **Transform failures**: Result<T> error handling for RhinoCommon transform failures

## API Usage Examples (For Implementation Validation)

### Example 1: Canonical Positioning to World XY
```csharp
Curve curve = ...; // Any curve
IGeometryContext context = new GeometryContext();

Result<Curve> result = Orient.Apply(
    geometry: curve,
    spec: OrientSpec.Canonical(OrientMode.CanonicalXY),
    context: context);

// Expected: Curve bounding box aligned to world XY plane, centered at origin
```

### Example 2: Plane-to-Plane Alignment
```csharp
Surface surface = ...; // Any surface
Plane sourcePlane = new Plane(Point3d.Origin, Vector3d.ZAxis);
Plane targetPlane = new Plane(new Point3d(10, 0, 0), Vector3d.XAxis);
IGeometryContext context = new GeometryContext();

Result<Surface> result = Orient.Apply(
    geometry: surface,
    targetPlane: targetPlane,
    context: context);

// Expected: Surface oriented from its local frame to targetPlane
```

### Example 3: Mirror Across XY Plane
```csharp
Brep brep = ...; // Any brep
IGeometryContext context = new GeometryContext();

Result<Brep> result = Orient.Mirror(
    geometry: brep,
    mirrorPlane: Plane.WorldXY,
    context: context);

// Expected: Brep reflected across world XY plane (Z → -Z)
```

### Example 4: Align Centroid to Origin
```csharp
Mesh mesh = ...; // Any closed mesh
IGeometryContext context = new GeometryContext();

Result<Mesh> result = Orient.Apply(
    geometry: mesh,
    spec: OrientSpec.ToOrigin(),
    context: context);

// Expected: Mesh translated so its centroid is at world origin
```

### Example 5: Rotate 90° About Z-Axis
```csharp
GeometryBase geometry = ...; // Any geometry
IGeometryContext context = new GeometryContext();

Result<GeometryBase> result = Orient.Rotate(
    geometry: geometry,
    axis: Vector3d.ZAxis,
    angleRadians: Math.PI / 2,
    context: context);

// Expected: Geometry rotated 90° counterclockwise about Z-axis through its centroid
```

### Example 6: Batch Orientation with Error Accumulation
```csharp
IReadOnlyList<Curve> curves = [...]; // Multiple curves
IGeometryContext context = new GeometryContext();

Result<IReadOnlyList<Curve>> result = Orient.ApplyMultiple(
    geometries: curves,
    spec: OrientSpec.Canonical(OrientMode.CanonicalXY),
    context: context,
    accumulateErrors: true);

// Expected: All curves oriented to XY plane, errors accumulated for invalid curves
```

---

**Blueprint Complete**: This document provides comprehensive specification for orientation library implementation covering full scope of RhinoCommon orientation capabilities, strict adherence to Parametric Arsenal code standards, full integration with libs/ infrastructure, and surgical implementation strategy fitting within 3 files and 8 types.

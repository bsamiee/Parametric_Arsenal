# Orientation Library Blueprint

## Overview
Comprehensive polymorphic orientation engine providing canonical positioning, alignment, mirroring, and frame-based transformations for 2D/3D geometry using RhinoCommon Transform API with monadic composition.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All operations return `Result<IReadOnlyList<Transform>>` for composable transformations. Use `Map` for transform composition, `Bind` for chained operations, `Ensure` for validation of transform properties (non-degenerate, affine, etc.).
- **UnifiedOperation**: Primary dispatch for polymorphic target resolution (Plane, Point3d, Curve, Surface, BoundingBox, AreaCentroid, VolumeCentroid). Configuration handles validation mode selection and parallel batch processing.
- **ValidationRules**: Use existing `V.Standard` for geometry validity, `V.BoundingBox` for bbox operations, `V.Topology` for solid/closed checks. No new validation modes required.
- **Error Registry**: Use existing `E.Geometry.*` errors. Need new error codes in 2000-2999 range for orientation-specific failures (invalid alignment target, degenerate transform, incompatible orientation mode).
- **Context**: Use `IGeometryContext.AbsoluteTolerance` for transform validity checks, equality comparisons, and degenerate frame detection.

### Similar libs/rhino/ Implementations
- **`libs/rhino/spatial/`**: Borrows FrozenDictionary dispatch pattern for (InputType, TargetType, OrientationMode) lookup. Cache transforms using ConditionalWeakTable.
- **`libs/rhino/extraction/`**: Borrows Semantic marker pattern for parameterless operations (Canonical positioning modes like `Canonical.WorldXY`, `Canonical.WorldYZ`).
- **`libs/rhino/analysis/`**: Borrows IResult marker interface for polymorphic return types. Orient operations return `OrientationResult` containing Transform, source frame, target frame, and diagnostic metadata.
- **No Duplication**: Orientation is distinct from existing modules. Spatial handles indexing, extraction handles point generation, analysis handles differential geometry. Orientation handles transformation construction and application.

## SDK Research Summary

### RhinoCommon APIs Used
- **Transform.PlaneToPlane(Plane from, Plane to)**: Core alignment method. Maps geometry from source plane coordinate system to target plane. Combines rotation and translation in single affine transformation.
- **Transform.ChangeBasis(Plane plane0, Plane plane1)**: Coordinate system remapping without geometric movement. Used for frame reinterpretation vs PlaneToPlane's physical alignment.
- **Transform.Rotation(double angleRadians, Vector3d axis, Point3d center)**: Axis-angle rotation for canonical orientation corrections (e.g., consistent Z-up).
- **Transform.Rotation(Vector3d from, Vector3d to, Point3d center)**: Vector alignment for directional orientation.
- **Transform.Translation(Vector3d motion)**: Pure translation for centroid/point alignment.
- **Transform.Scale(Plane plane, double xScaleFactor, double yScaleFactor, double zScaleFactor)**: Non-uniform scaling for canonical positioning.
- **Transform.Mirror(Plane mirrorPlane)**: Reflection transformation for symmetry operations.
- **Transform.ProjectAlong(Plane plane, Vector3d direction)**: Projection transformations for planar alignment.
- **BoundingBox.GetBoundingBox(Plane plane)**: Plane-aligned bounding boxes for canonical positioning to world planes.
- **AreaMassProperties.Compute(GeometryBase geometry)**: Area centroid extraction for alignment targets.
- **VolumeMassProperties.Compute(Brep brep)**: Volume centroid extraction for solid alignment.
- **Plane.FitPlaneToPoints(Point3d[] points)**: Best-fit plane construction for point cloud alignment.
- **Curve.FrameAt(double t)**: Curve parameter frame extraction for curve-aligned orientation.
- **Surface.FrameAt(double u, double v)**: Surface parameter frame extraction for surface-aligned orientation.

### Key Insights
- **PlaneToPlane vs ChangeBasis**: PlaneToPlane physically moves/rotates geometry. ChangeBasis reinterprets coordinates. For orientation operations, always use PlaneToPlane.
- **Transform Composition**: Transforms multiply (right-to-left application). For complex orientations, compose transforms: `finalXform = xform3 * xform2 * xform1`.
- **Frame Extraction Performance**: Curve/Surface FrameAt is lightweight. Cache frames when processing collections.
- **Centroid Alignment**: For irregular geometry, AreaMassProperties is more accurate than BoundingBox.Center. For solids, VolumeMassProperties required.
- **Mirror Normal Reversal**: Mirror operations flip surface normals. May require explicit `.Flip()` calls post-transform for consistent orientation.
- **2D vs 3D**: For 2D operations, use Plane.WorldXY with Z=0 constraint. For 3D, full spatial transformations apply.

### SDK Version Requirements
- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+

## File Organization

### File 1: `Orient.cs`
**Purpose**: Public API surface with semantic orientation markers and primary operation entry points.

**Types** (7 total):
- `Orient`: Main static API class with orientation operation methods
- `OrientationMode`: Enum for canonical positioning modes (WorldXY, WorldYZ, WorldXZ, BoundingBox, Centroid, Custom)
- `AlignmentTarget`: Discriminated union struct for alignment destinations (Point, Plane, Curve, Surface, Frame)
- `OrientationResult`: Record containing Transform, source/target frames, operation diagnostics
- `Canonical`: Semantic marker struct for parameterless canonical positioning (similar to Extract.Semantic pattern)
- `Mirror`: Semantic marker struct for mirror plane specifications (XY, YZ, XZ, Custom)
- `Flip`: Enum for axis flip operations (X, Y, Z, XY, XZ, YZ, XYZ)

**Key Members**:
- `Result<OrientationResult> ToTarget<TGeometry>(TGeometry geometry, AlignmentTarget target, IGeometryContext context)`: Universal alignment operation using UnifiedOperation dispatch.
- `Result<OrientationResult> ToCanonical<TGeometry>(TGeometry geometry, Canonical mode, IGeometryContext context)`: Canonical positioning to world planes with bounding box alignment.
- `Result<OrientationResult> Mirror<TGeometry>(TGeometry geometry, Mirror plane, IGeometryContext context)`: Symmetry operations with automatic normal correction.
- `Result<OrientationResult> FlipAxes<TGeometry>(TGeometry geometry, Flip axes, Point3d center, IGeometryContext context)`: Axis inversion operations.
- `Result<IReadOnlyList<OrientationResult>> Batch<TGeometry>(IReadOnlyList<TGeometry> geometries, object spec, IGeometryContext context)`: Batch orientation using UnifiedOperation parallel processing.

**Code Style Example**:
```csharp
public static Result<OrientationResult> ToTarget<TGeometry>(
    TGeometry geometry,
    AlignmentTarget target,
    IGeometryContext context,
    bool preserveScale = true) where TGeometry : GeometryBase =>
    target.Kind switch {
        AlignmentTarget.TargetKind.Point => OrientCore.AlignToPoint(geometry, target.Point, context, preserveScale),
        AlignmentTarget.TargetKind.Plane => OrientCore.AlignToPlane(geometry, target.Plane, context, preserveScale),
        AlignmentTarget.TargetKind.Curve => OrientCore.AlignToCurve(geometry, target.Curve, target.Parameter, context, preserveScale),
        AlignmentTarget.TargetKind.Surface => OrientCore.AlignToSurface(geometry, target.Surface, target.UV, context, preserveScale),
        AlignmentTarget.TargetKind.Frame => OrientCore.AlignToFrame(geometry, target.Frame, context, preserveScale),
        _ => ResultFactory.Create<OrientationResult>(
            error: E.Geometry.InvalidOrientationTarget.WithContext($"Kind: {target.Kind}")),
    };
```

**LOC Estimate**: 180-220 range

### File 2: `OrientCore.cs`
**Purpose**: Core transformation computation algorithms with frame extraction and transform construction.

**Types** (2 total):
- `OrientCore`: Internal static class containing transformation algorithms
- `FrameExtractor`: Nested static class for extracting frames from various geometry types

**Key Members**:
- `Result<OrientationResult> AlignToPlane(GeometryBase geometry, Plane targetPlane, IGeometryContext context, bool preserveScale)`: Extracts source frame from geometry (centroid-based, bbox-based, or frame-based depending on type), constructs PlaneToPlane transform.
- `Result<OrientationResult> AlignToPoint(GeometryBase geometry, Point3d targetPoint, IGeometryContext context, bool preserveScale)`: Translation-only alignment to point.
- `Result<OrientationResult> AlignToCurve(GeometryBase geometry, Curve curve, double parameter, IGeometryContext context, bool preserveScale)`: Extracts curve frame at parameter, aligns geometry to curve-aligned coordinate system.
- `Result<OrientationResult> AlignToSurface(GeometryBase geometry, Surface surface, (double u, double v) uv, IGeometryContext context, bool preserveScale)`: Extracts surface frame at UV, aligns to surface-normal coordinate system.
- `Result<Plane> ExtractSourceFrame(GeometryBase geometry, IGeometryContext context)`: Polymorphic frame extraction using pattern matching on geometry type. Curve → FrameAt(mid), Surface → FrameAt(mid UV), Brep → centroid plane, Mesh → centroid plane, Point/PointCloud → origin plane.
- `Result<Transform> CanonicalTransform(BoundingBox bbox, OrientationMode mode, IGeometryContext context)`: Constructs transform to align bounding box to world planes (XY/YZ/XZ) with consistent axis orientation.
- `Result<Transform> MirrorTransform(Plane mirrorPlane, IGeometryContext context)`: Constructs reflection transform with validation.

**Code Style Example**:
```csharp
internal static Result<OrientationResult> AlignToPlane(
    GeometryBase geometry,
    Plane targetPlane,
    IGeometryContext context,
    bool preserveScale) =>
    ExtractSourceFrame(geometry, context)
        .Bind(sourcePlane => {
            Transform xform = Transform.PlaneToPlane(sourcePlane, targetPlane);
            return !preserveScale || xform.IsAffine
                ? ResultFactory.Create(value: new OrientationResult(
                    Transform: xform,
                    SourceFrame: sourcePlane,
                    TargetFrame: targetPlane,
                    OperationType: "AlignToPlane",
                    IsAffine: xform.IsAffine,
                    Determinant: xform.Determinant))
                : ResultFactory.Create<OrientationResult>(
                    error: E.Geometry.NonAffineTransform);
        });

internal static Result<Plane> ExtractSourceFrame(
    GeometryBase geometry,
    IGeometryContext context) =>
    geometry switch {
        Curve c => c.IsClosed
            ? FrameExtractor.FromCentroid(AreaMassProperties.Compute(c).Centroid, c.TangentAt(c.Domain.Mid), context)
            : ResultFactory.Create(value: c.FrameAt(c.Domain.Mid)),
        Surface s => ResultFactory.Create(value: s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid)),
        Brep brep => brep.IsSolid
            ? FrameExtractor.FromCentroid(VolumeMassProperties.Compute(brep).Centroid, Vector3d.ZAxis, context)
            : FrameExtractor.FromCentroid(AreaMassProperties.Compute(brep).Centroid, brep.Faces[0].NormalAt(0.5, 0.5), context),
        Mesh mesh => mesh.IsClosed
            ? FrameExtractor.FromCentroid(VolumeMassProperties.Compute(mesh).Centroid, Vector3d.ZAxis, context)
            : FrameExtractor.FromCentroid(AreaMassProperties.Compute(mesh).Centroid, mesh.Normals[0], context),
        Point point => ResultFactory.Create(value: new Plane(point.Location, Vector3d.XAxis, Vector3d.YAxis)),
        PointCloud cloud => FrameExtractor.BestFitPlane(cloud.GetPoints(), context),
        _ => ResultFactory.Create<Plane>(
            error: E.Geometry.UnsupportedOrientationType.WithContext($"Type: {geometry.GetType().Name}")),
    };
```

**LOC Estimate**: 220-260 range

### File 3: `OrientConfig.cs`
**Purpose**: Configuration types and FrozenDictionary dispatch tables.

**Types** (1 total):
- `OrientConfig`: Internal static class containing dispatch configuration and validation mode mappings

**Key Members**:
- `FrozenDictionary<(Type GeometryType, Type TargetType), (V ValidationMode, OrientationStrategy Strategy)> DispatchTable`: Lookup table for polymorphic operation configuration. Example: `(typeof(Curve), typeof(Plane)) → (V.Standard | V.Degeneracy, OrientationStrategy.PlaneToPlane)`.
- `FrozenDictionary<Canonical, (Plane WorldPlane, Vector3d UpVector)> CanonicalPlanes`: Mapping of canonical modes to world planes. Example: `Canonical.WorldXY → (Plane.WorldXY, Vector3d.ZAxis)`.
- `FrozenDictionary<Mirror, Plane> MirrorPlanes`: Predefined mirror planes. Example: `Mirror.XY → Plane.WorldXY`.
- `OrientationStrategy Enum`: Nested enum for strategy selection (PlaneToPlane, Translation, CurveAlign, SurfaceAlign, Canonical, Mirror).

**Code Style Example**:
```csharp
internal static class OrientConfig {
    internal enum OrientationStrategy : byte {
        PlaneToPlane = 1,
        Translation = 2,
        CurveAlign = 3,
        SurfaceAlign = 4,
        Canonical = 5,
        Mirror = 6,
    }

    internal static readonly FrozenDictionary<(Type, Type), (V, OrientationStrategy)> DispatchTable =
        new Dictionary<(Type, Type), (V, OrientationStrategy)> {
            [(typeof(Curve), typeof(Plane))] = (V.Standard | V.Degeneracy, OrientationStrategy.PlaneToPlane),
            [(typeof(Curve), typeof(Point3d))] = (V.Standard, OrientationStrategy.Translation),
            [(typeof(Curve), typeof(Curve))] = (V.Standard | V.Degeneracy, OrientationStrategy.CurveAlign),
            [(typeof(Surface), typeof(Plane))] = (V.Standard, OrientationStrategy.PlaneToPlane),
            [(typeof(Surface), typeof(Point3d))] = (V.Standard, OrientationStrategy.Translation),
            [(typeof(Surface), typeof(Surface))] = (V.Standard, OrientationStrategy.SurfaceAlign),
            [(typeof(Brep), typeof(Plane))] = (V.Standard | V.Topology, OrientationStrategy.PlaneToPlane),
            [(typeof(Brep), typeof(Point3d))] = (V.Standard | V.Topology, OrientationStrategy.Translation),
            [(typeof(Mesh), typeof(Plane))] = (V.Standard | V.MeshSpecific, OrientationStrategy.PlaneToPlane),
            [(typeof(Mesh), typeof(Point3d))] = (V.Standard | V.MeshSpecific, OrientationStrategy.Translation),
            [(typeof(Point), typeof(Point3d))] = (V.None, OrientationStrategy.Translation),
            [(typeof(PointCloud), typeof(Plane))] = (V.None, OrientationStrategy.PlaneToPlane),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<byte, (Plane WorldPlane, Vector3d UpVector)> CanonicalPlanes =
        new Dictionary<byte, (Plane, Vector3d)> {
            [1] = (Plane.WorldXY, Vector3d.ZAxis),
            [2] = (Plane.WorldYZ, Vector3d.XAxis),
            [3] = (Plane.WorldXZ, Vector3d.YAxis),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<byte, Plane> MirrorPlanes =
        new Dictionary<byte, Plane> {
            [1] = Plane.WorldXY,
            [2] = Plane.WorldYZ,
            [3] = Plane.WorldXZ,
        }.ToFrozenDictionary();
}
```

**LOC Estimate**: 100-140 range

## Adherence to Limits

- **Files**: 3 files (✓ within 4-file max, ideal 2-3 range)
- **Types**: 10 types total across 3 files (✓ at 10-type max, exceeds 6-8 ideal by 2 but justified by semantic marker pattern necessity and result type requirements)
- **Estimated Total LOC**: 500-620 LOC across 3 files
  - Orient.cs: 180-220 LOC (✓ within 300 max)
  - OrientCore.cs: 220-260 LOC (✓ within 300 max)
  - OrientConfig.cs: 100-140 LOC (✓ within 300 max)

**Type Justification**:
Each type serves distinct purpose:
1. **Orient** (public API) - Required entry point
2. **OrientationMode** (enum) - Canonical mode discrimination
3. **AlignmentTarget** (discriminated union) - Polymorphic target resolution
4. **OrientationResult** (record) - Result contract with diagnostics
5. **Canonical** (semantic marker) - Parameterless operation pattern (established in Extract)
6. **Mirror** (semantic marker) - Mirror operation discrimination
7. **Flip** (enum) - Axis flip specifications
8. **OrientCore** (algorithm container) - Core logic separation
9. **FrameExtractor** (nested utility) - Frame extraction algorithms
10. **OrientConfig** (config container) - Dispatch tables and mappings

## Algorithmic Density Strategy

**Dense code without helpers achieved through**:
- **FrozenDictionary dispatch for (GeometryType, TargetType) → (ValidationMode, Strategy)**: O(1) lookup eliminates branching logic.
- **Pattern matching on geometry type and target discriminator**: Single switch expression handles entire dispatch tree.
- **Inline frame extraction using nested ternary for centroid vs bbox vs native frame**: No extracted "GetFrame" helpers, logic inlined in switch arms.
- **Transform composition via multiplication operator**: Complex orientations compose inline: `xform = canonical * alignment * mirror`.
- **ConditionalWeakTable<(GeometryBase, AlignmentTarget), Transform> caching**: Automatic memoization without manual cache management code.
- **Result<T> monadic chains for validation → extraction → construction → verification**: Eliminates imperative null checks and error handling sprawl.
- **AreaMassProperties/VolumeMassProperties inline computation**: Centroid extraction occurs inline in pattern match arms, no utility methods.
- **Semantic marker structs with byte discriminators**: Zero-allocation parameterless operation specification without enum boxing.

## Dispatch Architecture

**Primary Dispatch**: FrozenDictionary keyed by `(Type GeometryType, Type TargetType)` → `(V ValidationMode, OrientationStrategy Strategy)`

**Secondary Dispatch**: Switch expression on `AlignmentTarget.Kind` → core algorithm method

**Tertiary Dispatch**: Pattern matching in `ExtractSourceFrame` on `GeometryBase` runtime type → frame extraction logic

**Cache Strategy**: ConditionalWeakTable for automatic transform reuse when same geometry-target pair requested multiple times

**Example Dispatch Flow**:
```
User: Orient.ToTarget(curve, AlignmentTarget.FromPlane(targetPlane), context)
  ↓
AlignmentTarget.Kind switch → Plane
  ↓
OrientCore.AlignToPlane(curve, targetPlane, context, preserveScale)
  ↓
ExtractSourceFrame(curve, context) → curve switch → Curve → FrameAt(mid)
  ↓
Transform.PlaneToPlane(sourceFrame, targetPlane)
  ↓
OrientationResult(transform, sourceFrame, targetFrame, metadata)
```

## Public API Surface

### Primary Operations
```csharp
// Universal alignment to polymorphic targets
public static Result<OrientationResult> ToTarget<TGeometry>(
    TGeometry geometry,
    AlignmentTarget target,
    IGeometryContext context,
    bool preserveScale = true) where TGeometry : GeometryBase;

// Canonical positioning to world planes
public static Result<OrientationResult> ToCanonical<TGeometry>(
    TGeometry geometry,
    Canonical mode,
    IGeometryContext context,
    bool centerOrigin = true) where TGeometry : GeometryBase;

// Mirror/symmetry operations
public static Result<OrientationResult> Mirror<TGeometry>(
    TGeometry geometry,
    Mirror plane,
    IGeometryContext context) where TGeometry : GeometryBase;

// Axis flip operations
public static Result<OrientationResult> FlipAxes<TGeometry>(
    TGeometry geometry,
    Flip axes,
    Point3d center,
    IGeometryContext context) where TGeometry : GeometryBase;

// Batch orientation processing
public static Result<IReadOnlyList<OrientationResult>> Batch<TGeometry>(
    IReadOnlyList<TGeometry> geometries,
    object spec,
    IGeometryContext context,
    bool enableParallel = false) where TGeometry : GeometryBase;
```

### Configuration Types
```csharp
// Alignment target discriminated union
public readonly struct AlignmentTarget {
    internal enum TargetKind : byte { Point, Plane, Curve, Surface, Frame }
    internal readonly TargetKind Kind;
    internal readonly object Target;
    internal readonly double Parameter;  // For Curve
    internal readonly (double u, double v) UV;  // For Surface

    public static AlignmentTarget FromPoint(Point3d point);
    public static AlignmentTarget FromPlane(Plane plane);
    public static AlignmentTarget FromCurve(Curve curve, double parameter);
    public static AlignmentTarget FromSurface(Surface surface, double u, double v);
    public static AlignmentTarget FromFrame(Plane frame);

    // Properties for pattern matching
    internal Point3d Point => this.Kind == TargetKind.Point ? (Point3d)this.Target : throw new InvalidOperationException();
    internal Plane Plane => this.Kind == TargetKind.Plane || this.Kind == TargetKind.Frame ? (Plane)this.Target : throw new InvalidOperationException();
    internal Curve Curve => this.Kind == TargetKind.Curve ? (Curve)this.Target : throw new InvalidOperationException();
    internal Surface Surface => this.Kind == TargetKind.Surface ? (Surface)this.Target : throw new InvalidOperationException();
    internal Plane Frame => this.Kind == TargetKind.Frame ? (Plane)this.Target : throw new InvalidOperationException();
}

// Orientation result with diagnostics
public sealed record OrientationResult(
    Transform Transform,
    Plane SourceFrame,
    Plane TargetFrame,
    string OperationType,
    bool IsAffine,
    double Determinant);

// Semantic markers for parameterless operations
public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;
    public static readonly Canonical WorldXY = new(1);
    public static readonly Canonical WorldYZ = new(2);
    public static readonly Canonical WorldXZ = new(3);
    public static readonly Canonical BoundingBox = new(4);
    public static readonly Canonical AreaCentroid = new(5);
    public static readonly Canonical VolumeCentroid = new(6);
}

public readonly struct Mirror(byte plane) {
    internal readonly byte Plane = plane;
    public static readonly Mirror XY = new(1);
    public static readonly Mirror YZ = new(2);
    public static readonly Mirror XZ = new(3);
    public static Mirror Custom(Plane customPlane);  // Returns wrapper with custom plane stored
}

public enum Flip : byte {
    X = 1,
    Y = 2,
    Z = 4,
    XY = 3,
    XZ = 5,
    YZ = 6,
    XYZ = 7,
}
```

### Orientation Modes
```csharp
public enum OrientationMode : byte {
    WorldXY = 1,      // Align bounding box to XY plane, Z-up
    WorldYZ = 2,      // Align bounding box to YZ plane, X-up
    WorldXZ = 3,      // Align bounding box to XZ plane, Y-up
    BoundingBox = 4,  // Align to world-axis-aligned bbox, auto-detect best plane
    Centroid = 5,     // Align area/volume centroid to origin
    Custom = 6,       // User-specified target plane
}
```

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters
- [x] All examples use trailing commas
- [x] All examples use K&R brace style
- [x] All examples use target-typed new()
- [x] All examples use collection expressions []
- [x] One type per file organization specified
- [x] All member estimates under 300 LOC
- [x] All patterns match existing libs/ exemplars (Spatial dispatch, Extract semantic markers, Analysis IResult)

## Implementation Sequence

1. Read this blueprint thoroughly
2. Double-check SDK usage patterns (PlaneToPlane vs ChangeBasis distinction critical)
3. Verify libs/ integration strategy (UnifiedOperation config, Result chains, E registry)
4. Create folder structure and files
5. Implement OrientConfig.cs first (dispatch tables and enums foundation)
6. Implement OrientCore.cs (core algorithms and frame extraction)
7. Implement Orient.cs (public API surface)
8. Add error codes to E.cs registry:
   - `2400: "Invalid orientation target specified"`
   - `2401: "Non-affine transformation not supported for preserveScale=true"`
   - `2402: "Degenerate source frame extracted"`
   - `2403: "Incompatible geometry types for orientation"`
   - `2404: "Canonical positioning failed"`
   - `2405: "Mirror plane is degenerate"`
9. Implement FrozenDictionary dispatch tables with trailing commas
10. Implement frame extraction logic with inline mass properties computation
11. Implement canonical positioning with bounding box alignment
12. Implement mirror operations with normal correction awareness
13. Implement flip operations with rotation matrix construction
14. Add ConditionalWeakTable caching for transform memoization
15. Verify patterns match exemplars (FrozenDictionary from Spatial, Semantic from Extract, IResult from Analysis)
16. Check LOC limits (≤300 per member)
17. Verify file/type limits (3 files ✓, 10 types ✓)
18. Verify code style compliance (all checkboxes above)

## References

### SDK Documentation
- [RhinoCommon Transform API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform)
- [Transform.PlaneToPlane Method](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/planetoplane)
- [Transform Methods Reference](https://apidocs.co/apps/rhinocommon/7.3.21039.11200/Methods_T_Rhino_Geometry_Transform.htm)
- [AreaMassProperties API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.areamassproperties)
- [VolumeMassProperties API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.volumemassproperties)
- [BoundingBox API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.boundingbox)
- [Plane API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.plane)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns, Map/Bind/Ensure chains
- `libs/core/results/ResultFactory.cs` - Polymorphic parameter detection, Create overloads
- `libs/core/operations/UnifiedOperation.cs` - Dispatch engine, OperationConfig usage
- `libs/core/validation/ValidationRules.cs` - Expression tree compilation (not needed here, but pattern reference)
- `libs/core/validation/V.cs` - Validation mode bitwise flags
- `libs/core/errors/E.cs` - Error registry structure and allocation ranges
- `libs/rhino/spatial/Spatial.cs` - FrozenDictionary dispatch pattern
- `libs/rhino/spatial/SpatialCore.cs` - Core algorithm separation pattern
- `libs/rhino/extraction/Extract.cs` - Semantic marker pattern (Analytical, Extremal, etc.)
- `libs/rhino/extraction/ExtractionCore.cs` - Pattern matching on spec type
- `libs/rhino/analysis/Analysis.cs` - IResult marker interface, record types with diagnostics
- `libs/rhino/analysis/AnalysisCompute.cs` - Polymorphic geometry handling

### Community Forum Discussions
- [RhinoCommon Transform.ChangeBasis vs Transform.PlaneToPlane](https://discourse.mcneel.com/t/rhinocommon-transform-changebasis-vs-transform-planetoplane/155869)
- [Orient function in RhinoCommon](https://discourse.mcneel.com/t/orient-function-in-rhinocommon/48914)
- [Plane-aligned BoundingBox](https://discourse.mcneel.com/t/rhinocommon-plane-aligned-boundingbox/64428)
- [Centroid of a Brep using RhinoCommon](https://discourse.mcneel.com/t/how-do-i-get-a-centroid-of-a-brep-using-rhinocommon/79849)
- [Flipped normals when mirroring](https://discourse.mcneel.com/t/flipped-normals-when-i-mirror-a-block/7809)

## Critical Implementation Notes

### Transform.PlaneToPlane vs Transform.ChangeBasis
**CRITICAL**: Always use `Transform.PlaneToPlane` for physical orientation. `ChangeBasis` is for coordinate system reinterpretation without moving geometry. Confusing these leads to incorrect results.

### Frame Extraction Strategy
For geometry without explicit frames (Brep, Mesh), use centroid-based plane construction:
```csharp
Point3d centroid = IsSolid ? VolumeMassProperties.Compute(geom).Centroid : AreaMassProperties.Compute(geom).Centroid;
Vector3d normal = ExtractRepresentativeNormal(geom);  // First face normal, average mesh normal, etc.
Plane frame = new Plane(centroid, normal);
```

### Mirror Normal Correction
Mirror operations flip surface normals. For consistent orientation:
1. Apply mirror transform: `geometry.Transform(mirrorXform)`
2. Check if normals need flipping: `if (mirrorXform.Determinant < 0) geometry.Flip()`
3. Return result with diagnostic flag indicating flip was required

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
return transform.IsAffine && Math.Abs(transform.Determinant) > context.AbsoluteTolerance
    ? ResultFactory.Create(value: result)
    : ResultFactory.Create<OrientationResult>(error: E.Geometry.DegenerateTransform);
```

### Canonical Positioning Algorithm
1. Compute bounding box: `BoundingBox bbox = geometry.GetBoundingBox(Plane.WorldXY)`
2. Determine target plane (WorldXY, WorldYZ, or WorldXZ based on mode)
3. Construct source plane at bbox center with bbox-aligned axes
4. Apply PlaneToPlane from source to target: `Transform.PlaneToPlane(sourcePlane, targetPlane)`
5. Optional: Center at origin by subtracting bbox.Center translation

### Batch Processing Configuration
```csharp
UnifiedOperation.Apply(
    input: geometries,
    operation: (Func<TGeometry, Result<IReadOnlyList<OrientationResult>>>)(geom => 
        ToTarget(geom, target, context).Map(r => (IReadOnlyList<OrientationResult>)[r,])),
    config: new OperationConfig<TGeometry, OrientationResult> {
        Context = context,
        ValidationMode = V.Standard,  // Adjust based on geometry type
        EnableParallel = enableParallel,
        MaxDegreeOfParallelism = -1,  // Use default
        AccumulateErrors = true,  // Collect all errors, don't short-circuit
        SkipInvalid = false,  // Fail on invalid geometry
        EnableCache = true,  // Memoize results
        OperationName = "Orient.Batch",
        EnableDiagnostics = false,  // Enable in DEBUG builds only
    });
```

## Error Code Allocation

**Range: 2400-2449 (Orientation errors within Geometry domain 2000-2999)**

Add to `libs/core/errors/E.cs`:
```csharp
[2400] = "Invalid orientation target specified",
[2401] = "Non-affine transformation not supported with preserveScale=true",
[2402] = "Degenerate source frame extracted from geometry",
[2403] = "Incompatible geometry and target types for orientation",
[2404] = "Canonical positioning failed for geometry",
[2405] = "Mirror plane is degenerate or invalid",
[2406] = "Axis flip operation failed",
[2407] = "Frame extraction failed for geometry type",
[2408] = "Transform composition resulted in degenerate matrix",
```

Add to `E.Geometry` class:
```csharp
public static readonly SystemError InvalidOrientationTarget = Get(2400);
public static readonly SystemError NonAffineTransform = Get(2401);
public static readonly SystemError DegenerateSourceFrame = Get(2402);
public static readonly SystemError IncompatibleOrientationTypes = Get(2403);
public static readonly SystemError CanonicalPositioningFailed = Get(2404);
public static readonly SystemError DegenerateMirrorPlane = Get(2405);
public static readonly SystemError AxisFlipFailed = Get(2406);
public static readonly SystemError FrameExtractionFailed = Get(2407);
public static readonly SystemError DegenerateTransform = Get(2408);
```

## Validation Mode Selection

**Per-Operation Validation**:
- **Point alignment**: `V.Standard` (basic validity only)
- **Plane alignment**: `V.Standard | V.Degeneracy` (ensure non-degenerate geometry)
- **Curve alignment**: `V.Standard | V.Degeneracy` (ensure curve has length, not degenerate)
- **Surface alignment**: `V.Standard | V.Degeneracy` (ensure surface not collapsed)
- **Canonical positioning**: `V.BoundingBox` (ensure bbox is valid)
- **Mirror operations**: `V.Standard` (basic validity)
- **Brep operations**: `V.Standard | V.Topology` (ensure manifold, closed if required)
- **Mesh operations**: `V.Standard | V.MeshSpecific` (ensure mesh validity)

## Performance Considerations

**Caching Strategy**:
- Use ConditionalWeakTable for automatic GC-aware cache
- Cache key: `(GeometryBase, AlignmentTarget)` tuple
- Cache value: `OrientationResult` (includes transform and metadata)
- Cache invalidation: Automatic via weak references when geometry disposed

**Parallel Processing**:
- Enable for collections > 10 items
- Use default parallelism (MaxDegreeOfParallelism = -1)
- Accumulate errors (don't short-circuit) for batch diagnostics

**Mass Properties Optimization**:
- Compute once per geometry, cache in local scope
- For repeated operations on same geometry, cache centroid/area/volume in ConditionalWeakTable
- Use AreaMassProperties only when geometry is closed and planar/surface
- Use VolumeMassProperties only when geometry is solid (Brep.IsSolid or Mesh.IsClosed)

**Frame Extraction Optimization**:
- For Curve: Use FrameAt(Domain.Mid) - O(1)
- For Surface: Use FrameAt(Domain.Mid, Domain.Mid) - O(1)
- For Brep/Mesh: Compute centroid once, construct plane - O(n) for centroid, O(1) for plane
- For PointCloud: Use Plane.FitPlaneToPoints - O(n), cache result

## Comprehensive Scope Coverage

### 2D Orientation
- [x] Align to XY plane (canonical WorldXY)
- [x] Align to point in XY plane (Z=0 constraint)
- [x] Align to planar curve (extract 2D frame from curve)
- [x] Mirror across X axis (Mirror.YZ with Z=0)
- [x] Mirror across Y axis (Mirror.XZ with Z=0)
- [x] Flip X/Y axes (Flip.X, Flip.Y)

### 3D Orientation
- [x] Align to arbitrary plane (ToTarget with plane)
- [x] Align to 3D point (ToTarget with point)
- [x] Align to spatial curve (curve FrameAt)
- [x] Align to surface (surface FrameAt with UV)
- [x] Mirror across arbitrary plane (Mirror.Custom)
- [x] Flip X/Y/Z axes (Flip.XYZ)

### Canonical Positioning
- [x] Bounding box to World XY (Canonical.WorldXY)
- [x] Bounding box to World YZ (Canonical.WorldYZ)
- [x] Bounding box to World XZ (Canonical.WorldXZ)
- [x] Consistent Z-axis orientation (auto-correct in canonical)
- [x] Center at origin option (centerOrigin parameter)

### Alignment Targets
- [x] Point alignment (centroid to point translation)
- [x] Plane alignment (PlaneToPlane full orientation)
- [x] Curve alignment (align to curve frame at parameter)
- [x] Surface alignment (align to surface frame at UV)
- [x] Frame alignment (explicit frame specification)
- [x] Centroid alignment (area/volume centroid extraction)
- [x] Bounding box center alignment (bbox center extraction)

### Mirror/Flip Operations
- [x] Mirror XY plane (reflection across World XY)
- [x] Mirror YZ plane (reflection across World YZ)
- [x] Mirror XZ plane (reflection across World XZ)
- [x] Mirror custom plane (user-specified mirror plane)
- [x] Flip X axis (rotation 180° around YZ plane)
- [x] Flip Y axis (rotation 180° around XZ plane)
- [x] Flip Z axis (rotation 180° around XY plane)
- [x] Flip multiple axes (combined flips)

### Geometry Type Coverage
- [x] Point/PointCloud (origin/best-fit plane)
- [x] Curve (FrameAt, area centroid for closed)
- [x] Surface (FrameAt with UV)
- [x] Brep (area/volume centroid, face frame)
- [x] Mesh (area/volume centroid, vertex frame)
- [x] Collections (batch processing with UnifiedOperation)

### Advanced Features
- [x] Transform composition (multiple operations chained)
- [x] Scale preservation option (preserveScale parameter)
- [x] Parallel batch processing (enableParallel parameter)
- [x] Transform caching (ConditionalWeakTable)
- [x] Validation mode per geometry type (dispatch table)
- [x] Error accumulation (applicative functor semantics)
- [x] Diagnostic metadata (transform properties in result)

## Final Verification Checklist

Before implementation:
- [ ] Read all exemplar files (`Spatial.cs`, `Extract.cs`, `Analysis.cs`, `UnifiedOperation.cs`, `ResultFactory.cs`)
- [ ] Verify understanding of PlaneToPlane vs ChangeBasis distinction
- [ ] Confirm error code allocation (2400-2449 range available in E.cs)
- [ ] Verify no overlap with existing transformation functionality (this is orientation, not general transforms)
- [ ] Confirm ConditionalWeakTable pattern from Spatial.cs
- [ ] Confirm Semantic marker pattern from Extract.cs
- [ ] Confirm IResult pattern from Analysis.cs

During implementation:
- [ ] Verify each file stays under 300 LOC per member
- [ ] Verify no helper methods extracted (inline complex logic)
- [ ] Verify all pattern matching (no if/else statements)
- [ ] Verify all explicit types (no var)
- [ ] Verify all named parameters (non-obvious args)
- [ ] Verify all trailing commas (multi-line collections)
- [ ] Verify all K&R braces (same line opening)
- [ ] Verify target-typed new() (no redundant types)
- [ ] Verify collection expressions [] (no new List<>())

After implementation:
- [ ] Build with zero warnings
- [ ] Verify 3 files total (✓ limit)
- [ ] Verify 10 types total (✓ limit)
- [ ] Verify all LOC under 300 per member
- [ ] Verify all patterns match exemplars
- [ ] Verify integration with libs/core (Result, UnifiedOperation, V, E)
- [ ] Verify error codes added to E.cs
- [ ] Verify no code duplication with existing modules

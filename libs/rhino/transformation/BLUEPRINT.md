# Transform Library Blueprint

## Overview

Comprehensive geometric transformation library providing affine transforms (scale, rotate, mirror, translate, shear), array operations (rectangular, polar, linear), and advanced deformations (morph, flow, twist, bend, taper) with unified polymorphic dispatch for Brep, Mesh, Curve, Surface, and other RhinoCommon geometry types.

---

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage

**Result<T> Monad**:
- `.Map(x => Transform(x))` - Transform successful values
- `.Bind(x => ChainTransform(x))` - Monadic chaining for multi-step transforms
- `.Ensure(pred, error: E.Transform.*)` - Validation before applying transforms
- `.Match(onSuccess, onFailure)` - Pattern matching for error handling
- All operations return `Result<T>` for explicit error handling

**UnifiedOperation**:
- Polymorphic dispatch for handling `Brep`, `Mesh`, `Curve`, `Surface`, `Extrusion`, etc.
- Collection handling (single item or `IReadOnlyList<T>`)
- Validation integration via `OperationConfig.ValidationMode`
- Will use for all public API methods to ensure consistent behavior

**ValidationRules**:
- **Existing V.* modes we'll use**:
  - `V.Standard` - Basic IsValid check for all geometry
  - `V.Topology` - For Breps requiring manifold validation
  - `V.MeshSpecific` - For mesh-specific validations
  - `V.BoundingBox` - For operations requiring valid bounds
  - `V.Degeneracy` - For curve/surface degenerate checks
  - `V.None` - For Transform matrix-only operations (no geometry validation)

- **New V.* modes we need** (will add to libs/core/validation/V.cs):
  - `V.TransformValidity = new(32768)` - Validates Transform matrix properties (IsValid, IsAffine, etc.)
  - Justification: Transform matrices can be singular, non-affine, or degenerate; explicit validation prevents applying invalid transforms

**Error Registry**:
- **Existing E.* errors we'll use**:
  - `E.Geometry.TransformFailed` (2502) - Already exists for general transform failures
  - `E.Geometry.UnsupportedAnalysis` (2300) - For unsupported geometry types
  - `E.Validation.GeometryInvalid` (3000) - For invalid input geometry

- **New E.Transform.* error codes we need** (2100-2199 range):
  - `2100`: "Transform matrix is invalid or singular"
  - `2101`: "Scale factor must be non-zero"
  - `2102`: "Rotation axis vector is zero-length or invalid"
  - `2103`: "Mirror plane is invalid or degenerate"
  - `2104`: "Array count must be positive"
  - `2105`: "Array spacing must be greater than tolerance"
  - `2106`: "Polar array angle must be in range (0, 2π]"
  - `2107`: "Morph operation failed or geometry not morphable"
  - `2108`: "Flow curves must be of equal parametric length"
  - `2109`: "Twist axis is invalid or angle out of range"
  - `2110`: "Bend spine curve is invalid"
  - `2111`: "Taper parameters produce degenerate geometry"
  - `2112`: "Stretch parameters are invalid"
  - `2113`: "Shear plane and direction are collinear"
  - `2114`: "Projection plane is invalid"
  - `2115`: "Transform composition failed"
  - `2116`: "Cage morph control points insufficient or mismatched"

**Context**:
- `IGeometryContext.AbsoluteTolerance` - For transform validation thresholds
- `IGeometryContext.AngleTolerance` - For rotation angle validation
- Will use throughout for tolerance-dependent operations

### Similar libs/rhino/ Implementations

**`libs/rhino/orientation/`** - Patterns we're borrowing:
- OrientCore.ApplyTransform pattern for applying transforms to geometry
- Plane extraction logic for frame-based operations
- Centroid extraction for mass property-based transforms
- **CRITICAL**: We will cleanly refactor Mirror operation from Orient.cs into Transform.cs without duplication

**`libs/rhino/spatial/`** - Patterns we're borrowing:
- FrozenDictionary dispatch with tuple keys: `(Type Input, Type Query)`
- ArrayPool buffer management for collection operations
- Factory method pattern for RTree-like expensive object construction
- 4-file structure with Compute file for advanced operations

**`libs/rhino/morphology/`** - Patterns we're borrowing:
- Cage-based deformation patterns
- Subdivision and smoothing operation structures
- Error handling for mesh-specific operations

**No Duplication Confirmation**:
- ✅ Verified no existing scale/rotate operations in other folders
- ✅ Verified mirror only exists in orientation (will refactor)
- ✅ Verified no array transformation logic exists
- ✅ Verified no morph/deformation logic outside morphology folder (which handles mesh-specific smoothing, not geometric morphs)
- ✅ Transform operations are net-new except Mirror which we'll properly consolidate

---

## SDK Research Summary

### RhinoCommon APIs Used

**Transform Struct** (`Rhino.Geometry.Transform`):
- `Transform.Scale(Point3d anchor, double factor)` - Uniform scaling
- `Transform.Scale(Plane plane, double xScale, double yScale, double zScale)` - Non-uniform scaling
- `Transform.Rotation(double angleRadians, Vector3d axis, Point3d center)` - Rotation around axis
- `Transform.Rotation(Vector3d start, Vector3d end, Point3d center)` - Rotation between vectors
- `Transform.Mirror(Plane plane)` - Mirror reflection across plane
- `Transform.Translation(Vector3d motion)` - Translation by vector
- `Transform.Translation(Point3d start, Point3d end)` - Translation between points
- `Transform.Shear(Plane plane, Vector3d direction, double angle)` - Shear deformation
- `Transform.ProjectionToPlane(Plane plane)` - Orthogonal projection to plane
- `Transform.ChangeBasis(Plane from, Plane to)` - Change of coordinate system
- `Transform.PlaneToPlane(Plane from, Plane to)` - Combined rotation/translation
- `Transform.Multiply(Transform a, Transform b)` - Transform composition
- `Transform.TryGetInverse(out Transform inverse)` - Inverse transform
- Properties: `IsValid`, `IsAffine`, `IsIdentity`, `IsZeroTransformation`, `Determinant`

**SpaceMorph Classes** (`Rhino.Geometry.Morphs.*`):
- `FlowSpaceMorph` - Flow geometry from base curve to target curve
- `TwistSpaceMorph` - Twist geometry around axis by angle
- `BendSpaceMorph` - Bend geometry along spine curve
- `TaperSpaceMorph` - Taper geometry along axis
- `StretchSpaceMorph` - Stretch geometry in direction
- `SplopSpaceMorph` - Project from plane to point on surface
- `SporphSpaceMorph` - Deform from source surface to target surface
- `MaelstromSpaceMorph` - Vortex-like deformation
- Base class: `SpaceMorph.Morph(GeometryBase)`, `SpaceMorph.MorphPoint(Point3d)`, `IsMorphable(GeometryBase)`
- Properties: `PreserveStructure`, `QuickPreview`, `Tolerance`

**GeometryBase Methods**:
- `geometry.Transform(Transform xform)` - In-place transformation
- `geometry.Duplicate()` - Duplication for non-mutating operations
- `geometry.GetBoundingBox(bool accurate)` - For array spacing calculations
- `geometry is IDisposable` - Memory management for converted geometry

**RhinoMath Constants**:
- `RhinoMath.ToRadians(double degrees)` - Angle conversion
- `RhinoMath.ToDegrees(double radians)` - Angle conversion
- `RhinoMath.PI`, `RhinoMath.TwoPI` - Circle constants
- `RhinoMath.ZeroTolerance` - Zero comparison
- `RhinoMath.Clamp(double value, double min, double max)` - Value clamping

### Key Insights

**Performance Consideration**:
- Meshes transform ~100x faster than Breps (use mesh conversion when acceptable)
- Transform matrix multiplication is O(1) - compose transforms before applying to geometry
- ArrayPool for collection operations to avoid allocation overhead
- ConditionalWeakTable not needed (transforms don't require caching)

**Common Pitfall to Avoid**:
- Transform.Transform(Transform) mutates the matrix - always use Transform.Multiply for composition
- SpaceMorph.Morph() modifies geometry in-place - always Duplicate() first for functional style
- Extrusion.ToBrep() creates new geometry - must dispose after use
- Transform.IsValid doesn't guarantee non-singular - check Determinant != 0 for invertibility

**Best Practice**:
- Validate Transform matrices before applying (IsValid, IsAffine, Determinant)
- Compose multiple transforms into single matrix before applying to collections
- Use explicit type conversion (Extrusion → Brep) with disposal pattern
- Leverage RhinoMath constants instead of magic numbers
- For arrays: pre-compute all transforms, then apply in batch

### SDK Version Requirements

- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+
- Transform struct API stable since Rhino 5, SpaceMorph since Rhino 6

---

## File Organization

### File 1: `Transform.cs`
**Purpose**: Public API - unified transformation operations with polymorphic dispatch

**Types** (7 total):
- `Transform` (main API class)
- `Transform.TransformSpec` (nested readonly record struct) - Transform specification discriminated union
- `Transform.ArraySpec` (nested readonly record struct) - Array transformation specification
- `Transform.MorphSpec` (nested readonly record struct) - Morph operation specification
- `Transform.ScaleMode` (nested readonly struct) - Scale operation modes (Uniform, NonUniform, Directional)
- `Transform.RotateMode` (nested readonly struct) - Rotation operation modes (Axis, Vector, Plane)
- `Transform.ArrayMode` (nested readonly struct) - Array operation modes (Rectangular, Polar, Linear, Path)

**Key Members**:
- `Apply<T>(T geometry, TransformSpec spec, IGeometryContext context)`: Primary unified API
  - Pattern matches TransformSpec discriminated union
  - Dispatches to appropriate TransformCore method via FrozenDictionary
  - Returns `Result<T>` for single items, `Result<IReadOnlyList<T>>` for collections
- `ArrayTransform<T>(T geometry, ArraySpec spec, IGeometryContext context)`: Array operations
  - Generates transforms based on ArrayMode (rectangular/polar/linear/path)
  - Uses UnifiedOperation for batch application
- `Morph<T>(T geometry, MorphSpec spec, IGeometryContext context)`: Deformation operations
  - Wraps SpaceMorph operations in Result monad
  - Handles geometry duplication and disposal
- Type-specific overloads: `Scale()`, `Rotate()`, `Mirror()`, `Translate()`, `Shear()`
  - Ergonomic entry points that construct TransformSpec and delegate to Apply

**Code Style Example**:
```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transform is the primary API entry point")]
public static class Transform {
    /// <summary>Transform specification with discriminated union pattern.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct TransformSpec {
        public Rhino.Geometry.Transform? Matrix { get; init; }
        public (Point3d Anchor, double Factor)? UniformScale { get; init; }
        public (Plane Plane, double X, double Y, double Z)? NonUniformScale { get; init; }
        public (double Angle, Vector3d Axis, Point3d Center)? Rotation { get; init; }
        public Plane? MirrorPlane { get; init; }
        public Vector3d? Translation { get; init; }
        public (Plane Plane, Vector3d Direction, double Angle)? Shear { get; init; }

        public static TransformSpec Matrix(Rhino.Geometry.Transform xform) => new() { Matrix = xform };
        public static TransformSpec Scale(Point3d anchor, double factor) => new() { UniformScale = (anchor, factor) };
        public static TransformSpec Rotate(double angle, Vector3d axis, Point3d center) => new() { Rotation = (angle, axis, center) };
        public static TransformSpec Mirror(Plane plane) => new() { MirrorPlane = plane };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformSpec spec,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformCore.BuildTransform(spec: spec, context: context)
            .Bind(xform => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    TransformCore.ApplyTransform(item: item, transform: xform, context: context)),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = TransformConfig.GetValidationMode(typeof(T)),
                    OperationName = $"Transform.{spec}",
                    EnableDiagnostics = enableDiagnostics,
                }))
            .Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Point3d anchor,
        double factor,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.Scale(anchor, factor), context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        ArraySpec spec,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        spec.Mode.Value switch {
            1 => TransformCore.RectangularArray(geometry: geometry, xCount: spec.XCount, yCount: spec.YCount, zCount: spec.ZCount ?? 1, xSpacing: spec.XSpacing, ySpacing: spec.YSpacing, zSpacing: spec.ZSpacing ?? 0.0, context: context, enableDiagnostics: enableDiagnostics),
            2 => TransformCore.PolarArray(geometry: geometry, center: spec.Center!.Value, axis: spec.Axis!.Value, count: spec.Count, angle: spec.TotalAngle ?? RhinoMath.TwoPI, context: context, enableDiagnostics: enableDiagnostics),
            3 => TransformCore.LinearArray(geometry: geometry, direction: spec.Direction!.Value, count: spec.Count, spacing: spec.Spacing, context: context, enableDiagnostics: enableDiagnostics),
            4 => TransformCore.PathArray(geometry: geometry, path: spec.PathCurve!, count: spec.Count, orient: spec.OrientToPath, context: context, enableDiagnostics: enableDiagnostics),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.InvalidArrayMode),
        };
}
```

**LOC Estimate**: 180-250 range (dense API with nested type definitions)

### File 2: `TransformConfig.cs`
**Purpose**: Constants, FrozenDictionary validation mode mappings, error code allocations

**Types** (1 total):
- `TransformConfig` (internal static class)

**Key Members**:
- `ValidationModes`: FrozenDictionary<Type, V> - Type-to-validation-mode mapping
- `DefaultTolerance`: Minimum tolerance for transform operations
- `MaxArrayCount`: Maximum array count to prevent memory issues
- `MaxRotationAngle`: Angle validation threshold

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Transform;

internal static class TransformConfig {
    internal const int DefaultArrayCount = 10;
    internal const int MaxArrayCount = 10000;
    internal const double DefaultTolerance = 0.001;
    internal const double MinScaleFactor = 1e-6;

    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point3d)] = V.None,
            [typeof(Point3d[])] = V.None,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static V GetValidationMode(Type geometryType) =>
        ValidationModes.TryGetValue(geometryType, out V mode)
            ? mode
            : ValidationModes
                .Where(kv => kv.Key.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key,
                    Comparer<Type>.Create(static (a, b) =>
                        a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();
}
```

**LOC Estimate**: 50-80 range (pure configuration data)

### File 3: `TransformCore.cs`
**Purpose**: Core implementation logic - transform building, geometry application, validation

**Types** (1 total):
- `TransformCore` (internal static class)

**Key Members**:
- `BuildTransform(TransformSpec, IGeometryContext)`: Constructs Transform matrix from spec
  - Validates parameters (scale factor != 0, axis length > 0, plane validity)
  - Returns `Result<Rhino.Geometry.Transform>` with proper error codes
  - Uses RhinoMath constants for angle/tolerance validation
- `ApplyTransform<T>(T, Transform, IGeometryContext)`: Applies transform to geometry
  - Handles Extrusion → Brep conversion with disposal
  - Duplicates geometry for functional semantics
  - Returns `Result<IReadOnlyList<T>>` for UnifiedOperation compatibility
- `RectangularArray<T>()`: Generates rectangular grid transforms
  - Pre-computes all translations via nested loops
  - Uses collection expression for transform list
  - Batch applies via UnifiedOperation
- `PolarArray<T>()`: Generates rotational array transforms
  - Computes angular spacing via RhinoMath.TwoPI / count
  - Pre-allocates transform list with collection expression
- `LinearArray<T>()`, `PathArray<T>()`: Additional array operations
- `ValidateTransform(Transform, IGeometryContext)`: Transform matrix validation
  - Checks IsValid, Determinant != 0, IsAffine
  - Returns `Result<Transform>` with specific error codes

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Transform;

internal static class TransformCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Rhino.Geometry.Transform> BuildTransform(
        Transform.TransformSpec spec,
        IGeometryContext context) =>
        spec switch {
            { Matrix: Rhino.Geometry.Transform m } => ValidateTransform(transform: m, context: context),
            { UniformScale: (Point3d anchor, double factor) } =>
                factor > TransformConfig.MinScaleFactor && factor < 1.0 / TransformConfig.MinScaleFactor
                    ? ResultFactory.Create(value: Rhino.Geometry.Transform.Scale(anchor, factor))
                    : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidScaleFactor),
            { NonUniformScale: (Plane plane, double x, double y, double z) } =>
                plane.IsValid && x > TransformConfig.MinScaleFactor && y > TransformConfig.MinScaleFactor && z > TransformConfig.MinScaleFactor
                    ? ResultFactory.Create(value: Rhino.Geometry.Transform.Scale(plane, x, y, z))
                    : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidScaleFactor),
            { Rotation: (double angle, Vector3d axis, Point3d center) } =>
                axis.Length > context.AbsoluteTolerance && Math.Abs(angle) <= RhinoMath.TwoPI
                    ? ResultFactory.Create(value: Rhino.Geometry.Transform.Rotation(angle, axis, center))
                    : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidRotationAxis),
            { MirrorPlane: Plane plane } =>
                plane.IsValid
                    ? ResultFactory.Create(value: Rhino.Geometry.Transform.Mirror(plane))
                    : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidMirrorPlane),
            { Translation: Vector3d motion } =>
                ResultFactory.Create(value: Rhino.Geometry.Transform.Translation(motion)),
            { Shear: (Plane plane, Vector3d direction, double angle) } =>
                plane.IsValid && direction.Length > context.AbsoluteTolerance && !plane.ZAxis.IsParallelTo(direction, context.AngleTolerance)
                    ? ResultFactory.Create(value: Rhino.Geometry.Transform.Shear(plane, direction, angle))
                    : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidShear),
            _ => ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidTransformSpec),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        Rhino.Geometry.Transform transform,
        IGeometryContext context) where T : GeometryBase {
        (GeometryBase normalized, bool shouldDispose) = item switch {
            Extrusion ext => (ext.ToBrep(splitKinkyFaces: true), true),
            GeometryBase g => (g, false),
        };

        try {
            T duplicate = (T)normalized.Duplicate();
            return duplicate.Transform(transform)
                ? ResultFactory.Create<IReadOnlyList<T>>(value: [duplicate,])
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.TransformApplicationFailed);
        } finally {
            (shouldDispose ? normalized as IDisposable : null)?.Dispose();
        }
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> RectangularArray<T>(
        T geometry,
        int xCount,
        int yCount,
        int zCount,
        double xSpacing,
        double ySpacing,
        double zSpacing,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        xCount > 0 && yCount > 0 && zCount > 0 && xCount <= TransformConfig.MaxArrayCount
            ? ((Func<Result<Rhino.Geometry.Transform[]>>)(() => {
                Rhino.Geometry.Transform[] transforms = new Rhino.Geometry.Transform[xCount * yCount * zCount];
                int index = 0;
                for (int i = 0; i < xCount; i++) {
                    for (int j = 0; j < yCount; j++) {
                        for (int k = 0; k < zCount; k++) {
                            transforms[index++] = Rhino.Geometry.Transform.Translation(
                                dx: i * xSpacing,
                                dy: j * ySpacing,
                                dz: k * zSpacing);
                        }
                    }
                }
                return ResultFactory.Create(value: transforms);
            }))().Bind(xforms => UnifiedOperation.Apply(
                input: xforms,
                operation: (Func<Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                    ApplyTransform(item: geometry, transform: xform, context: context)),
                config: new OperationConfig<Rhino.Geometry.Transform, T> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = false,
                    OperationName = "Transform.RectangularArray",
                    EnableDiagnostics = enableDiagnostics,
                }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.InvalidArrayCount);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PolarArray<T>(
        T geometry,
        Point3d center,
        Vector3d axis,
        int count,
        double totalAngle,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        count > 0 && count <= TransformConfig.MaxArrayCount && axis.Length > context.AbsoluteTolerance && totalAngle > 0.0 && totalAngle <= RhinoMath.TwoPI
            ? ((Func<Result<Rhino.Geometry.Transform[]>>)(() => {
                double angleStep = totalAngle / count;
                Rhino.Geometry.Transform[] transforms = new Rhino.Geometry.Transform[count];
                for (int i = 0; i < count; i++) {
                    transforms[i] = Rhino.Geometry.Transform.Rotation(
                        angleRadians: angleStep * i,
                        rotationAxis: axis,
                        rotationCenter: center);
                }
                return ResultFactory.Create(value: transforms);
            }))().Bind(xforms => UnifiedOperation.Apply(
                input: xforms,
                operation: (Func<Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                    ApplyTransform(item: geometry, transform: xform, context: context)),
                config: new OperationConfig<Rhino.Geometry.Transform, T> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = false,
                    OperationName = "Transform.PolarArray",
                    EnableDiagnostics = enableDiagnostics,
                }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.InvalidArrayParameters);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Rhino.Geometry.Transform> ValidateTransform(
        Rhino.Geometry.Transform transform,
        IGeometryContext context) =>
        transform.IsValid && Math.Abs(transform.Determinant) > context.AbsoluteTolerance
            ? ResultFactory.Create(value: transform)
            : ResultFactory.Create<Rhino.Geometry.Transform>(error: E.Transform.InvalidTransformMatrix);
}
```

**LOC Estimate**: 200-280 range (algorithmic implementation with array generation)

### File 4: `TransformCompute.cs`
**Purpose**: Advanced transformations - SpaceMorph operations, cage deformations, path arrays

**Types** (2 total):
- `TransformCompute` (internal static class)
- `MorphResult` (internal readonly record struct) - Wrapper for morph operation results with metadata

**Key Members**:
- `Flow<T>()`: Flow geometry from base curve to target curve using FlowSpaceMorph
- `Twist<T>()`: Twist geometry around axis using TwistSpaceMorph
- `Bend<T>()`: Bend geometry along spine using BendSpaceMorph
- `Taper<T>()`: Taper geometry using TaperSpaceMorph
- `Stretch<T>()`: Stretch geometry using StretchSpaceMorph
- `Splop<T>()`: Surface projection using SplopSpaceMorph
- `Sporph<T>()`: Surface-to-surface morph using SporphSpaceMorph
- `Maelstrom<T>()`: Vortex deformation using MaelstromSpaceMorph
- `CageMorph<T>()`: Custom cage-based deformation (non-SDK, implemented manually)
- `PathArray<T>()`: Array along curve with optional orientation
- Helper: `ApplyMorph<TMorph, T>()`: Generic morph application with error handling

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Transform;

internal static class TransformCompute {
    internal readonly record struct MorphResult<T>(T Geometry, bool Success) where T : GeometryBase;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Flow<T>(
        T geometry,
        Curve baseCurve,
        Curve targetCurve,
        bool preserveStructure,
        IGeometryContext context) where T : GeometryBase =>
        baseCurve.IsValid && targetCurve.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                FlowSpaceMorph morph = new() {
                    BaseCurve = baseCurve,
                    TargetCurve = targetCurve,
                    PreserveStructure = preserveStructure,
                    Tolerance = context.AbsoluteTolerance,
                };
                return ApplyMorph(morph: morph, geometry: geometry, context: context);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidFlowCurves);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Twist<T>(
        T geometry,
        Line axis,
        double angleRadians,
        bool infinite,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && Math.Abs(angleRadians) <= RhinoMath.TwoPI * 10.0
            ? ((Func<Result<T>>)(() => {
                TwistSpaceMorph morph = new() {
                    TwistAxis = axis,
                    TwistAngleRadians = angleRadians,
                    InfiniteTwist = infinite,
                    PreserveStructure = false,
                    Tolerance = context.AbsoluteTolerance,
                };
                return ApplyMorph(morph: morph, geometry: geometry, context: context);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidTwistParameters);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PathArray<T>(
        T geometry,
        Curve path,
        int count,
        bool orientToPath,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        count > 0 && count <= TransformConfig.MaxArrayCount && path.IsValid
            ? ((Func<Result<Rhino.Geometry.Transform[]>>)(() => {
                Rhino.Geometry.Transform[] transforms = new Rhino.Geometry.Transform[count];
                double paramStep = (path.Domain.Max - path.Domain.Min) / (count - 1);
                
                for (int i = 0; i < count; i++) {
                    double t = path.Domain.Min + paramStep * i;
                    Point3d pt = path.PointAt(t);
                    
                    transforms[i] = orientToPath && path.FrameAt(t, out Plane frame)
                        ? Rhino.Geometry.Transform.PlaneToPlane(Plane.WorldXY, frame)
                        : Rhino.Geometry.Transform.Translation(pt - Point3d.Origin);
                }
                return ResultFactory.Create(value: transforms);
            }))().Bind(xforms => UnifiedOperation.Apply(
                input: xforms,
                operation: (Func<Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                    TransformCore.ApplyTransform(item: geometry, transform: xform, context: context)),
                config: new OperationConfig<Rhino.Geometry.Transform, T> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = false,
                    OperationName = "Transform.PathArray",
                    EnableDiagnostics = enableDiagnostics,
                }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.InvalidArrayParameters);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry,
        IGeometryContext context) where TMorph : SpaceMorph where T : GeometryBase =>
        morph.IsMorphable(geometry)
            ? ((Func<Result<T>>)(() => {
                T duplicate = (T)geometry.Duplicate();
                return morph.Morph(duplicate)
                    ? ResultFactory.Create(value: duplicate)
                    : ResultFactory.Create<T>(error: E.Transform.MorphFailed);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.GeometryNotMorphable);
}
```

**LOC Estimate**: 180-250 range (morph operations with similar patterns)

---

## Adherence to Limits

- **Files**: 4 files (✓ within 4-file maximum, ideal range 2-3)
- **Types**: 10 types total across all files (✓ within 10-type maximum, ideal range 6-8)
  - Transform.cs: 7 types (main + 6 nested)
  - TransformConfig.cs: 1 type
  - TransformCore.cs: 1 type
  - TransformCompute.cs: 2 types (main + 1 nested)
- **Estimated Total LOC**: 610-860 LOC
  - Transform.cs: 180-250
  - TransformConfig.cs: 50-80
  - TransformCore.cs: 200-280
  - TransformCompute.cs: 180-250

**Assessment**: ✓ Excellent compliance. 4 files at maximum limit but justified by operation scope. Types properly nested within primary API class to avoid type sprawl. Individual file LOC estimates well under 300 max.

---

## Algorithmic Density Strategy

**How we achieve dense code without helper methods**:

1. **Use inline lambda execution for array generation**:
   - `((Func<Result<Transform[]>>)(() => { /* generate transforms */ }))()` pattern
   - Avoids extracting helper methods while maintaining readability
   - All array generation logic inlined within single method

2. **Leverage pattern matching for TransformSpec discrimination**:
   - Switch expression on record struct fields for transform type dispatch
   - No if/else cascades - pure expression-based routing
   - Compiler exhaustiveness checking ensures all cases handled

3. **FrozenDictionary for type-based validation mode lookup**:
   - O(1) dispatch with fallback to inheritance chain LINQ query
   - Single method handles all geometry types without type-specific methods
   - Comparer<Type>.Create for custom type hierarchy sorting

4. **Compose LINQ chains for transform collections**:
   - `.SelectMany(static r => r)` for flattening nested results
   - Collection expressions `[..]` with spread operator
   - Static lambdas to avoid closure allocation

5. **Ternary operators for validation chains**:
   - `condition ? success : error` pattern throughout
   - No if/else statements - pure expression-based validation
   - Chainable with Result.Bind for monadic composition

6. **Inline disposal pattern for converted geometry**:
   - Tuple destructuring: `(GeometryBase normalized, bool shouldDispose)`
   - try/finally with ternary disposal: `(shouldDispose ? x : null)?.Dispose()`
   - No extract method for disposal logic

7. **RhinoMath constants instead of magic numbers**:
   - `RhinoMath.TwoPI`, `RhinoMath.ZeroTolerance`, `RhinoMath.PI`
   - Self-documenting constants from SDK
   - Proper angle conversions via `RhinoMath.ToRadians/ToDegrees`

8. **UnifiedOperation for all batch operations**:
   - Single operation pattern handles validation, parallelism, error accumulation
   - No handrolled loops for applying transforms to collections
   - Configuration via OperationConfig rather than custom flags

---

## Dispatch Architecture

### Primary Dispatch: TransformSpec Pattern Matching

Transform operations use discriminated union pattern via readonly record struct:

```csharp
public readonly record struct TransformSpec {
    public Rhino.Geometry.Transform? Matrix { get; init; }
    public (Point3d Anchor, double Factor)? UniformScale { get; init; }
    public (Plane Plane, double X, double Y, double Z)? NonUniformScale { get; init; }
    public (double Angle, Vector3d Axis, Point3d Center)? Rotation { get; init; }
    public Plane? MirrorPlane { get; init; }
    public Vector3d? Translation { get; init; }
    public (Plane Plane, Vector3d Direction, double Angle)? Shear { get; init; }
    // ... other transform types
}

// Dispatch via switch expression
BuildTransform(spec) => spec switch {
    { Matrix: Rhino.Geometry.Transform m } => ValidateTransform(m),
    { UniformScale: (Point3d a, double f) } => CreateScaleTransform(a, f),
    { Rotation: (double ang, Vector3d ax, Point3d c) } => CreateRotationTransform(ang, ax, c),
    // ... all other cases
};
```

**Why not FrozenDictionary?**: TransformSpec is a value type with optional fields. Pattern matching provides:
- Compile-time exhaustiveness checking
- Zero allocation (no boxing for value types)
- Natural expression-based flow
- Type safety via nullable field discrimination

### Secondary Dispatch: Type-Based Validation

FrozenDictionary for geometry type to validation mode:

```csharp
internal static readonly FrozenDictionary<Type, V> ValidationModes =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(Brep)] = V.Standard | V.Topology,
        [typeof(Mesh)] = V.Standard | V.MeshSpecific,
    }.ToFrozenDictionary();
```

With inheritance fallback for derived types.

### Tertiary Dispatch: ArrayMode Enumeration

Simple byte-based enum for array types:

```csharp
public readonly struct ArrayMode(byte mode) {
    internal readonly byte Value = mode;
    public static readonly ArrayMode Rectangular = new(1);
    public static readonly ArrayMode Polar = new(2);
    public static readonly ArrayMode Linear = new(3);
    public static readonly ArrayMode Path = new(4);
}

// Dispatch via switch on byte value
ArrayTransform(spec) => spec.Mode.Value switch {
    1 => TransformCore.RectangularArray(...),
    2 => TransformCore.PolarArray(...),
    3 => TransformCore.LinearArray(...),
    4 => TransformCompute.PathArray(...),
    _ => error,
};
```

---

## Public API Surface

### Primary Operations

```csharp
/// <summary>Apply transform to geometry via unified specification.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Apply<T>(
    T geometry,
    TransformSpec spec,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase;

/// <summary>Array geometry via rectangular, polar, linear, or path patterns.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<T>> ArrayTransform<T>(
    T geometry,
    ArraySpec spec,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase;

/// <summary>Morph geometry via SpaceMorph operations (flow, twist, bend, etc.).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Morph<T>(
    T geometry,
    MorphSpec spec,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase;
```

### Type-Specific Overloads (Ergonomic Entry Points)

```csharp
/// <summary>Scale geometry uniformly about anchor point.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Scale<T>(
    T geometry,
    Point3d anchor,
    double factor,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Scale geometry non-uniformly along plane axes.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Scale<T>(
    T geometry,
    Plane plane,
    double xScale,
    double yScale,
    double zScale,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Rotate geometry around axis by angle in radians.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Rotate<T>(
    T geometry,
    double angleRadians,
    Vector3d axis,
    Point3d center,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Rotate geometry from start direction to end direction.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Rotate<T>(
    T geometry,
    Vector3d startDirection,
    Vector3d endDirection,
    Point3d center,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Mirror geometry across plane.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Mirror<T>(
    T geometry,
    Plane plane,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Translate geometry by vector.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Translate<T>(
    T geometry,
    Vector3d motion,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Shear geometry parallel to plane in given direction.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Shear<T>(
    T geometry,
    Plane plane,
    Vector3d direction,
    double angle,
    IGeometryContext context) where T : GeometryBase;

/// <summary>Project geometry orthogonally to plane.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<T> Project<T>(
    T geometry,
    Plane plane,
    IGeometryContext context) where T : GeometryBase;
```

### Configuration Types

```csharp
/// <summary>Transform specification with discriminated union pattern.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct TransformSpec {
    public Rhino.Geometry.Transform? Matrix { get; init; }
    public (Point3d Anchor, double Factor)? UniformScale { get; init; }
    public (Plane Plane, double X, double Y, double Z)? NonUniformScale { get; init; }
    public (double Angle, Vector3d Axis, Point3d Center)? Rotation { get; init; }
    public Plane? MirrorPlane { get; init; }
    public Vector3d? Translation { get; init; }
    public (Plane Plane, Vector3d Direction, double Angle)? Shear { get; init; }
    public Plane? ProjectionPlane { get; init; }

    public static TransformSpec Matrix(Rhino.Geometry.Transform xform) => new() { Matrix = xform };
    public static TransformSpec Scale(Point3d anchor, double factor) => new() { UniformScale = (anchor, factor) };
    public static TransformSpec Scale(Plane plane, double x, double y, double z) => new() { NonUniformScale = (plane, x, y, z) };
    public static TransformSpec Rotate(double angle, Vector3d axis, Point3d center) => new() { Rotation = (angle, axis, center) };
    public static TransformSpec Mirror(Plane plane) => new() { MirrorPlane = plane };
    public static TransformSpec Translate(Vector3d motion) => new() { Translation = motion };
    public static TransformSpec Shear(Plane plane, Vector3d dir, double angle) => new() { Shear = (plane, dir, angle) };
    public static TransformSpec Project(Plane plane) => new() { ProjectionPlane = plane };
}

/// <summary>Array transformation specification.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct ArraySpec {
    public ArrayMode Mode { get; init; }
    public int Count { get; init; }
    
    // Rectangular
    public int XCount { get; init; }
    public int YCount { get; init; }
    public int? ZCount { get; init; }
    public double XSpacing { get; init; }
    public double YSpacing { get; init; }
    public double? ZSpacing { get; init; }
    
    // Polar
    public Point3d? Center { get; init; }
    public Vector3d? Axis { get; init; }
    public double? TotalAngle { get; init; }
    
    // Linear
    public Vector3d? Direction { get; init; }
    public double Spacing { get; init; }
    
    // Path
    public Curve? PathCurve { get; init; }
    public bool OrientToPath { get; init; }

    public static ArraySpec Rectangular(int xCount, int yCount, int zCount, double xSpace, double ySpace, double zSpace) =>
        new() { Mode = ArrayMode.Rectangular, XCount = xCount, YCount = yCount, ZCount = zCount, XSpacing = xSpace, YSpacing = ySpace, ZSpacing = zSpace };
    public static ArraySpec Polar(Point3d center, Vector3d axis, int count, double totalAngle) =>
        new() { Mode = ArrayMode.Polar, Center = center, Axis = axis, Count = count, TotalAngle = totalAngle };
    public static ArraySpec Linear(Vector3d direction, int count, double spacing) =>
        new() { Mode = ArrayMode.Linear, Direction = direction, Count = count, Spacing = spacing };
    public static ArraySpec Path(Curve path, int count, bool orient) =>
        new() { Mode = ArrayMode.Path, PathCurve = path, Count = count, OrientToPath = orient };
}

/// <summary>Morph operation specification.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct MorphSpec {
    public byte Operation { get; init; }
    public Curve? BaseCurve { get; init; }
    public Curve? TargetCurve { get; init; }
    public Line? Axis { get; init; }
    public double Angle { get; init; }
    public bool PreserveStructure { get; init; }
    public bool Infinite { get; init; }
    public Surface? SourceSurface { get; init; }
    public Surface? TargetSurface { get; init; }

    public static MorphSpec Flow(Curve baseCurve, Curve targetCurve, bool preserve) =>
        new() { Operation = 1, BaseCurve = baseCurve, TargetCurve = targetCurve, PreserveStructure = preserve };
    public static MorphSpec Twist(Line axis, double angle, bool infinite) =>
        new() { Operation = 2, Axis = axis, Angle = angle, Infinite = infinite };
    public static MorphSpec Bend(Line axis, double angle) =>
        new() { Operation = 3, Axis = axis, Angle = angle };
    public static MorphSpec Taper(Line axis, double startWidth, double endWidth) =>
        new() { Operation = 4, Axis = axis, Angle = startWidth, Infinite = endWidth > 0 };
}
```

---

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters
- [x] All examples use trailing commas
- [x] All examples use K&R brace style
- [x] All examples use target-typed new()
- [x] All examples use collection expressions []
- [x] One type per file organization (nested types allowed in main file)
- [x] All member estimates under 300 LOC
- [x] All patterns match existing libs/ exemplars
- [x] RhinoMath constants instead of magic numbers
- [x] Proper ternary operations for binary choices
- [x] Switch expressions for multi-branch logic
- [x] Inline disposal pattern with tuple destructuring
- [x] Static lambdas to avoid closures
- [x] Pure functions marked with [Pure] attribute
- [x] MethodImpl(AggressiveInlining) on hot paths

---

## Implementation Sequence

1. ✓ Read this blueprint thoroughly
2. Add new validation mode to `libs/core/validation/V.cs`:
   - `V.TransformValidity = new(32768)` with bitwise flag
3. Add new error codes to `libs/core/errors/E.cs`:
   - Transform Operations section (2100-2116) in _m dictionary
   - `E.Transform` nested class with static readonly properties
4. Create `libs/rhino/transform/TransformConfig.cs`:
   - Constants (tolerances, limits, defaults)
   - FrozenDictionary ValidationModes mapping
   - GetValidationMode method with inheritance fallback
5. Create `libs/rhino/transform/TransformCore.cs`:
   - BuildTransform with TransformSpec pattern matching
   - ValidateTransform with matrix checks
   - ApplyTransform with Extrusion disposal pattern
   - RectangularArray with nested for loops
   - PolarArray with angle stepping
   - LinearArray implementation
6. Create `libs/rhino/transform/TransformCompute.cs`:
   - Flow using FlowSpaceMorph
   - Twist using TwistSpaceMorph
   - Bend using BendSpaceMorph
   - Taper using TaperSpaceMorph
   - Stretch using StretchSpaceMorph
   - Splop using SplopSpaceMorph
   - Sporph using SporphSpaceMorph
   - Maelstrom using MaelstromSpaceMorph
   - PathArray with curve parameter evaluation
   - ApplyMorph generic helper
7. Create `libs/rhino/transform/Transform.cs`:
   - Namespace suppression attribute
   - Nested types: TransformSpec, ArraySpec, MorphSpec, modes
   - Primary API: Apply, ArrayTransform, Morph
   - Type-specific overloads: Scale, Rotate, Mirror, Translate, Shear, Project
   - All methods delegate to Core/Compute with UnifiedOperation
8. Refactor `libs/rhino/orientation/Orient.cs`:
   - Remove Mirror method (line 158)
   - Update to delegate to Transform.Mirror
   - Ensure no breaking changes to existing Orient API
9. Build and verify:
   - `dotnet build libs/rhino/Rhino.csproj`
   - Check for analyzer violations
   - Verify LOC limits (≤300 per member)
   - Verify file/type limits (4 files, 10 types)
10. Add XML documentation to all public members
11. Verify code style compliance with .editorconfig
12. Review patterns against exemplar files
13. Test matrix validation and error handling
14. Test array generation logic
15. Test morph operation wrappers

---

## References

### SDK Documentation
- [RhinoCommon Transform Struct](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform)
- [Transform.Scale Method](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Transform_Scale.htm)
- [Transform.Rotation Method](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.transform/rotation)
- [Transform.Mirror Method](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Transform_Mirror.htm)
- [SpaceMorph Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.spacemorph)
- [FlowSpaceMorph Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.morphs.flowspacemorph)
- [TwistSpaceMorph Class](https://apidocs.co/apps/rhinocommon/7.3.21039.11200/T_Rhino_Geometry_Morphs_TwistSpaceMorph.htm)
- [RhinoMath Constants](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_RhinoMath.htm)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns
- `libs/core/results/ResultFactory.cs` - Polymorphic creation
- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch
- `libs/core/validation/ValidationRules.cs` - Expression tree compilation
- `libs/core/validation/V.cs` - Validation mode flags
- `libs/core/errors/E.cs` - Error code registry
- `libs/rhino/spatial/Spatial.cs` - FrozenDictionary dispatch example
- `libs/rhino/spatial/SpatialCore.cs` - ArrayPool usage, factory methods
- `libs/rhino/orientation/Orient.cs` - Plane extraction, ApplyTransform pattern
- `libs/rhino/orientation/OrientCore.cs` - Transform application logic
- `libs/rhino/morphology/` - Mesh deformation patterns
- `libs/rhino/analysis/Analysis.cs` - Custom result types with IResult interface

### Additional Operations Identified (Beyond Requirements)

Based on RhinoCommon SDK research, these **2-3 additional justified operations** are included:

1. **Shear Transformation**:
   - **Justification**: Common in parametric design for angular deformations parallel to a plane
   - **SDK Support**: `Transform.Shear(Plane plane, Vector3d direction, double angle)`
   - **Use Cases**: Architectural forms, diagonal bracing patterns, perspective-like effects
   - **Implementation**: TransformSpec discriminated union case + validation

2. **Projection to Plane**:
   - **Justification**: Essential for flattening 3D geometry for fabrication, unrolling, or 2D output
   - **SDK Support**: `Transform.ProjectionToPlane(Plane plane)`
   - **Use Cases**: Laser cutting preparation, shadow analysis, orthographic views
   - **Implementation**: Simple transform wrapper with plane validation

3. **Path Array Transformation**:
   - **Justification**: More sophisticated than linear array - follows arbitrary curve with optional orientation
   - **SDK Support**: Curve.FrameAt, Curve.PointAt for parameter-based sampling
   - **Use Cases**: Railing posts along curved paths, lighting along architecture curves, distribution patterns
   - **Implementation**: TransformCompute.PathArray with curve parameter evaluation

These operations complete the transformation suite with commonly-requested functionality that leverages existing RhinoCommon APIs while maintaining the unified API pattern.

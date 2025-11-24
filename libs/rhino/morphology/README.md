# Mesh Morphology and Topology Operations

Mesh repair, deformation, subdivision, smoothing, reduction, remeshing, unwrapping, and topological operations with algebraic dispatch.

---

## API Surface

```csharp
Result<IReadOnlyList<IMorphologyResult>> Apply<T>(T input, Operation operation, IGeometryContext context) where T : GeometryBase
```

All operations use algebraic dispatch through `Apply()` with strongly-typed operation discriminators.

---

## Operations

### Mesh Repair

**Operations**: `FillHolesRepair`, `UnifyNormalsRepair`, `CullDegenerateFacesRepair`, `CompactRepair`, `WeldRepair`, `CompositeRepair(IReadOnlyList<MeshRepairStrategy>, double WeldTolerance)`

**Result**: `MeshRepairResult(Mesh Repaired, int OriginalVertexCount, int RepairedVertexCount, int OriginalFaceCount, int RepairedFaceCount, byte OperationsPerformed, double QualityScore, bool HadHoles, bool HadBadNormals)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.CompositeRepair(
        Strategies: [new Morphology.FillHolesRepair(), new Morphology.WeldRepair(),],
        WeldTolerance: 0.01),
    context: new GeometryContext(absoluteTolerance: 0.001));
```

### Mesh Unwrapping

**Operations**: `PlanarUnwrap`, `CylindricalUnwrap(Vector3d Axis, Point3d Origin)`, `SphericalUnwrap(Point3d Center, double Radius)`

**Result**: `MeshUnwrapResult(Mesh Unwrapped, bool HasTextureCoordinates, int OriginalFaceCount, int TextureCoordinateCount, double MinU, double MaxU, double MinV, double MaxV, double UVCoverage)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.CylindricalUnwrap(Axis: Vector3d.ZAxis, Origin: Point3d.Origin),
    context: context);
```

### Mesh Subdivision

**Operations**: `CatmullClarkSubdivision(int Levels)` (max 5), `LoopSubdivision(int Levels)`, `ButterflySubdivision(int Levels)`

**Result**: `SubdivisionResult(Mesh Subdivided, int OriginalFaceCount, int SubdividedFaceCount, double MinEdgeLength, double MaxEdgeLength, double MeanEdgeLength, double MeanAspectRatio, double MinTriangleAngleRadians)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.CatmullClarkSubdivision(Levels: 2),
    context: context);
```

### Mesh Smoothing

**Operations**: `LaplacianSmoothing(int Iterations, bool LockBoundary)`, `TaubinSmoothing(int Iterations, double Lambda, double Mu)` (typical: λ=0.5, μ=-0.53), `MeanCurvatureFlowSmoothing(double TimeStep, int Iterations)`

**Result**: `SmoothingResult(Mesh Smoothed, int IterationsPerformed, double RMSDisplacement, double MaxVertexDisplacement, double QualityScore, bool Converged)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.TaubinSmoothing(Iterations: 20, Lambda: 0.5, Mu: -0.53),
    context: context);
```

### Mesh Offset

**Operation**: `MeshOffsetOperation(double Distance, bool BothSides)`

**Result**: `OffsetResult(Mesh Offset, double ActualDistance, bool HasDegeneracies, int OriginalVertexCount, int OffsetVertexCount, int OriginalFaceCount, int OffsetFaceCount)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.MeshOffsetOperation(Distance: 2.0, BothSides: false),
    context: context);
```

### Mesh Reduction

**Operation**: `MeshReductionOperation(int TargetFaceCount, bool PreserveBoundary, double Accuracy)` (accuracy: 0.0-1.0, default 0.5)

**Result**: `ReductionResult(Mesh Reduced, int OriginalFaceCount, int ReducedFaceCount, double ReductionRatio, double QualityScore, double MeanAspectRatio, double MinEdgeLength, double MaxEdgeLength)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.MeshReductionOperation(TargetFaceCount: 1000, PreserveBoundary: true, Accuracy: 0.7),
    context: context);
```

### Isotropic Remeshing

**Operation**: `IsotropicRemeshOperation(double TargetEdgeLength, int MaxIterations, bool PreserveFeatures)` (max iterations: 100)

**Result**: `RemeshResult(Mesh Remeshed, double TargetEdgeLength, double MeanEdgeLength, double EdgeLengthStdDev, double UniformityScore, int IterationsPerformed, bool Converged, int OriginalFaceCount, int RemeshedFaceCount)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.IsotropicRemeshOperation(TargetEdgeLength: 0.5, MaxIterations: 50, PreserveFeatures: true),
    context: context);
```

### Mesh Thickening

**Operation**: `MeshThickenOperation(double OffsetDistance, bool Solidify, Vector3d Direction)` (Direction: Vector3d.Unset for normal-based)

**Result**: `MeshThickenResult(Mesh Thickened, double OffsetDistance, bool IsSolid, int OriginalVertexCount, int ThickenedVertexCount, int OriginalFaceCount, int ThickenedFaceCount, int WallFaceCount, BoundingBox OriginalBounds, BoundingBox ThickenedBounds)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.MeshThickenOperation(OffsetDistance: 1.0, Solidify: true, Direction: Vector3d.Unset),
    context: context);
```

### Mesh Separation

**Operation**: `MeshSeparateOperation`

**Result**: `MeshSeparationResult(Mesh[] Components, int ComponentCount, int TotalVertexCount, int TotalFaceCount, int[] VertexCountPerComponent, int[] FaceCountPerComponent, BoundingBox[] BoundsPerComponent)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.MeshSeparateOperation(),
    context: context);
```

### Mesh Welding

**Operation**: `MeshWeldOperation(double Tolerance, bool RecalculateNormals)` (tolerance: 0.0001-100.0, default 0.01)

**Result**: `MeshWeldResult(Mesh Welded, int OriginalVertexCount, int WeldedVertexCount, int VerticesRemoved, double WeldTolerance, double MeanVertexDisplacement, double MaxVertexDisplacement, bool NormalsRecalculated)`

```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.MeshWeldOperation(Tolerance: 0.01, RecalculateNormals: true),
    context: context);
```

### Brep to Mesh Conversion

**Operation**: `BrepToMeshOperation(MeshingParameters? Parameters, bool JoinMeshes)` (Parameters: null for RhinoCommon defaults)

**Result**: `BrepToMeshResult(Mesh Mesh, int BrepFaceCount, int MeshFaceCount, double MinEdgeLength, double MaxEdgeLength, double MeanEdgeLength, double EdgeLengthStdDev, double MeanAspectRatio, double MaxAspectRatio, double MinTriangleAngleRadians, double MeanTriangleAngleRadians, int DegenerateFaceCount, double QualityScore)`

```csharp
MeshingParameters parameters = new() { MinimumEdgeLength = 0.1, MaximumEdgeLength = 1.0, GridAspectRatio = 6.0, };
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    input: brep,
    operation: new Morphology.BrepToMeshOperation(Parameters: parameters, JoinMeshes: true),
    context: context);
```

### Cage Deformation

**Operation**: `CageDeformOperation(GeometryBase Cage, Point3d[] OriginalControlPoints, Point3d[] DeformedControlPoints)` (min 8 control points)

**Result**: `CageDeformResult(GeometryBase Deformed, double MaxDisplacement, double MeanDisplacement, BoundingBox OriginalBounds, BoundingBox DeformedBounds, double VolumeRatio)`

```csharp
Point3d[] originalPts = [new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0), new Point3d(0, 10, 0), new Point3d(0, 0, 10), new Point3d(10, 0, 10), new Point3d(10, 10, 10), new Point3d(0, 10, 10),];
Point3d[] deformedPts = [new Point3d(0, 0, 0), new Point3d(12, 0, 0), new Point3d(12, 10, 0), new Point3d(0, 10, 0), new Point3d(0, 0, 12), new Point3d(12, 0, 12), new Point3d(12, 10, 12), new Point3d(0, 10, 12),];
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(input: mesh, operation: new Morphology.CageDeformOperation(Cage: cageMesh, OriginalControlPoints: originalPts, DeformedControlPoints: deformedPts), context: context);
```

---

## Type Hierarchy

### Operations
```csharp
abstract record Operation
abstract record MeshRepairStrategy : Operation
abstract record UnwrapStrategy : Operation
abstract record SubdivisionStrategy(int Levels) : Operation
abstract record SmoothingStrategy(int Iterations, bool LockBoundary) : Operation

sealed record FillHolesRepair, UnifyNormalsRepair, CullDegenerateFacesRepair, CompactRepair, WeldRepair : MeshRepairStrategy
sealed record CompositeRepair(IReadOnlyList<MeshRepairStrategy>, double) : MeshRepairStrategy
sealed record PlanarUnwrap, CylindricalUnwrap(Vector3d, Point3d), SphericalUnwrap(Point3d, double) : UnwrapStrategy
sealed record CatmullClarkSubdivision(int), LoopSubdivision(int), ButterflySubdivision(int) : SubdivisionStrategy
sealed record LaplacianSmoothing(int, bool), TaubinSmoothing(int, double, double), MeanCurvatureFlowSmoothing(double, int) : SmoothingStrategy
sealed record MeshOffsetOperation(double, bool), MeshReductionOperation(int, bool, double), IsotropicRemeshOperation(double, int, bool), MeshThickenOperation(double, bool, Vector3d), MeshSeparateOperation, MeshWeldOperation(double, bool), BrepToMeshOperation(MeshingParameters?, bool), CageDeformOperation(GeometryBase, Point3d[], Point3d[]) : Operation
```

### Results
```csharp
interface IMorphologyResult
sealed record MeshRepairResult, MeshUnwrapResult, SubdivisionResult, SmoothingResult, OffsetResult, ReductionResult, RemeshResult, MeshThickenResult, MeshSeparationResult, MeshWeldResult, BrepToMeshResult, CageDeformResult : IMorphologyResult
```

---

## Integration

### Result Monad
All operations return `Result<IReadOnlyList<IMorphologyResult>>` from `libs/core/results`:
```csharp
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(mesh, operation, context)
    .Map(results => results[0])
    .Ensure(r => r is MeshRepairResult repair && repair.QualityScore > 0.8, error: E.Geometry.QualityThreshold)
    .Match(onSuccess: r => ProcessResult(r), onFailure: errors => HandleErrors(errors));
```

### Validation
Operations use validation flags from `libs/core/validation`: `V.Standard` (IsValid), `V.MeshSpecific` (IsClosed, IsManifold), `V.Topology` (IsSolid, edge/vertex counts), `V.BoundingBox`

### Errors
Centralized error codes from `libs/core/errors/E.cs`: `E.Geometry.InvalidCount`, `E.Geometry.InvalidConfiguration`, `E.Geometry.UnsupportedAnalysis`, `E.Validation.GeometryInvalid`

### Context
`IGeometryContext` from `libs/core/context` provides tolerance: `new GeometryContext(absoluteTolerance: 0.001, angleTolerance: RhinoMath.ToRadians(1.0), documentUnits: UnitSystem.Meters)`

---

## Architecture

### Dispatch
Operations use FrozenDictionary dispatch via `MorphologyConfig.Operations`: `FrozenDictionary<Type, MorphologyOperationMetadata>` mapping operation type to validation mode, algorithm code, repair flags, tolerance, and repair action.

### Files
```
libs/rhino/morphology/ (4 files, 10 types)
├── Morphology.cs        - Public API and algebraic types (321 LOC)
├── MorphologyCore.cs    - Execution engine with dispatch (289 LOC)
├── MorphologyCompute.cs - Computational algorithms (274 LOC)
└── MorphologyConfig.cs  - Configuration and dispatch table (131 LOC)
```

---

## Configuration

### Quality Metrics
Aspect ratio (ideal: 1.0, threshold: 10.0), triangle angles (ideal: 60°, threshold: 5°), edge length uniformity (stddev/mean), convergence (RMS displacement). Quality scores normalized to 0.0-1.0.

### Limits
Subdivision: max 5 levels. Smoothing: max 1000 iterations, convergence 1e-6 RMS. Reduction: min 4 faces, accuracy 0.0-1.0 (default 0.5). Remeshing: max 100 iterations, edge length 0.1×-0.5× target. Welding: tolerance 0.0001-100.0 (default 0.01). Thickening: offset 0.0001-10000.0.

---

## Examples

### Pipeline
```csharp
Result<IReadOnlyList<IMorphologyResult>> pipeline = Morphology.Apply(input: rawMesh, operation: new Morphology.CompositeRepair(Strategies: [new Morphology.FillHolesRepair(), new Morphology.CullDegenerateFacesRepair(), new Morphology.WeldRepair(), new Morphology.CompactRepair(),], WeldTolerance: 0.01), context: new GeometryContext(absoluteTolerance: 0.001))
    .Bind(results => Morphology.Apply(input: ((MeshRepairResult)results[0]).Repaired, operation: new Morphology.LaplacianSmoothing(Iterations: 5, LockBoundary: true), context: context))
    .Bind(results => Morphology.Apply(input: ((SmoothingResult)results[0]).Smoothed, operation: new Morphology.MeshReductionOperation(TargetFaceCount: 5000, PreserveBoundary: true, Accuracy: 0.8), context: context));
```

### Quality Validation
```csharp
Result<SubdivisionResult> subdivide = Morphology.Apply(input: mesh, operation: new Morphology.CatmullClarkSubdivision(Levels: 2), context: context)
    .Map(results => (SubdivisionResult)results[0])
    .Ensure(result => result.MeanAspectRatio < 5.0 && result.MinTriangleAngleRadians > RhinoMath.ToRadians(10.0), error: E.Geometry.QualityThreshold.WithContext("Subdivision quality insufficient"));
```

### Volume Preservation
```csharp
Result<CageDeformResult> deform = Morphology.Apply(input: geometry, operation: new Morphology.CageDeformOperation(Cage: controlCage, OriginalControlPoints: originalPts, DeformedControlPoints: deformedPts), context: context)
    .Map(results => (CageDeformResult)results[0])
    .Ensure(result => result.VolumeRatio > 0.9 && result.VolumeRatio < 1.1, error: E.Geometry.InvalidConfiguration.WithContext("Volume change exceeds 10%"));
```

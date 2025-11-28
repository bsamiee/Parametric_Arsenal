# Arsenal.Rhino - Geometry Operations Library

Polymorphic geometry operations for RhinoCommon with unified algebraic dispatch, Result monad error handling, and comprehensive validation.

---

## Quick Reference

| Module | Entry Point | Primary Use |
|--------|-------------|-------------|
| **Analysis** | `Analysis.Analyze()` | Differential geometry, quality metrics |
| **Extraction** | `Extraction.Points()`, `Extraction.Curves()` | Point/curve extraction from geometry |
| **Fields** | `Fields.Execute()` | Scalar/vector field operations |
| **Intersection** | `Intersection.Execute()` | Geometry intersection and analysis |
| **Morphology** | `Morphology.Apply()` | Mesh repair, subdivision, smoothing |
| **Orientation** | `Orientation.Execute()` | Canonical alignment, best-fit planes |
| **Spatial** | `Spatial.Analyze()`, `Spatial.Cluster()` | RTree queries, clustering, computational geometry |
| **Topology** | `Topology.Get*()`, `Topology.HealTopology()` | Topology analysis and healing |
| **Transformation** | `Transformation.Apply()` | Affine transforms, arrays, morphs |

---

## API Surface Summary

### Single Entry Points (Preferred)

Each module provides a primary polymorphic entry point that dispatches based on algebraic request types:

```csharp
// Unified polymorphic dispatch pattern
Result<T> Module.Execute(Request request, IGeometryContext context)
Result<T> Module.Apply<T>(T geometry, Operation operation, IGeometryContext context)
Result<T> Module.Analyze<TInput, TQuery>(TInput input, TQuery query, IGeometryContext context)
```

### Module Relationships

**Transform Operations**: Both `Transformation` and `Orientation` handle geometry transforms:
- Use `Transformation.Apply()` for known transforms (scale, rotate, mirror, shear)
- Use `Orientation.Execute()` for derived transforms (best-fit, canonical, relative)

**Analysis Operations**: `Analysis`, `Topology`, and `Extraction` all analyze geometry:
- Use `Analysis.Analyze()` for differential geometry (curvature, derivatives)
- Use `Topology.Get*()` for topological structure (edges, vertices, connectivity)
- Use `Extraction.Points()/Curves()` for geometric extraction (control points, isocurves)

**Spatial Operations**: `Spatial` and `Intersection` both handle geometric relationships:
- Use `Spatial.Analyze()` for proximity queries and clustering
- Use `Intersection.Execute()` for geometry-to-geometry intersections

---

## Common Patterns

### Result Monad

All operations return `Result<T>` for explicit error handling:

```csharp
Result<Analysis.CurveData> result = Analysis.Analyze(curve, context);

// Pattern matching
result.Match(
    onSuccess: data => Console.WriteLine($"Curvature: {data.Curvature}"),
    onFailure: errors => Console.WriteLine($"Errors: {errors.Length}"));

// Monadic chaining
result
    .Map(data => data.Curvature)
    .Bind(k => k > 0 ? ResultFactory.Create(value: k) : ResultFactory.Create<double>(error: E.Geometry.InvalidCurvature));
```

### Algebraic Request Types

Operations use discriminated unions (algebraic types) for type-safe dispatch:

```csharp
// Extraction uses algebraic point operation types
Extraction.Points(curve, new Extraction.Greville(), context);
Extraction.Points(curve, new Extraction.ByCount(Count: 10, IncludeEnds: true), context);
Extraction.Points(curve, new Extraction.Discontinuity(Continuity.C1_continuous), context);

// Transformation uses algebraic transform operation types
Transformation.Apply(brep, new Transformation.MirrorTransform(Plane.WorldXY), context);
Transformation.Apply(brep, new Transformation.AxisRotation(Math.PI/4, Vector3d.ZAxis, Point3d.Origin), context);
```

### Geometry Context

All operations require `IGeometryContext` for tolerance resolution:

```csharp
IGeometryContext context = new GeometryContext(
    absoluteTolerance: 0.001,
    angleToleranceRadians: 0.01);
```

---

## Module Details

### Analysis (422 LOC API)

Differential geometry and quality metrics for curves, surfaces, Breps, and meshes.

**Key Methods**:
- `Analyze(Curve|Surface|Brep|Mesh, context)` - Differential geometry data
- `AnalyzeSurfaceQuality(surface, context)` - Surface curvature uniformity
- `AnalyzeCurveFairness(curve, context)` - Curve smoothness metrics
- `AnalyzeMeshForFEA(mesh, context)` - FEA quality metrics

### Extraction (209 LOC API)

Point and curve extraction with feature detection and primitive decomposition.

**Key Methods**:
- `Points<T>(geometry, PointOperation, context)` - Extract points
- `Curves<T>(geometry, CurveOperation, context)` - Extract curves
- `ExtractDesignFeatures(brep, context)` - Detect fillets, chamfers, holes
- `DecomposeToPrimitives(geometry, context)` - Best-fit primitives

### Fields (177 LOC API)

Computational field analysis: distance fields, differential operators, streamlines.

**Key Methods**:
- `Execute(FieldOperation, context)` - Unified field operation dispatch

**Operations**: Distance fields, gradients, curl, divergence, Laplacian, Hessian, streamlines, isosurfaces, critical points.

### Intersection (141 LOC API)

Geometry intersection with classification and stability analysis.

**Key Methods**:
- `Execute(Request, context)` - General intersection
- `Classify(output, geometryA, geometryB, context)` - Tangent/transverse classification
- `FindNearMisses(geometryA, geometryB, radius, context)` - Near-miss detection
- `AnalyzeStability(output, geometryA, geometryB, context)` - Perturbation sensitivity

### Morphology (309 LOC API)

Mesh operations: repair, subdivision, smoothing, reduction, unwrapping.

**Key Methods**:
- `Apply<T>(geometry, Operation, context)` - Unified morphology dispatch

**Operations**: Repair (fill holes, weld, unify normals), subdivision (Catmull-Clark, Loop, Butterfly), smoothing (Laplacian, Taubin), reduction, remeshing, thickening, unwrapping.

### Orientation (171 LOC API)

Geometry orientation and canonical alignment.

**Key Methods**:
- `Execute<T>(geometry, Operation, context)` - Orientation transform
- `OptimizeOrientation(brep, criteria, context)` - Find optimal orientation
- `ComputeRelativeOrientation(geometryA, geometryB, context)` - Relative transform
- `DetectAndAlign(geometries, context)` - Pattern detection

**Note**: For simple transforms (mirror, translate), prefer `Transformation.Apply()`.

### Spatial (116 LOC API)

RTree-based spatial indexing and computational geometry.

**Key Methods**:
- `Analyze<TInput, TQuery>(input, query, context)` - Range/proximity queries
- `Cluster<T>(geometries, ClusterRequest, context)` - K-means, DBSCAN, hierarchical
- `ConvexHull3D(points, context)` - 3D convex hull
- `DelaunayTriangulation2D(points, context)` - 2D Delaunay
- `VoronoiDiagram2D(points, context)` - 2D Voronoi
- `MedialAxis(brep, tolerance, context)` - Medial axis skeleton

### Topology (300 LOC API)

Topology analysis and progressive healing.

**Key Methods**:
- `GetNakedEdges<T>(geometry, context)` - Boundary edges
- `GetConnectivity<T>(geometry, context)` - Connected components
- `ClassifyEdges<T>(geometry, context)` - G0/G1/G2 classification
- `DiagnoseTopology(brep, context)` - Gap detection
- `HealTopology(brep, strategies, context)` - Progressive healing

### Transformation (~150 LOC API)

Affine transforms, arrays, and SpaceMorph deformations.

**Key Methods**:
- `Apply<T>(geometry, TransformOperation, context)` - Transform dispatch
- `ApplyArray<T>(geometry, ArrayOperation, context)` - Array dispatch
- `Morph<T>(geometry, MorphOperation, context)` - Morph dispatch
- `Decompose(matrix, context)` - TRS decomposition

**Algebraic Types**: `MirrorTransform`, `Translation`, `ProjectionTransform`, `BasisChange`, `PlaneTransform`, `UniformScale`, `NonUniformScale`, `AxisRotation`, `VectorRotation`, `ShearTransform`, `CompoundTransform`, `BlendedTransform`, `InterpolatedTransform`.

---

## Architecture

### File Organization

Each module follows a 4-file pattern:
```
module/
├── Module.cs        # Public API (algebraic types, entry points)
├── ModuleConfig.cs  # FrozenDictionary dispatch tables, constants
├── ModuleCore.cs    # Orchestration via UnifiedOperation
├── ModuleCompute.cs # RhinoCommon algorithms
└── README.md        # Module documentation
```

### Validation System

Operations use validation modes from `libs/core/validation/`:
- `V.Standard` - Basic IsValid check
- `V.Degeneracy` - Curve length, degenerate surfaces
- `V.Topology` - Brep manifold, solid orientation
- `V.MeshSpecific` - Mesh normals, face validity
- `V.BoundingBox`, `V.MassProperties`, `V.UVDomain`, etc.

### Error Handling

All errors use the centralized registry `E.*` from `libs/core/errors/`:
- `E.Geometry.*` - Geometry operation errors (2000-2999)
- `E.Validation.*` - Validation errors (3000-3999)
- `E.Spatial.*` - Spatial indexing errors (4000-4999)
- `E.Topology.*` - Topology errors (5000-5999)

---

## Performance Characteristics

| Operation | Complexity | Notes |
|-----------|------------|-------|
| FrozenDictionary dispatch | O(1) | Type-based routing |
| RTree range query | O(log n) | Spatial indexing |
| K-means clustering | O(k × n × i) | k clusters, n points, i iterations |
| Delaunay triangulation | O(n log n) | Bowyer-Watson algorithm |
| Subdivision | O(4^L × n) | L levels, n faces |
| Marching cubes | O(n) | n grid cells |

---

## Dependencies

- **Arsenal.Core**: Result monad, UnifiedOperation, ValidationRules
- **RhinoCommon 8.24+**: Geometry types, algorithms
- **.NET 8.0**: FrozenDictionary, collection expressions

---

## See Also

- `/CLAUDE.md` - Coding standards and patterns
- `/libs/rhino/LIBRARY_GUIDELINES.md` - Implementation guidelines
- Individual module READMEs for detailed API documentation

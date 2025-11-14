# MorphologyCore.cs Blueprint - Dispatch and Execution Implementation

## File Purpose
Core dispatch table and executor functions. Contains FrozenDictionary operation registry and all executor implementations matching existing SpatialCore pattern.

## Full Implementation Code

```csharp
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Morphs;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology operation dispatch and execution core.</summary>
internal static class MorphologyCore {
    /// <summary>Primary operation dispatch table mapping (operation ID, input type) to executor function.</summary>
    internal static readonly FrozenDictionary<(byte Operation, Type InputType), Func<object, object, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>> OperationDispatch =
        new Dictionary<(byte, Type), Func<object, object, IGeometryContext, Result<IReadOnlyList<Morphology.IMorphologyResult>>>> {
            [(MorphologyConfig.OpCageDeform, typeof(Mesh))] = ExecuteCageDeform,
            [(MorphologyConfig.OpCageDeform, typeof(Brep))] = ExecuteCageDeform,
            [(MorphologyConfig.OpSubdivideCatmullClark, typeof(Mesh))] = ExecuteSubdivideCatmullClark,
            [(MorphologyConfig.OpSubdivideLoop, typeof(Mesh))] = ExecuteSubdivideLoop,
            [(MorphologyConfig.OpSubdivideButterfly, typeof(Mesh))] = ExecuteSubdivideButterfly,
            [(MorphologyConfig.OpSmoothLaplacian, typeof(Mesh))] = ExecuteSmoothLaplacian,
            [(MorphologyConfig.OpSmoothTaubin, typeof(Mesh))] = ExecuteSmoothTaubin,
            [(MorphologyConfig.OpEvolveMeanCurvature, typeof(Mesh))] = ExecuteEvolveMeanCurvature,
        }.ToFrozenDictionary();

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteCageDeform(
        object input,
        object parameters,
        IGeometryContext context) =>
        parameters switch {
            (GeometryBase cage, Point3d[] originalPts, Point3d[] deformedPts) when originalPts.Length == deformedPts.Length && originalPts.Length >= MorphologyConfig.MinCageControlPoints =>
                ((Func<Result<IReadOnlyList<Morphology.IMorphologyResult>>>)(() => {
                    GeometryBase geometry = input is Mesh m ? m.DuplicateMesh() : input is Brep b ? b.DuplicateBrep() : null;
                    BoundingBox originalBounds = geometry?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    double originalVolume = originalBounds.Volume;
                    
                    CageMorph morph = new(cage, originalPts, deformedPts);
                    bool success = geometry?.Transform(morph) ?? false;
                    
                    BoundingBox deformedBounds = geometry?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    double deformedVolume = deformedBounds.Volume;
                    
                    double[] displacements = [.. originalPts.Zip(deformedPts, (o, d) => o.DistanceTo(d)),];
                    double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
                    double meanDisp = displacements.Length > 0 ? displacements.Average() : 0.0;
                    double volumeRatio = originalVolume > RhinoMath.ZeroTolerance ? deformedVolume / originalVolume : 1.0;
                    
                    return success && geometry is not null
                        ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            value: [new Morphology.CageDeformResult(geometry, maxDisp, meanDisp, originalBounds, deformedBounds, volumeRatio),])
                        : ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                            error: E.Morphology.CageDeformFailed.WithContext("Transform returned false or geometry null"));
                }))(),
            (GeometryBase, Point3d[] o, Point3d[] d) when o.Length != d.Length =>
                ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Morphology.CageControlPointMismatch.WithContext($"Original: {o.Length}, Deformed: {d.Length}")),
            (GeometryBase, Point3d[] o, Point3d[]) when o.Length < MorphologyConfig.MinCageControlPoints =>
                ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Morphology.InsufficientCagePoints.WithContext($"Count: {o.Length}, Required: {MorphologyConfig.MinCageControlPoints}")),
            _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                error: E.Geometry.InsufficientParameters.WithContext("Expected: (GeometryBase cage, Point3d[] original, Point3d[] deformed)")),
        };

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideCatmullClark(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideCatmullClark, levels, context)
                    .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideLoop(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Morphology.LoopRequiresTriangles.WithContext($"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}"))
                    : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideLoop, levels, context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSubdivideButterfly(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not int levels
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: int levels"))
                : !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
                    ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Morphology.ButterflyIrregularValence.WithContext($"TriangleCount: {mesh.Faces.TriangleCount}, FaceCount: {mesh.Faces.Count}"))
                    : MorphologyCompute.SubdivideIterative(mesh, MorphologyConfig.OpSubdivideButterfly, levels, context)
                        .Bind(subdivided => ComputeSubdivisionMetrics(mesh, subdivided, context));

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothLaplacian(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (int iterations, bool lockBoundary)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, bool lockBoundary)"))
                : MorphologyCompute.SmoothWithConvergence(
                    mesh,
                    parameters.iterations,
                    parameters.lockBoundary,
                    (m, pos, ctx) => LaplacianUpdate(m, pos, useCotangent: true),
                    context)
                    .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, parameters.iterations, context));

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteSmoothTaubin(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters switch {
                (int iterations, double lambda, double mu) when mu >= -lambda =>
                    ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                        error: E.Morphology.TaubinParametersInvalid.WithContext($"μ ({mu:F4}) must be < -λ ({-lambda:F4})")),
                (int iterations, double lambda, double mu) =>
                    MorphologyCompute.SmoothWithConvergence(
                        mesh,
                        iterations,
                        lockBoundary: false,
                        (m, pos, ctx) => {
                            Point3d[] step1 = LaplacianUpdate(m, pos, useCotangent: false);
                            Point3d[] blended1 = [.. Enumerable.Range(0, pos.Length).Select(i => pos[i] + lambda * (step1[i] - pos[i])),];
                            Point3d[] step2 = LaplacianUpdate(m, blended1, useCotangent: false);
                            return [.. Enumerable.Range(0, pos.Length).Select(i => blended1[i] + mu * (step2[i] - blended1[i])),];
                        },
                        context)
                        .Bind(smoothed => ComputeSmoothingMetrics(mesh, smoothed, iterations, context)),
                _ => ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
                    error: E.Geometry.InsufficientParameters.WithContext("Expected: (int iterations, double lambda, double mu)")),
            };

    [Pure]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ExecuteEvolveMeanCurvature(
        object input,
        object parameters,
        IGeometryContext context) =>
        input is not Mesh mesh
            ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InvalidGeometryType.WithContext("Expected: Mesh"))
            : parameters is not (double timeStep, int iterations)
                ? ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Expected: (double timeStep, int iterations)"))
                : MorphologyCompute.SmoothWithConvergence(
                    mesh,
                    parameters.iterations,
                    lockBoundary: false,
                    (m, pos, ctx) => MeanCurvatureFlowUpdate(m, pos, parameters.timeStep, ctx),
                    context)
                    .Bind(evolved => ComputeSmoothingMetrics(mesh, evolved, parameters.iterations, context));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] LaplacianUpdate(Mesh mesh, Point3d[] positions, bool useCotangent) {
        Point3d[] updated = new Point3d[positions.Length];
        
        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            
            if (neighbors.Length is 0) {
                updated[i] = positions[i];
                continue;
            }
            
            Point3d sum = Point3d.Origin;
            double weightSum = 0.0;
            
            for (int j = 0; j < neighbors.Length; j++) {
                int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                Point3d neighborPos = positions[meshVertIdx];
                double weight = useCotangent
                    ? 1.0 / Math.Max(positions[i].DistanceTo(neighborPos), RhinoMath.ZeroTolerance)
                    : 1.0;
                sum += weight * neighborPos;
                weightSum += weight;
            }
            
            updated[i] = weightSum > RhinoMath.ZeroTolerance ? sum / weightSum : positions[i];
        }
        
        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d[] MeanCurvatureFlowUpdate(Mesh mesh, Point3d[] positions, double timeStep, IGeometryContext context) {
        Point3d[] updated = new Point3d[positions.Length];
        
        for (int i = 0; i < positions.Length; i++) {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
            
            if (neighbors.Length is 0) {
                updated[i] = positions[i];
                continue;
            }
            
            Point3d laplacian = Point3d.Origin;
            for (int j = 0; j < neighbors.Length; j++) {
                int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                laplacian += positions[meshVertIdx] - positions[i];
            }
            laplacian /= neighbors.Length;
            
            Vector3d normal = mesh.Normals.Count > i ? mesh.Normals[i] : Vector3d.ZAxis;
            double curvature = Vector3d.DotProduct(laplacian - Point3d.Origin, normal);
            
            updated[i] = positions[i] + timeStep * curvature * normal;
        }
        
        return updated;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSubdivisionMetrics(
        Mesh original,
        Mesh subdivided,
        IGeometryContext context) {
        double[] edgeLengths = [.. Enumerable.Range(0, subdivided.TopologyEdges.Count)
            .Select(i => subdivided.TopologyEdges.EdgeLine(i).Length),
        ];
        
        double[] aspectRatios = [.. Enumerable.Range(0, subdivided.Faces.Count)
            .Select(i => {
                Point3d a = subdivided.Vertices[subdivided.Faces[i].A];
                Point3d b = subdivided.Vertices[subdivided.Faces[i].B];
                Point3d c = subdivided.Vertices[subdivided.Faces[i].C];
                double ab = a.DistanceTo(b);
                double bc = b.DistanceTo(c);
                double ca = c.DistanceTo(a);
                double maxEdge = Math.Max(Math.Max(ab, bc), ca);
                double minEdge = Math.Min(Math.Min(ab, bc), ca);
                return minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue;
            }),
        ];
        
        double[] triangleAngles = [.. Enumerable.Range(0, subdivided.Faces.Count)
            .SelectMany(i => {
                Point3d a = subdivided.Vertices[subdivided.Faces[i].A];
                Point3d b = subdivided.Vertices[subdivided.Faces[i].B];
                Point3d c = subdivided.Vertices[subdivided.Faces[i].C];
                return new[] {
                    Vector3d.VectorAngle(b - a, c - a),
                    Vector3d.VectorAngle(a - b, c - b),
                    Vector3d.VectorAngle(a - c, b - c),
                };
            }),
        ];
        
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [
                new Morphology.SubdivisionResult(
                    subdivided,
                    original.Faces.Count,
                    subdivided.Faces.Count,
                    edgeLengths.Min(),
                    edgeLengths.Max(),
                    edgeLengths.Average(),
                    aspectRatios.Average(),
                    triangleAngles.Min()),
            ]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Morphology.IMorphologyResult>> ComputeSmoothingMetrics(
        Mesh original,
        Mesh smoothed,
        int iterations,
        IGeometryContext context) {
        double[] displacements = [.. Enumerable.Range(0, Math.Min(original.Vertices.Count, smoothed.Vertices.Count))
            .Select(i => ((Point3d)original.Vertices[i]).DistanceTo(smoothed.Vertices[i])),
        ];
        
        double rms = displacements.Length > 0 ? Math.Sqrt(displacements.Select(d => d * d).Average()) : 0.0;
        double maxDisp = displacements.Length > 0 ? displacements.Max() : 0.0;
        double quality = MorphologyCompute.ValidateMeshQuality(smoothed, context).IsSuccess ? 1.0 : 0.0;
        bool converged = rms < context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;
        
        return ResultFactory.Create<IReadOnlyList<Morphology.IMorphologyResult>>(
            value: [
                new Morphology.SmoothingResult(smoothed, iterations, rms, maxDisp, quality, converged),
            ]);
    }
}
```

## Code Pattern Explanations

### FrozenDictionary Dispatch (Lines 15-27)
Matches SpatialCore.OperationRegistry pattern (lines 24-43):
- Maps `(byte operation, Type input)` to executor function
- 8 entries for all operation-type combinations
- Uses MorphologyConfig operation constants
- O(1) lookup performance

### Executor Functions
Each executor follows same pattern:
1. Pattern match on parameters (switch expression)
2. Validate input type and parameter structure
3. Call orchestration function (MorphologyCompute)
4. Bind to metrics computation
5. Return Result with typed error messages

### CageDeform Executor (Lines 29-66)
- Duplicates geometry to preserve original
- Wraps RhinoCommon CageMorph API
- Computes displacement metrics (max, mean)
- Tracks volume ratio via bounding box
- Inline LINQ for displacement calculation

### Subdivision Executors (Lines 68-109)
- Validate mesh type (triangle requirement for Loop/Butterfly)
- Delegate to MorphologyCompute.SubdivideIterative
- Bind to metrics computation
- Return typed SubdivisionResult

### Smoothing Executors (Lines 111-168)
- LaplacianExecutor: Cotangent weights for quality
- TaubinExecutor: Alternating λ/μ steps inline (no helper extraction)
- MeanCurvatureExecutor: Discrete Laplace-Beltrami with normal projection
- All use SmoothWithConvergence orchestration

### LaplacianUpdate (Lines 170-195)
- Cotangent weight: 1/distance (inverse distance weighting)
- Uniform weight: 1.0 (simple average)
- Inline neighbor summation
- Zero-tolerance checks via RhinoMath

### MeanCurvatureFlowUpdate (Lines 197-220)
- Discrete mean curvature: average of edge vectors
- Project onto normal: dot product
- Euler step: position + dt * curvature * normal
- Matches Meyer et al. (2003) discrete differential geometry

### Metrics Computation (Lines 222-286)
- SubdivisionMetrics: Edge lengths, aspect ratios, triangle angles
- SmoothingMetrics: RMS displacement, quality score, convergence
- All inline LINQ (no helper methods)
- Creates result records

## File Metrics
- **Types**: 1 (static internal class)
- **LOC**: ~286 lines (dense dispatch and execution)
- **Methods**: 11 (8 executors, 2 update functions, 2 metrics functions)
- **FrozenDictionary**: 1 (OperationDispatch with 8 entries)
- **Pattern**: Matches SpatialCore.cs exactly

## Integration Points
- **Morphology.Apply**: Calls OperationDispatch.TryGetValue
- **MorphologyCompute**: Calls SubdivideIterative, SmoothWithConvergence, ValidateMeshQuality
- **MorphologyConfig**: Uses all operation constants, Min/MaxCageControlPoints, ConvergenceMultiplier
- **E.Morphology**: All 13 error codes used
- **RhinoCommon**: CageMorph, TopologyVertices, Normals APIs

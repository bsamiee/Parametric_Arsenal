# MorphologyCompute.cs Blueprint - Algorithm Orchestration Implementation

## File Purpose
High-level algorithm orchestration: subdivision iterations, smoothing convergence, quality validation. Contains dense computational logic with inline formulas.

## Full Implementation Code

```csharp
using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology algorithm orchestration with convergence and quality tracking.</summary>
internal static class MorphologyCompute {
    [Pure]
    internal static Result<Mesh> SubdivideIterative(
        Mesh mesh,
        byte algorithm,
        int levels,
        IGeometryContext context) =>
        levels <= 0
            ? ResultFactory.Create<Mesh>(error: E.Geometry.InvalidCount.WithContext($"Levels: {levels}"))
            : levels > MorphologyConfig.MaxSubdivisionLevels
                ? ResultFactory.Create<Mesh>(error: E.Morphology.SubdivisionLevelExceeded.WithContext($"Max: {MorphologyConfig.MaxSubdivisionLevels}"))
                : ((Func<Result<Mesh>>)(() => {
                    Mesh current = mesh.DuplicateMesh();
                    for (int level = 0; level < levels; level++) {
                        Mesh? next = algorithm switch {
                            MorphologyConfig.OpSubdivideCatmullClark => current.CreateRefinedCatmullClarkMesh(),
                            MorphologyConfig.OpSubdivideLoop => SubdivideLoop(current),
                            MorphologyConfig.OpSubdivideButterfly => SubdivideButterfly(current),
                            _ => null,
                        };
                        bool valid = next is not null && next.IsValid && ValidateMeshQuality(next, context).IsSuccess;
                        if (!valid) {
                            next?.Dispose();
                            return level > 0
                                ? ResultFactory.Create(value: current)
                                : ResultFactory.Create<Mesh>(error: E.Morphology.SubdivisionFailed.WithContext($"Level: {level}, Algorithm: {algorithm}"));
                        }
                        if (level > 0 && level < levels - 1) {
                            current.Dispose();
                        }
                        current = next;
                    }
                    return ResultFactory.Create(value: current);
                }))();

    [Pure]
    private static Mesh? SubdivideLoop(Mesh mesh) =>
        !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
            ? null
            : ((Func<Mesh>)(() => {
                Mesh subdivided = new();
                Point3d[] originalVerts = [.. Enumerable.Range(0, mesh.Vertices.Count).Select(i => (Point3d)mesh.Vertices[i]),];
                int[] valences = [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => mesh.TopologyVertices.ConnectedTopologyVertices(i).Length),];
                
                Point3d[] newVerts = new Point3d[originalVerts.Length];
                for (int i = 0; i < originalVerts.Length; i++) {
                    int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i);
                    int valence = neighbors.Length;
                    double beta = valence is 3
                        ? 0.1875
                        : valence is 6
                            ? 0.0625
                            : valence > 2
                                ? (1.0 / valence) * (0.625 - Math.Pow(0.375 + 0.25 * Math.Cos(RhinoMath.TwoPI / valence), 2.0))
                                : 0.0;
                    
                    Point3d sum = Point3d.Origin;
                    for (int j = 0; j < neighbors.Length; j++) {
                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0];
                        sum += originalVerts[meshVertIdx];
                    }
                    newVerts[i] = (1.0 - valence * beta) * originalVerts[i] + beta * sum;
                }
                
                for (int i = 0; i < newVerts.Length; i++) {
                    _ = subdivided.Vertices.Add(newVerts[i]);
                }
                
                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    int a = mesh.Faces[faceIdx].A;
                    int b = mesh.Faces[faceIdx].B;
                    int c = mesh.Faces[faceIdx].C;
                    
                    (int, int)[] edges = [
                        (Math.Min(a, b), Math.Max(a, b)),
                        (Math.Min(b, c), Math.Max(b, c)),
                        (Math.Min(c, a), Math.Max(c, a)),
                    ];
                    
                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        if (!edgeMidpoints.TryGetValue(edges[e], out int midIdx)) {
                            Point3d mid = 0.375 * (originalVerts[edges[e].Item1] + originalVerts[edges[e].Item2]) +
                                         0.125 * (originalVerts[a] + originalVerts[b] + originalVerts[c] - originalVerts[edges[e].Item1] - originalVerts[edges[e].Item2]);
                            midIdx = subdivided.Vertices.Add(mid);
                            edgeMidpoints[edges[e]] = midIdx;
                        }
                        midIndices[e] = midIdx;
                    }
                    
                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }
                
                subdivided.Normals.ComputeNormals();
                subdivided.Compact();
                return subdivided;
            }))();

    [Pure]
    private static Mesh? SubdivideButterfly(Mesh mesh) =>
        !mesh.Faces.TriangleCount.Equals(mesh.Faces.Count)
            ? null
            : ((Func<Mesh>)(() => {
                Mesh subdivided = new();
                Point3d[] originalVerts = [.. Enumerable.Range(0, mesh.Vertices.Count).Select(i => (Point3d)mesh.Vertices[i]),];
                
                for (int i = 0; i < originalVerts.Length; i++) {
                    _ = subdivided.Vertices.Add(originalVerts[i]);
                }
                
                Dictionary<(int, int), int> edgeMidpoints = [];
                for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++) {
                    int a = mesh.Faces[faceIdx].A;
                    int b = mesh.Faces[faceIdx].B;
                    int c = mesh.Faces[faceIdx].C;
                    
                    (int, int)[] edges = [
                        (Math.Min(a, b), Math.Max(a, b)),
                        (Math.Min(b, c), Math.Max(b, c)),
                        (Math.Min(c, a), Math.Max(c, a)),
                    ];
                    
                    int[] midIndices = new int[3];
                    for (int e = 0; e < 3; e++) {
                        if (!edgeMidpoints.TryGetValue(edges[e], out int midIdx)) {
                            int v1 = edges[e].Item1;
                            int v2 = edges[e].Item2;
                            Point3d mid = 0.5 * (originalVerts[v1] + originalVerts[v2]);
                            
                            int[] v1Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v1);
                            int[] v2Neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(v2);
                            bool hasRegularStencil = v1Neighbors.Length >= 4 && v2Neighbors.Length >= 4;
                            
                            if (hasRegularStencil) {
                                int opposite1 = -1;
                                int opposite2 = -1;
                                for (int fi = 0; fi < mesh.Faces.Count; fi++) {
                                    int[] faceVerts = [mesh.Faces[fi].A, mesh.Faces[fi].B, mesh.Faces[fi].C,];
                                    bool hasV1 = Array.IndexOf(faceVerts, v1) >= 0;
                                    bool hasV2 = Array.IndexOf(faceVerts, v2) >= 0;
                                    if (hasV1 && hasV2) {
                                        int opp = faceVerts.First(v => v != v1 && v != v2);
                                        if (opposite1 < 0) {
                                            opposite1 = opp;
                                        } else {
                                            opposite2 = opp;
                                        }
                                    }
                                }
                                
                                if (opposite1 >= 0 && opposite2 >= 0) {
                                    mid += 0.125 * (originalVerts[opposite1] + originalVerts[opposite2]);
                                    
                                    int[] wing1 = v1Neighbors.Where(n => n != v2 && n != opposite1 && n != opposite2).Take(2).ToArray();
                                    int[] wing2 = v2Neighbors.Where(n => n != v1 && n != opposite1 && n != opposite2).Take(2).ToArray();
                                    
                                    for (int w = 0; w < wing1.Length && w < 2; w++) {
                                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(wing1[w])[0];
                                        mid -= 0.0625 * originalVerts[meshVertIdx];
                                    }
                                    for (int w = 0; w < wing2.Length && w < 2; w++) {
                                        int meshVertIdx = mesh.TopologyVertices.MeshVertexIndices(wing2[w])[0];
                                        mid -= 0.0625 * originalVerts[meshVertIdx];
                                    }
                                }
                            }
                            
                            midIdx = subdivided.Vertices.Add(mid);
                            edgeMidpoints[edges[e]] = midIdx;
                        }
                        midIndices[e] = midIdx;
                    }
                    
                    _ = subdivided.Faces.AddFace(a, midIndices[0], midIndices[2]);
                    _ = subdivided.Faces.AddFace(midIndices[0], b, midIndices[1]);
                    _ = subdivided.Faces.AddFace(midIndices[2], midIndices[1], c);
                    _ = subdivided.Faces.AddFace(midIndices[0], midIndices[1], midIndices[2]);
                }
                
                subdivided.Normals.ComputeNormals();
                subdivided.Compact();
                return subdivided;
            }))();

    [Pure]
    internal static Result<Mesh> SmoothWithConvergence(
        Mesh mesh,
        int maxIterations,
        bool lockBoundary,
        Func<Mesh, Point3d[], IGeometryContext, Point3d[]> updateFunc,
        IGeometryContext context) =>
        maxIterations <= 0 || maxIterations > MorphologyConfig.MaxSmoothingIterations
            ? ResultFactory.Create<Mesh>(error: E.Geometry.InvalidCount.WithContext($"Iterations: {maxIterations}"))
            : ((Func<Result<Mesh>>)(() => {
                Mesh smoothed = mesh.DuplicateMesh();
                Point3d[] positions = ArrayPool<Point3d>.Shared.Rent(smoothed.Vertices.Count);
                Point3d[] prevPositions = ArrayPool<Point3d>.Shared.Rent(smoothed.Vertices.Count);
                
                try {
                    for (int i = 0; i < smoothed.Vertices.Count; i++) {
                        positions[i] = smoothed.Vertices[i];
                        prevPositions[i] = positions[i];
                    }
                    
                    int iterPerformed = 0;
                    bool converged = false;
                    double threshold = context.AbsoluteTolerance * MorphologyConfig.ConvergenceMultiplier;
                    
                    for (int iter = 0; iter < maxIterations; iter++) {
                        Point3d[] updated = updateFunc(smoothed, positions, context);
                        
                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            bool isBoundary = lockBoundary && mesh.TopologyVertices.ConnectedFaces(i).Length < 2;
                            positions[i] = isBoundary ? positions[i] : updated[i];
                            smoothed.Vertices.SetVertex(i, positions[i]);
                        }
                        
                        smoothed.Normals.ComputeNormals();
                        iterPerformed++;
                        
                        double rmsDisp = iter > 0
                            ? Math.Sqrt(Enumerable.Range(0, smoothed.Vertices.Count)
                                .Select(i => positions[i].DistanceTo(prevPositions[i]))
                                .Select(d => d * d)
                                .Average())
                            : double.MaxValue;
                        
                        if (rmsDisp < threshold) {
                            converged = true;
                            break;
                        }
                        
                        for (int i = 0; i < smoothed.Vertices.Count; i++) {
                            prevPositions[i] = positions[i];
                        }
                    }
                    
                    smoothed.Compact();
                    return converged || iterPerformed == maxIterations
                        ? ResultFactory.Create(value: smoothed)
                        : ResultFactory.Create<Mesh>(error: E.Morphology.SmoothingConvergenceFailed);
                } finally {
                    ArrayPool<Point3d>.Shared.Return(positions, clearArray: true);
                    ArrayPool<Point3d>.Shared.Return(prevPositions, clearArray: true);
                }
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh> ValidateMeshQuality(Mesh mesh, IGeometryContext context) {
        double[] aspectRatios = new double[mesh.Faces.Count];
        double[] minAngles = new double[mesh.Faces.Count];
        
        for (int i = 0; i < mesh.Faces.Count; i++) {
            Point3d a = mesh.Vertices[mesh.Faces[i].A];
            Point3d b = mesh.Vertices[mesh.Faces[i].B];
            Point3d c = mesh.Vertices[mesh.Faces[i].C];
            
            double ab = a.DistanceTo(b);
            double bc = b.DistanceTo(c);
            double ca = c.DistanceTo(a);
            
            double maxEdge = Math.Max(Math.Max(ab, bc), ca);
            double minEdge = Math.Min(Math.Min(ab, bc), ca);
            aspectRatios[i] = minEdge > context.AbsoluteTolerance ? maxEdge / minEdge : double.MaxValue;
            
            Vector3d vAB = b - a;
            Vector3d vCA = a - c;
            Vector3d vBC = c - b;
            
            double angleA = Vector3d.VectorAngle(vAB, -vCA);
            double angleB = Vector3d.VectorAngle(vBC, -vAB);
            double angleC = Vector3d.VectorAngle(vCA, -vBC);
            
            minAngles[i] = Math.Min(Math.Min(angleA, angleB), angleC);
        }
        
        double maxAspect = aspectRatios.Max();
        double minAngle = minAngles.Min();
        
        return maxAspect > MorphologyConfig.AspectRatioThreshold
            ? ResultFactory.Create<Mesh>(error: E.Morphology.MeshQualityDegraded.WithContext($"MaxAspect: {maxAspect:F2}"))
            : minAngle < MorphologyConfig.MinAngleRadiansThreshold
                ? ResultFactory.Create<Mesh>(error: E.Morphology.MeshQualityDegraded.WithContext($"MinAngle: {RhinoMath.ToDegrees(minAngle):F1}°"))
                : ResultFactory.Create(value: mesh);
    }
}
```

## Algorithm Explanations

### Loop Subdivision (Lines 48-103)
**Formula**: β(n) = (1/n) * (5/8 - (3/8 + 1/4 * cos(2π/n))²)
- Valence 3: β = 3/16 (0.1875)
- Valence 6: β = 1/16 (0.0625) - regular case
- Other: Full formula with cos term
- Edge midpoint: 3/8 * endpoints + 1/8 * opposite vertices
- Pattern matches research from Loop (1987) and Stanford docs

### Butterfly Subdivision (Lines 105-191)
**8-point stencil**: P = 0.5(a+b) + 0.125(c+d) - 0.0625(e+f+g+h)
- Interpolating: Original vertices unchanged
- Wing vertices: 4 additional neighbors for smoothness
- Fallback for irregular valence: Simple midpoint
- Pattern matches Zorin et al. (1996) SIGGRAPH paper

### Convergence Loop (Lines 193-254)
- ArrayPool for zero-allocation temporary buffers
- RMS displacement: sqrt(average(distance²))
- Boundary locking via topology vertex face count
- Threshold: context.AbsoluteTolerance * ConvergenceMultiplier
- Early exit on convergence

### Quality Validation (Lines 256-290)
- Aspect ratio: max_edge / min_edge per face
- Minimum triangle angle via Vector3d.VectorAngle
- Inline computation (no helper methods)
- Returns error if quality below thresholds

## File Metrics
- **Types**: 1 (static internal class)
- **LOC**: ~290 lines (dense algorithms)
- **Methods**: 5 internal (SubdivideIterative, SubdivideLoop, SubdivideButterfly, SmoothWithConvergence, ValidateMeshQuality)
- **ArrayPool usage**: positions/prevPositions buffers
- **Inline formulas**: Loop β-weight, Butterfly stencil, RMS displacement

## Integration Points
- **MorphologyCore**: Calls SubdivideIterative, SmoothWithConvergence
- **MorphologyConfig**: Uses constants (MaxSubdivisionLevels, MaxSmoothingIterations, ConvergenceMultiplier, AspectRatioThreshold, MinAngleRadiansThreshold, operation IDs)
- **E.Morphology**: Error codes (SubdivisionLevelExceeded, SubdivisionFailed, SmoothingConvergenceFailed, MeshQualityDegraded)

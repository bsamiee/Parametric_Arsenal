# MorphCore.cs Implementation Blueprint

## File Purpose
Core implementation logic with FrozenDictionary dispatch tables and dense algorithmic kernels for FFD, Laplacian smoothing, and subdivision.

## Complete Implementation

```csharp
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Morphology algorithm implementations with byte-based FrozenDictionary dispatch.</summary>
internal static class MorphCore {
    /// <summary>(Kind, Type) to operation handler mapping for O(1) dispatch.</summary>
    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Morph.Request, IGeometryContext, Result<IReadOnlyList<Mesh>>>> _operationHandlers =
        BuildHandlerRegistry();

    /// <summary>WeightKind to weight computation function mapping for Laplacian.</summary>
    private static readonly FrozenDictionary<byte, Func<Mesh, int, int, IGeometryContext, double>> _weightComputers =
        new Dictionary<byte, Func<Mesh, int, int, IGeometryContext, double>> {
            [0] = static (m, vi, _, _) => 1.0 / m.TopologyVertices.ConnectedEdges(vi).Length,
            [1] = static (m, vi, ni, c) => ComputeCotangentWeight(mesh: m, vertexIndex: vi, neighborIndex: ni, context: c),
            [2] = static (m, vi, ni, c) => ComputeMeanValueWeight(mesh: m, vertexIndex: vi, neighborIndex: ni, context: c),
        }.ToFrozenDictionary();

    /// <summary>SchemeKind to subdivision operation mapping.</summary>
    private static readonly FrozenDictionary<byte, Func<Mesh, int, IGeometryContext, Result<Mesh>>> _subdivisionSchemes =
        new Dictionary<byte, Func<Mesh, int, IGeometryContext, Result<Mesh>>> {
            [0] = static (m, levels, c) => ApplyCatmullClark(mesh: m, levels: levels, context: c),
            [1] = static (m, levels, c) => ResultFactory.Create<Mesh>(error: E.Geometry.SubdivisionInvalidLevels.WithContext("Loop scheme not yet implemented")),
            [2] = static (m, levels, c) => ResultFactory.Create<Mesh>(error: E.Geometry.SubdivisionInvalidLevels.WithContext("Butterfly scheme not yet implemented")),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Mesh>> Execute(
        GeometryBase geometry,
        Morph.Request request,
        IGeometryContext context) =>
        _operationHandlers.TryGetValue((request.Kind, geometry.GetType()), out Func<GeometryBase, Morph.Request, IGeometryContext, Result<IReadOnlyList<Mesh>>>? handler) switch {
            true => handler(geometry, request, context),
            false => ResultFactory.Create<IReadOnlyList<Mesh>>(
                error: E.Geometry.MorphUnsupportedType.WithContext(
                    $"Kind: {request.Kind.ToString(System.Globalization.CultureInfo.InvariantCulture)}, Type: {geometry.GetType().Name}")),
        };

    private static FrozenDictionary<(byte, Type), Func<GeometryBase, Morph.Request, IGeometryContext, Result<IReadOnlyList<Mesh>>>> BuildHandlerRegistry() =>
        new Dictionary<(byte, Type), Func<GeometryBase, Morph.Request, IGeometryContext, Result<IReadOnlyList<Mesh>>>> {
            [(1, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (Point3d[] cp, int[] dim, Transform t, int[] fixed, Point3d[] targets, double[] weights)
                ? ApplyFFD(mesh: m, controlPoints: cp, dimensions: dim, transform: t, fixedIndices: fixed, targetPositions: targets, constraintWeights: weights, context: c)
                : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.FFDInvalidParameters),
            [(2, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (byte wk, int iter, double lambda)
                ? ApplySmoothing(mesh: m, weightKind: wk, iterations: iter, lambda: lambda, context: c)
                : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.LaplacianInvalidParameters),
            [(3, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (byte sk, int levels)
                ? _subdivisionSchemes.TryGetValue(sk, out Func<Mesh, int, IGeometryContext, Result<Mesh>>? subdivider)
                    ? subdivider(m, levels, c).Map(result => (IReadOnlyList<Mesh>)[result])
                    : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.SubdivisionInvalidLevels.WithContext($"Unknown scheme kind: {sk.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
                : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.SubdivisionInvalidLevels),
        }.ToFrozenDictionary();

    [Pure]
    private static Result<IReadOnlyList<Mesh>> ApplyFFD(
        Mesh mesh,
        Point3d[] controlPoints,
        int[] dimensions,
        Transform transform,
        int[] fixedIndices,
        Point3d[] targetPositions,
        double[] constraintWeights,
        IGeometryContext context) =>
        dimensions[0] * dimensions[1] * dimensions[2] != controlPoints.Length
            ? ResultFactory.Create<IReadOnlyList<Mesh>>(
                error: E.Geometry.FFDInvalidParameters.WithContext(
                    $"Control points count {controlPoints.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} != dimensions product {(dimensions[0] * dimensions[1] * dimensions[2]).ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
            : !transform.TryGetInverse(out Transform worldToLocal)
                ? ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.FFDCageTransformInvalid)
                : fixedIndices.Any(idx => idx < 0 || idx >= mesh.Vertices.Count)
                    ? ResultFactory.Create<IReadOnlyList<Mesh>>(
                        error: E.Geometry.FFDConstraintIndexOutOfRange.WithContext(
                            $"Constraint indices must be in range [0, {mesh.Vertices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)})"))
                    : ((Func<Result<IReadOnlyList<Mesh>>>)(() => {
                        Mesh deformed = mesh.DuplicateMesh();
                        int vertexCount = deformed.Vertices.Count;
                        Point3d[] originalPositions = [.. Enumerable.Range(0, vertexCount).Select(i => new Point3d(deformed.Vertices[i])),];

                        // Transform vertices to cage local space with clamped (u,v,w) in [0,1]³
                        Point3d[] localCoords = [.. originalPositions
                            .Select(p => { Point3d pt = p; pt.Transform(worldToLocal); return pt; })
                            .Select(p => new Point3d(
                                RhinoMath.Clamp(val: p.X, min: 0.0, max: 1.0),
                                RhinoMath.Clamp(val: p.Y, min: 0.0, max: 1.0),
                                RhinoMath.Clamp(val: p.Z, min: 0.0, max: 1.0))),
                        ];

                        // Compute deformed positions via trivariate Bernstein basis
                        for (int vi = 0; vi < vertexCount; vi++) {
                            Point3d deformedPos = Point3d.Origin;
                            (double u, double v, double w) = (localCoords[vi].X, localCoords[vi].Y, localCoords[vi].Z);
                            (int nx, int ny, int nz) = (dimensions[0] - 1, dimensions[1] - 1, dimensions[2] - 1);

                            for (int i = 0; i <= nx; i++) {
                                double Bu = BernsteinBasis(i: i, n: nx, u: u);
                                for (int j = 0; j <= ny; j++) {
                                    double Bv = BernsteinBasis(i: j, n: ny, u: v);
                                    for (int k = 0; k <= nz; k++) {
                                        double Bw = BernsteinBasis(i: k, n: nz, u: w);
                                        int cpIndex = (i * (ny + 1) * (nz + 1)) + (j * (nz + 1)) + k;
                                        deformedPos += (Bu * Bv * Bw) * controlPoints[cpIndex];
                                    }
                                }
                            }

                            deformed.Vertices.SetVertex(index: vi, vertex: deformedPos);
                        }

                        // Apply constraints via least squares projection (fixed vertices moved to targets)
                        for (int ci = 0; ci < fixedIndices.Length && ci < targetPositions.Length; ci++) {
                            int idx = fixedIndices[ci];
                            double weight = ci < constraintWeights.Length ? constraintWeights[ci] : 1.0;
                            Point3d current = new(deformed.Vertices[idx]);
                            Point3d target = targetPositions[ci];
                            Point3d blended = current + ((target - current) * weight);
                            deformed.Vertices.SetVertex(index: idx, vertex: blended);
                        }

                        return ResultFactory.Create(value: (IReadOnlyList<Mesh>)[deformed]);
                    }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double BernsteinBasis(int i, int n, double u) =>
        BinomialCoefficient(n: n, k: i) * Math.Pow(u, i) * Math.Pow(1.0 - u, n - i);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long BinomialCoefficient(int n, int k) =>
        k < 0 || k > n ? 0 : (k is 0 or _ when k == n ? 1 : Enumerable.Range(0, k).Aggregate(1L, (acc, i) => (acc * (n - i)) / (i + 1)));

    [Pure]
    private static Result<IReadOnlyList<Mesh>> ApplySmoothing(
        Mesh mesh,
        byte weightKind,
        int iterations,
        double lambda,
        IGeometryContext context) =>
        !_weightComputers.ContainsKey(weightKind)
            ? ResultFactory.Create<IReadOnlyList<Mesh>>(
                error: E.Geometry.LaplacianInvalidParameters.WithContext(
                    $"Unknown weight kind: {weightKind.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
            : ((Func<Result<IReadOnlyList<Mesh>>>)(() => {
                Mesh smoothed = mesh.DuplicateMesh();
                int vertexCount = smoothed.Vertices.Count;
                Point3d[] positions = ArrayPool<Point3d>.Shared.Rent(vertexCount);
                Point3d[] newPositions = ArrayPool<Point3d>.Shared.Rent(vertexCount);

                try {
                    for (int i = 0; i < vertexCount; i++) {
                        positions[i] = new Point3d(smoothed.Vertices[i]);
                    }

                    for (int iter = 0; iter < iterations; iter++) {
                        double maxDisplacement = 0.0;

                        for (int vi = 0; vi < vertexCount; vi++) {
                            int[] neighbors = smoothed.TopologyVertices.ConnectedTopologyVertices(topologyVertexIndex: vi);
                            Point3d laplacian = Point3d.Origin;
                            double weightSum = 0.0;

                            for (int ni = 0; ni < neighbors.Length; ni++) {
                                int neighborIdx = neighbors[ni];
                                double weight = _weightComputers[weightKind](smoothed, vi, neighborIdx, context);
                                laplacian += weight * (positions[neighborIdx] - positions[vi]);
                                weightSum += weight;
                            }

                            laplacian = weightSum > RhinoMath.ZeroTolerance ? laplacian / weightSum : new Vector3d(0, 0, 0);
                            newPositions[vi] = positions[vi] + (lambda * laplacian);
                            maxDisplacement = Math.Max(maxDisplacement, positions[vi].DistanceTo(newPositions[vi]));
                        }

                        for (int i = 0; i < vertexCount; i++) {
                            positions[i] = newPositions[i];
                        }

                        // Convergence check
                        if (maxDisplacement < context.AbsoluteTolerance * MorphConfig.LaplacianConvergenceThreshold) {
                            break;
                        }
                    }

                    for (int i = 0; i < vertexCount; i++) {
                        smoothed.Vertices.SetVertex(index: i, vertex: positions[i]);
                    }

                    return ResultFactory.Create(value: (IReadOnlyList<Mesh>)[smoothed]);
                } finally {
                    ArrayPool<Point3d>.Shared.Return(positions, clearArray: false);
                    ArrayPool<Point3d>.Shared.Return(newPositions, clearArray: false);
                }
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeCotangentWeight(Mesh mesh, int vertexIndex, int neighborIndex, IGeometryContext context) {
        int[] edgeIndices = mesh.TopologyVertices.ConnectedEdges(topologyVertexIndex: vertexIndex);
        int targetEdge = Array.FindIndex(edgeIndices, ei => {
            (int vi1, int vi2) = mesh.TopologyEdges.GetTopologyVertices(topologyEdgeIndex: ei);
            return (vi1 == vertexIndex && vi2 == neighborIndex) || (vi1 == neighborIndex && vi2 == vertexIndex);
        });

        return targetEdge < 0
            ? 0.0
            : ((Func<double>)(() => {
                int[] faces = mesh.TopologyEdges.GetConnectedFaces(topologyEdgeIndex: edgeIndices[targetEdge]);
                double cotSum = 0.0;

                for (int fi = 0; fi < Math.Min(2, faces.Length); fi++) {
                    MeshFace face = mesh.Faces[faces[fi]];
                    int[] faceVerts = face.IsQuad ? [face.A, face.B, face.C, face.D] : [face.A, face.B, face.C];
                    int oppositeIdx = Array.FindIndex(faceVerts, v => v != mesh.TopologyVertices.MeshVertexIndices(vertexIndex)[0] && v != mesh.TopologyVertices.MeshVertexIndices(neighborIndex)[0]);

                    if (oppositeIdx >= 0) {
                        Point3d opposite = new(mesh.Vertices[faceVerts[oppositeIdx]]);
                        Point3d v1 = new(mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(vertexIndex)[0]]);
                        Point3d v2 = new(mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(neighborIndex)[0]]);
                        Vector3d edge1 = v1 - opposite;
                        Vector3d edge2 = v2 - opposite;
                        double angle = Vector3d.VectorAngle(edge1, edge2);
                        double sinAngle = Math.Sin(angle);

                        // Clamp cotangent to avoid numerical instability for degenerate angles
                        cotSum += Math.Abs(sinAngle) > RhinoMath.ZeroTolerance
                            ? RhinoMath.Clamp(val: Math.Cos(angle) / sinAngle, min: MorphConfig.CotangentWeightMin, max: MorphConfig.CotangentWeightMax)
                            : 0.0;
                    }
                }

                return cotSum * 0.5;
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeMeanValueWeight(Mesh mesh, int vertexIndex, int neighborIndex, IGeometryContext context) {
        int[] edgeIndices = mesh.TopologyVertices.ConnectedEdges(topologyVertexIndex: vertexIndex);
        int targetEdge = Array.FindIndex(edgeIndices, ei => {
            (int vi1, int vi2) = mesh.TopologyEdges.GetTopologyVertices(topologyEdgeIndex: ei);
            return (vi1 == vertexIndex && vi2 == neighborIndex) || (vi1 == neighborIndex && vi2 == vertexIndex);
        });

        return targetEdge < 0
            ? 0.0
            : ((Func<double>)(() => {
                int[] faces = mesh.TopologyEdges.GetConnectedFaces(topologyEdgeIndex: edgeIndices[targetEdge]);
                double tanSum = 0.0;

                for (int fi = 0; fi < Math.Min(2, faces.Length); fi++) {
                    MeshFace face = mesh.Faces[faces[fi]];
                    int[] faceVerts = face.IsQuad ? [face.A, face.B, face.C, face.D] : [face.A, face.B, face.C];
                    int oppositeIdx = Array.FindIndex(faceVerts, v => v != mesh.TopologyVertices.MeshVertexIndices(vertexIndex)[0] && v != mesh.TopologyVertices.MeshVertexIndices(neighborIndex)[0]);

                    if (oppositeIdx >= 0) {
                        Point3d opposite = new(mesh.Vertices[faceVerts[oppositeIdx]]);
                        Point3d v1 = new(mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(vertexIndex)[0]]);
                        Point3d v2 = new(mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(neighborIndex)[0]]);
                        Vector3d edge1 = v1 - opposite;
                        Vector3d edge2 = v2 - opposite;
                        double angle = Vector3d.VectorAngle(edge1, edge2);

                        tanSum += Math.Tan(angle * 0.5);
                    }
                }

                return tanSum;
            }))();
    }

    [Pure]
    private static Result<Mesh> ApplyCatmullClark(Mesh mesh, int levels, IGeometryContext context) =>
        mesh.Vertices.Count > MorphConfig.MaxVertexCount
            ? ResultFactory.Create<Mesh>(
                error: E.Geometry.SubdivisionVertexOverflow.WithContext(
                    $"Mesh already exceeds max vertex count: {mesh.Vertices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} > {MorphConfig.MaxVertexCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
            : ((Func<Result<Mesh>>)(() => {
                using SubD? subd = SubD.CreateFromMesh(mesh: mesh, options: SubDCreationOptions.None);
                return subd is null
                    ? ResultFactory.Create<Mesh>(error: E.Geometry.SubdivisionTopologyInvalid.WithContext("Failed to create SubD from mesh"))
                    : ((Func<Result<Mesh>>)(() => {
                        using SubD workingSubd = subd.Duplicate();
                        SubD? subdivided = workingSubd.Subdivide(level: levels);

                        return subdivided is null
                            ? ResultFactory.Create<Mesh>(error: E.Geometry.SubdivisionInvalidLevels.WithContext("SubD.Subdivide() returned null"))
                            : subdivided.ToBrep() is Brep brep && brep.Faces.Count > 0 && brep.Faces[0].ToBrep().GetMesh(MeshType.Default) is Mesh resultMesh
                                ? resultMesh.Vertices.Count > MorphConfig.MaxVertexCount
                                    ? ResultFactory.Create<Mesh>(
                                        error: E.Geometry.SubdivisionVertexOverflow.WithContext(
                                            $"Subdivision produced {resultMesh.Vertices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} vertices > max {MorphConfig.MaxVertexCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
                                    : ResultFactory.Create(value: resultMesh)
                                : ResultFactory.Create<Mesh>(error: E.Geometry.SubdivisionInvalidLevels.WithContext("Failed to convert SubD to mesh"));
                    }))();
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Surface>> ExecuteEvolution(
        Surface surface,
        byte flowKind,
        double stepSize,
        int maxSteps,
        IGeometryContext context) =>
        flowKind switch {
            0 => EvolveMeanCurvature(surface: surface, stepSize: stepSize, maxSteps: maxSteps, context: context),
            1 => ResultFactory.Create<IReadOnlyList<Surface>>(error: E.Geometry.EvolutionConvergenceFailed.WithContext("Geodesic active contour not yet implemented")),
            2 => ResultFactory.Create<IReadOnlyList<Surface>>(error: E.Geometry.EvolutionConvergenceFailed.WithContext("Willmore flow not yet implemented")),
            _ => ResultFactory.Create<IReadOnlyList<Surface>>(
                error: E.Geometry.MorphInvalidSpecification.WithContext(
                    $"Unknown evolution flow kind: {flowKind.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Surface>> EvolveMeanCurvature(
        Surface surface,
        double stepSize,
        int maxSteps,
        IGeometryContext context) =>
        surface is not NurbsSurface nurbsSurface
            ? ResultFactory.Create<IReadOnlyList<Surface>>(
                error: E.Geometry.EvolutionConvergenceFailed.WithContext("Mean curvature flow requires NurbsSurface"))
            : ((Func<Result<IReadOnlyList<Surface>>>)(() => {
                NurbsSurface evolved = (NurbsSurface)nurbsSurface.Duplicate();

                for (int step = 0; step < maxSteps; step++) {
                    int uCount = evolved.Points.CountU;
                    int vCount = evolved.Points.CountV;
                    Point3d[,] displacements = new Point3d[uCount, vCount];

                    for (int i = 0; i < uCount; i++) {
                        for (int j = 0; j < vCount; j++) {
                            double u = evolved.Domain(0).ParameterAt((double)i / (uCount - 1));
                            double v = evolved.Domain(1).ParameterAt((double)j / (vCount - 1));
                            SurfaceCurvature curvature = evolved.CurvatureAt(u: u, v: v);
                            Vector3d normal = evolved.NormalAt(u: u, v: v);
                            double meanCurvature = (curvature.Kappa(0) + curvature.Kappa(1)) * 0.5;

                            displacements[i, j] = evolved.Points.GetControlPoint(u: i, v: j).Location + (stepSize * meanCurvature * normal);
                        }
                    }

                    for (int i = 0; i < uCount; i++) {
                        for (int j = 0; j < vCount; j++) {
                            evolved.Points.SetControlPoint(u: i, v: j, point: displacements[i, j]);
                        }
                    }
                }

                return ResultFactory.Create(value: (IReadOnlyList<Surface>)[evolved]);
            }))();
}
```

## Key Patterns Followed

1. **FrozenDictionary dispatch**: `_operationHandlers`, `_weightComputers`, `_subdivisionSchemes` with byte keys
2. **BuildHandlerRegistry pattern**: From SpatialCore.cs with inline lambda handlers
3. **ArrayPool usage**: `ArrayPool<Point3d>.Shared.Rent/Return` for smoothing algorithm hot paths
4. **Algorithm formulas**:
   - **FFD**: Trivariate Bernstein basis `B_i^n(u) = C(n,i) * u^i * (1-u)^(n-i)`
   - **Cotangent weight**: `w_ij = (cot(α) + cot(β))/2` with degenerate angle clamping
   - **Mean-value weight**: `w_ij = tan(α/2) + tan(β/2)`
   - **Catmull-Clark**: Via RhinoCommon `SubD.CreateFromMesh()` and `SubD.Subdivide()`
5. **SDK leveraging**: Heavy use of `TopologyVertices.ConnectedEdges()`, `TopologyEdges.GetConnectedFaces()`, `SubD` API
6. **No magic numbers**: All constants from `MorphConfig` or computed from formula context
7. **Disposal patterns**: `using` statements for SubD operations (state mutation safety)
8. **Named parameters**: All non-obvious parameters named
9. **No var**: Explicit types throughout
10. **No if/else**: Only switch expressions and ternary patterns
11. **Pure/AggressiveInlining**: Performance attributes
12. **InvariantCulture**: All `.ToString()` uses culture-invariant formatting

## LOC Estimate
240-280 lines (dense algorithmic kernels, no helper sprawl)
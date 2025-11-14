using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Dense fields algorithms: gradient, streamline, isosurface.</summary>
[Pure]
internal static class FieldsCompute {
    // ============================================================================
    // GRADIENT FIELD COMPUTATION (central difference approximation)
    // ============================================================================

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Gradients)> ComputeGradient(
        double[] distances,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        int totalSamples = distances.Length;
        Vector3d[] gradients = ArrayPool<Vector3d>.Shared.Rent(totalSamples);

        try {
            double dx = gridDelta.X;
            double dy = gridDelta.Y;
            double dz = gridDelta.Z;
            double twoDx = 2.0 * dx;
            double twoDy = 2.0 * dy;
            double twoDz = 2.0 * dz;

            // Central difference: ∇f = [(f(x+dx) - f(x-dx))/(2dx), (f(y+dy) - f(y-dy))/(2dy), (f(z+dz) - f(z-dz))/(2dz)]
            for (int i = 0; i < resolution; i++) {
                for (int j = 0; j < resolution; j++) {
                    for (int k = 0; k < resolution; k++) {
                        int idx = (i * resolution * resolution) + (j * resolution) + k;

                        double dfdx = (i < resolution - 1 && i > 0)
                            ? (distances[((i + 1) * resolution * resolution) + (j * resolution) + k] - distances[((i - 1) * resolution * resolution) + (j * resolution) + k]) / twoDx
                            : (i == 0 && resolution > 1)
                                ? (distances[((i + 1) * resolution * resolution) + (j * resolution) + k] - distances[idx]) / dx
                                : (i == resolution - 1 && resolution > 1)
                                    ? (distances[idx] - distances[((i - 1) * resolution * resolution) + (j * resolution) + k]) / dx
                                    : 0.0;

                        double dfdy = (j < resolution - 1 && j > 0)
                            ? (distances[(i * resolution * resolution) + ((j + 1) * resolution) + k] - distances[(i * resolution * resolution) + ((j - 1) * resolution) + k]) / twoDy
                            : (j == 0 && resolution > 1)
                                ? (distances[(i * resolution * resolution) + ((j + 1) * resolution) + k] - distances[idx]) / dy
                                : (j == resolution - 1 && resolution > 1)
                                    ? (distances[idx] - distances[(i * resolution * resolution) + ((j - 1) * resolution) + k]) / dy
                                    : 0.0;

                        double dfdz = (k < resolution - 1 && k > 0)
                            ? (distances[(i * resolution * resolution) + (j * resolution) + (k + 1)] - distances[(i * resolution * resolution) + (j * resolution) + (k - 1)]) / twoDz
                            : (k == 0 && resolution > 1)
                                ? (distances[(i * resolution * resolution) + (j * resolution) + (k + 1)] - distances[idx]) / dz
                                : (k == resolution - 1 && resolution > 1)
                                    ? (distances[idx] - distances[(i * resolution * resolution) + (j * resolution) + (k - 1)]) / dz
                                    : 0.0;

                        gradients[idx] = new Vector3d(dfdx, dfdy, dfdz);
                    }
                }
            }

            Vector3d[] finalGradients = [.. gradients[..totalSamples]];
            return ResultFactory.Create(value: (Grid: grid, Gradients: finalGradients));
        } finally {
            ArrayPool<Vector3d>.Shared.Return(gradients, clearArray: true);
        }
    }

    // ============================================================================
    // STREAMLINE INTEGRATION (RK4 with vector field interpolation)
    // ============================================================================

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> IntegrateStreamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        double stepSize,
        byte integrationMethod,
        IGeometryContext context) {
        Curve[] streamlines = new Curve[seeds.Length];
        Point3d[] pathBuffer = ArrayPool<Point3d>.Shared.Rent(FieldsConfig.MaxStreamlineSteps);

        try {
            for (int seedIdx = 0; seedIdx < seeds.Length; seedIdx++) {
                Point3d current = seeds[seedIdx];
                int stepCount = 0;
                pathBuffer[stepCount++] = current;

                for (int step = 0; step < FieldsConfig.MaxStreamlineSteps - 1; step++) {
                    Vector3d k1 = InterpolateVectorField(vectorField, gridPoints, current);

                    // RK4 requires sequential computation: k2 depends on k1, k3 depends on k2, k4 depends on k3
                    Vector3d k2 = integrationMethod switch {
                        0 => k1,  // Euler: use k1 for all
                        1 or 2 or 3 => InterpolateVectorField(vectorField, gridPoints, current + (stepSize * FieldsConfig.RK4HalfSteps[0] * k1)),
                        _ => k1,
                    };

                    Vector3d k3 = integrationMethod switch {
                        0 or 1 => k1,  // Euler/RK2: not used
                        2 or 3 => InterpolateVectorField(vectorField, gridPoints, current + (stepSize * FieldsConfig.RK4HalfSteps[1] * k2)),  // RK4: use k2
                        _ => k1,
                    };

                    Vector3d k4 = integrationMethod switch {
                        0 or 1 => k1,  // Euler/RK2: not used
                        2 or 3 => InterpolateVectorField(vectorField, gridPoints, current + (stepSize * FieldsConfig.RK4HalfSteps[2] * k3)),  // RK4: use k3
                        _ => k1,
                    };

                    Vector3d delta = integrationMethod switch {
                        0 => stepSize * k1,  // Euler
                        1 => stepSize * ((FieldsConfig.RK4Weights[0] * k1) + (FieldsConfig.RK4Weights[1] * k2)),  // RK2
                        2 or 3 => stepSize * ((FieldsConfig.RK4Weights[0] * k1) + (FieldsConfig.RK4Weights[1] * k2) + (FieldsConfig.RK4Weights[2] * k3) + (FieldsConfig.RK4Weights[3] * k4)),  // RK4
                        _ => Vector3d.Zero,
                    };

                    bool shouldContinue = delta.Length > context.AbsoluteTolerance && stepCount < FieldsConfig.MaxStreamlineSteps - 1;
                    _ = shouldContinue switch {
                        false => 0,  // Stop iteration
                        true => ((Func<int>)(() => {
                            current += delta;
                            pathBuffer[stepCount] = current;
                            stepCount++;
                            return 1;
                        }))(),
                    };

                    // Early exit when converged
                    step = shouldContinue ? step : FieldsConfig.MaxStreamlineSteps;
                }

                streamlines[seedIdx] = stepCount > 1
                    ? Curve.CreateInterpolatedCurve([.. pathBuffer[..stepCount]], degree: 3)
                    : new LineCurve(seeds[seedIdx], seeds[seedIdx] + new Vector3d(context.AbsoluteTolerance, 0, 0));
            }

            return ResultFactory.Create(value: streamlines);
        } finally {
            ArrayPool<Point3d>.Shared.Return(pathBuffer, clearArray: true);
        }
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3d InterpolateVectorField(Vector3d[] field, Point3d[] grid, Point3d query) {
        // Nearest neighbor interpolation (inline - NO helper method)
        double minDist = double.MaxValue;
        int nearestIdx = 0;
        for (int i = 0; i < grid.Length; i++) {
            double dist = query.DistanceTo(grid[i]);
            (nearestIdx, minDist) = dist < minDist ? (i, dist) : (nearestIdx, minDist);
        }
        return field[nearestIdx];
    }

    // ============================================================================
    // ISOSURFACE EXTRACTION (marching cubes with 256-case lookup)
    // ============================================================================

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Mesh[]> ExtractIsosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        int resolution,
        double[] isovalues) {
        Mesh[] meshes = new Mesh[isovalues.Length];

        for (int isoIdx = 0; isoIdx < isovalues.Length; isoIdx++) {
            double isovalue = isovalues[isoIdx];
            List<Point3f> vertices = [];
            List<MeshFace> faces = [];

            // March through each cube in the grid
            for (int i = 0; i < resolution - 1; i++) {
                for (int j = 0; j < resolution - 1; j++) {
                    for (int k = 0; k < resolution - 1; k++) {
                        // Get 8 corner indices for this cube
                        int[] cornerIndices = [
                            (i * resolution * resolution) + (j * resolution) + k,  // 0
                            ((i + 1) * resolution * resolution) + (j * resolution) + k,  // 1
                            ((i + 1) * resolution * resolution) + ((j + 1) * resolution) + k,  // 2
                            (i * resolution * resolution) + ((j + 1) * resolution) + k,  // 3
                            (i * resolution * resolution) + (j * resolution) + (k + 1),  // 4
                            ((i + 1) * resolution * resolution) + (j * resolution) + (k + 1),  // 5
                            ((i + 1) * resolution * resolution) + ((j + 1) * resolution) + (k + 1),  // 6
                            (i * resolution * resolution) + ((j + 1) * resolution) + (k + 1),  // 7
                        ];

                        // Determine cube configuration (8 bits for 8 corners)
                        int cubeIndex = 0;
                        for (int c = 0; c < 8; c++) {
                            cubeIndex = scalarField[cornerIndices[c]] < isovalue ? cubeIndex | (1 << c) : cubeIndex;
                        }

                        // Get triangle configuration from lookup table
                        int[] triangleEdges = FieldsConfig.MarchingCubesTable[cubeIndex];

                        // Process cube using pattern matching (NO if statement)
                        _ = triangleEdges.Length switch {
                            0 => 0,  // No triangles - skip this cube
                            _ => ((Func<int>)(() => {
                                Point3f[] edgeVertices = new Point3f[12];

                                // Generate vertices by linear interpolation on edges
                                for (int e = 0; e < triangleEdges.Length; e++) {
                                    int edgeIdx = triangleEdges[e];
                                    (int v1, int v2) = FieldsConfig.EdgeVertexPairs[edgeIdx];
                                    double f1 = scalarField[cornerIndices[v1]];
                                    double f2 = scalarField[cornerIndices[v2]];
                                    double t = Math.Abs(f2 - f1) > RhinoMath.ZeroTolerance
                                        ? (isovalue - f1) / (f2 - f1)
                                        : 0.5;
                                    Point3d p1 = gridPoints[cornerIndices[v1]];
                                    Point3d p2 = gridPoints[cornerIndices[v2]];
                                    edgeVertices[edgeIdx] = new Point3f((float)(p1.X + (t * (p2.X - p1.X))), (float)(p1.Y + (t * (p2.Y - p1.Y))), (float)(p1.Z + (t * (p2.Z - p1.Z))));
                                }

                                // Add triangles (every 3 edge indices form a triangle)
                                for (int t = 0; t < triangleEdges.Length; t += 3) {
                                    int vIdx = vertices.Count;
                                    vertices.Add(edgeVertices[triangleEdges[t]]);
                                    vertices.Add(edgeVertices[triangleEdges[t + 1]]);
                                    vertices.Add(edgeVertices[triangleEdges[t + 2]]);
                                    faces.Add(new MeshFace(vIdx, vIdx + 1, vIdx + 2));
                                }
                                return 1;
                            }))(),
                        };
                    }
                }
            }

            Mesh mesh = new();
            mesh.Vertices.AddVertices(vertices);
            _ = mesh.Faces.AddFaces(faces);
            _ = mesh.Normals.ComputeNormals();
            _ = mesh.Compact();
            meshes[isoIdx] = mesh;
        }

        return ResultFactory.Create(value: meshes);
    }
}

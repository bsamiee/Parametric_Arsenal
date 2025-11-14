using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Dense field algorithms: gradient, curl, divergence, laplacian, vector potential, interpolation, streamline, isosurface.</summary>
[Pure]
internal static class FieldsCompute {
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

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Curl)> ComputeCurl(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        return (vectorField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidCurlComputation.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidCurlComputation.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                int totalSamples = vectorField.Length;
                Vector3d[] curl = ArrayPool<Vector3d>.Shared.Rent(totalSamples);
                try {
                    double dx = gridDelta.X;
                    double dy = gridDelta.Y;
                    double dz = gridDelta.Z;
                    double twoDx = 2.0 * dx;
                    double twoDy = 2.0 * dy;
                    double twoDz = 2.0 * dz;

                    int resolutionSquared = resolution * resolution;
                    for (int i = 0; i < resolution; i++) {
                        int baseI = i * resolutionSquared;
                        for (int j = 0; j < resolution; j++) {
                            int baseJ = baseI + (j * resolution);
                            for (int k = 0; k < resolution; k++) {
                                int idx = baseJ + k;

                                double dFz_dy = (j < resolution - 1 && j > 0) ? (vectorField[baseI + ((j + 1) * resolution) + k].Z - vectorField[baseI + ((j - 1) * resolution) + k].Z) / twoDy : 0.0;
                                double dFy_dz = (k < resolution - 1 && k > 0) ? (vectorField[baseJ + (k + 1)].Y - vectorField[baseJ + (k - 1)].Y) / twoDz : 0.0;

                                double dFx_dz = (k < resolution - 1 && k > 0) ? (vectorField[baseJ + (k + 1)].X - vectorField[baseJ + (k - 1)].X) / twoDz : 0.0;
                                double dFz_dx = (i < resolution - 1 && i > 0) ? (vectorField[((i + 1) * resolutionSquared) + (j * resolution) + k].Z - vectorField[((i - 1) * resolutionSquared) + (j * resolution) + k].Z) / twoDx : 0.0;

                                double dFy_dx = (i < resolution - 1 && i > 0) ? (vectorField[((i + 1) * resolutionSquared) + (j * resolution) + k].Y - vectorField[((i - 1) * resolutionSquared) + (j * resolution) + k].Y) / twoDx : 0.0;
                                double dFx_dy = (j < resolution - 1 && j > 0) ? (vectorField[baseI + ((j + 1) * resolution) + k].X - vectorField[baseI + ((j - 1) * resolution) + k].X) / twoDy : 0.0;

                                curl[idx] = new Vector3d(dFz_dy - dFy_dz, dFx_dz - dFz_dx, dFy_dx - dFx_dy);
                            }
                        }
                    }

                    Vector3d[] finalCurl = [.. curl[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Curl: finalCurl));
                } finally {
                    ArrayPool<Vector3d>.Shared.Return(curl, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Divergence)> ComputeDivergence(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        return (vectorField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDivergenceComputation.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDivergenceComputation.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = vectorField.Length;
                double[] divergence = ArrayPool<double>.Shared.Rent(totalSamples);
                try {
                    double dx = gridDelta.X;
                    double dy = gridDelta.Y;
                    double dz = gridDelta.Z;
                    double twoDx = 2.0 * dx;
                    double twoDy = 2.0 * dy;
                    double twoDz = 2.0 * dz;

                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resolution * resolution) + (j * resolution) + k;

                                double dFx_dx = (i < resolution - 1 && i > 0) ? (vectorField[((i + 1) * resolution * resolution) + (j * resolution) + k].X - vectorField[((i - 1) * resolution * resolution) + (j * resolution) + k].X) / twoDx : 0.0;
                                double dFy_dy = (j < resolution - 1 && j > 0) ? (vectorField[(i * resolution * resolution) + ((j + 1) * resolution) + k].Y - vectorField[(i * resolution * resolution) + ((j - 1) * resolution) + k].Y) / twoDy : 0.0;
                                double dFz_dz = (k < resolution - 1 && k > 0) ? (vectorField[(i * resolution * resolution) + (j * resolution) + (k + 1)].Z - vectorField[(i * resolution * resolution) + (j * resolution) + (k - 1)].Z) / twoDz : 0.0;

                                divergence[idx] = dFx_dx + dFy_dy + dFz_dz;
                            }
                        }
                    }

                    double[] finalDivergence = [.. divergence[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Divergence: finalDivergence));
                } finally {
                    ArrayPool<double>.Shared.Return(divergence, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Laplacian)> ComputeLaplacian(
        double[] scalarField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        return (scalarField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidLaplacianComputation.WithContext("Scalar field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidLaplacianComputation.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = scalarField.Length;
                double[] laplacian = ArrayPool<double>.Shared.Rent(totalSamples);
                try {
                    double dx2 = gridDelta.X * gridDelta.X;
                    double dy2 = gridDelta.Y * gridDelta.Y;
                    double dz2 = gridDelta.Z * gridDelta.Z;

                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resolution * resolution) + (j * resolution) + k;

                                double d2f_dx2 = (i > 0 && i < resolution - 1) ? (scalarField[((i + 1) * resolution * resolution) + (j * resolution) + k] - (2.0 * scalarField[idx]) + scalarField[((i - 1) * resolution * resolution) + (j * resolution) + k]) / dx2 : 0.0;
                                double d2f_dy2 = (j > 0 && j < resolution - 1) ? (scalarField[(i * resolution * resolution) + ((j + 1) * resolution) + k] - (2.0 * scalarField[idx]) + scalarField[(i * resolution * resolution) + ((j - 1) * resolution) + k]) / dy2 : 0.0;
                                double d2f_dz2 = (k > 0 && k < resolution - 1) ? (scalarField[(i * resolution * resolution) + (j * resolution) + (k + 1)] - (2.0 * scalarField[idx]) + scalarField[(i * resolution * resolution) + (j * resolution) + (k - 1)]) / dz2 : 0.0;

                                laplacian[idx] = d2f_dx2 + d2f_dy2 + d2f_dz2;
                            }
                        }
                    }

                    double[] finalLaplacian = [.. laplacian[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Laplacian: finalLaplacian));
                } finally {
                    ArrayPool<double>.Shared.Return(laplacian, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Potential)> ComputeVectorPotential(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        return (vectorField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                int totalSamples = vectorField.Length;
                Vector3d[] potential = ArrayPool<Vector3d>.Shared.Rent(totalSamples);
                try {
                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resolution * resolution) + (j * resolution) + k;
                                potential[idx] = i > 0
                                    ? potential[((i - 1) * resolution * resolution) + (j * resolution) + k] + (gridDelta.X * vectorField[idx])
                                    : Vector3d.Zero;
                            }
                        }
                    }

                    Vector3d[] finalPotential = [.. potential[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Potential: finalPotential));
                } finally {
                    ArrayPool<Vector3d>.Shared.Return(potential, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] grid,
        int resolution,
        BoundingBox bounds,
        byte interpolationMethod) {
        return interpolationMethod switch {
            FieldsConfig.InterpolationNearest => InterpolateNearest(query, scalarField, grid),
            FieldsConfig.InterpolationTrilinear => InterpolateTrilinearScalar(query: query, scalarField: scalarField, resolution: resolution, bounds: bounds),
            _ => ResultFactory.Create<double>(error: E.Geometry.InvalidFieldInterpolation.WithContext($"Unsupported interpolation method: {interpolationMethod.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        BoundingBox bounds,
        byte interpolationMethod) {
        return interpolationMethod switch {
            FieldsConfig.InterpolationNearest => InterpolateNearest(query, vectorField, grid),
            FieldsConfig.InterpolationTrilinear => InterpolateTrilinearVector(query: query, vectorField: vectorField, resolution: resolution, bounds: bounds),
            _ => ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidFieldInterpolation.WithContext($"Unsupported interpolation method: {interpolationMethod.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> InterpolateNearest<T>(Point3d query, T[] field, Point3d[] grid) {
        int nearestIdx = grid.Length > FieldsConfig.FieldRTreeThreshold
            ? FindNearestRTree(query, grid)
            : FindNearestLinear(query, grid);
        return nearestIdx >= 0
            ? ResultFactory.Create(value: field[nearestIdx])
            : ResultFactory.Create<T>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Nearest neighbor search failed"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNearestLinear(Point3d query, Point3d[] grid) {
        double minDist = double.MaxValue;
        int nearestIdx = 0;
        for (int i = 0; i < grid.Length; i++) {
            double dist = query.DistanceTo(grid[i]);
            (nearestIdx, minDist) = dist < minDist ? (i, dist) : (nearestIdx, minDist);
        }
        return nearestIdx;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNearestRTree(Point3d query, Point3d[] grid) {
        using RTree tree = RTree.CreateFromPointArray(grid);
        int nearestIdx = -1;
        _ = tree.Search(new Sphere(query, radius: double.MaxValue), (sender, args) => nearestIdx = args.Id);
        return nearestIdx;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double> InterpolateTrilinearScalar(Point3d query, double[] scalarField, int resolution, BoundingBox bounds) {
        double dx = bounds.Max.X - bounds.Min.X;
        double dy = bounds.Max.Y - bounds.Min.Y;
        double dz = bounds.Max.Z - bounds.Min.Z;
        return (RhinoMath.EpsilonEquals(dx, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dy, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dz, 0.0, epsilon: RhinoMath.SqrtEpsilon)) switch {
            true => ResultFactory.Create<double>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Bounds have zero extent in one or more dimensions")),
            false => ((Func<Result<double>>)(() => {
                int resSquared = resolution * resolution;
                double normX = (query.X - bounds.Min.X) / dx;
                double normY = (query.Y - bounds.Min.Y) / dy;
                double normZ = (query.Z - bounds.Min.Z) / dz;
                double fi = RhinoMath.Clamp(normX * (resolution - 1), 0.0, resolution - 1);
                double fj = RhinoMath.Clamp(normY * (resolution - 1), 0.0, resolution - 1);
                double fk = RhinoMath.Clamp(normZ * (resolution - 1), 0.0, resolution - 1);

                int i0 = (int)fi;
                int j0 = (int)fj;
                int k0 = (int)fk;
                int i1 = i0 < resolution - 1 ? i0 + 1 : i0;
                int j1 = j0 < resolution - 1 ? j0 + 1 : j0;
                int k1 = k0 < resolution - 1 ? k0 + 1 : k0;

                double tx = fi - i0;
                double ty = fj - j0;
                double tz = fk - k0;

                double c000 = scalarField[(i0 * resSquared) + (j0 * resolution) + k0];
                double c001 = scalarField[(i0 * resSquared) + (j0 * resolution) + k1];
                double c010 = scalarField[(i0 * resSquared) + (j1 * resolution) + k0];
                double c011 = scalarField[(i0 * resSquared) + (j1 * resolution) + k1];
                double c100 = scalarField[(i1 * resSquared) + (j0 * resolution) + k0];
                double c101 = scalarField[(i1 * resSquared) + (j0 * resolution) + k1];
                double c110 = scalarField[(i1 * resSquared) + (j1 * resolution) + k0];
                double c111 = scalarField[(i1 * resSquared) + (j1 * resolution) + k1];

                double c00 = c000 + (tx * (c100 - c000));
                double c01 = c001 + (tx * (c101 - c001));
                double c10 = c010 + (tx * (c110 - c010));
                double c11 = c011 + (tx * (c111 - c011));

                double c0 = c00 + (ty * (c10 - c00));
                double c1 = c01 + (ty * (c11 - c01));

                return ResultFactory.Create(value: c0 + (tz * (c1 - c0)));
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Vector3d> InterpolateTrilinearVector(Point3d query, Vector3d[] vectorField, int resolution, BoundingBox bounds) {
        double dx = bounds.Max.X - bounds.Min.X;
        double dy = bounds.Max.Y - bounds.Min.Y;
        double dz = bounds.Max.Z - bounds.Min.Z;
        return (RhinoMath.EpsilonEquals(dx, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dy, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dz, 0.0, epsilon: RhinoMath.SqrtEpsilon)) switch {
            true => ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Bounds have zero extent in one or more dimensions")),
            false => ((Func<Result<Vector3d>>)(() => {
                int resSquared = resolution * resolution;
                double normX = (query.X - bounds.Min.X) / dx;
                double normY = (query.Y - bounds.Min.Y) / dy;
                double normZ = (query.Z - bounds.Min.Z) / dz;
                double fi = RhinoMath.Clamp(normX * (resolution - 1), 0.0, resolution - 1);
                double fj = RhinoMath.Clamp(normY * (resolution - 1), 0.0, resolution - 1);
                double fk = RhinoMath.Clamp(normZ * (resolution - 1), 0.0, resolution - 1);

                int i0 = (int)fi;
                int j0 = (int)fj;
                int k0 = (int)fk;
                int i1 = i0 < resolution - 1 ? i0 + 1 : i0;
                int j1 = j0 < resolution - 1 ? j0 + 1 : j0;
                int k1 = k0 < resolution - 1 ? k0 + 1 : k0;

                double tx = fi - i0;
                double ty = fj - j0;
                double tz = fk - k0;

                Vector3d c000 = vectorField[(i0 * resSquared) + (j0 * resolution) + k0];
                Vector3d c001 = vectorField[(i0 * resSquared) + (j0 * resolution) + k1];
                Vector3d c010 = vectorField[(i0 * resSquared) + (j1 * resolution) + k0];
                Vector3d c011 = vectorField[(i0 * resSquared) + (j1 * resolution) + k1];
                Vector3d c100 = vectorField[(i1 * resSquared) + (j0 * resolution) + k0];
                Vector3d c101 = vectorField[(i1 * resSquared) + (j0 * resolution) + k1];
                Vector3d c110 = vectorField[(i1 * resSquared) + (j1 * resolution) + k0];
                Vector3d c111 = vectorField[(i1 * resSquared) + (j1 * resolution) + k1];

                Vector3d c00 = c000 + (tx * (c100 - c000));
                Vector3d c01 = c001 + (tx * (c101 - c001));
                Vector3d c10 = c010 + (tx * (c110 - c010));
                Vector3d c11 = c011 + (tx * (c111 - c011));

                Vector3d c0 = c00 + (ty * (c10 - c00));
                Vector3d c1 = c01 + (ty * (c11 - c01));

                return ResultFactory.Create(value: c0 + (tz * (c1 - c0)));
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> IntegrateStreamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        double stepSize,
        byte integrationMethod,
        int resolution,
        BoundingBox bounds,
        IGeometryContext context) {
        Curve[] streamlines = new Curve[seeds.Length];
        Point3d[] pathBuffer = ArrayPool<Point3d>.Shared.Rent(FieldsConfig.MaxStreamlineSteps);

        using RTree? tree = gridPoints.Length > FieldsConfig.StreamlineRTreeThreshold
            ? RTree.CreateFromPointArray(gridPoints)
            : null;

        try {
            for (int seedIdx = 0; seedIdx < seeds.Length; seedIdx++) {
                Point3d current = seeds[seedIdx];
                int stepCount = 0;
                pathBuffer[stepCount++] = current;

                for (int step = 0; step < FieldsConfig.MaxStreamlineSteps - 1; step++) {
                    Vector3d k1 = InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current, tree: tree, resolution: resolution, bounds: bounds);

                    Vector3d k2 = integrationMethod switch {
                        0 => k1,
                        1 or 2 or 3 => InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current + (stepSize * FieldsConfig.RK4HalfSteps[0] * k1), tree: tree, resolution: resolution, bounds: bounds),
                        _ => k1,
                    };

                    Vector3d k3 = integrationMethod switch {
                        0 or 1 => k1,
                        2 or 3 => InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current + (stepSize * FieldsConfig.RK4HalfSteps[1] * k2), tree: tree, resolution: resolution, bounds: bounds),
                        _ => k1,
                    };

                    Vector3d k4 = integrationMethod switch {
                        0 or 1 => k1,
                        2 or 3 => InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current + (stepSize * FieldsConfig.RK4HalfSteps[2] * k3), tree: tree, resolution: resolution, bounds: bounds),
                        _ => k1,
                    };

                    Vector3d delta = integrationMethod switch {
                        0 => stepSize * k1,
                        1 => stepSize * ((FieldsConfig.RK4Weights[0] * k1) + (FieldsConfig.RK4Weights[1] * k2)),
                        2 or 3 => stepSize * ((FieldsConfig.RK4Weights[0] * k1) + (FieldsConfig.RK4Weights[1] * k2) + (FieldsConfig.RK4Weights[2] * k3) + (FieldsConfig.RK4Weights[3] * k4)),
                        _ => Vector3d.Zero,
                    };

                    bool shouldContinue = delta.Length > context.AbsoluteTolerance && stepCount < FieldsConfig.MaxStreamlineSteps - 1;
                    _ = shouldContinue switch {
                        false => 0,
                        true => ((Func<int>)(() => {
                            current += delta;
                            pathBuffer[stepCount] = current;
                            stepCount++;
                            return 1;
                        }))(),
                    };

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
    private static Vector3d InterpolateVectorFieldInternal(Vector3d[] vectorField, Point3d[] gridPoints, Point3d query, RTree? tree, int resolution, BoundingBox bounds) =>
        tree is not null
            ? InterpolateTrilinearVector(query: query, vectorField: vectorField, resolution: resolution, bounds: bounds).Match(onSuccess: v => v, onFailure: _ => Vector3d.Zero)
            : InterpolateNearest(query, vectorField, gridPoints).Match(onSuccess: v => v, onFailure: _ => Vector3d.Zero);

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

            for (int i = 0; i < resolution - 1; i++) {
                for (int j = 0; j < resolution - 1; j++) {
                    for (int k = 0; k < resolution - 1; k++) {
                        int[] cornerIndices = [
                            (i * resolution * resolution) + (j * resolution) + k,
                            ((i + 1) * resolution * resolution) + (j * resolution) + k,
                            ((i + 1) * resolution * resolution) + ((j + 1) * resolution) + k,
                            (i * resolution * resolution) + ((j + 1) * resolution) + k,
                            (i * resolution * resolution) + (j * resolution) + (k + 1),
                            ((i + 1) * resolution * resolution) + (j * resolution) + (k + 1),
                            ((i + 1) * resolution * resolution) + ((j + 1) * resolution) + (k + 1),
                            (i * resolution * resolution) + ((j + 1) * resolution) + (k + 1),
                        ];

                        int cubeIndex = 0;
                        for (int c = 0; c < 8; c++) {
                            cubeIndex = scalarField[cornerIndices[c]] < isovalue ? cubeIndex | (1 << c) : cubeIndex;
                        }

                        int[] triangleEdges = FieldsConfig.MarchingCubesTable[cubeIndex];

                        _ = triangleEdges.Length switch {
                            0 => 0,
                            _ => ((Func<int>)(() => {
                                Point3f[] edgeVertices = new Point3f[12];

                                for (int e = 0; e < triangleEdges.Length; e++) {
                                    int edgeIdx = triangleEdges[e];
                                    (int v1, int v2) = FieldsConfig.EdgeVertexPairs[edgeIdx];
                                    double f1 = scalarField[cornerIndices[v1]];
                                    double f2 = scalarField[cornerIndices[v2]];
                                    double t = RhinoMath.EpsilonEquals(f2, f1, epsilon: RhinoMath.ZeroTolerance) ? 0.5 : (isovalue - f1) / (f2 - f1);
                                    Point3d p1 = gridPoints[cornerIndices[v1]];
                                    Point3d p2 = gridPoints[cornerIndices[v2]];
                                    edgeVertices[edgeIdx] = new Point3f((float)(p1.X + (t * (p2.X - p1.X))), (float)(p1.Y + (t * (p2.Y - p1.Y))), (float)(p1.Z + (t * (p2.Z - p1.Z))));
                                }

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

using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Dense field computation algorithms for gradient, curl, divergence, Laplacian, Hessian, vector potential, interpolation, streamlines, isosurfaces, critical points, and statistics.</summary>
[Pure]
internal static class FieldsCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Gradients)> ComputeGradient(
        double[] distances,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) =>
        ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
            int totalSamples = distances.Length;
            Vector3d[] gradients = ArrayPool<Vector3d>.Shared.Rent(totalSamples);
            try {
                (double dx, double dy, double dz, double twoDx, double twoDy, double twoDz) = (gridDelta.X, gridDelta.Y, gridDelta.Z, 2.0 * gridDelta.X, 2.0 * gridDelta.Y, 2.0 * gridDelta.Z);
                int resSquared = resolution * resolution;
                for (int i = 0; i < resolution; i++) {
                    for (int j = 0; j < resolution; j++) {
                        for (int k = 0; k < resolution; k++) {
                            int idx = (i * resSquared) + (j * resolution) + k;
                            double dfdx = (i, resolution) switch {
                                (var x, var r) when x > 0 && x < r - 1 => (distances[idx + resSquared] - distances[idx - resSquared]) / twoDx,
                                (0, > 1) => (distances[idx + resSquared] - distances[idx]) / dx,
                                (var x, var r) when x == r - 1 && r > 1 => (distances[idx] - distances[idx - resSquared]) / dx,
                                _ => 0.0,
                            };
                            double dfdy = (j, resolution) switch {
                                (var y, var r) when y > 0 && y < r - 1 => (distances[idx + resolution] - distances[idx - resolution]) / twoDy,
                                (0, > 1) => (distances[idx + resolution] - distances[idx]) / dy,
                                (var y, var r) when y == r - 1 && r > 1 => (distances[idx] - distances[idx - resolution]) / dy,
                                _ => 0.0,
                            };
                            double dfdz = (k, resolution) switch {
                                (var z, var r) when z > 0 && z < r - 1 => (distances[idx + 1] - distances[idx - 1]) / twoDz,
                                (0, > 1) => (distances[idx + 1] - distances[idx]) / dz,
                                (var z, var r) when z == r - 1 && r > 1 => (distances[idx] - distances[idx - 1]) / dz,
                                _ => 0.0,
                            };
                            gradients[idx] = new Vector3d(dfdx, dfdy, dfdz);
                        }
                    }
                }
                Vector3d[] finalGradients = [.. gradients[..totalSamples]];
                return ResultFactory.Create(value: (Grid: grid, Gradients: finalGradients));
            } finally {
                ArrayPool<Vector3d>.Shared.Return(gradients, clearArray: true);
            }
        }))();

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
                    (double dx, double dy, double dz, double twoDx, double twoDy, double twoDz) = (gridDelta.X, gridDelta.Y, gridDelta.Z, 2.0 * gridDelta.X, 2.0 * gridDelta.Y, 2.0 * gridDelta.Z);
                    int resolutionSquared = resolution * resolution;
                    for (int i = 0; i < resolution; i++) {
                        int baseI = i * resolutionSquared;
                        for (int j = 0; j < resolution; j++) {
                            int baseJ = baseI + (j * resolution);
                            for (int k = 0; k < resolution; k++) {
                                int idx = baseJ + k;

                                double dFz_dy = (j < resolution - 1 && j > 0) ? (vectorField[idx + resolution].Z - vectorField[idx - resolution].Z) / twoDy : 0.0;
                                double dFy_dz = (k < resolution - 1 && k > 0) ? (vectorField[idx + 1].Y - vectorField[idx - 1].Y) / twoDz : 0.0;

                                double dFx_dz = (k < resolution - 1 && k > 0) ? (vectorField[idx + 1].X - vectorField[idx - 1].X) / twoDz : 0.0;
                                double dFz_dx = (i < resolution - 1 && i > 0) ? (vectorField[idx + resolutionSquared].Z - vectorField[idx - resolutionSquared].Z) / twoDx : 0.0;

                                double dFy_dx = (i < resolution - 1 && i > 0) ? (vectorField[idx + resolutionSquared].Y - vectorField[idx - resolutionSquared].Y) / twoDx : 0.0;
                                double dFx_dy = (j < resolution - 1 && j > 0) ? (vectorField[idx + resolution].X - vectorField[idx - resolution].X) / twoDy : 0.0;

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
                    (double dx, double dy, double dz, double twoDx, double twoDy, double twoDz) = (gridDelta.X, gridDelta.Y, gridDelta.Z, 2.0 * gridDelta.X, 2.0 * gridDelta.Y, 2.0 * gridDelta.Z);
                    int resSquared = resolution * resolution;
                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resSquared) + (j * resolution) + k;

                                double dFx_dx = (i < resolution - 1 && i > 0) ? (vectorField[idx + resSquared].X - vectorField[idx - resSquared].X) / twoDx : 0.0;
                                double dFy_dy = (j < resolution - 1 && j > 0) ? (vectorField[idx + resolution].Y - vectorField[idx - resolution].Y) / twoDy : 0.0;
                                double dFz_dz = (k < resolution - 1 && k > 0) ? (vectorField[idx + 1].Z - vectorField[idx - 1].Z) / twoDz : 0.0;

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
                    int resSquared = resolution * resolution;

                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resSquared) + (j * resolution) + k;

                                double d2f_dx2 = (i > 0 && i < resolution - 1) ? (scalarField[idx + resSquared] - (2.0 * scalarField[idx]) + scalarField[idx - resSquared]) / dx2 : 0.0;
                                double d2f_dy2 = (j > 0 && j < resolution - 1) ? (scalarField[idx + resolution] - (2.0 * scalarField[idx]) + scalarField[idx - resolution]) / dy2 : 0.0;
                                double d2f_dz2 = (k > 0 && k < resolution - 1) ? (scalarField[idx + 1] - (2.0 * scalarField[idx]) + scalarField[idx - 1]) / dz2 : 0.0;

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
            ? (() => {
                using RTree tree = RTree.CreateFromPointArray(grid);
                int idx = -1;
                _ = tree.Search(new Sphere(query, radius: double.MaxValue), (sender, args) => idx = args.Id);
                return idx;
            })()
            : (() => {
                double minDist = double.MaxValue;
                int idx = 0;
                for (int i = 0; i < grid.Length; i++) {
                    double dist = query.DistanceTo(grid[i]);
                    (idx, minDist) = dist < minDist ? (i, dist) : (idx, minDist);
                }
                return idx;
            })();
        return nearestIdx >= 0
            ? ResultFactory.Create(value: field[nearestIdx])
            : ResultFactory.Create<T>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Nearest neighbor search failed"));
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
        IGeometryContext context) =>
        ((Func<Result<Curve[]>>)(() => {
            Curve[] streamlines = new Curve[seeds.Length];
            Point3d[] pathBuffer = ArrayPool<Point3d>.Shared.Rent(FieldsConfig.MaxStreamlineSteps);
            using RTree? tree = gridPoints.Length > FieldsConfig.StreamlineRTreeThreshold ? RTree.CreateFromPointArray(gridPoints) : null;
            try {
                for (int seedIdx = 0; seedIdx < seeds.Length; seedIdx++) {
                    Point3d current = seeds[seedIdx];
                    int stepCount = 0;
                    pathBuffer[stepCount++] = current;
                    for (int step = 0; step < FieldsConfig.MaxStreamlineSteps - 1; step++) {
                        Vector3d k1 = InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current, tree: tree, resolution: resolution, bounds: bounds);
                        double k1Magnitude = k1.Length;
                        Vector3d k2 = integrationMethod switch {
                            0 => k1,
                            1 => InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current + (stepSize * FieldsConfig.RK2HalfStep * k1), tree: tree, resolution: resolution, bounds: bounds),
                            2 or 3 => InterpolateVectorFieldInternal(vectorField: vectorField, gridPoints: gridPoints, query: current + (stepSize * FieldsConfig.RK4HalfSteps[0] * k1), tree: tree, resolution: resolution, bounds: bounds),
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
                        Point3d nextPoint = current + delta;
                        bool shouldContinue = bounds.Contains(nextPoint) && k1Magnitude >= FieldsConfig.MinFieldMagnitude && delta.Length >= RhinoMath.ZeroTolerance && stepCount < FieldsConfig.MaxStreamlineSteps - 1;
                        if (shouldContinue) {
                            current = nextPoint;
                            pathBuffer[stepCount++] = current;
                        }
                        step = shouldContinue ? step : FieldsConfig.MaxStreamlineSteps;
                    }
                    streamlines[seedIdx] = stepCount > 1 ? Curve.CreateInterpolatedCurve([.. pathBuffer[..stepCount]], degree: 3) : new LineCurve(seeds[seedIdx], seeds[seedIdx] + new Vector3d(context.AbsoluteTolerance, 0, 0));
                }
                return ResultFactory.Create(value: streamlines);
            } finally {
                ArrayPool<Point3d>.Shared.Return(pathBuffer, clearArray: true);
            }
        }))();

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
        double[] isovalues) =>
        ((Func<Result<Mesh[]>>)(() => {
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
                            if (triangleEdges.Length > 0) {
                                Point3f[] edgeVertices = new Point3f[12];
                                for (int e = 0; e < triangleEdges.Length; e++) {
                                    int edgeIdx = triangleEdges[e];
                                    (int v1, int v2) = FieldsConfig.EdgeVertexPairs[edgeIdx];
                                    (double f1, double f2) = (scalarField[cornerIndices[v1]], scalarField[cornerIndices[v2]]);
                                    double t = RhinoMath.EpsilonEquals(f2, f1, epsilon: RhinoMath.ZeroTolerance) ? 0.5 : (isovalue - f1) / (f2 - f1);
                                    (Point3d p1, Point3d p2) = (gridPoints[cornerIndices[v1]], gridPoints[cornerIndices[v2]]);
                                    edgeVertices[edgeIdx] = new Point3f((float)(p1.X + (t * (p2.X - p1.X))), (float)(p1.Y + (t * (p2.Y - p1.Y))), (float)(p1.Z + (t * (p2.Z - p1.Z))));
                                }
                                for (int t = 0; t < triangleEdges.Length; t += 3) {
                                    int vIdx = vertices.Count;
                                    vertices.Add(edgeVertices[triangleEdges[t]]);
                                    vertices.Add(edgeVertices[triangleEdges[t + 1]]);
                                    vertices.Add(edgeVertices[triangleEdges[t + 2]]);
                                    faces.Add(new MeshFace(vIdx, vIdx + 1, vIdx + 2));
                                }
                            }
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
        }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 symmetric matrix structure is mathematically clear and appropriate")]
    internal static Result<(Point3d[] Grid, double[,][] Hessian)> ComputeHessian(
        double[] scalarField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        return (scalarField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], double[,][])>(error: E.Geometry.InvalidHessianComputation.WithContext("Scalar field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], double[,][])>(error: E.Geometry.InvalidHessianComputation.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ((Func<Result<(Point3d[], double[,][])>>)(() => {
                int totalSamples = scalarField.Length;
                double[,][] hessian = new double[3, 3][];
                for (int row = 0; row < 3; row++) {
                    for (int col = 0; col < 3; col++) {
                        hessian[row, col] = ArrayPool<double>.Shared.Rent(totalSamples);
                    }
                }

                try {
                    double dx2 = gridDelta.X * gridDelta.X;
                    double dy2 = gridDelta.Y * gridDelta.Y;
                    double dz2 = gridDelta.Z * gridDelta.Z;
                    double dxdy = gridDelta.X * gridDelta.Y;
                    double dxdz = gridDelta.X * gridDelta.Z;
                    double dydz = gridDelta.Y * gridDelta.Z;
                    int resSquared = resolution * resolution;

                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resSquared) + (j * resolution) + k;
                                double center = scalarField[idx];

                                double d2f_dx2 = (i > 0 && i < resolution - 1)
                                    ? (scalarField[((i + 1) * resSquared) + (j * resolution) + k] - (2.0 * center) + scalarField[((i - 1) * resSquared) + (j * resolution) + k]) / dx2
                                    : 0.0;

                                double d2f_dy2 = (j > 0 && j < resolution - 1)
                                    ? (scalarField[(i * resSquared) + ((j + 1) * resolution) + k] - (2.0 * center) + scalarField[(i * resSquared) + ((j - 1) * resolution) + k]) / dy2
                                    : 0.0;

                                double d2f_dz2 = (k > 0 && k < resolution - 1)
                                    ? (scalarField[(i * resSquared) + (j * resolution) + (k + 1)] - (2.0 * center) + scalarField[(i * resSquared) + (j * resolution) + (k - 1)]) / dz2
                                    : 0.0;

                                double d2f_dxdy = (i > 0 && i < resolution - 1 && j > 0 && j < resolution - 1)
                                    ? (scalarField[((i + 1) * resSquared) + ((j + 1) * resolution) + k] - scalarField[((i + 1) * resSquared) + ((j - 1) * resolution) + k] - scalarField[((i - 1) * resSquared) + ((j + 1) * resolution) + k] + scalarField[((i - 1) * resSquared) + ((j - 1) * resolution) + k]) / (4.0 * dxdy)
                                    : 0.0;

                                double d2f_dxdz = (i > 0 && i < resolution - 1 && k > 0 && k < resolution - 1)
                                    ? (scalarField[((i + 1) * resSquared) + (j * resolution) + (k + 1)] - scalarField[((i + 1) * resSquared) + (j * resolution) + (k - 1)] - scalarField[((i - 1) * resSquared) + (j * resolution) + (k + 1)] + scalarField[((i - 1) * resSquared) + (j * resolution) + (k - 1)]) / (4.0 * dxdz)
                                    : 0.0;

                                double d2f_dydz = (j > 0 && j < resolution - 1 && k > 0 && k < resolution - 1)
                                    ? (scalarField[(i * resSquared) + ((j + 1) * resolution) + (k + 1)] - scalarField[(i * resSquared) + ((j + 1) * resolution) + (k - 1)] - scalarField[(i * resSquared) + ((j - 1) * resolution) + (k + 1)] + scalarField[(i * resSquared) + ((j - 1) * resolution) + (k - 1)]) / (4.0 * dydz)
                                    : 0.0;

                                hessian[0, 0][idx] = d2f_dx2;
                                hessian[1, 1][idx] = d2f_dy2;
                                hessian[2, 2][idx] = d2f_dz2;
                                hessian[0, 1][idx] = d2f_dxdy;
                                hessian[1, 0][idx] = d2f_dxdy;
                                hessian[0, 2][idx] = d2f_dxdz;
                                hessian[2, 0][idx] = d2f_dxdz;
                                hessian[1, 2][idx] = d2f_dydz;
                                hessian[2, 1][idx] = d2f_dydz;
                            }
                        }
                    }

                    double[,][] finalHessian = new double[3, 3][];
                    for (int row = 0; row < 3; row++) {
                        for (int col = 0; col < 3; col++) {
                            finalHessian[row, col] = [.. hessian[row, col][..totalSamples]];
                        }
                    }
                    return ResultFactory.Create(value: (Grid: grid, Hessian: finalHessian));
                } finally {
                    for (int row = 0; row < 3; row++) {
                        for (int col = 0; col < 3; col++) {
                            ArrayPool<double>.Shared.Return(hessian[row, col], clearArray: true);
                        }
                    }
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> ComputeDirectionalDerivative(
        Vector3d[] gradientField,
        Point3d[] grid,
        Vector3d direction) {
        return (gradientField.Length == grid.Length, direction.IsValid && direction.Length > RhinoMath.ZeroTolerance) switch {
            (false, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDirectionalDerivative.WithContext("Gradient field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDirectionalDerivative.WithContext("Direction vector must be valid and non-zero")),
            (true, true) => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = gradientField.Length;
                double[] directionalDerivatives = ArrayPool<double>.Shared.Rent(totalSamples);
                Vector3d unitDirection = direction / direction.Length;

                try {
                    for (int i = 0; i < totalSamples; i++) {
                        directionalDerivatives[i] = Vector3d.Multiply(gradientField[i], unitDirection);
                    }

                    double[] finalDerivatives = [.. directionalDerivatives[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, DirectionalDerivatives: finalDerivatives));
                } finally {
                    ArrayPool<double>.Shared.Return(directionalDerivatives, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Magnitudes)> ComputeFieldMagnitude(
        Vector3d[] vectorField,
        Point3d[] grid) {
        return (vectorField.Length == grid.Length) switch {
            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldMagnitude.WithContext("Vector field length must match grid points")),
            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = vectorField.Length;
                double[] magnitudes = ArrayPool<double>.Shared.Rent(totalSamples);

                try {
                    for (int i = 0; i < totalSamples; i++) {
                        magnitudes[i] = vectorField[i].Length;
                    }

                    double[] finalMagnitudes = [.. magnitudes[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Magnitudes: finalMagnitudes));
                } finally {
                    ArrayPool<double>.Shared.Return(magnitudes, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Normalized)> NormalizeVectorField(
        Vector3d[] vectorField,
        Point3d[] grid) {
        return (vectorField.Length == grid.Length) switch {
            false => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidFieldNormalization.WithContext("Vector field length must match grid points")),
            true => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                int totalSamples = vectorField.Length;
                Vector3d[] normalized = ArrayPool<Vector3d>.Shared.Rent(totalSamples);

                try {
                    for (int i = 0; i < totalSamples; i++) {
                        double magnitude = vectorField[i].Length;
                        normalized[i] = magnitude > FieldsConfig.MinFieldMagnitude
                            ? vectorField[i] / magnitude
                            : Vector3d.Zero;
                    }

                    Vector3d[] finalNormalized = [.. normalized[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Normalized: finalNormalized));
                } finally {
                    ArrayPool<Vector3d>.Shared.Return(normalized, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] grid,
        int component) {
        return (scalarField.Length == grid.Length, vectorField.Length == grid.Length, component is >= 0 and <= 2) switch {
            (false, _, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition.WithContext("Vector field length must match grid points")),
            (_, _, false) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition.WithContext("Component must be 0 (X), 1 (Y), or 2 (Z)")),
            (true, true, true) => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = scalarField.Length;
                double[] product = ArrayPool<double>.Shared.Rent(totalSamples);

                try {
                    for (int i = 0; i < totalSamples; i++) {
                        product[i] = component switch {
                            0 => scalarField[i] * vectorField[i].X,
                            1 => scalarField[i] * vectorField[i].Y,
                            2 => scalarField[i] * vectorField[i].Z,
                            _ => 0.0,
                        };
                    }

                    double[] finalProduct = [.. product[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Product: finalProduct));
                } finally {
                    ArrayPool<double>.Shared.Return(product, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DotProduct)> VectorDotProduct(
        Vector3d[] vectorField1,
        Vector3d[] vectorField2,
        Point3d[] grid) {
        return (vectorField1.Length == grid.Length, vectorField2.Length == grid.Length) switch {
            (false, _) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition.WithContext("First vector field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidFieldComposition.WithContext("Second vector field length must match grid points")),
            (true, true) => ((Func<Result<(Point3d[], double[])>>)(() => {
                int totalSamples = vectorField1.Length;
                double[] dotProduct = ArrayPool<double>.Shared.Rent(totalSamples);

                try {
                    for (int i = 0; i < totalSamples; i++) {
                        dotProduct[i] = Vector3d.Multiply(vectorField1[i], vectorField2[i]);
                    }

                    double[] finalDotProduct = [.. dotProduct[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, DotProduct: finalDotProduct));
                } finally {
                    ArrayPool<double>.Shared.Return(dotProduct, clearArray: true);
                }
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 Hessian matrix parameter is mathematically appropriate")]
    internal static Result<Fields.CriticalPoint[]> DetectCriticalPoints(
        double[] scalarField,
        Vector3d[] gradientField,
        double[,][] hessian,
        Point3d[] grid,
        int resolution) {
        return (scalarField.Length == grid.Length, gradientField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _, _) => ResultFactory.Create<Fields.CriticalPoint[]>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<Fields.CriticalPoint[]>(error: E.Geometry.InvalidCriticalPointDetection.WithContext("Gradient field length must match grid points")),
            (_, _, false) => ResultFactory.Create<Fields.CriticalPoint[]>(error: E.Geometry.InvalidCriticalPointDetection.WithContext($"Resolution {resolution.ToString(System.Globalization.CultureInfo.InvariantCulture)} below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true, true) => ((Func<Result<Fields.CriticalPoint[]>>)(() => {
                List<Fields.CriticalPoint> criticalPoints = [];
                int resSquared = resolution * resolution;

                for (int i = 1; i < resolution - 1; i++) {
                    for (int j = 1; j < resolution - 1; j++) {
                        for (int k = 1; k < resolution - 1; k++) {
                            int idx = (i * resSquared) + (j * resolution) + k;

                            if (gradientField[idx].Length < FieldsConfig.MinFieldMagnitude) {
                                double[,] localHessian = new double[3, 3];
                                for (int row = 0; row < 3; row++) {
                                    for (int col = 0; col < 3; col++) {
                                        localHessian[row, col] = hessian[row, col][idx];
                                    }
                                }

                                (double[] eigenvalues, Vector3d[] eigenvectors) = ComputeEigendecomposition3x3(localHessian);
                                int positiveCount = eigenvalues.Count(ev => ev > FieldsConfig.EigenvalueThreshold);
                                int negativeCount = eigenvalues.Count(ev => ev < -FieldsConfig.EigenvalueThreshold);
                                byte criticalType = (positiveCount, negativeCount) switch {
                                    (3, 0) => FieldsConfig.CriticalPointMinimum,
                                    (0, 3) => FieldsConfig.CriticalPointMaximum,
                                    _ => FieldsConfig.CriticalPointSaddle,
                                };

                                criticalPoints.Add(new Fields.CriticalPoint(
                                    Location: grid[idx],
                                    Type: criticalType,
                                    Value: scalarField[idx],
                                    Eigenvectors: eigenvectors,
                                    Eigenvalues: eigenvalues));
                            }
                        }
                    }
                }

                Fields.CriticalPoint[] result = [.. criticalPoints];
                return ResultFactory.Create(value: result);
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 matrix parameter is mathematically appropriate for eigendecomposition")]
    private static (double[] Eigenvalues, Vector3d[] Eigenvectors) ComputeEigendecomposition3x3(double[,] matrix) {
        double a = matrix[0, 0];
        double b = matrix[0, 1];
        double c = matrix[0, 2];
        double d = matrix[1, 1];
        double e = matrix[1, 2];
        double f = matrix[2, 2];

        double p1 = (b * b) + (c * c) + (e * e);
        bool isDiagonal = p1 < RhinoMath.SqrtEpsilon;

        return isDiagonal switch {
            true => ([a, d, f,], [new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),]),
            false => ((Func<(double[], Vector3d[])>)(() => {
                double trace = a + d + f;
                double p = ((b * b) + (c * c) + (e * e)) + ((((a - d) * (a - d)) + ((a - f) * (a - f)) + ((d - f) * (d - f))) / 6.0);
                double q = ((a - (trace / 3.0)) * (((d - (trace / 3.0)) * (f - (trace / 3.0))) - (e * e))) - (b * ((b * (f - (trace / 3.0))) - (c * e))) + (c * ((b * e) - (c * (d - (trace / 3.0)))));
                double phi = q / (2.0 * Math.Pow(p, 1.5)) switch {
                    double r when r <= -1.0 => Math.PI / 3.0,
                    double r when r >= 1.0 => 0.0,
                    double r => Math.Acos(r) / 3.0,
                };

                double sqrtP = Math.Sqrt(p);
                double lambda1 = (trace / 3.0) + (2.0 * sqrtP * Math.Cos(phi));
                double lambda2 = (trace / 3.0) - (sqrtP * (Math.Cos(phi) + (Math.Sqrt(3.0) * Math.Sin(phi))));
                double lambda3 = (trace / 3.0) - (sqrtP * (Math.Cos(phi) - (Math.Sqrt(3.0) * Math.Sin(phi))));

                Vector3d v1 = ComputeEigenvector3x3(matrix, lambda1);
                Vector3d v2 = ComputeEigenvector3x3(matrix, lambda2);
                Vector3d v3 = ComputeEigenvector3x3(matrix, lambda3);

                return ([lambda1, lambda2, lambda3,], [v1, v2, v3,]);
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "3x3 matrix parameter is mathematically appropriate")]
    private static Vector3d ComputeEigenvector3x3(double[,] matrix, double eigenvalue) {
        (double a, double b, double c, double d, double e, double f) = (matrix[0, 0] - eigenvalue, matrix[0, 1], matrix[0, 2], matrix[1, 1] - eigenvalue, matrix[1, 2], matrix[2, 2] - eigenvalue);
        (Vector3d cross1, Vector3d cross2, Vector3d cross3) = (
            new Vector3d((b * e) - (c * d), (c * b) - (a * e), (a * d) - (b * b)),
            new Vector3d((b * f) - (c * e), (c * c) - (a * f), (a * e) - (b * c)),
            new Vector3d((d * f) - (e * e), (e * c) - (b * f), (b * e) - (c * d)));
        Vector3d result = cross1.Length > cross2.Length ? (cross1.Length > cross3.Length ? cross1 : cross3) : (cross2.Length > cross3.Length ? cross2 : cross3);
        return result.Length > RhinoMath.ZeroTolerance ? result / result.Length : new Vector3d(1, 0, 0);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fields.FieldStatistics> ComputeFieldStatistics(
        double[] scalarField,
        Point3d[] grid) {
        return (scalarField.Length == grid.Length, scalarField.Length > 0) switch {
            (false, _) => ResultFactory.Create<Fields.FieldStatistics>(error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field length must match grid points")),
            (_, false) => ResultFactory.Create<Fields.FieldStatistics>(error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field must not be empty")),
            (true, true) => ((Func<Result<Fields.FieldStatistics>>)(() => {
                double min = double.MaxValue;
                double max = double.MinValue;
                double sum = 0.0;
                int minIdx = 0;
                int maxIdx = 0;
                int validCount = 0;

                for (int i = 0; i < scalarField.Length; i++) {
                    double value = scalarField[i];
                    if (RhinoMath.IsValidDouble(value)) {
                        sum += value;
                        validCount++;
                        (min, minIdx) = value < min ? (value, i) : (min, minIdx);
                        (max, maxIdx) = value > max ? (value, i) : (max, maxIdx);
                    }
                }

                double mean = validCount > 0 ? sum / validCount : 0.0;
                double sumSquaredDiff = 0.0;
                for (int i = 0; i < scalarField.Length; i++) {
                    double value = scalarField[i];
                    if (RhinoMath.IsValidDouble(value)) {
                        double diff = value - mean;
                        sumSquaredDiff += diff * diff;
                    }
                }
                double stdDev = validCount > 0 ? Math.Sqrt(sumSquaredDiff / validCount) : 0.0;

                return ResultFactory.Create(value: new Fields.FieldStatistics(
                    Min: min,
                    Max: max,
                    Mean: mean,
                    StdDev: stdDev,
                    MinLocation: grid[minIdx],
                    MaxLocation: grid[maxIdx]));
            }))(),
        };
    }
}

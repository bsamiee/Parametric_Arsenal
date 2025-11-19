using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Dense field computation algorithms: gradient, curl, divergence, Laplacian, Hessian, vector potential, interpolation, streamlines, isosurfaces, critical points, statistics.</summary>
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
                (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
                (double invDx, double invDy, double invDz) = (hasDx ? 1.0 / gridDelta.X : 0.0, hasDy ? 1.0 / gridDelta.Y : 0.0, hasDz ? 1.0 / gridDelta.Z : 0.0);
                (double invTwoDx, double invTwoDy, double invTwoDz) = (hasDx ? 0.5 * invDx : 0.0, hasDy ? 0.5 * invDy : 0.0, hasDz ? 0.5 * invDz : 0.0);
                int resSquared = resolution * resolution;
                for (int i = 0; i < resolution; i++) {
                    for (int j = 0; j < resolution; j++) {
                        for (int k = 0; k < resolution; k++) {
                            int idx = (i * resSquared) + (j * resolution) + k;
                            double dfdx = hasDx switch {
                                false => 0.0,
                                true => (i, resolution) switch {
                                    (var x, var r) when x > 0 && x < r - 1 => (distances[idx + resSquared] - distances[idx - resSquared]) * invTwoDx,
                                    (0, > 1) => (distances[idx + resSquared] - distances[idx]) * invDx,
                                    (var x, var r) when x == r - 1 && r > 1 => (distances[idx] - distances[idx - resSquared]) * invDx,
                                    _ => 0.0,
                                },
                            };
                            double dfdy = hasDy switch {
                                false => 0.0,
                                true => (j, resolution) switch {
                                    (var y, var r) when y > 0 && y < r - 1 => (distances[idx + resolution] - distances[idx - resolution]) * invTwoDy,
                                    (0, > 1) => (distances[idx + resolution] - distances[idx]) * invDy,
                                    (var y, var r) when y == r - 1 && r > 1 => (distances[idx] - distances[idx - resolution]) * invDy,
                                    _ => 0.0,
                                },
                            };
                            double dfdz = hasDz switch {
                                false => 0.0,
                                true => (k, resolution) switch {
                                    (var z, var r) when z > 0 && z < r - 1 => (distances[idx + 1] - distances[idx - 1]) * invTwoDz,
                                    (0, > 1) => (distances[idx + 1] - distances[idx]) * invDz,
                                    (var z, var r) when z == r - 1 && r > 1 => (distances[idx] - distances[idx - 1]) * invDz,
                                    _ => 0.0,
                                },
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
    private static Result<(Point3d[] Grid, TOut[] Output)> ComputeDerivativeField<TIn, TOut>(
        TIn[] inputField,
        Point3d[] grid,
        int resolution,
        Func<TIn[], int, int, int, int, int, int, TOut> stencilOp,
        SystemError lengthError,
        SystemError resolutionError) where TIn : struct where TOut : struct =>
        (inputField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], TOut[])>(error: lengthError),
            (_, false) => ResultFactory.Create<(Point3d[], TOut[])>(error: resolutionError),
            (true, true) => ((Func<Result<(Point3d[], TOut[])>>)(() => {
                int totalSamples = inputField.Length;
                TOut[] output = ArrayPool<TOut>.Shared.Rent(totalSamples);
                try {
                    int resSquared = resolution * resolution;
                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resSquared) + (j * resolution) + k;
                                output[idx] = stencilOp(inputField, idx, i, j, k, resolution, resSquared);
                            }
                        }
                    }
                    TOut[] finalOutput = [.. output[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Output: finalOutput));
                } finally {
                    ArrayPool<TOut>.Shared.Return(output, clearArray: true);
                }
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Curl)> ComputeCurl(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
        (double invTwoDx, double invTwoDy, double invTwoDz) = (hasDx ? 0.5 / gridDelta.X : 0.0, hasDy ? 0.5 / gridDelta.Y : 0.0, hasDz ? 0.5 / gridDelta.Z : 0.0);
        return ComputeDerivativeField(
            inputField: vectorField,
            grid: grid,
            resolution: resolution,
            stencilOp: (field, idx, i, j, k, res, resSquared) => {
                double dFz_dy = hasDy && j > 0 && j < res - 1 ? (field[idx + res].Z - field[idx - res].Z) * invTwoDy : 0.0;
                double dFy_dz = hasDz && k > 0 && k < res - 1 ? (field[idx + 1].Y - field[idx - 1].Y) * invTwoDz : 0.0;
                double dFx_dz = hasDz && k > 0 && k < res - 1 ? (field[idx + 1].X - field[idx - 1].X) * invTwoDz : 0.0;
                double dFz_dx = hasDx && i > 0 && i < res - 1 ? (field[idx + resSquared].Z - field[idx - resSquared].Z) * invTwoDx : 0.0;
                double dFy_dx = hasDx && i > 0 && i < res - 1 ? (field[idx + resSquared].Y - field[idx - resSquared].Y) * invTwoDx : 0.0;
                double dFx_dy = hasDy && j > 0 && j < res - 1 ? (field[idx + res].X - field[idx - res].X) * invTwoDy : 0.0;
                return new Vector3d(dFz_dy - dFy_dz, dFx_dz - dFz_dx, dFy_dx - dFx_dy);
            },
            lengthError: E.Geometry.InvalidCurlComputation.WithContext("Vector field length must match grid points"),
            resolutionError: E.Geometry.InvalidCurlComputation.WithContext($"Resolution below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Divergence)> ComputeDivergence(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
        (double invTwoDx, double invTwoDy, double invTwoDz) = (hasDx ? 0.5 / gridDelta.X : 0.0, hasDy ? 0.5 / gridDelta.Y : 0.0, hasDz ? 0.5 / gridDelta.Z : 0.0);
        return ComputeDerivativeField(
            inputField: vectorField,
            grid: grid,
            resolution: resolution,
            stencilOp: (field, idx, i, j, k, res, resSquared) => {
                double dFx_dx = hasDx && i > 0 && i < res - 1 ? (field[idx + resSquared].X - field[idx - resSquared].X) * invTwoDx : 0.0;
                double dFy_dy = hasDy && j > 0 && j < res - 1 ? (field[idx + res].Y - field[idx - res].Y) * invTwoDy : 0.0;
                double dFz_dz = hasDz && k > 0 && k < res - 1 ? (field[idx + 1].Z - field[idx - 1].Z) * invTwoDz : 0.0;
                return dFx_dx + dFy_dy + dFz_dz;
            },
            lengthError: E.Geometry.InvalidDivergenceComputation.WithContext("Vector field length must match grid points"),
            resolutionError: E.Geometry.InvalidDivergenceComputation.WithContext($"Resolution below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Laplacian)> ComputeLaplacian(
        double[] scalarField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) {
        (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
        (double invDx2, double invDy2, double invDz2) = (hasDx ? 1.0 / (gridDelta.X * gridDelta.X) : 0.0, hasDy ? 1.0 / (gridDelta.Y * gridDelta.Y) : 0.0, hasDz ? 1.0 / (gridDelta.Z * gridDelta.Z) : 0.0);
        return ComputeDerivativeField(
            inputField: scalarField,
            grid: grid,
            resolution: resolution,
            stencilOp: (field, idx, i, j, k, res, resSquared) => {
                double d2f_dx2 = hasDx && i > 0 && i < res - 1 ? (field[idx + resSquared] - (2.0 * field[idx]) + field[idx - resSquared]) * invDx2 : 0.0;
                double d2f_dy2 = hasDy && j > 0 && j < res - 1 ? (field[idx + res] - (2.0 * field[idx]) + field[idx - res]) * invDy2 : 0.0;
                double d2f_dz2 = hasDz && k > 0 && k < res - 1 ? (field[idx + 1] - (2.0 * field[idx]) + field[idx - 1]) * invDz2 : 0.0;
                return d2f_dx2 + d2f_dy2 + d2f_dz2;
            },
            lengthError: E.Geometry.InvalidLaplacianComputation.WithContext("Scalar field length must match grid points"),
            resolutionError: E.Geometry.InvalidLaplacianComputation.WithContext($"Resolution below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Potential)> ComputeVectorPotential(
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        Vector3d gridDelta) =>
        (vectorField.Length == grid.Length, resolution >= FieldsConfig.MinResolution) switch {
            (false, _) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext($"Resolution below minimum {FieldsConfig.MinResolution.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (true, true) => ComputeCurl(
                vectorField: vectorField,
                grid: grid,
                resolution: resolution,
                gridDelta: gridDelta).Bind(curl => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                    (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
                    return (hasDx && hasDy && hasDz) switch {
                        false => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Grid bounds must have non-zero extent across X, Y, and Z")),
                        true => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                            int totalSamples = vectorField.Length;
                            (double invDx2, double invDy2, double invDz2) = (1.0 / (gridDelta.X * gridDelta.X), 1.0 / (gridDelta.Y * gridDelta.Y), 1.0 / (gridDelta.Z * gridDelta.Z));
                            double diagonal = (2.0 * invDx2) + (2.0 * invDy2) + (2.0 * invDz2);
                            return (diagonal > RhinoMath.ZeroTolerance) switch {
                                false => ResultFactory.Create<(Point3d[], Vector3d[])>(error: E.Geometry.InvalidVectorPotentialComputation.WithContext("Degenerate Laplacian diagonal due to invalid spacing")),
                                true => ((Func<Result<(Point3d[], Vector3d[])>>)(() => {
                                    double[] ax = ArrayPool<double>.Shared.Rent(totalSamples);
                                    double[] ay = ArrayPool<double>.Shared.Rent(totalSamples);
                                    double[] az = ArrayPool<double>.Shared.Rent(totalSamples);
                                    Vector3d[] potential = ArrayPool<Vector3d>.Shared.Rent(totalSamples);
                                    try {
                                        Array.Clear(array: ax, index: 0, length: totalSamples);
                                        Array.Clear(array: ay, index: 0, length: totalSamples);
                                        Array.Clear(array: az, index: 0, length: totalSamples);
                                        int resSquared = resolution * resolution;
                                        Vector3d[] curlField = curl.Curl;
                                        for (int iteration = 0; iteration < FieldsConfig.VectorPotentialIterations; iteration++) {
                                            double maxDelta = 0.0;
                                            for (int i = 1; i < resolution - 1; i++) {
                                                for (int j = 1; j < resolution - 1; j++) {
                                                    for (int k = 1; k < resolution - 1; k++) {
                                                        int idx = (i * resSquared) + (j * resolution) + k;
                                                        int idxXp = idx + resSquared;
                                                        int idxXm = idx - resSquared;
                                                        int idxYp = idx + resolution;
                                                        int idxYm = idx - resolution;
                                                        int idxZp = idx + 1;
                                                        int idxZm = idx - 1;
                                                        Vector3d curlValue = curlField[idx];
                                                        double neighborAx = ((ax[idxXp] + ax[idxXm]) * invDx2) + ((ax[idxYp] + ax[idxYm]) * invDy2) + ((ax[idxZp] + ax[idxZm]) * invDz2);
                                                        double neighborAy = ((ay[idxXp] + ay[idxXm]) * invDx2) + ((ay[idxYp] + ay[idxYm]) * invDy2) + ((ay[idxZp] + ay[idxZm]) * invDz2);
                                                        double neighborAz = ((az[idxXp] + az[idxXm]) * invDx2) + ((az[idxYp] + az[idxYm]) * invDy2) + ((az[idxZp] + az[idxZm]) * invDz2);
                                                        double newAx = (neighborAx - curlValue.X) / diagonal;
                                                        double newAy = (neighborAy - curlValue.Y) / diagonal;
                                                        double newAz = (neighborAz - curlValue.Z) / diagonal;
                                                        double deltaX = Math.Abs(newAx - ax[idx]);
                                                        double deltaY = Math.Abs(newAy - ay[idx]);
                                                        double deltaZ = Math.Abs(newAz - az[idx]);
                                                        maxDelta = Math.Max(maxDelta, Math.Max(deltaX, Math.Max(deltaY, deltaZ)));
                                                        ax[idx] = newAx;
                                                        ay[idx] = newAy;
                                                        az[idx] = newAz;
                                                    }
                                                }
                                            }
                                            if (maxDelta < FieldsConfig.VectorPotentialTolerance) {
                                                break;
                                            }
                                        }
                                        for (int idx = 0; idx < totalSamples; idx++) {
                                            potential[idx] = new(ax[idx], ay[idx], az[idx]);
                                        }
                                        Vector3d[] finalPotential = [.. potential[..totalSamples]];
                                        return ResultFactory.Create(value: (Grid: grid, Potential: finalPotential));
                                    } finally {
                                        ArrayPool<double>.Shared.Return(ax, clearArray: true);
                                        ArrayPool<double>.Shared.Return(ay, clearArray: true);
                                        ArrayPool<double>.Shared.Return(az, clearArray: true);
                                        ArrayPool<Vector3d>.Shared.Return(potential, clearArray: true);
                                    }
                                }))(),
                            };
                        }))(),
                    };
                }))()),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> InterpolateField<T>(
        Point3d query,
        T[] field,
        Point3d[] grid,
        int resolution,
        BoundingBox bounds,
        Fields.InterpolationMode mode,
        Func<T, T, double, T> lerp) where T : struct =>
        mode switch {
            Fields.NearestInterpolationMode => InterpolateNearest(query, field, grid),
            Fields.TrilinearInterpolationMode => InterpolateTrilinear(query: query, field: field, resolution: resolution, bounds: bounds, lerp: lerp),
            _ => ResultFactory.Create<T>(error: E.Geometry.InvalidFieldInterpolation.WithContext($"Unsupported interpolation mode: {mode.GetType().Name}")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<double> InterpolateScalar(
        Point3d query,
        double[] scalarField,
        Point3d[] grid,
        int resolution,
        BoundingBox bounds,
        Fields.InterpolationMode mode) =>
        InterpolateField(query: query, field: scalarField, grid: grid, resolution: resolution, bounds: bounds, mode: mode, lerp: (a, b, t) => a + (t * (b - a)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> InterpolateVector(
        Point3d query,
        Vector3d[] vectorField,
        Point3d[] grid,
        int resolution,
        BoundingBox bounds,
        Fields.InterpolationMode mode) =>
        InterpolateField(query: query, field: vectorField, grid: grid, resolution: resolution, bounds: bounds, mode: mode, lerp: (a, b, t) => a + (t * (b - a)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> InterpolateNearest<T>(Point3d query, T[] field, Point3d[] grid) {
        int nearestIdx = grid.Length > FieldsConfig.FieldRTreeThreshold
            ? ((Func<int>)(() => {
                using RTree tree = RTree.CreateFromPointArray(grid);
                int idx = -1;
                double bestDistance = double.MaxValue;
                _ = tree.Search(new Sphere(query, radius: double.MaxValue), (sender, args) => {
                    int candidateIdx = args.Id;
                    double distance = query.DistanceTo(grid[candidateIdx]);
                    (idx, bestDistance) = distance < bestDistance ? (candidateIdx, distance) : (idx, bestDistance);
                });
                return idx;
            }))()
            : ((Func<int>)(() => {
                double minDist = double.MaxValue;
                int idx = 0;
                for (int i = 0; i < grid.Length; i++) {
                    double dist = query.DistanceTo(grid[i]);
                    (idx, minDist) = dist < minDist ? (i, dist) : (idx, minDist);
                }
                return idx;
            }))();
        return nearestIdx >= 0
            ? ResultFactory.Create(value: field[nearestIdx])
            : ResultFactory.Create<T>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Nearest neighbor search failed"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> InterpolateTrilinear<T>(
        Point3d query,
        T[] field,
        int resolution,
        BoundingBox bounds,
        Func<T, T, double, T> lerp) where T : struct {
        (double dx, double dy, double dz) = (bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z);
        return (RhinoMath.EpsilonEquals(dx, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dy, 0.0, epsilon: RhinoMath.SqrtEpsilon) || RhinoMath.EpsilonEquals(dz, 0.0, epsilon: RhinoMath.SqrtEpsilon)) switch {
            true => ResultFactory.Create<T>(error: E.Geometry.InvalidFieldInterpolation.WithContext("Bounds have zero extent in one or more dimensions")),
            false => ((Func<Result<T>>)(() => {
                int resSquared = resolution * resolution;
                (double normX, double normY, double normZ) = ((query.X - bounds.Min.X) / dx, (query.Y - bounds.Min.Y) / dy, (query.Z - bounds.Min.Z) / dz);
                (double fi, double fj, double fk) = (RhinoMath.Clamp(normX * (resolution - 1), 0.0, resolution - 1), RhinoMath.Clamp(normY * (resolution - 1), 0.0, resolution - 1), RhinoMath.Clamp(normZ * (resolution - 1), 0.0, resolution - 1));
                (int i0, int j0, int k0) = ((int)fi, (int)fj, (int)fk);
                (int i1, int j1, int k1) = (i0 < resolution - 1 ? i0 + 1 : i0, j0 < resolution - 1 ? j0 + 1 : j0, k0 < resolution - 1 ? k0 + 1 : k0);
                (double tx, double ty, double tz) = (fi - i0, fj - j0, fk - k0);
                (T c000, T c001, T c010, T c011, T c100, T c101, T c110, T c111) = (
                    field[(i0 * resSquared) + (j0 * resolution) + k0],
                    field[(i0 * resSquared) + (j0 * resolution) + k1],
                    field[(i0 * resSquared) + (j1 * resolution) + k0],
                    field[(i0 * resSquared) + (j1 * resolution) + k1],
                    field[(i1 * resSquared) + (j0 * resolution) + k0],
                    field[(i1 * resSquared) + (j0 * resolution) + k1],
                    field[(i1 * resSquared) + (j1 * resolution) + k0],
                    field[(i1 * resSquared) + (j1 * resolution) + k1]);
                (T c00, T c01, T c10, T c11) = (lerp(c000, c100, tx), lerp(c001, c101, tx), lerp(c010, c110, tx), lerp(c011, c111, tx));
                (T c0, T c1) = (lerp(c00, c10, ty), lerp(c01, c11, ty));
                return ResultFactory.Create(value: lerp(c0, c1, tz));
            }))(),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Curve[]> IntegrateStreamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        double stepSize,
        Fields.IntegrationScheme scheme,
        int resolution,
        BoundingBox bounds,
        IGeometryContext context) =>
        ((Func<Result<Curve[]>>)(() => {
            Curve[] streamlines = new Curve[seeds.Length];
            Point3d[] pathBuffer = ArrayPool<Point3d>.Shared.Rent(FieldsConfig.MaxStreamlineSteps);
            try {
                for (int seedIdx = 0; seedIdx < seeds.Length; seedIdx++) {
                    Point3d current = seeds[seedIdx];
                    int stepCount = 0;
                    pathBuffer[stepCount++] = current;
                    for (int step = 0; step < FieldsConfig.MaxStreamlineSteps - 1; step++) {
                        Vector3d k1 = InterpolateTrilinear(query: current, field: vectorField, resolution: resolution, bounds: bounds, lerp: (a, b, t) => a + (t * (b - a)))
                            .OnError(_ => InterpolateNearest(query: current, field: vectorField, grid: gridPoints))
                            .Match(onSuccess: value => value, onFailure: _ => Vector3d.Zero);
                        Vector3d Interpolate(double coeff, Vector3d k) => InterpolateTrilinear(query: current + (stepSize * coeff * k), field: vectorField, resolution: resolution, bounds: bounds, lerp: (a, b, t) => a + (t * (b - a)))
                            .OnError(_ => InterpolateNearest(query: current + (stepSize * coeff * k), field: vectorField, grid: gridPoints))
                            .Match(onSuccess: value => value, onFailure: _ => Vector3d.Zero);
                        Vector3d delta = scheme switch {
                            Fields.EulerIntegrationScheme => stepSize * k1,
                            Fields.MidpointIntegrationScheme => stepSize * Interpolate(FieldsConfig.RK2HalfStep, k1),
                            Fields.RungeKutta4IntegrationScheme => ((Func<Vector3d>)(() => {
                                Vector3d k2 = Interpolate(FieldsConfig.RK4HalfSteps[0], k1);
                                Vector3d k3 = Interpolate(FieldsConfig.RK4HalfSteps[1], k2);
                                Vector3d k4 = Interpolate(FieldsConfig.RK4HalfSteps[2], k3);
                                return stepSize * ((FieldsConfig.RK4Weights[0] * k1) + (FieldsConfig.RK4Weights[1] * k2) + (FieldsConfig.RK4Weights[2] * k3) + (FieldsConfig.RK4Weights[3] * k4));
                            }))(),
                            _ => Vector3d.Zero,
                        };
                        Point3d nextPoint = current + delta;
                        bool shouldContinue = bounds.Contains(nextPoint) && k1.Length >= FieldsConfig.MinFieldMagnitude && delta.Length >= RhinoMath.ZeroTolerance && stepCount < FieldsConfig.MaxStreamlineSteps - 1;
                        pathBuffer[stepCount] = shouldContinue ? nextPoint : pathBuffer[stepCount];
                        current = shouldContinue ? nextPoint : current;
                        stepCount += shouldContinue ? 1 : 0;
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
                    (bool hasDx, bool hasDy, bool hasDz) = (Math.Abs(gridDelta.X) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Y) > RhinoMath.ZeroTolerance, Math.Abs(gridDelta.Z) > RhinoMath.ZeroTolerance);
                    (bool hasDxDy, bool hasDxDz, bool hasDyDz) = (hasDx && hasDy, hasDx && hasDz, hasDy && hasDz);
                    (double invDx2, double invDy2, double invDz2) = (hasDx ? 1.0 / (gridDelta.X * gridDelta.X) : 0.0, hasDy ? 1.0 / (gridDelta.Y * gridDelta.Y) : 0.0, hasDz ? 1.0 / (gridDelta.Z * gridDelta.Z) : 0.0);
                    (double invFourDxDy, double invFourDxDz, double invFourDyDz) = (hasDxDy ? 0.25 / (gridDelta.X * gridDelta.Y) : 0.0, hasDxDz ? 0.25 / (gridDelta.X * gridDelta.Z) : 0.0, hasDyDz ? 0.25 / (gridDelta.Y * gridDelta.Z) : 0.0);
                    int resSquared = resolution * resolution;

                    for (int i = 0; i < resolution; i++) {
                        for (int j = 0; j < resolution; j++) {
                            for (int k = 0; k < resolution; k++) {
                                int idx = (i * resSquared) + (j * resolution) + k;
                                double center = scalarField[idx];

                                double d2f_dx2 = hasDx && i > 0 && i < resolution - 1
                                    ? (scalarField[((i + 1) * resSquared) + (j * resolution) + k] - (2.0 * center) + scalarField[((i - 1) * resSquared) + (j * resolution) + k]) * invDx2
                                    : 0.0;

                                double d2f_dy2 = hasDy && j > 0 && j < resolution - 1
                                    ? (scalarField[(i * resSquared) + ((j + 1) * resolution) + k] - (2.0 * center) + scalarField[(i * resSquared) + ((j - 1) * resolution) + k]) * invDy2
                                    : 0.0;

                                double d2f_dz2 = hasDz && k > 0 && k < resolution - 1
                                    ? (scalarField[(i * resSquared) + (j * resolution) + (k + 1)] - (2.0 * center) + scalarField[(i * resSquared) + (j * resolution) + (k - 1)]) * invDz2
                                    : 0.0;

                                double d2f_dxdy = hasDxDy && i > 0 && i < resolution - 1 && j > 0 && j < resolution - 1
                                    ? (scalarField[((i + 1) * resSquared) + ((j + 1) * resolution) + k] - scalarField[((i + 1) * resSquared) + ((j - 1) * resolution) + k] - scalarField[((i - 1) * resSquared) + ((j + 1) * resolution) + k] + scalarField[((i - 1) * resSquared) + ((j - 1) * resolution) + k]) * invFourDxDy
                                    : 0.0;

                                double d2f_dxdz = hasDxDz && i > 0 && i < resolution - 1 && k > 0 && k < resolution - 1
                                    ? (scalarField[((i + 1) * resSquared) + (j * resolution) + (k + 1)] - scalarField[((i + 1) * resSquared) + (j * resolution) + (k - 1)] - scalarField[((i - 1) * resSquared) + (j * resolution) + (k + 1)] + scalarField[((i - 1) * resSquared) + (j * resolution) + (k - 1)]) * invFourDxDz
                                    : 0.0;

                                double d2f_dydz = hasDyDz && j > 0 && j < resolution - 1 && k > 0 && k < resolution - 1
                                    ? (scalarField[(i * resSquared) + ((j + 1) * resolution) + (k + 1)] - scalarField[(i * resSquared) + ((j + 1) * resolution) + (k - 1)] - scalarField[(i * resSquared) + ((j - 1) * resolution) + (k + 1)] + scalarField[(i * resSquared) + ((j - 1) * resolution) + (k - 1)]) * invFourDyDz
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
    private static Result<(Point3d[] Grid, TOut[] Output)> ApplyFieldOperation<TIn, TOut>(
        TIn[] inputField,
        Point3d[] grid,
        Func<TIn, int, TOut> operation,
        SystemError error) where TIn : struct where TOut : struct =>
        (inputField.Length == grid.Length) switch {
            false => ResultFactory.Create<(Point3d[], TOut[])>(error: error),
            true => ((Func<Result<(Point3d[], TOut[])>>)(() => {
                int totalSamples = inputField.Length;
                TOut[] output = ArrayPool<TOut>.Shared.Rent(totalSamples);
                try {
                    for (int i = 0; i < totalSamples; i++) {
                        output[i] = operation(inputField[i], i);
                    }
                    TOut[] finalOutput = [.. output[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Output: finalOutput));
                } finally {
                    ArrayPool<TOut>.Shared.Return(output, clearArray: true);
                }
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Point3d[] Grid, TOut[] Output)> ApplyBinaryFieldOperation<T1, T2, TOut>(
        T1[] inputField1,
        T2[] inputField2,
        Point3d[] grid,
        Func<T1, T2, int, TOut> operation,
        SystemError error1,
        SystemError error2) where T1 : struct where T2 : struct where TOut : struct =>
        (inputField1.Length == grid.Length, inputField2.Length == grid.Length) switch {
            (false, _) => ResultFactory.Create<(Point3d[], TOut[])>(error: error1),
            (_, false) => ResultFactory.Create<(Point3d[], TOut[])>(error: error2),
            (true, true) => ((Func<Result<(Point3d[], TOut[])>>)(() => {
                int totalSamples = inputField1.Length;
                TOut[] output = ArrayPool<TOut>.Shared.Rent(totalSamples);
                try {
                    for (int i = 0; i < totalSamples; i++) {
                        output[i] = operation(inputField1[i], inputField2[i], i);
                    }
                    TOut[] finalOutput = [.. output[..totalSamples]];
                    return ResultFactory.Create(value: (Grid: grid, Output: finalOutput));
                } finally {
                    ArrayPool<TOut>.Shared.Return(output, clearArray: true);
                }
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DirectionalDerivatives)> ComputeDirectionalDerivative(
        Vector3d[] gradientField,
        Point3d[] grid,
        Vector3d direction) =>
        (direction.IsValid && direction.Length > RhinoMath.ZeroTolerance) switch {
            false => ResultFactory.Create<(Point3d[], double[])>(error: E.Geometry.InvalidDirectionalDerivative.WithContext("Direction vector must be valid and non-zero")),
            true => ((Func<Result<(Point3d[], double[])>>)(() => {
                Vector3d unitDirection = direction / direction.Length;
                return ApplyFieldOperation(
                    inputField: gradientField,
                    grid: grid,
                    operation: (gradient, _) => Vector3d.Multiply(gradient, unitDirection),
                    error: E.Geometry.InvalidDirectionalDerivative.WithContext("Gradient field length must match grid points"));
            }))(),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Magnitudes)> ComputeFieldMagnitude(
        Vector3d[] vectorField,
        Point3d[] grid) =>
        ApplyFieldOperation(
            inputField: vectorField,
            grid: grid,
            operation: (vector, _) => vector.Length,
            error: E.Geometry.InvalidFieldMagnitude.WithContext("Vector field length must match grid points"));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, Vector3d[] Normalized)> NormalizeVectorField(
        Vector3d[] vectorField,
        Point3d[] grid) =>
        ApplyFieldOperation(
            inputField: vectorField,
            grid: grid,
            operation: (vector, _) => (vector.Length > FieldsConfig.MinFieldMagnitude) switch {
                true => vector / vector.Length,
                false => Vector3d.Zero,
            },
            error: E.Geometry.InvalidFieldNormalization.WithContext("Vector field length must match grid points"));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] Product)> ScalarVectorProduct(
        double[] scalarField,
        Vector3d[] vectorField,
        Point3d[] grid,
        Fields.VectorComponent component) {
        Func<Vector3d, double> selector = component switch {
            Fields.XComponent => v => v.X,
            Fields.YComponent => v => v.Y,
            Fields.ZComponent => v => v.Z,
            _ => throw new InvalidOperationException($"Unsupported component: {component.GetType().Name}"),
        };

        return ApplyBinaryFieldOperation(
            inputField1: scalarField,
            inputField2: vectorField,
            grid: grid,
            operation: (scalar, vector, _) => scalar * selector(vector),
            error1: E.Geometry.InvalidFieldComposition.WithContext("Scalar field length must match grid points"),
            error2: E.Geometry.InvalidFieldComposition.WithContext("Vector field length must match grid points"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[] Grid, double[] DotProduct)> VectorDotProduct(
        Vector3d[] vectorField1,
        Vector3d[] vectorField2,
        Point3d[] grid) =>
        ApplyBinaryFieldOperation(
            inputField1: vectorField1,
            inputField2: vectorField2,
            grid: grid,
            operation: (v1, v2, _) => Vector3d.Multiply(v1, v2),
            error1: E.Geometry.InvalidFieldComposition.WithContext("First vector field length must match grid points"),
            error2: E.Geometry.InvalidFieldComposition.WithContext("Second vector field length must match grid points"));

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

                                double a = localHessian[0, 0];
                                double b = localHessian[0, 1];
                                double c = localHessian[0, 2];
                                double d = localHessian[1, 1];
                                double e = localHessian[1, 2];
                                double f = localHessian[2, 2];
                                double p1 = (b * b) + (c * c) + (e * e);
                                bool isDiagonal = p1 < RhinoMath.SqrtEpsilon;
                                (double[] eigenvalues, Vector3d[] eigenvectors) = isDiagonal switch {
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
                                        double sqrt3 = Math.Sqrt(3.0);
                                        (double lambda1, double lambda2, double lambda3) = ((trace / 3.0) + (2.0 * sqrtP * Math.Cos(phi)), (trace / 3.0) - (sqrtP * (Math.Cos(phi) + (sqrt3 * Math.Sin(phi)))), (trace / 3.0) - (sqrtP * (Math.Cos(phi) - (sqrt3 * Math.Sin(phi)))));
                                        Vector3d ComputeEigenvector(double lambda) {
                                            (double aShift, double bVal, double cVal, double dShift, double eVal, double fShift) = (localHessian[0, 0] - lambda, localHessian[0, 1], localHessian[0, 2], localHessian[1, 1] - lambda, localHessian[1, 2], localHessian[2, 2] - lambda);
                                            (Vector3d cross1, Vector3d cross2, Vector3d cross3) = (
                                                new Vector3d((bVal * eVal) - (cVal * dShift), (cVal * bVal) - (aShift * eVal), (aShift * dShift) - (bVal * bVal)),
                                                new Vector3d((bVal * fShift) - (cVal * eVal), (cVal * cVal) - (aShift * fShift), (aShift * eVal) - (bVal * cVal)),
                                                new Vector3d((dShift * fShift) - (eVal * eVal), (eVal * cVal) - (bVal * fShift), (bVal * eVal) - (cVal * dShift)));
                                            Vector3d result = cross1.Length > cross2.Length ? (cross1.Length > cross3.Length ? cross1 : cross3) : (cross2.Length > cross3.Length ? cross2 : cross3);
                                            return result.Length > RhinoMath.ZeroTolerance ? result / result.Length : new Vector3d(1, 0, 0);
                                        }
                                        return ([lambda1, lambda2, lambda3,], [ComputeEigenvector(lambda1), ComputeEigenvector(lambda2), ComputeEigenvector(lambda3),]);
                                    }))(),
                                };
                                int positiveCount = eigenvalues.Count(ev => ev > FieldsConfig.EigenvalueThreshold);
                                int negativeCount = eigenvalues.Count(ev => ev < -FieldsConfig.EigenvalueThreshold);
                                Fields.CriticalPointKind criticalKind = (positiveCount, negativeCount) switch {
                                    (3, 0) => new Fields.MinimumCriticalPoint(),
                                    (0, 3) => new Fields.MaximumCriticalPoint(),
                                    _ => new Fields.SaddleCriticalPoint(),
                                };

                                criticalPoints.Add(new Fields.CriticalPoint(
                                    Location: grid[idx],
                                    Kind: criticalKind,
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

                return validCount > 0
                    ? ((Func<Result<Fields.FieldStatistics>>)(() => {
                        double mean = sum / validCount;
                        double sumSquaredDiff = 0.0;
                        for (int i = 0; i < scalarField.Length; i++) {
                            double value = scalarField[i];
                            if (RhinoMath.IsValidDouble(value)) {
                                double diff = value - mean;
                                sumSquaredDiff += diff * diff;
                            }
                        }
                        double stdDev = Math.Sqrt(sumSquaredDiff / validCount);

                        return ResultFactory.Create(value: new Fields.FieldStatistics(
                            Min: min,
                            Max: max,
                            Mean: mean,
                            StdDev: stdDev,
                            MinLocation: grid[minIdx],
                            MaxLocation: grid[maxIdx]));
                    }))()
                    : ResultFactory.Create<Fields.FieldStatistics>(error: E.Geometry.InvalidFieldStatistics.WithContext("Scalar field contains no valid samples"));
            }))(),
        };
    }
}

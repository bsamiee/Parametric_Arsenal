using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Arsenal.Rhino.Document;
using Arsenal.Rhino.Points;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Geometry;

/// <summary>High-performance batch intersection operations using spatial indexing.</summary>
public static class Intersect
{
    /// <summary>Finds all curve-curve intersections in a collection using RTree spatial indexing for O(n log n) performance.</summary>
    public static Result<CurveCurveIntersection[]> CurveCurveAll(
        IEnumerable<Curve>? curves,
        double? tolerance = null,
        bool includeSelfIntersections = false)
    {
        Result<IEnumerable<Curve>> curvesValidation = Guard.RequireNonNull(curves, nameof(curves));
        if (!curvesValidation.Ok)
        {
            return Result<CurveCurveIntersection[]>.Fail(curvesValidation.Error!);
        }

        List<Curve> curveList = curvesValidation.Value!.ToList();

        if (curveList.Count == 0)
        {
            return Result<CurveCurveIntersection[]>.Success([]);
        }

        // Validate all curves using pattern matching
        for (int i = 0; i < curveList.Count; i++)
        {
            if (curveList[i] is not { IsValid: true })
            {
                return Result<CurveCurveIntersection[]>.Fail($"Curve at index {i} is null or invalid");
            }
        }

        double tol = tolerance ?? Tolerances.Abs();
        List<CurveCurveIntersection> allIntersections = [];

        // Build RTree for spatial indexing
        using (RTree rTree = new())
        {
            // Insert all curve bounding boxes
            for (int i = 0; i < curveList.Count; i++)
            {
                BoundingBox bbox = curveList[i].GetBoundingBox(false);
                bbox.Inflate(tol); // Inflate by tolerance for robust search
                rTree.Insert(bbox, i);
            }

            // Find intersections using spatial search
            for (int i = 0; i < curveList.Count; i++)
            {
                Curve curveA = curveList[i];
                BoundingBox searchBox = curveA.GetBoundingBox(false);
                searchBox.Inflate(tol);

                List<int> candidates = [];
                int currentIndex = i; // Capture loop variable to avoid closure issues
                rTree.Search(searchBox, (_, args) =>
                {
                    if (args.Id > currentIndex) // Avoid duplicate pairs
                    {
                        candidates.Add(args.Id);
                    }
                });

                // Compute actual intersections for candidates
                foreach (int j in candidates)
                {
                    Curve curveB = curveList[j];
                    CurveIntersections events = Intersection.CurveCurve(curveA, curveB, tol, tol);

                    if (events is { Count: > 0 })
                    {
                        foreach (IntersectionEvent evt in events)
                        {
                            allIntersections.Add(new(
                                i, j,
                                evt.PointA,
                                evt.ParameterA, evt.ParameterB,
                                evt.IsOverlap
                            ));
                        }
                    }
                }

                // Self-intersections if requested
                if (includeSelfIntersections)
                {
                    CurveIntersections selfEvents = Intersection.CurveSelf(curveA, tol);
                    if (selfEvents is { Count: > 0 })
                    {
                        foreach (IntersectionEvent evt in selfEvents)
                        {
                            allIntersections.Add(new(
                                i, i,
                                evt.PointA,
                                evt.ParameterA, evt.ParameterB,
                                evt.IsOverlap
                            ));
                        }
                    }
                }
            }
        }

        // Deduplicate intersection points
        if (allIntersections.Count > 0)
        {
            Point3d[] allPoints = allIntersections.Select(i => i.Point).ToArray();
            Result<Point3d[]> deduplicationResult = PointDeduplication.RemoveWithDocTolerance(allPoints);

            if (!deduplicationResult.Ok)
            {
                return Result<CurveCurveIntersection[]>.Fail(
                    $"Failed to deduplicate intersection points: {deduplicationResult.Error}");
            }

            // Keep only intersections with unique points
            HashSet<Point3d> uniqueSet = [.. deduplicationResult.Value!];
            allIntersections = allIntersections
                .Where(i => uniqueSet.Contains(i.Point))
                .ToList();
        }

        return Result<CurveCurveIntersection[]>.Success(allIntersections.ToArray());
    }

    /// <summary>Finds all mesh-ray intersections using RhinoCommon SDK methods.</summary>
    public static Result<MeshRayIntersection[]> MeshRayAll(Mesh? mesh, IEnumerable<Ray3d>? rays)
    {
        Result<Mesh> meshValidation = Guard.RequireNonNull(mesh, nameof(mesh));
        if (!meshValidation.Ok)
        {
            return Result<MeshRayIntersection[]>.Fail(meshValidation.Error!);
        }

        if (mesh is not { IsValid: true })
        {
            return Result<MeshRayIntersection[]>.Fail("Mesh is not valid");
        }

        Result<IEnumerable<Ray3d>> raysValidation = Guard.RequireNonNull(rays, nameof(rays));
        if (!raysValidation.Ok)
        {
            return Result<MeshRayIntersection[]>.Fail(raysValidation.Error!);
        }

        List<Ray3d> rayList = raysValidation.Value!.ToList();
        List<MeshRayIntersection> results = new(rayList.Count);

        // Ensure mesh has face normals for result
        if (mesh.FaceNormals.Count == 0)
        {
            mesh.FaceNormals.ComputeFaceNormals();
        }

        foreach (Ray3d ray in rayList)
        {
            // Use SDK method that returns face indices directly
            double intersectionParam = Intersection.MeshRay(mesh, ray, out int[] faceIndices);

            if (intersectionParam >= 0.0 && faceIndices.Length > 0)
            {
                Point3d hitPoint = ray.PointAt(intersectionParam);
                int hitFaceIndex = faceIndices[0]; // First intersected face
                Vector3d faceNormal = mesh.FaceNormals[hitFaceIndex];

                results.Add(new(
                    true,
                    hitPoint,
                    intersectionParam,
                    hitFaceIndex,
                    faceNormal
                ));
            }
            else
            {
                results.Add(new(
                    false,
                    Point3d.Unset,
                    -1,
                    -1,
                    Vector3d.Unset
                ));
            }
        }

        return Result<MeshRayIntersection[]>.Success(results.ToArray());
    }

    /// <summary>Finds all surface-curve intersections in batch.</summary>
    public static Result<SurfaceCurveIntersection[]> SurfaceCurveAll(
        IEnumerable<Surface>? surfaces,
        IEnumerable<Curve>? curves,
        double? tolerance = null)
    {
        Result<IEnumerable<Surface>> surfacesValidation = Guard.RequireNonNull(surfaces, nameof(surfaces));
        if (!surfacesValidation.Ok)
        {
            return Result<SurfaceCurveIntersection[]>.Fail(surfacesValidation.Error!);
        }

        Result<IEnumerable<Curve>> curvesValidation = Guard.RequireNonNull(curves, nameof(curves));
        if (!curvesValidation.Ok)
        {
            return Result<SurfaceCurveIntersection[]>.Fail(curvesValidation.Error!);
        }

        List<Surface> surfaceList = surfacesValidation.Value!.ToList();
        List<Curve> curveList = curvesValidation.Value!.ToList();

        if (surfaceList.Count == 0 || curveList.Count == 0)
        {
            return Result<SurfaceCurveIntersection[]>.Success([]);
        }

        // Validate inputs using pattern matching
        for (int i = 0; i < surfaceList.Count; i++)
        {
            if (surfaceList[i] is not { IsValid: true })
            {
                return Result<SurfaceCurveIntersection[]>.Fail($"Surface at index {i} is null or invalid");
            }
        }

        for (int i = 0; i < curveList.Count; i++)
        {
            if (curveList[i] is not { IsValid: true })
            {
                return Result<SurfaceCurveIntersection[]>.Fail($"Curve at index {i} is null or invalid");
            }
        }

        double tol = tolerance ?? Tolerances.Abs();
        List<SurfaceCurveIntersection> allIntersections = [];

        // Build RTree for surface bounding boxes
        using (RTree rTree = new())
        {
            for (int i = 0; i < surfaceList.Count; i++)
            {
                BoundingBox bbox = surfaceList[i].GetBoundingBox(false);
                bbox.Inflate(tol);
                rTree.Insert(bbox, i);
            }

            // Test each curve against candidate surfaces
            for (int curveIdx = 0; curveIdx < curveList.Count; curveIdx++)
            {
                Curve curve = curveList[curveIdx];
                BoundingBox curveBBox = curve.GetBoundingBox(false);
                curveBBox.Inflate(tol);

                List<int> candidateSurfaces = [];
                rTree.Search(curveBBox, (_, args) => candidateSurfaces.Add(args.Id));

                foreach (int surfIdx in candidateSurfaces)
                {
                    Surface surface = surfaceList[surfIdx];
                    CurveIntersections events = Intersection.CurveSurface(curve, surface, tol, tol);

                    if (events is { Count: > 0 })
                    {
                        foreach (IntersectionEvent evt in events)
                        {
                            // Get UV parameters at intersection
                            surface.ClosestPoint(evt.PointA, out double u, out double v);

                            allIntersections.Add(new(
                                surfIdx, curveIdx,
                                evt.PointA,
                                evt.ParameterA,
                                u, v,
                                evt.IsOverlap
                            ));
                        }
                    }
                }
            }
        }

        // Deduplicate intersection points
        if (allIntersections.Count > 0)
        {
            Point3d[] allPoints = allIntersections.Select(i => i.Point).ToArray();
            Result<Point3d[]> deduplicationResult = PointDeduplication.RemoveWithDocTolerance(allPoints);

            if (!deduplicationResult.Ok)
            {
                return Result<SurfaceCurveIntersection[]>.Fail(
                    $"Failed to deduplicate intersection points: {deduplicationResult.Error}");
            }

            // Keep only intersections with unique points
            HashSet<Point3d> uniqueSet = [.. deduplicationResult.Value!];
            allIntersections = allIntersections
                .Where(i => uniqueSet.Contains(i.Point))
                .ToList();
        }

        return Result<SurfaceCurveIntersection[]>.Success(allIntersections.ToArray());
    }
}

/// <summary>Result from curve-curve intersection.</summary>
public readonly record struct CurveCurveIntersection(
    int CurveIndexA,
    int CurveIndexB,
    Point3d Point,
    double ParameterA,
    double ParameterB,
    bool IsOverlap
);

/// <summary>Result from mesh-ray intersection with SDK integration.</summary>
public readonly record struct MeshRayIntersection(
    bool Hit,
    Point3d HitPoint,
    double RayParameter,
    int FaceIndex,
    Vector3d FaceNormal
);

/// <summary>Result from surface-curve intersection.</summary>
public readonly record struct SurfaceCurveIntersection(
    int SurfaceIndex,
    int CurveIndex,
    Point3d Point,
    double CurveParameter,
    double SurfaceU,
    double SurfaceV,
    bool IsOverlap
);

using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Spatial;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using RhinoCurve = Rhino.Geometry.Curve;
using RhinoMesh = Rhino.Geometry.Mesh;
using RhinoSurface = Rhino.Geometry.Surface;

namespace Arsenal.Rhino.Geometry.Intersect;

/// <summary>Intersection operations using RhinoCommon.</summary>
public sealed class Intersect : IIntersect
{
    /// <summary>Computes curve-curve intersections.</summary>
    public Result<IReadOnlyList<CurveCurveHit>> CurveCurve(IEnumerable<RhinoCurve> curves, GeoContext context, bool includeSelf = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<RhinoCurve>> collectionResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!collectionResult.IsSuccess)
        {
            return Result<IReadOnlyList<CurveCurveHit>>.Fail(collectionResult.Failure!);
        }

        IReadOnlyList<RhinoCurve> curveList = collectionResult.Value!.ToList();
        double tol = context.AbsoluteTolerance;

        for (int i = 0; i < curveList.Count; i++)
        {
            if (!curveList[i].IsValid)
            {
                return Result<IReadOnlyList<CurveCurveHit>>.Fail(new Failure("intersect.curve.invalid", $"Curve at index {i} is invalid."));
            }
        }

        List<CurveCurveHit> hits = [];

        using RTree tree = new();
        for (int i = 0; i < curveList.Count; i++)
        {
            BoundingBox bbox = curveList[i].GetBoundingBox(false);
            bbox.Inflate(tol);
            tree.Insert(bbox, i);
        }

        for (int i = 0; i < curveList.Count; i++)
        {
            RhinoCurve curveA = curveList[i];
            BoundingBox search = curveA.GetBoundingBox(false);
            search.Inflate(tol);

            List<int> candidates = [];
            int current = i;
            tree.Search(search, (_, args) =>
            {
                if (args.Id > current)
                {
                    candidates.Add(args.Id);
                }
            });

            foreach (int j in candidates)
            {
                RhinoCurve curveB = curveList[j];
                CurveIntersections intersections = Intersection.CurveCurve(curveA, curveB, tol, tol);
                if (intersections is { Count: > 0 })
                {
                    foreach (IntersectionEvent evt in intersections)
                    {
                        hits.Add(new CurveCurveHit(i, j, evt.PointA, evt.ParameterA, evt.ParameterB, evt.IsOverlap));
                    }
                }
            }

            if (includeSelf)
            {
                CurveIntersections self = Intersection.CurveSelf(curveA, tol);
                if (self is { Count: > 0 })
                {
                    foreach (IntersectionEvent evt in self)
                    {
                        hits.Add(new CurveCurveHit(i, i, evt.PointA, evt.ParameterA, evt.ParameterB, evt.IsOverlap));
                    }
                }
            }
        }

        if (hits.Count == 0)
        {
            return Result<IReadOnlyList<CurveCurveHit>>.Success([]);
        }

        Result<IReadOnlyList<Point3d>> dedupe = PointIndex.Deduplicate(hits.Select(h => h.Point), tol);
        if (!dedupe.IsSuccess)
        {
            return Result<IReadOnlyList<CurveCurveHit>>.Fail(dedupe.Failure!);
        }

        HashSet<Point3d> unique = new(dedupe.Value!);
        IReadOnlyList<CurveCurveHit> filtered = hits.Where(hit => unique.Contains(hit.Point)).ToList();
        return Result<IReadOnlyList<CurveCurveHit>>.Success(filtered);
    }

    /// <summary>Computes mesh-ray intersections.</summary>
    public Result<IReadOnlyList<MeshRayHit>> MeshRay(RhinoMesh mesh, IEnumerable<Ray3d> rays, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<RhinoMesh> meshResult = Guard.AgainstNull(mesh, nameof(mesh));
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<MeshRayHit>>.Fail(meshResult.Failure!);
        }

        if (!mesh.IsValid)
        {
            return Result<IReadOnlyList<MeshRayHit>>.Fail(new Failure("intersect.mesh.invalid", "Mesh is not valid."));
        }

        Result<IReadOnlyCollection<Ray3d>> raysResult = Guard.AgainstEmpty(rays, nameof(rays));
        if (!raysResult.IsSuccess)
        {
            return Result<IReadOnlyList<MeshRayHit>>.Fail(raysResult.Failure!);
        }

        if (mesh.FaceNormals.Count == 0)
        {
            mesh.FaceNormals.ComputeFaceNormals();
        }

        List<MeshRayHit> hits = new(raysResult.Value!.Count);

        foreach (Ray3d ray in raysResult.Value)
        {
            double parameter = Intersection.MeshRay(mesh, ray, out int[] faces);
            if (parameter >= 0.0 && faces.Length > 0)
            {
                int faceIndex = faces[0];
                Vector3d normal = mesh.FaceNormals[faceIndex];
                Point3d point = ray.PointAt(parameter);

                hits.Add(new MeshRayHit(true, point, parameter, faceIndex, normal));
            }
            else
            {
                hits.Add(new MeshRayHit(false, Point3d.Unset, -1, -1, Vector3d.Unset));
            }
        }

        return Result<IReadOnlyList<MeshRayHit>>.Success(hits);
    }

    /// <summary>Computes surface-curve intersections.</summary>
    public Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurve(IEnumerable<RhinoSurface> surfaces, IEnumerable<RhinoCurve> curves, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<RhinoSurface>> surfaceResult = Guard.AgainstEmpty(surfaces, nameof(surfaces));
        if (!surfaceResult.IsSuccess)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(surfaceResult.Failure!);
        }

        Result<IReadOnlyCollection<RhinoCurve>> curveResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!curveResult.IsSuccess)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(curveResult.Failure!);
        }

        IReadOnlyList<RhinoSurface> surfaceList = surfaceResult.Value!.ToList();
        IReadOnlyList<RhinoCurve> curveList = curveResult.Value!.ToList();
        double tol = context.AbsoluteTolerance;

        for (int i = 0; i < surfaceList.Count; i++)
        {
            if (!surfaceList[i].IsValid)
            {
                return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(new Failure("intersect.surface.invalid", $"Surface at index {i} is invalid."));
            }
        }

        for (int i = 0; i < curveList.Count; i++)
        {
            if (!curveList[i].IsValid)
            {
                return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(new Failure("intersect.curve.invalid", $"Curve at index {i} is invalid."));
            }
        }

        List<SurfaceCurveHit> hits = [];

        using RTree tree = new();
        for (int i = 0; i < surfaceList.Count; i++)
        {
            BoundingBox bbox = surfaceList[i].GetBoundingBox(false);
            bbox.Inflate(tol);
            tree.Insert(bbox, i);
        }

        for (int i = 0; i < curveList.Count; i++)
        {
            RhinoCurve curve = curveList[i];
            BoundingBox search = curve.GetBoundingBox(false);
            search.Inflate(tol);

            List<int> candidates = [];
            tree.Search(search, (_, args) => candidates.Add(args.Id));

            foreach (int surfaceIndex in candidates)
            {
                RhinoSurface surface = surfaceList[surfaceIndex];
                CurveIntersections intersections = Intersection.CurveSurface(curve, surface, tol, tol);
                if (intersections is { Count: > 0 })
                {
                    foreach (IntersectionEvent evt in intersections)
                    {
                        surface.ClosestPoint(evt.PointA, out double u, out double v);
                        hits.Add(new SurfaceCurveHit(surfaceIndex, i, evt.PointA, evt.ParameterA, u, v, evt.IsOverlap));
                    }
                }
            }
        }

        if (hits.Count == 0)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Success([]);
        }

        Result<IReadOnlyList<Point3d>> dedupe = PointIndex.Deduplicate(hits.Select(h => h.Point), tol);
        if (!dedupe.IsSuccess)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(dedupe.Failure!);
        }

        HashSet<Point3d> unique = new(dedupe.Value!);
        IReadOnlyList<SurfaceCurveHit> filtered = hits.Where(hit => unique.Contains(hit.Point)).ToList();
        return Result<IReadOnlyList<SurfaceCurveHit>>.Success(filtered);
    }
}

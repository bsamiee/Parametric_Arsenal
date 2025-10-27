using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Spatial;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Geometry.Intersect;

/// <summary>RhinoCommon-backed intersection operations.</summary>
public sealed class Intersect : IIntersect
{
    /// <summary>Computes intersections between curves.</summary>
    /// <param name="curves">The curves to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <param name="includeSelf">Whether to include self-intersections.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<CurveCurveHit>> CurveCurve(IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context, bool includeSelf = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<global::Rhino.Geometry.Curve>> collectionResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!collectionResult.IsSuccess)
        {
            return Result<IReadOnlyList<CurveCurveHit>>.Fail(collectionResult.Failure!);
        }

        IReadOnlyList<global::Rhino.Geometry.Curve> curveList = collectionResult.Value!.ToList();
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
            global::Rhino.Geometry.Curve curveA = curveList[i];
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
                global::Rhino.Geometry.Curve curveB = curveList[j];
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

        HashSet<Point3d> unique = new(dedupe.Value);
        IReadOnlyList<CurveCurveHit> filtered = hits.Where(hit => unique.Contains(hit.Point)).ToList();
        return Result<IReadOnlyList<CurveCurveHit>>.Success(filtered);
    }

    /// <summary>Computes intersections between a mesh and rays.</summary>
    /// <param name="mesh">The mesh to intersect with.</param>
    /// <param name="rays">The rays to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<MeshRayHit>> MeshRay(global::Rhino.Geometry.Mesh mesh, IEnumerable<Ray3d> rays, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<global::Rhino.Geometry.Mesh> meshResult = Guard.AgainstNull(mesh, nameof(mesh));
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

    /// <summary>Computes intersections between surfaces and curves.</summary>
    /// <param name="surfaces">The surfaces to intersect with.</param>
    /// <param name="curves">The curves to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurve(IEnumerable<global::Rhino.Geometry.Surface> surfaces, IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<IReadOnlyCollection<global::Rhino.Geometry.Surface>> surfaceResult = Guard.AgainstEmpty(surfaces, nameof(surfaces));
        if (!surfaceResult.IsSuccess)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(surfaceResult.Failure!);
        }

        Result<IReadOnlyCollection<global::Rhino.Geometry.Curve>> curveResult = Guard.AgainstEmpty(curves, nameof(curves));
        if (!curveResult.IsSuccess)
        {
            return Result<IReadOnlyList<SurfaceCurveHit>>.Fail(curveResult.Failure!);
        }

        IReadOnlyList<global::Rhino.Geometry.Surface> surfaceList = surfaceResult.Value!.ToList();
        IReadOnlyList<global::Rhino.Geometry.Curve> curveList = curveResult.Value!.ToList();
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
            global::Rhino.Geometry.Curve curve = curveList[i];
            BoundingBox search = curve.GetBoundingBox(false);
            search.Inflate(tol);

            List<int> candidates = [];
            tree.Search(search, (_, args) => candidates.Add(args.Id));

            foreach (int surfaceIndex in candidates)
            {
                global::Rhino.Geometry.Surface surface = surfaceList[surfaceIndex];
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

        HashSet<Point3d> unique = new(dedupe.Value);
        IReadOnlyList<SurfaceCurveHit> filtered = hits.Where(hit => unique.Contains(hit.Point)).ToList();
        return Result<IReadOnlyList<SurfaceCurveHit>>.Success(filtered);
    }
}

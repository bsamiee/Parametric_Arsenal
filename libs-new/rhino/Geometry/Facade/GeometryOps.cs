using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Analysis.Vector;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Brep;
using Arsenal.Rhino.Geometry.Core;
using Arsenal.Rhino.Geometry.Curve;
using Arsenal.Rhino.Geometry.Elements;
using Arsenal.Rhino.Geometry.Intersect;
using Arsenal.Rhino.Geometry.Mesh;
using Arsenal.Rhino.Geometry.Surface;
using Rhino.Geometry;
using RhinoBrep = Rhino.Geometry.Brep;
using RhinoCurve = Rhino.Geometry.Curve;
using RhinoMesh = Rhino.Geometry.Mesh;
using RhinoSurface = Rhino.Geometry.Surface;

namespace Arsenal.Rhino.Geometry.Facade;

/// <summary>Unified geometry operations facade.</summary>
public sealed class GeometryOps : IElementOperations
{
    private readonly ICurve _curves;
    private readonly ISurface _surfaces;
    private readonly IMesh _meshes;
    private readonly IBrep _breps;
    private readonly ICentroid _centroids;
    private readonly IIntersect _intersections;
    private readonly IVectorAnalysis _vectors;

    /// <summary>Initializes geometry operations with all required implementations.</summary>
    public GeometryOps(
        ICurve curves,
        ISurface surfaces,
        IMesh meshes,
        IBrep breps,
        ICentroid centroids,
        IIntersect intersections,
        IVectorAnalysis vectors)
    {
        _curves = curves ?? throw new ArgumentNullException(nameof(curves));
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _breps = breps ?? throw new ArgumentNullException(nameof(breps));
        _centroids = centroids ?? throw new ArgumentNullException(nameof(centroids));
        _intersections = intersections ?? throw new ArgumentNullException(nameof(intersections));
        _vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));

    }

    /// <summary>Extracts vertices from geometry.</summary>
    public Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> Vertices(global::Rhino.Geometry.GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            RhinoBrep brep => _breps.Vertices(brep),
            RhinoMesh mesh => _meshes.Vertices(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Vertices),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Vertices),
            RhinoSurface surface => WithBrep(RhinoBrep.CreateFromSurface(surface), _breps.Vertices),
            RhinoCurve curve => Result<IReadOnlyList<global::Rhino.Geometry.Point3d>>.Success([curve.PointAtStart, curve.PointAtEnd]),
            _ => Result<IReadOnlyList<global::Rhino.Geometry.Point3d>>.Fail(new Failure("geometry.vertices.unsupported",
                $"Vertices extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts edges from geometry.</summary>
    public Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            RhinoBrep brep => _breps.Edges(brep),
            RhinoMesh mesh => _meshes.Edges(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Edges),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Edges),
            RhinoSurface surface => WithBrep(RhinoBrep.CreateFromSurface(surface), _breps.Edges),
            RhinoCurve curve => Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Success([curve.DuplicateCurve()]),
            _ => Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Fail(new Failure("geometry.edges.unsupported",
                $"Edge extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts faces from geometry.</summary>
    public Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>> Faces(global::Rhino.Geometry.GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            RhinoBrep brep => _breps.Faces(brep),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Faces),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Faces),
            RhinoSurface surface => Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>>.Success([surface.Duplicate()]),
            RhinoMesh => Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>>.Fail(new Failure("geometry.faces.unsupported",
                "Mesh faces should be accessed directly via mesh topology.")),
            _ => Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>>.Fail(new Failure("geometry.faces.unsupported",
                $"Face extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Computes edge midpoints from geometry.</summary>
    public Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> EdgeMidpoints(global::Rhino.Geometry.GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            RhinoBrep brep => _breps.EdgeMidpoints(brep),
            RhinoMesh mesh => _meshes.EdgeMidpoints(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.EdgeMidpoints),
            SubD subd => WithBrep(subd.ToBrep(), _breps.EdgeMidpoints),
            RhinoSurface surface => WithBrep(RhinoBrep.CreateFromSurface(surface), _breps.EdgeMidpoints),
            RhinoCurve curve => Result<IReadOnlyList<global::Rhino.Geometry.Point3d>>.Success([curve.PointAt(curve.Domain.Mid)]),
            _ => Result<IReadOnlyList<global::Rhino.Geometry.Point3d>>.Fail(new Failure("geometry.edgeMidpoints.unsupported",
                $"Edge midpoint extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Computes geometry centroid.</summary>
    public Result<global::Rhino.Geometry.Point3d> Centroid(global::Rhino.Geometry.GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _centroids.Compute(geometry, context);
    }

    /// <summary>Finds closest point on curve to test point.</summary>
    public Result<CurveClosestPoint> CurveClosestPoint(RhinoCurve curve, Point3d testPoint, GeoContext context) =>
        _curves.ClosestPoint(curve, testPoint, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes tangent vector at curve parameter.</summary>
    public Result<Vector3d> CurveTangent(RhinoCurve curve, double parameter) =>
        _curves.TangentAt(curve, parameter);

    /// <summary>Computes quadrant points for circular/elliptical curves.</summary>
    public Result<IReadOnlyList<Point3d>> CurveQuadrants(RhinoCurve curve, GeoContext context) =>
        _curves.QuadrantPoints(curve, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes curve midpoint.</summary>
    public Result<Point3d> CurveMidpoint(RhinoCurve curve) =>
        _curves.Midpoint(curve);

    /// <summary>Finds closest point on surface to test point.</summary>
    public Result<SurfaceClosestPoint> SurfaceClosestPoint(RhinoSurface surface, Point3d testPoint, GeoContext context) =>
        _surfaces.ClosestPoint(surface, testPoint, context ?? throw new ArgumentNullException(nameof(context)));



    /// <summary>Computes curve-curve intersections.</summary>
    public Result<IReadOnlyList<CurveCurveHit>> CurveIntersections(IEnumerable<RhinoCurve> curves, GeoContext context, bool includeSelf = false) =>
        _intersections.CurveCurve(curves, context ?? throw new ArgumentNullException(nameof(context)), includeSelf);

    /// <summary>Computes mesh-ray intersections.</summary>
    public Result<IReadOnlyList<MeshRayHit>> MeshRayIntersections(RhinoMesh mesh, IEnumerable<Ray3d> rays, GeoContext context) =>
        _intersections.MeshRay(mesh, rays, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes surface-curve intersections.</summary>
    public Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurveIntersections(IEnumerable<RhinoSurface> surfaces, IEnumerable<RhinoCurve> curves, GeoContext context) =>
        _intersections.SurfaceCurve(surfaces, curves, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts vector samples from geometry.</summary>
    public Result<IReadOnlyList<VectorSample>> VectorSamples(GeometryBase geometry, GeoContext context) =>
        _vectors.ExtractAll(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts tangent vectors from geometry.</summary>
    public Result<IReadOnlyList<Vector3d>> Tangents(GeometryBase geometry, GeoContext context) =>
        _vectors.Tangents(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts normal vectors from geometry.</summary>
    public Result<IReadOnlyList<Vector3d>> Normals(GeometryBase geometry, GeoContext context) =>
        _vectors.Normals(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    private static Result<IReadOnlyList<T>> WithBrep<T>(RhinoBrep? brep, Func<RhinoBrep, Result<IReadOnlyList<T>>> selector)
    {
        if (brep is null)
        {
            return Result<IReadOnlyList<T>>.Fail(new Failure("geometry.brepConversion", "Failed to convert geometry to brep."));
        }

        using (brep)
        {
            return selector(brep);
        }
    }
}

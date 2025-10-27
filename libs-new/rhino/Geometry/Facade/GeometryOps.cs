using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Base;
using Arsenal.Rhino.Geometry.Brep;
using Arsenal.Rhino.Geometry.Centroid;
using Arsenal.Rhino.Geometry.Curve;
using Arsenal.Rhino.Geometry.Intersect;
using Arsenal.Rhino.Geometry.Mesh;
using Arsenal.Rhino.Geometry.Surface;
using Arsenal.Rhino.Geometry.Vector;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Facade;

/// <summary>Aggregates geometry operations across Rhino types.</summary>
public sealed class GeometryOps : IOperations
{
    private readonly ICurve _curves;
    private readonly ISurface _surfaces;
    private readonly IMesh _meshes;
    private readonly IBrep _breps;
    private readonly ICentroid _centroids;
    private readonly IIntersect _intersections;
    private readonly IVector _vectors;

    /// <summary>Initializes a new instance of the GeometryOps class.</summary>
    /// <param name="curves">The curve operations implementation.</param>
    /// <param name="surfaces">The surface operations implementation.</param>
    /// <param name="meshes">The mesh operations implementation.</param>
    /// <param name="breps">The brep operations implementation.</param>
    /// <param name="centroids">The centroid operations implementation.</param>
    /// <param name="intersections">The intersection operations implementation.</param>
    /// <param name="vectors">The vector operations implementation.</param>
    public GeometryOps(
        ICurve curves,
        ISurface surfaces,
        IMesh meshes,
        IBrep breps,
        ICentroid centroids,
        IIntersect intersections,
        IVector vectors)
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
    /// <param name="geometry">The geometry to extract vertices from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the vertices or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> Vertices(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep => _breps.Vertices(brep),
            global::Rhino.Geometry.Mesh mesh => _meshes.Vertices(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Vertices),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Vertices),
            global::Rhino.Geometry.Surface surface => WithBrep(global::Rhino.Geometry.Brep.CreateFromSurface(surface), _breps.Vertices),
            global::Rhino.Geometry.Curve curve => Result<IReadOnlyList<Point3d>>.Success([curve.PointAtStart, curve.PointAtEnd]),
            _ => Result<IReadOnlyList<Point3d>>.Fail(new Failure("geometry.vertices.unsupported",
                $"Vertices extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts edges from geometry.</summary>
    /// <param name="geometry">The geometry to extract edges from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the edges or a failure.</returns>
    public Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep => _breps.Edges(brep),
            global::Rhino.Geometry.Mesh mesh => _meshes.Edges(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Edges),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Edges),
            global::Rhino.Geometry.Surface surface => WithBrep(global::Rhino.Geometry.Brep.CreateFromSurface(surface), _breps.Edges),
            global::Rhino.Geometry.Curve curve => Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Success([curve.DuplicateCurve()]),
            _ => Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Fail(new Failure("geometry.edges.unsupported",
                $"Edge extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts faces from geometry.</summary>
    /// <param name="geometry">The geometry to extract faces from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the faces or a failure.</returns>
    public Result<IReadOnlyList<GeometryBase>> Faces(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep => _breps.Faces(brep),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.Faces),
            SubD subd => WithBrep(subd.ToBrep(), _breps.Faces),
            global::Rhino.Geometry.Surface surface => Result<IReadOnlyList<GeometryBase>>.Success([surface.Duplicate()]),
            global::Rhino.Geometry.Mesh => Result<IReadOnlyList<GeometryBase>>.Fail(new Failure("geometry.faces.unsupported",
                "Mesh faces should be accessed directly via mesh topology.")),
            _ => Result<IReadOnlyList<GeometryBase>>.Fail(new Failure("geometry.faces.unsupported",
                $"Face extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Calculates edge midpoints from geometry.</summary>
    /// <param name="geometry">The geometry to calculate edge midpoints for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the edge midpoints or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep => _breps.EdgeMidpoints(brep),
            global::Rhino.Geometry.Mesh mesh => _meshes.EdgeMidpoints(mesh),
            Extrusion extrusion => WithBrep(extrusion.ToBrep(), _breps.EdgeMidpoints),
            SubD subd => WithBrep(subd.ToBrep(), _breps.EdgeMidpoints),
            global::Rhino.Geometry.Surface surface => WithBrep(global::Rhino.Geometry.Brep.CreateFromSurface(surface), _breps.EdgeMidpoints),
            global::Rhino.Geometry.Curve curve => Result<IReadOnlyList<Point3d>>.Success([curve.PointAt(curve.Domain.Mid)]),
            _ => Result<IReadOnlyList<Point3d>>.Fail(new Failure("geometry.edgeMidpoints.unsupported",
                $"Edge midpoint extraction is not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Calculates centroid of geometry.</summary>
    /// <param name="geometry">The geometry to calculate the centroid for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the centroid point or a failure.</returns>
    public Result<Point3d> Centroid(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _centroids.Compute(geometry, context);
    }

    /// <summary>Finds the closest point on a curve to a test point.</summary>
    /// <param name="curve">The curve to project onto.</param>
    /// <param name="testPoint">The point to project.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the closest point information or a failure.</returns>
    public Result<CurveClosestPoint> CurveClosestPoint(global::Rhino.Geometry.Curve curve, Point3d testPoint, GeoContext context) =>
        _curves.ClosestPoint(curve, testPoint, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes the tangent vector at a curve parameter.</summary>
    /// <param name="curve">The curve to evaluate.</param>
    /// <param name="parameter">The parameter to evaluate at.</param>
    /// <returns>A result containing the unit tangent vector or a failure.</returns>
    public Result<Vector3d> CurveTangent(global::Rhino.Geometry.Curve curve, double parameter) =>
        _curves.TangentAt(curve, parameter);

    /// <summary>Computes quadrant points for circular or elliptical curves.</summary>
    /// <param name="curve">The curve to compute quadrant points for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the quadrant points or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> CurveQuadrants(global::Rhino.Geometry.Curve curve, GeoContext context) =>
        _curves.QuadrantPoints(curve, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes the midpoint of a curve.</summary>
    /// <param name="curve">The curve to compute the midpoint for.</param>
    /// <returns>A result containing the midpoint or a failure.</returns>
    public Result<Point3d> CurveMidpoint(global::Rhino.Geometry.Curve curve) =>
        _curves.Midpoint(curve);

    /// <summary>Finds the closest point on a surface to a test point.</summary>
    /// <param name="surface">The surface to project onto.</param>
    /// <param name="testPoint">The point to project.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the closest point information or a failure.</returns>
    public Result<SurfaceClosestPoint> SurfaceClosestPoint(global::Rhino.Geometry.Surface surface, Point3d testPoint, GeoContext context) =>
        _surfaces.ClosestPoint(surface, testPoint, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes the surface frame at specified parameters.</summary>
    /// <param name="surface">The surface to evaluate.</param>
    /// <param name="u">The U parameter.</param>
    /// <param name="v">The V parameter.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the surface frame or a failure.</returns>
    public Result<SurfaceFrame> SurfaceFrame(global::Rhino.Geometry.Surface surface, double u, double v, GeoContext context) =>
        _surfaces.FrameAt(surface, u, v, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes intersections between curves.</summary>
    /// <param name="curves">The curves to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <param name="includeSelf">Whether to include self-intersections.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<CurveCurveHit>> CurveIntersections(IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context, bool includeSelf = false) =>
        _intersections.CurveCurve(curves, context ?? throw new ArgumentNullException(nameof(context)), includeSelf);

    /// <summary>Computes intersections between a mesh and rays.</summary>
    /// <param name="mesh">The mesh to intersect with.</param>
    /// <param name="rays">The rays to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<MeshRayHit>> MeshRayIntersections(global::Rhino.Geometry.Mesh mesh, IEnumerable<Ray3d> rays, GeoContext context) =>
        _intersections.MeshRay(mesh, rays, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Computes intersections between surfaces and curves.</summary>
    /// <param name="surfaces">The surfaces to intersect with.</param>
    /// <param name="curves">The curves to intersect.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the intersection hits or a failure.</returns>
    public Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurveIntersections(IEnumerable<global::Rhino.Geometry.Surface> surfaces, IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context) =>
        _intersections.SurfaceCurve(surfaces, curves, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts all vector samples from geometry.</summary>
    /// <param name="geometry">The geometry to extract vectors from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the vector samples or a failure.</returns>
    public Result<IReadOnlyList<VectorSample>> VectorSamples(GeometryBase geometry, GeoContext context) =>
        _vectors.ExtractAll(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts tangent vectors from geometry.</summary>
    /// <param name="geometry">The geometry to extract tangents from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the tangent vectors or a failure.</returns>
    public Result<IReadOnlyList<Vector3d>> Tangents(GeometryBase geometry, GeoContext context) =>
        _vectors.Tangents(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    /// <summary>Extracts normal vectors from geometry.</summary>
    /// <param name="geometry">The geometry to extract normals from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the normal vectors or a failure.</returns>
    public Result<IReadOnlyList<Vector3d>> Normals(GeometryBase geometry, GeoContext context) =>
        _vectors.Normals(geometry, context ?? throw new ArgumentNullException(nameof(context)));

    private static Result<IReadOnlyList<T>> WithBrep<T>(global::Rhino.Geometry.Brep? brep, Func<global::Rhino.Geometry.Brep, Result<IReadOnlyList<T>>> selector)
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

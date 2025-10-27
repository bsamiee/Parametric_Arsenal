using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Vector;

/// <summary>RhinoCommon-backed vector extraction operations.</summary>
public sealed class Vector : IVector
{
    /// <summary>Extracts all vector samples from the geometry.</summary>
    /// <param name="geometry">The geometry to extract vectors from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the vector samples or a failure.</returns>
    public Result<IReadOnlyList<VectorSample>> ExtractAll(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep => ExtractFromBrep(brep),
            BrepFace face => VectorAdapters.GetBrepFaceVectors(face),
            Extrusion extrusion => VectorAdapters.GetExtrusionVectors(extrusion),
            global::Rhino.Geometry.Surface surface => VectorAdapters.GetSurfaceVectors(surface),
            global::Rhino.Geometry.Curve curve => VectorAdapters.GetCurveVectors(curve),
            global::Rhino.Geometry.Mesh mesh => VectorAdapters.GetMeshVectors(mesh),
            SubD subd => ExtractFromSubD(subd),
            _ => Result<IReadOnlyList<VectorSample>>.Success([])
        };
    }

    /// <summary>Extracts tangent vectors from the geometry.</summary>
    /// <param name="geometry">The geometry to extract tangents from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the tangent vectors or a failure.</returns>
    public Result<IReadOnlyList<Vector3d>> Tangents(GeometryBase geometry, GeoContext context)
    {
        Result<IReadOnlyList<VectorSample>> samples = ExtractAll(geometry, context);
        if (!samples.IsSuccess)
        {
            return Result<IReadOnlyList<Vector3d>>.Fail(samples.Failure!);
        }

        List<Vector3d> tangents = samples.Value!
            .Where(sample => sample.Tangent.HasValue)
            .Select(sample => sample.Tangent!.Value)
            .ToList();

        return Result<IReadOnlyList<Vector3d>>.Success(tangents);
    }

    /// <summary>Extracts normal vectors from the geometry.</summary>
    /// <param name="geometry">The geometry to extract normals from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the normal vectors or a failure.</returns>
    public Result<IReadOnlyList<Vector3d>> Normals(GeometryBase geometry, GeoContext context)
    {
        Result<IReadOnlyList<VectorSample>> samples = ExtractAll(geometry, context);
        if (!samples.IsSuccess)
        {
            return Result<IReadOnlyList<Vector3d>>.Fail(samples.Failure!);
        }

        List<Vector3d> normals = samples.Value!
            .Where(sample => sample.Normal.HasValue)
            .Select(sample => sample.Normal!.Value)
            .ToList();

        return Result<IReadOnlyList<Vector3d>>.Success(normals);
    }

    private static Result<IReadOnlyList<VectorSample>> ExtractFromBrep(global::Rhino.Geometry.Brep brep)
    {
        Result<global::Rhino.Geometry.Brep> validation = Guard.AgainstNull(brep, nameof(brep));
        if (!validation.IsSuccess)
        {
            return Result<IReadOnlyList<VectorSample>>.Fail(validation.Failure!);
        }

        if (!brep.IsValid)
        {
            return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.brep.invalid", "Brep is not valid."));
        }

        List<VectorSample> all = [];
        foreach (BrepFace face in brep.Faces)
        {
            Result<IReadOnlyList<VectorSample>> result = VectorAdapters.GetBrepFaceVectors(face);
            if (result.IsSuccess && result.Value!.Count > 0)
            {
                all.AddRange(result.Value);
            }
        }

        return all.Count == 0
            ? Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.brep.empty", "No vectors were extracted from brep faces."))
            : Result<IReadOnlyList<VectorSample>>.Success(all);
    }

    private static Result<IReadOnlyList<VectorSample>> ExtractFromSubD(SubD subd)
    {
        Result<SubD> validation = Guard.AgainstNull(subd, nameof(subd));
        if (!validation.IsSuccess)
        {
            return Result<IReadOnlyList<VectorSample>>.Fail(validation.Failure!);
        }

        if (!subd.IsValid)
        {
            return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.subd.invalid", "SubD is not valid."));
        }

        try
        {
            using global::Rhino.Geometry.Brep? brep = subd.ToBrep();
            if (brep is null || !brep.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.subd.convert", "Failed to convert SubD to brep."));
            }

            return ExtractFromBrep(brep);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<VectorSample>>.Fail(Failure.From(ex));
        }
    }

    private static class VectorAdapters
    {
        public static Result<IReadOnlyList<VectorSample>> GetSurfaceVectors(global::Rhino.Geometry.Surface surface)
        {
            if (!surface.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.surface.invalid", "Surface is not valid."));
            }

            Interval uDomain = surface.Domain(0);
            Interval vDomain = surface.Domain(1);
            double u = uDomain.Mid;
            double v = vDomain.Mid;

            bool success = surface.Evaluate(u, v, 1, out Point3d point, out Vector3d[] derivatives);
            if (!success || derivatives.Length < 2)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.surface.evaluate", "Surface evaluation failed."));
            }

            Vector3d tangentU = derivatives[0];
            Vector3d tangentV = derivatives[1];
            Vector3d normal = Vector3d.CrossProduct(tangentU, tangentV);

            if (!normal.IsValid || normal.Length < RhinoMath.ZeroTolerance)
            {
                normal = surface.NormalAt(u, v);
            }

            VectorSample sample = new(point, null, normal, tangentU, tangentV);
            return Result<IReadOnlyList<VectorSample>>.Success([sample]);
        }

        public static Result<IReadOnlyList<VectorSample>> GetBrepFaceVectors(BrepFace face)
        {
            if (!face.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.brepFace.invalid", "Brep face is not valid."));
            }

            Interval uDomain = face.Domain(0);
            Interval vDomain = face.Domain(1);
            double u = uDomain.Mid;
            double v = vDomain.Mid;

            Vector3d normal = face.NormalAt(u, v);
            bool evaluated = face.Evaluate(u, v, 1, out Point3d point, out Vector3d[] derivatives);
            if (!evaluated || derivatives.Length < 2 || !normal.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.brepFace.evaluate", "Failed to evaluate brep face."));
            }

            VectorSample sample = new(point, null, normal, derivatives[0], derivatives[1]);
            return Result<IReadOnlyList<VectorSample>>.Success([sample]);
        }

        public static Result<IReadOnlyList<VectorSample>> GetCurveVectors(global::Rhino.Geometry.Curve curve)
        {
            if (!curve.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.curve.invalid", "Curve is not valid."));
            }

            if (curve is PolyCurve polyCurve)
            {
                List<VectorSample> results = [];
                for (int i = 0; i < polyCurve.SegmentCount; i++)
                {
                    global::Rhino.Geometry.Curve? segment = polyCurve.SegmentCurve(i);
                    if (segment is null || !segment.IsValid)
                    {
                        continue;
                    }

                    Interval segmentDomain = segment.Domain;
                    double mid = segmentDomain.Mid;
                    Point3d point = segment.PointAt(mid);
                    Vector3d tangent = segment.TangentAt(mid);

                    if (!tangent.IsValid || tangent.Length < RhinoMath.ZeroTolerance)
                    {
                        continue;
                    }

                    tangent.Unitize();
                    results.Add(new VectorSample(point, tangent, null, null, null));
                }

                if (results.Count == 0)
                {
                    return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.curve.empty", "No tangent vectors extracted from polycurve."));
                }

                return Result<IReadOnlyList<VectorSample>>.Success(results);
            }
            else
            {
                double mid = curve.Domain.Mid;
                Point3d point = curve.PointAt(mid);
                Vector3d tangent = curve.TangentAt(mid);

                if (!tangent.IsValid || tangent.Length < RhinoMath.ZeroTolerance)
                {
                    return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.curve.tangent", "Curve tangent is invalid."));
                }

                tangent.Unitize();
                return Result<IReadOnlyList<VectorSample>>.Success([new VectorSample(point, tangent, null, null, null)]);
            }
        }

        public static Result<IReadOnlyList<VectorSample>> GetMeshVectors(global::Rhino.Geometry.Mesh mesh)
        {
            if (!mesh.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.mesh.invalid", "Mesh is not valid."));
            }

            if (mesh.FaceNormals.Count == 0)
            {
                mesh.FaceNormals.ComputeFaceNormals();
            }

            List<VectorSample> samples = new(mesh.Faces.Count);
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                MeshFace face = mesh.Faces[i];
                Vector3f normal = mesh.FaceNormals[i];
                if (!normal.IsValid || normal.Length < RhinoMath.ZeroTolerance)
                {
                    continue;
                }

                Point3d center = FaceCenter(mesh, face);
                samples.Add(new VectorSample(center, null, new Vector3d(normal), null, null));
            }

            if (samples.Count == 0)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.mesh.empty", "No valid mesh normals found."));
            }

            return Result<IReadOnlyList<VectorSample>>.Success(samples);
        }

        public static Result<IReadOnlyList<VectorSample>> GetExtrusionVectors(Extrusion extrusion)
        {
            Vector3d direction = extrusion.PathEnd - extrusion.PathStart;
            if (!direction.IsValid || direction.Length < RhinoMath.ZeroTolerance)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.extrusion.direction", "Extrusion direction is invalid."));
            }

            Point3d point = (extrusion.PathStart + extrusion.PathEnd) * 0.5;
            return Result<IReadOnlyList<VectorSample>>.Success([new VectorSample(point, direction, null, null, null)]);
        }

        private static Point3d FaceCenter(global::Rhino.Geometry.Mesh mesh, MeshFace face)
        {
            Point3f a = mesh.Vertices[face.A];
            Point3f b = mesh.Vertices[face.B];
            Point3f c = mesh.Vertices[face.C];

            if (face.IsQuad)
            {
                Point3f d = mesh.Vertices[face.D];
                return new Point3d((a.X + b.X + c.X + d.X) / 4.0,
                    (a.Y + b.Y + c.Y + d.Y) / 4.0,
                    (a.Z + b.Z + c.Z + d.Z) / 4.0);
            }

            return new Point3d((a.X + b.X + c.X) / 3.0,
                (a.Y + b.Y + c.Y) / 3.0,
                (a.Z + b.Z + c.Z) / 3.0);
        }
    }
}

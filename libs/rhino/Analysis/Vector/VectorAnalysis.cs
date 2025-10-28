using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;
using RhinoMesh = Rhino.Geometry.Mesh;
using RhinoSurface = Rhino.Geometry.Surface;
using RhinoVector3d = Rhino.Geometry.Vector3d;
using RhinoVector3f = Rhino.Geometry.Vector3f;

namespace Arsenal.Rhino.Analysis.Vector;

/// <summary>Vector extraction using RhinoCommon.</summary>
public sealed class VectorAnalysis : IVectorAnalysis
{
    /// <summary>Extracts vector samples from geometry.</summary>
    public Result<IReadOnlyList<VectorSample>> ExtractAll(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return geometry switch
        {
            Brep brep => ExtractFromBrep(brep),
            BrepFace face => VectorAdapters.GetBrepFaceVectors(face),
            Extrusion extrusion => VectorAdapters.GetExtrusionVectors(extrusion),
            global::Rhino.Geometry.Surface surface => VectorAdapters.GetSurfaceVectors(surface),
            Curve curve => VectorAdapters.GetCurveVectors(curve),
            RhinoMesh mesh => VectorAdapters.GetMeshVectors(mesh),
            SubD subd => ExtractFromSubD(subd),
            _ => Result<IReadOnlyList<VectorSample>>.Success([])
        };
    }

    /// <summary>Extracts tangent vectors from geometry.</summary>
    public Result<IReadOnlyList<RhinoVector3d>> Tangents(GeometryBase geometry, GeoContext context)
    {
        Result<IReadOnlyList<VectorSample>> samples = ExtractAll(geometry, context);
        if (!samples.IsSuccess)
        {
            return Result<IReadOnlyList<RhinoVector3d>>.Fail(samples.Failure!);
        }

        List<RhinoVector3d> tangents = samples.Value!
            .Where(sample => sample.Tangent.HasValue)
            .Select(sample => sample.Tangent!.Value)
            .ToList();

        return Result<IReadOnlyList<RhinoVector3d>>.Success(tangents);
    }

    /// <summary>Extracts normal vectors from geometry.</summary>
    public Result<IReadOnlyList<RhinoVector3d>> Normals(GeometryBase geometry, GeoContext context)
    {
        Result<IReadOnlyList<VectorSample>> samples = ExtractAll(geometry, context);
        if (!samples.IsSuccess)
        {
            return Result<IReadOnlyList<RhinoVector3d>>.Fail(samples.Failure!);
        }

        List<RhinoVector3d> normals = samples.Value!
            .Where(sample => sample.Normal.HasValue)
            .Select(sample => sample.Normal!.Value)
            .ToList();

        return Result<IReadOnlyList<RhinoVector3d>>.Success(normals);
    }

    private static Result<IReadOnlyList<VectorSample>> ExtractFromBrep(Brep brep)
    {
        Result<Brep> validation = Guard.AgainstNull(brep, nameof(brep));
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
            using Brep? brep = subd.ToBrep();
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
        public static Result<IReadOnlyList<VectorSample>> GetSurfaceVectors(RhinoSurface surface)
        {
            if (!surface.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.surface.invalid", "Surface is not valid."));
            }

            Interval uDomain = surface.Domain(0);
            Interval vDomain = surface.Domain(1);
            double u = uDomain.Mid;
            double v = vDomain.Mid;

            bool success = surface.Evaluate(u, v, 1, out Point3d point, out RhinoVector3d[] derivatives);
            if (!success || derivatives.Length < 2)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.surface.evaluate", "Surface evaluation failed."));
            }

            RhinoVector3d tangentU = derivatives[0];
            RhinoVector3d tangentV = derivatives[1];
            RhinoVector3d normal = RhinoVector3d.CrossProduct(tangentU, tangentV);

            if (!normal.IsValid || normal.Length < 1e-12)
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

            RhinoVector3d normal = face.NormalAt(u, v);
            bool evaluated = face.Evaluate(u, v, 1, out Point3d point, out RhinoVector3d[] derivatives);
            if (!evaluated || derivatives.Length < 2 || !normal.IsValid)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.brepFace.evaluate", "Failed to evaluate brep face."));
            }

            VectorSample sample = new(point, null, normal, derivatives[0], derivatives[1]);
            return Result<IReadOnlyList<VectorSample>>.Success([sample]);
        }

        public static Result<IReadOnlyList<VectorSample>> GetCurveVectors(Curve curve)
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
                    Curve? segment = polyCurve.SegmentCurve(i);
                    if (segment is null || !segment.IsValid)
                    {
                        continue;
                    }

                    Interval segmentDomain = segment.Domain;
                    double mid = segmentDomain.Mid;
                    Point3d point = segment.PointAt(mid);
                    RhinoVector3d tangent = segment.TangentAt(mid);

                    if (!tangent.IsValid || tangent.Length < 1e-12)
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

            double midParam = curve.Domain.Mid;
            Point3d midPoint = curve.PointAt(midParam);
            RhinoVector3d tangentVector = curve.TangentAt(midParam);

            if (!tangentVector.IsValid || tangentVector.Length < 1e-12)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.curve.tangent", "Curve tangent is invalid."));
            }

            tangentVector.Unitize();
            return Result<IReadOnlyList<VectorSample>>.Success([new VectorSample(midPoint, tangentVector, null, null, null)]);
        }

        public static Result<IReadOnlyList<VectorSample>> GetMeshVectors(RhinoMesh mesh)
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
                RhinoVector3f normal = mesh.FaceNormals[i];
                if (!normal.IsValid || normal.Length < 1e-12)
                {
                    continue;
                }

                Point3d center = FaceCenter(mesh, face);
                samples.Add(new VectorSample(center, null, new RhinoVector3d(normal), null, null));
            }

            if (samples.Count == 0)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.mesh.empty", "No valid mesh normals found."));
            }

            return Result<IReadOnlyList<VectorSample>>.Success(samples);
        }

        public static Result<IReadOnlyList<VectorSample>> GetExtrusionVectors(Extrusion extrusion)
        {
            RhinoVector3d direction = extrusion.PathEnd - extrusion.PathStart;
            if (!direction.IsValid || direction.Length < 1e-12)
            {
                return Result<IReadOnlyList<VectorSample>>.Fail(new Failure("vector.extrusion.direction", "Extrusion direction is invalid."));
            }

            Point3d point = (extrusion.PathStart + extrusion.PathEnd) * 0.5;
            return Result<IReadOnlyList<VectorSample>>.Success([new VectorSample(point, direction, null, null, null)]);
        }

        private static Point3d FaceCenter(RhinoMesh mesh, MeshFace face)
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
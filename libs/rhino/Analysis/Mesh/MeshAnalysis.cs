using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.FileIO;
using Rhino.Geometry;
using RhinoMesh = Rhino.Geometry.Mesh;

namespace Arsenal.Rhino.Analysis.Mesh;

/// <summary>Mesh analysis using RhinoCommon.</summary>
public sealed class MeshAnalysis : IMeshAnalysis
{
    /// <summary>Analyzes mesh face planarity.</summary>
    public Result<PlanarityReport> Planarity(RhinoMesh mesh, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<RhinoMesh> validation = Guard.AgainstNull(mesh, nameof(mesh));
        if (!validation.IsSuccess)
        {
            return Result<PlanarityReport>.Fail(validation.Failure!);
        }

        if (!mesh.IsValid || mesh.Faces.Count == 0)
        {
            return Result<PlanarityReport>.Fail(new Failure("mesh.invalid", "Mesh must be valid and contain faces."));
        }

        double tol = context.AbsoluteTolerance;
        List<double> deviations = new(mesh.Faces.Count);
        List<int> nonPlanar = [];

        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            double deviation = FacePlanarityDeviation(mesh, mesh.Faces[i]);
            deviations.Add(deviation);
            if (deviation > tol)
            {
                nonPlanar.Add(i);
            }
        }

        PlanarityReport report = new(
            deviations.Max(),
            deviations.Average(),
            1.0 - nonPlanar.Count / (double)mesh.Faces.Count,
            nonPlanar.ToArray(),
            mesh.Faces.Count);

        return Result<PlanarityReport>.Success(report);
    }

    /// <summary>Computes mesh quality metrics.</summary>
    public Result<MeshMetrics> Metrics(RhinoMesh mesh, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<RhinoMesh> validation = Guard.AgainstNull(mesh, nameof(mesh));
        if (!validation.IsSuccess)
        {
            return Result<MeshMetrics>.Fail(validation.Failure!);
        }

        if (!mesh.IsValid)
        {
            return Result<MeshMetrics>.Fail(new Failure("mesh.invalid", "Mesh must be valid."));
        }

        mesh.FaceNormals.ComputeFaceNormals();
        mesh.Compact();

        List<double> edgeLengths = new(mesh.TopologyEdges.Count);
        for (int i = 0; i < mesh.TopologyEdges.Count; i++)
        {
            edgeLengths.Add(mesh.TopologyEdges.EdgeLine(i).Length);
        }

        List<double> faceAreas = new(mesh.Faces.Count);
        List<double> faceAngles = new(mesh.Faces.Count * 4);

        foreach (MeshFace face in mesh.Faces)
        {
            faceAreas.Add(FaceArea(mesh, face));
            faceAngles.AddRange(FaceAngles(mesh, face));
        }

        BoundingBox bbox = mesh.GetBoundingBox(false);
        Vector3d diagonal = bbox.Diagonal;
        double[] dims = [Math.Abs(diagonal.X), Math.Abs(diagonal.Y), Math.Abs(diagonal.Z)];
        Array.Sort(dims);
        double aspect = dims[0] > 0 ? dims[2] / dims[0] : double.PositiveInfinity;

        MeshMetrics metrics = new(
            edgeLengths.Count > 0 ? edgeLengths.Min() : 0,
            edgeLengths.Count > 0 ? edgeLengths.Max() : 0,
            edgeLengths.Count > 0 ? edgeLengths.Average() : 0,
            faceAreas.Count > 0 ? faceAreas.Min() : 0,
            faceAreas.Count > 0 ? faceAreas.Max() : 0,
            faceAreas.Count > 0 ? faceAreas.Average() : 0,
            faceAngles.Count > 0 ? faceAngles.Min() : 0,
            faceAngles.Count > 0 ? faceAngles.Max() : 0,
            aspect);

        return Result<MeshMetrics>.Success(metrics);
    }

    /// <summary>Validates mesh integrity.</summary>
    public Result<MeshValidation> Validate(RhinoMesh mesh, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<RhinoMesh> validation = Guard.AgainstNull(mesh, nameof(mesh));
        if (!validation.IsSuccess)
        {
            return Result<MeshValidation>.Fail(validation.Failure!);
        }

        List<string> issues = [];

        if (!mesh.IsValid)
        {
            issues.Add("Mesh is geometrically invalid.");
        }

        if (!mesh.IsClosed)
        {
            issues.Add("Mesh is not closed.");
        }

        if (mesh.DisjointMeshCount > 1)
        {
            issues.Add($"Mesh has {mesh.DisjointMeshCount} disjoint parts.");
        }

        int degenerateFaces = 0;
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            if (!mesh.Faces[i].IsValid())
            {
                degenerateFaces++;
            }
        }

        if (degenerateFaces > 0)
        {
            issues.Add($"Mesh has {degenerateFaces} degenerate faces.");
        }
        if (mesh.Faces.Count == 0)
        {
            issues.Add("Mesh has no faces.");
        }

        int nonManifold = 0;
        for (int i = 0; i < mesh.TopologyEdges.Count; i++)
        {
            if (mesh.TopologyEdges.GetConnectedFaces(i).Length > 2)
            {
                nonManifold++;
            }
        }

        if (nonManifold > 0)
        {
            issues.Add($"Mesh has {nonManifold} non-manifold edges.");
        }

        if (issues.Count == 0)
        {
            using TextLog log = new();
            MeshCheckParameters parameters = new();
            bool ok = mesh.Check(log, ref parameters);
            if (!ok)
            {
                string text = log.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    issues.Add(text);
                }
            }
        }

        MeshValidation result = new(issues.Count == 0, issues.ToArray());
        return Result<MeshValidation>.Success(result);
    }

    private static double FacePlanarityDeviation(RhinoMesh mesh, MeshFace face)
    {
        Point3d[] vertices = FaceVertices(mesh, face);
        if (vertices.Length < 4)
        {
            return 0;
        }

        PlaneFitResult fit = Plane.FitPlaneToPoints(vertices, out Plane plane);
        if (fit != PlaneFitResult.Success)
        {
            return double.MaxValue;
        }

        double max = 0;
        foreach (Point3d vertex in vertices)
        {
            max = Math.Max(max, Math.Abs(plane.DistanceTo(vertex)));
        }

        return max;
    }

    private static Point3d[] FaceVertices(RhinoMesh mesh, MeshFace face)
    {
        if (face.IsQuad)
        {
            return
            [
                mesh.Vertices[face.A],
                mesh.Vertices[face.B],
                mesh.Vertices[face.C],
                mesh.Vertices[face.D]
            ];
        }

        return
        [
            mesh.Vertices[face.A],
            mesh.Vertices[face.B],
            mesh.Vertices[face.C]
        ];
    }

    private static double FaceArea(RhinoMesh mesh, MeshFace face)
    {
        Point3d[] vertices = FaceVertices(mesh, face);

        if (face.IsQuad)
        {
            Vector3d v1 = vertices[1] - vertices[0];
            Vector3d v2 = vertices[2] - vertices[0];
            Vector3d v3 = vertices[3] - vertices[0];

            double area1 = 0.5 * Vector3d.CrossProduct(v1, v2).Length;
            double area2 = 0.5 * Vector3d.CrossProduct(v2, v3).Length;
            return area1 + area2;
        }

        Vector3d a = vertices[1] - vertices[0];
        Vector3d b = vertices[2] - vertices[0];
        return 0.5 * Vector3d.CrossProduct(a, b).Length;
    }

    private static IEnumerable<double> FaceAngles(RhinoMesh mesh, MeshFace face)
    {
        Point3d[] vertices = FaceVertices(mesh, face);
        int count = vertices.Length;

        for (int i = 0; i < count; i++)
        {
            Point3d prev = vertices[(i - 1 + count) % count];
            Point3d current = vertices[i];
            Point3d next = vertices[(i + 1) % count];

            Vector3d v1 = prev - current;
            Vector3d v2 = next - current;
            yield return Vector3d.VectorAngle(v1, v2);
        }
    }
}

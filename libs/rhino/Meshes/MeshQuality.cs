using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Arsenal.Rhino.Document;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Arsenal.Rhino.Meshes;

/// <summary>Mesh quality analysis utilities.</summary>
public static class MeshQuality
{
    /// <summary>Analyzes mesh face planarity.</summary>
    public static Result<PlanarityAnalysis> AnalyzePlanarity(Mesh? mesh, double? tolerance = null)
    {
        if (mesh is null)
        {
            return Result<PlanarityAnalysis>.Fail($"{nameof(mesh)} cannot be null");
        }

        if (!mesh.IsValid)
        {
            return Result<PlanarityAnalysis>.Fail("Mesh is not valid");
        }

        if (mesh.Faces.Count == 0)
        {
            return Result<PlanarityAnalysis>.Fail("Mesh has no faces");
        }

        double tol = tolerance ?? Tolerances.Abs();
        if (tol < 0)
        {
            return Result<PlanarityAnalysis>.Fail($"{nameof(tolerance)} must be non-negative, but was {tol}");
        }

        List<double> deviations = new(mesh.Faces.Count);
        List<int> nonPlanarFaces = [];

        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            MeshFace face = mesh.Faces[i];
            double deviation = CalculateFacePlanarityDeviation(mesh, face);
            deviations.Add(deviation);

            if (deviation > tol)
            {
                nonPlanarFaces.Add(i);
            }
        }

        double maxDeviation = deviations.Max();
        double avgDeviation = deviations.Average();
        double planarityRatio = 1.0 - nonPlanarFaces.Count / (double)mesh.Faces.Count;

        PlanarityAnalysis result = new(
            maxDeviation,
            avgDeviation,
            planarityRatio,
            [.. nonPlanarFaces],
            mesh.Faces.Count
        );

        return Result<PlanarityAnalysis>.Success(result);
    }

    /// <summary>Computes quality metrics for a mesh.</summary>
    public static Result<QualityMetrics> ComputeQualityMetrics(Mesh? mesh)
    {
        if (mesh is null)
        {
            return Result<QualityMetrics>.Fail($"{nameof(mesh)} cannot be null");
        }

        if (!mesh.IsValid)
        {
            return Result<QualityMetrics>.Fail("Mesh is not valid");
        }

        mesh.FaceNormals.ComputeFaceNormals();
        mesh.Compact();
        List<double> edgeLengths = [];
        for (int i = 0; i < mesh.TopologyEdges.Count; i++)
        {
            Line edge = mesh.TopologyEdges.EdgeLine(i);
            edgeLengths.Add(edge.Length);
        }

        double minEdgeLength = edgeLengths.Count > 0 ? edgeLengths.Min() : 0;
        double maxEdgeLength = edgeLengths.Count > 0 ? edgeLengths.Max() : 0;
        double avgEdgeLength = edgeLengths.Count > 0 ? edgeLengths.Average() : 0;

        List<double> faceAreas = [];
        List<double> faceAngles = [];

        foreach (MeshFace face in mesh.Faces)
        {
            double area = ComputeFaceAreaWithSDK(mesh, face);
            faceAreas.Add(area);

            double[] angles = ComputeFaceAnglesWithSDK(mesh, face);
            faceAngles.AddRange(angles);
        }

        double minFaceArea = faceAreas.Count > 0 ? faceAreas.Min() : 0;
        double maxFaceArea = faceAreas.Count > 0 ? faceAreas.Max() : 0;
        double avgFaceArea = faceAreas.Count > 0 ? faceAreas.Average() : 0;

        double minAngle = faceAngles.Count > 0 ? faceAngles.Min() : 0;
        double maxAngle = faceAngles.Count > 0 ? faceAngles.Max() : 0;

        BoundingBox bbox = mesh.GetBoundingBox(false);
        Vector3d diagonal = bbox.Diagonal;
        double[] dimensions = [diagonal.X, diagonal.Y, diagonal.Z];
        Array.Sort(dimensions);
        double aspectRatio = dimensions[0] > 0 ? dimensions[2] / dimensions[0] : double.PositiveInfinity;

        QualityMetrics metrics = new(
            minEdgeLength, maxEdgeLength, avgEdgeLength,
            minFaceArea, maxFaceArea, avgFaceArea,
            minAngle, maxAngle, aspectRatio
        );

        return Result<QualityMetrics>.Success(metrics);
    }

    /// <summary>Validates mesh for common quality issues.</summary>
    public static Result<ValidationResults> Validate(Mesh? mesh)
    {
        if (mesh is null)
        {
            return Result<ValidationResults>.Fail($"{nameof(mesh)} cannot be null");
        }

        List<string> issues = [];

        if (!mesh.IsValid)
        {
            issues.Add("Mesh is geometrically invalid");
        }

        if (!mesh.IsClosed)
        {
            issues.Add("Mesh is not closed (has naked edges)");
        }

        if (mesh.DisjointMeshCount > 1)
        {
            issues.Add($"Mesh has {mesh.DisjointMeshCount} disjoint parts");
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
            issues.Add($"Mesh has {degenerateFaces} degenerate faces");
        }

        if (mesh.Vertices.Count == 0)
        {
            issues.Add("Mesh has no vertices");
        }

        if (mesh.Faces.Count == 0)
        {
            issues.Add("Mesh has no faces");
        }
        int nonManifoldEdges = 0;
        for (int i = 0; i < mesh.TopologyEdges.Count; i++)
        {
            int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
            if (connectedFaces.Length > 2)
            {
                nonManifoldEdges++;
            }
        }

        if (nonManifoldEdges > 0)
        {
            issues.Add($"Mesh has {nonManifoldEdges} non-manifold edges");
        }

        if (issues.Count == 0)
        {
            using TextLog log = new();
            MeshCheckParameters parameters = new();
            bool detailedValid = mesh.Check(log, ref parameters);

            if (!detailedValid)
            {
                string logText = log.ToString();
                if (!string.IsNullOrEmpty(logText))
                {
                    issues.Add($"Detailed validation failed: {logText}");
                }
            }
        }

        bool isValid = issues.Count == 0;
        ValidationResults results = new(isValid, [.. issues]);

        return Result<ValidationResults>.Success(results);
    }

    /// <summary>Calculates planarity deviation for a mesh face.</summary>
    private static double CalculateFacePlanarityDeviation(Mesh mesh, MeshFace face)
    {
        Point3d[] vertices = GetFaceVertices(mesh, face);

        if (vertices.Length < 4)
        {
            return 0;
        }

        PlaneFitResult planeFitResult = Plane.FitPlaneToPoints(vertices, out Plane plane);
        if (planeFitResult != PlaneFitResult.Success)
        {
            return double.MaxValue;
        }
        double maxDeviation = 0;
        foreach (Point3d vertex in vertices)
        {
            double distance = Math.Abs(plane.DistanceTo(vertex));
            maxDeviation = Math.Max(maxDeviation, distance);
        }

        return maxDeviation;
    }

    /// <summary>Gets vertices of a mesh face.</summary>
    private static Point3d[] GetFaceVertices(Mesh mesh, MeshFace face)
    {
        return face.IsQuad
            ? [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D]]
            : [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C]];
    }

    /// <summary>Computes face area.</summary>
    private static double ComputeFaceAreaWithSDK(Mesh mesh, MeshFace face)
    {
        Point3d[] vertices = GetFaceVertices(mesh, face);

        if (face.IsQuad)
        {
            Vector3d v1 = vertices[1] - vertices[0];
            Vector3d v2 = vertices[2] - vertices[0];
            Vector3d v3 = vertices[3] - vertices[0];

            double area1 = 0.5 * Vector3d.CrossProduct(v1, v2).Length;
            double area2 = 0.5 * Vector3d.CrossProduct(v2, v3).Length;
            return area1 + area2;
        }
        else
        {
            Vector3d v1 = vertices[1] - vertices[0];
            Vector3d v2 = vertices[2] - vertices[0];
            return 0.5 * Vector3d.CrossProduct(v1, v2).Length;
        }
    }

    /// <summary>Computes internal angles of a mesh face.</summary>
    private static double[] ComputeFaceAnglesWithSDK(Mesh mesh, MeshFace face)
    {
        Point3d[] vertices = GetFaceVertices(mesh, face);
        int count = face.IsQuad ? 4 : 3;
        double[] angles = new double[count];

        for (int i = 0; i < count; i++)
        {
            Point3d prev = vertices[(i - 1 + count) % count];
            Point3d curr = vertices[i];
            Point3d next = vertices[(i + 1) % count];

            Vector3d v1 = prev - curr;
            Vector3d v2 = next - curr;

            angles[i] = Vector3d.VectorAngle(v1, v2);
        }

        return angles;
    }
}

/// <summary>Mesh planarity analysis results.</summary>
public readonly record struct PlanarityAnalysis(
    double MaxDeviation,
    double AverageDeviation,
    double PlanarityRatio,
    int[] NonPlanarFaceIndices,
    int TotalFaceCount
);

/// <summary>Mesh quality metrics.</summary>
public readonly record struct QualityMetrics(
    double MinEdgeLength,
    double MaxEdgeLength,
    double AverageEdgeLength,
    double MinFaceArea,
    double MaxFaceArea,
    double AverageFaceArea,
    double MinAngle,
    double MaxAngle,
    double AspectRatio
);

/// <summary>Mesh validation results.</summary>
public readonly record struct ValidationResults(
    bool IsValid,
    string[] Issues
);

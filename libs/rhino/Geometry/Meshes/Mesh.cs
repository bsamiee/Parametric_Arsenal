using System.Collections.Generic;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using RhinoMesh = Rhino.Geometry.Mesh;

namespace Arsenal.Rhino.Geometry.Meshes;

/// <summary>Mesh operations using RhinoCommon.</summary>
public sealed class MeshOperations : IMesh
{
    /// <summary>Extracts all mesh vertices.</summary>
    public Result<IReadOnlyList<Point3d>> Vertices(RhinoMesh mesh)
    {
        Result<RhinoMesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(meshResult.Failure!);
        }

        Point3d[] vertices = mesh.Vertices.ToPoint3dArray();
        return Result<IReadOnlyList<Point3d>>.Success(vertices);
    }

    /// <summary>Extracts all mesh edge curves.</summary>
    public Result<IReadOnlyList<Curve>> Edges(RhinoMesh mesh)
    {
        Result<RhinoMesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<Curve>>.Fail(meshResult.Failure!);
        }

        MeshTopologyEdgeList topologyEdges = mesh.TopologyEdges;
        List<Curve> edges = new(topologyEdges.Count);

        for (int i = 0; i < topologyEdges.Count; i++)
        {
            Line edgeLine = topologyEdges.EdgeLine(i);
            edges.Add(new LineCurve(edgeLine));
        }

        return Result<IReadOnlyList<Curve>>.Success(edges);
    }

    /// <summary>Computes midpoints of all mesh edges.</summary>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(Mesh mesh)
    {
        Result<RhinoMesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(meshResult.Failure!);
        }

        MeshTopologyEdgeList topologyEdges = mesh.TopologyEdges;
        Point3d[] midpoints = new Point3d[topologyEdges.Count];

        for (int i = 0; i < topologyEdges.Count; i++)
        {
            Line edgeLine = topologyEdges.EdgeLine(i);
            midpoints[i] = edgeLine.PointAt(0.5);
        }

        return Result<IReadOnlyList<Point3d>>.Success(midpoints);
    }

    private static Result<RhinoMesh> ValidateMesh(RhinoMesh? mesh)
    {
        Result<RhinoMesh> guard = Guard.AgainstNull(mesh, nameof(mesh));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<RhinoMesh>.Fail(new Failure("mesh.invalid", "Mesh is not valid."));
        }

        return guard;
    }
}

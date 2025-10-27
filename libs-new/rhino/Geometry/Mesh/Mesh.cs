using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Mesh;

/// <summary>RhinoCommon-backed mesh operations.</summary>
public sealed class MeshOperations : IMesh
{
    /// <summary>Extracts all vertices from the mesh.</summary>
    /// <param name="mesh">The mesh to extract vertices from.</param>
    /// <returns>A result containing the vertices or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> Vertices(global::Rhino.Geometry.Mesh mesh)
    {
        Result<global::Rhino.Geometry.Mesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(meshResult.Failure!);
        }

        Point3d[] vertices = mesh.Vertices.ToPoint3dArray();
        return Result<IReadOnlyList<Point3d>>.Success(vertices);
    }

    /// <summary>Extracts all edges from the mesh as line curves.</summary>
    /// <param name="mesh">The mesh to extract edges from.</param>
    /// <returns>A result containing the edges or a failure.</returns>
    public Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.Mesh mesh)
    {
        Result<global::Rhino.Geometry.Mesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Fail(meshResult.Failure!);
        }

        global::Rhino.Geometry.Collections.MeshTopologyEdgeList topologyEdges = mesh.TopologyEdges;
        List<global::Rhino.Geometry.Curve> edges = new(topologyEdges.Count);

        for (int i = 0; i < topologyEdges.Count; i++)
        {
            Line edgeLine = topologyEdges.EdgeLine(i);
            edges.Add(new LineCurve(edgeLine));
        }

        return Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Success(edges);
    }

    /// <summary>Computes the midpoints of all edges in the mesh.</summary>
    /// <param name="mesh">The mesh to compute edge midpoints for.</param>
    /// <returns>A result containing the edge midpoints or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(global::Rhino.Geometry.Mesh mesh)
    {
        Result<global::Rhino.Geometry.Mesh> meshResult = ValidateMesh(mesh);
        if (!meshResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(meshResult.Failure!);
        }

        global::Rhino.Geometry.Collections.MeshTopologyEdgeList topologyEdges = mesh.TopologyEdges;
        Point3d[] midpoints = new Point3d[topologyEdges.Count];

        for (int i = 0; i < topologyEdges.Count; i++)
        {
            Line edgeLine = topologyEdges.EdgeLine(i);
            midpoints[i] = edgeLine.PointAt(0.5);
        }

        return Result<IReadOnlyList<Point3d>>.Success(midpoints);
    }

    private static Result<global::Rhino.Geometry.Mesh> ValidateMesh(global::Rhino.Geometry.Mesh? mesh)
    {
        Result<global::Rhino.Geometry.Mesh> guard = Guard.AgainstNull(mesh, nameof(mesh));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<global::Rhino.Geometry.Mesh>.Fail(new Failure("mesh.invalid", "Mesh is not valid."));
        }

        return guard;
    }
}

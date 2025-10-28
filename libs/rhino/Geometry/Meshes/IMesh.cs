using System.Collections.Generic;
using Arsenal.Core.Result;
using Rhino.Geometry;
using RhinoMesh = Rhino.Geometry.Mesh;

namespace Arsenal.Rhino.Geometry.Meshes;

/// <summary>Mesh geometry operations.</summary>
public interface IMesh
{
    /// <summary>Extracts all mesh vertices.</summary>
    Result<IReadOnlyList<Point3d>> Vertices(RhinoMesh mesh);

    /// <summary>Extracts all mesh edge curves.</summary>
    Result<IReadOnlyList<Curve>> Edges(RhinoMesh mesh);

    /// <summary>Computes midpoints of all mesh edges.</summary>
    Result<IReadOnlyList<Point3d>> EdgeMidpoints(RhinoMesh mesh);
}

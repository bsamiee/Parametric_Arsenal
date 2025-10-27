using System.Collections.Generic;
using Arsenal.Core.Result;

namespace Arsenal.Rhino.Geometry.Mesh;

/// <summary>Operations available for Rhino meshes.</summary>
public interface IMesh
{
    /// <summary>Extracts vertices from the mesh.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> Vertices(global::Rhino.Geometry.Mesh mesh);

    /// <summary>Extracts edge curves from the mesh.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.Mesh mesh);

    /// <summary>Calculates midpoints of all mesh edges.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> EdgeMidpoints(global::Rhino.Geometry.Mesh mesh);
}

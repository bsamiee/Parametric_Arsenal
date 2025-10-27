using System.Collections.Generic;
using Arsenal.Core.Result;
using Rhino.Geometry;
using RhinoBrep = Rhino.Geometry.Brep;

namespace Arsenal.Rhino.Geometry.Brep;

/// <summary>Brep geometry operations.</summary>
public interface IBrep
{
    /// <summary>Extracts all vertices from the brep.</summary>
    Result<IReadOnlyList<Point3d>> Vertices(RhinoBrep brep);

    /// <summary>Extracts all edge curves from the brep.</summary>
    Result<IReadOnlyList<Curve>> Edges(RhinoBrep brep);

    /// <summary>Extracts all face surfaces from the brep.</summary>
    Result<IReadOnlyList<GeometryBase>> Faces(RhinoBrep brep);

    /// <summary>Computes midpoints of all brep edges.</summary>
    Result<IReadOnlyList<Point3d>> EdgeMidpoints(RhinoBrep brep);
}

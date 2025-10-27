using System.Collections.Generic;
using Arsenal.Core.Result;

namespace Arsenal.Rhino.Geometry.Brep;

/// <summary>Operations available for Rhino breps.</summary>
public interface IBrep
{
    /// <summary>Extracts vertices from the brep.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> Vertices(global::Rhino.Geometry.Brep brep);

    /// <summary>Extracts edge curves from the brep.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.Brep brep);

    /// <summary>Extracts face surfaces from the brep.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>> Faces(global::Rhino.Geometry.Brep brep);

    /// <summary>Calculates midpoints of all brep edges.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> EdgeMidpoints(global::Rhino.Geometry.Brep brep);
}

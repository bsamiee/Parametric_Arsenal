using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;
using RhinoVector3d = Rhino.Geometry.Vector3d;

namespace Arsenal.Rhino.Analysis.Vector;

/// <summary>Vector extraction operations.</summary>
public interface IVectorAnalysis
{
    /// <summary>Extracts vector samples from geometry.</summary>
    Result<IReadOnlyList<VectorSample>> ExtractAll(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts tangent vectors from geometry.</summary>
    Result<IReadOnlyList<RhinoVector3d>> Tangents(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts normal vectors from geometry.</summary>
    Result<IReadOnlyList<RhinoVector3d>> Normals(GeometryBase geometry, GeoContext context);
}

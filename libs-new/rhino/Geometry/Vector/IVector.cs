using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Vector;

/// <summary>Vector extraction utilities for Rhino geometry.</summary>
public interface IVector
{
    /// <summary>Extracts all vector samples from the geometry.</summary>
    /// <param name="geometry">The geometry to extract vectors from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing vector samples or a failure.</returns>
    Result<IReadOnlyList<VectorSample>> ExtractAll(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts tangent vectors from the geometry.</summary>
    /// <param name="geometry">The geometry to extract tangents from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing tangent vectors or a failure.</returns>
    Result<IReadOnlyList<Vector3d>> Tangents(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts normal vectors from the geometry.</summary>
    /// <param name="geometry">The geometry to extract normals from.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing normal vectors or a failure.</returns>
    Result<IReadOnlyList<Vector3d>> Normals(GeometryBase geometry, GeoContext context);
}

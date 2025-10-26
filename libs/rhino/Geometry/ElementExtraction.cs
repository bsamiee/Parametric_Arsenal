using Arsenal.Core;
using Arsenal.Rhino.Curves;
using Arsenal.Rhino.Points;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>High-level geometry element extraction using the GeometryAdapters pattern.</summary>
public static class ElementExtraction
{
    /// <summary>Extracts all vertices from geometry as Point3d objects.</summary>
    public static Result<Point3d[]> ExtractVertices(GeometryBase? geom)
    {
        Result<Point3d[]> result = GeometryTraversal.Extract(geom, GeometryAdapters.GetVertices);
        if (!result.Ok || result.Value is null)
        {
            return result;
        }

        // Deduplicate vertices using document tolerance
        return PointDeduplication.RemoveWithDocTolerance(result.Value);
    }

    /// <summary>Extracts all edges from geometry as curve objects.</summary>
    public static Result<Curve[]> ExtractEdges(GeometryBase? geom)
    {
        return GeometryTraversal.Extract(geom, GeometryAdapters.GetEdges);
    }

    /// <summary>Extracts all faces from geometry as surface or mesh face objects.</summary>
    public static Result<GeometryBase[]> ExtractFaces(GeometryBase? geom)
    {
        return GeometryTraversal.Extract(geom, GeometryAdapters.GetFaces);
    }

    /// <summary>Extracts deduplicated edge midpoints from geometry.</summary>
    public static Result<Point3d[]> ExtractEdgeMidpoints(GeometryBase? geom)
    {
        Result<Point3d[]> result = GeometryTraversal.Extract(geom, GeometryAdapters.GetEdgeMidpoints);
        if (!result.Ok || result.Value is null)
        {
            return result;
        }

        // Deduplicate midpoints using document tolerance
        return PointDeduplication.RemoveWithDocTolerance(result.Value);
    }

    /// <summary>Extracts edge state points: quadrants for circles/ellipses, midpoints for all other geometry.</summary>
    public static Result<Point3d[]> ExtractEdgeStatePoints(GeometryBase? geom)
    {
        if (geom is null)
        {
            return Result<Point3d[]>.Fail("Geometry cannot be null");
        }

        if (geom is Curve curve)
        {
            // Try quadrant extraction for circles/ellipses first
            Result<Point3d[]> quadrantResult = QuadrantExtraction.ExtractPoints(curve);
            if (quadrantResult.Ok)
            {
                return quadrantResult;
            }
        }

        // Fall back to edge midpoints for all other geometry
        return ExtractEdgeMidpoints(geom);
    }
}

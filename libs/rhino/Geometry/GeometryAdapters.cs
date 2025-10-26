using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Resource-safe geometry adapters that handle SDK-specific extraction patterns.</summary>
public static class GeometryAdapters
{
    /// <summary>Extracts vertices from any geometry type.</summary>
    public static Point3d[] GetVertices(GeometryBase geometry)
    {
        return geometry switch
        {
            Brep brep => brep.DuplicateVertices(),
            Mesh mesh => mesh.Vertices.ToPoint3dArray(),
            Extrusion extrusion => ExtractExtrusionVertices(extrusion),
            SubD subd => ExtractSubDVertices(subd),
            PolyCurve polyCurve => ExtractPolyCurveVertices(polyCurve),
            PolylineCurve polylineCurve when polylineCurve.TryGetPolyline(out Polyline pl) =>
                pl.ToArray(),
            NurbsCurve nurbsCurve => nurbsCurve.Points.Select(p => p.Location).ToArray(),
            Curve curve => [curve.PointAtStart, curve.PointAtEnd],
            Surface surface => ExtractSurfaceVertices(surface),
            Point point => [point.Location],
            _ => []
        };
    }

    /// <summary>Extracts edges from any geometry type.</summary>
    public static Curve[] GetEdges(GeometryBase geometry)
    {
        return geometry switch
        {
            Brep brep => brep.Edges
                .Where(e => e.EdgeCurve is not null)
                .Select(e => e.EdgeCurve!)
                .ToArray(),
            Mesh mesh => ExtractMeshEdges(mesh),
            Extrusion extrusion => ExtractExtrusionEdges(extrusion),
            SubD subd => ExtractSubDEdges(subd),
            PolyCurve polyCurve => Enumerable.Range(0, polyCurve.SegmentCount)
                .Select(polyCurve.SegmentCurve)
                .ToArray(),
            PolylineCurve polylineCurve when polylineCurve.TryGetPolyline(out Polyline pl) =>
                ExtractPolylineEdges(pl),
            Curve curve => [curve],
            Surface surface => ExtractSurfaceEdges(surface),
            _ => []
        };
    }

    /// <summary>Extracts faces from any geometry type.</summary>
    public static GeometryBase[] GetFaces(GeometryBase geometry)
    {
        return geometry switch
        {
            Brep brep => ExtractBrepFaces(brep),
            Mesh mesh => ExtractMeshFaces(mesh),
            Extrusion extrusion => ExtractExtrusionFaces(extrusion),
            SubD subd => ExtractSubDFaces(subd),
            Surface surface => [surface],
            _ => []
        };
    }

    /// <summary>Extracts edge midpoints from any geometry type.</summary>
    public static Point3d[] GetEdgeMidpoints(GeometryBase geometry)
    {
        return geometry switch
        {
            Brep brep => brep.Edges.Select(e => e.PointAt(e.Domain.Mid)).ToArray(),
            Mesh mesh => ExtractMeshEdgeMidpoints(mesh),
            PolyCurve polyCurve => Enumerable.Range(0, polyCurve.SegmentCount)
                .Select(i => polyCurve.SegmentCurve(i))
                .Select(seg => seg.PointAt(seg.Domain.Mid))
                .ToArray(),
            PolylineCurve polylineCurve when polylineCurve.TryGetPolyline(out Polyline pl) =>
                ExtractPolylineMidpoints(pl),
            Curve curve => [curve.PointAt(curve.Domain.Mid)],
            _ => []
        };
    }

    private static Point3d[] ExtractPolyCurveVertices(PolyCurve polyCurve)
    {
        List<Point3d> vertices = [];
        for (int i = 0; i < polyCurve.SegmentCount; i++)
        {
            Curve segment = polyCurve.SegmentCurve(i);
            vertices.Add(segment.PointAtStart);
            vertices.Add(segment.PointAtEnd);
        }

        return vertices.ToArray();
    }

    private static Point3d[] ExtractSurfaceVertices(Surface surface)
    {
        return WithTemporaryBrep(surface, brep => brep.DuplicateVertices());
    }

    private static Point3d[] ExtractExtrusionVertices(Extrusion extrusion)
    {
        return WithTemporaryBrep(extrusion, brep => brep.DuplicateVertices());
    }

    private static Point3d[] ExtractSubDVertices(SubD subd)
    {
        return WithTemporaryBrep(subd, brep => brep.DuplicateVertices());
    }

    private static Curve[] ExtractMeshEdges(Mesh mesh)
    {
        return ExtractTopologyEdges(mesh,
            m => m.TopologyEdges.Count,
            (m, i) => m.TopologyEdges.EdgeLine(i));
    }

    private static Curve[] ExtractPolylineEdges(Polyline polyline)
    {
        return ExtractTopologyEdges(polyline,
            p => p.SegmentCount,
            (p, i) => p.SegmentAt(i));
    }

    private static Curve[] ExtractSurfaceEdges(Surface surface)
    {
        return WithTemporaryBrep(surface, brep => brep.Edges
            .Where(e => e.EdgeCurve is not null)
            .Select(e => e.EdgeCurve!)
            .ToArray());
    }

    private static Curve[] ExtractExtrusionEdges(Extrusion extrusion)
    {
        return WithTemporaryBrep(extrusion, brep => brep.Edges
            .Where(e => e.EdgeCurve is not null)
            .Select(e => e.EdgeCurve!)
            .ToArray());
    }

    private static Curve[] ExtractSubDEdges(SubD subd)
    {
        return WithTemporaryBrep(subd, brep => brep.Edges
            .Where(e => e.EdgeCurve is not null)
            .Select(e => e.EdgeCurve!)
            .ToArray());
    }

    private static GeometryBase[] ExtractBrepFaces(Brep brep)
    {
        int faceCount = brep.Faces.Count;
        List<GeometryBase> faces = new(faceCount);

        for (int i = 0; i < faceCount; i++)
        {
            Brep? extractedFace = brep.Faces.ExtractFace(i);
            if (extractedFace is not null)
            {
                faces.Add(extractedFace);
            }
        }

        return faces.ToArray();
    }

    private static GeometryBase[] ExtractMeshFaces(Mesh mesh)
    {
        int faceCount = mesh.Faces.Count;
        GeometryBase[] faces = new GeometryBase[faceCount];

        for (int i = 0; i < faceCount; i++)
        {
            MeshFace face = mesh.Faces[i];
            Mesh singleFaceMesh = new();

            singleFaceMesh.Vertices.Add(mesh.Vertices[face.A]);
            singleFaceMesh.Vertices.Add(mesh.Vertices[face.B]);
            singleFaceMesh.Vertices.Add(mesh.Vertices[face.C]);

            if (face.IsQuad)
            {
                singleFaceMesh.Vertices.Add(mesh.Vertices[face.D]);
                singleFaceMesh.Faces.AddFace(0, 1, 2, 3);
            }
            else
            {
                singleFaceMesh.Faces.AddFace(0, 1, 2);
            }

            faces[i] = singleFaceMesh;
        }

        return faces;
    }

    private static GeometryBase[] ExtractExtrusionFaces(Extrusion extrusion)
    {
        return WithTemporaryBrep(extrusion, ExtractBrepFaces);
    }

    private static GeometryBase[] ExtractSubDFaces(SubD subd)
    {
        return WithTemporaryBrep(subd, ExtractBrepFaces);
    }

    private static Point3d[] ExtractMeshEdgeMidpoints(Mesh mesh)
    {
        return ExtractTopologyMidpoints(mesh,
            m => m.TopologyEdges.Count,
            (m, i) => m.TopologyEdges.EdgeLine(i));
    }

    private static Point3d[] ExtractPolylineMidpoints(Polyline polyline)
    {
        return ExtractTopologyMidpoints(polyline,
            p => p.SegmentCount,
            (p, i) => p.SegmentAt(i));
    }

    private static T[] WithTemporaryBrep<T>(Surface surface, Func<Brep, T[]> operation)
    {
        using Brep? brep = Brep.CreateFromSurface(surface);
        return brep is not null ? operation(brep) : [];
    }

    private static T[] WithTemporaryBrep<T>(Extrusion extrusion, Func<Brep, T[]> operation)
    {
        using Brep? brep = extrusion.ToBrep();
        return brep is not null ? operation(brep) : [];
    }

    private static T[] WithTemporaryBrep<T>(SubD subd, Func<Brep, T[]> operation)
    {
        using Brep? brep = subd.ToBrep();
        return brep is not null ? operation(brep) : [];
    }

    private static Curve[] ExtractTopologyEdges<T>(T geometry, Func<T, int> getCount, Func<T, int, Line> getEdgeLine)
    {
        int edgeCount = getCount(geometry);
        Curve[] edges = new Curve[edgeCount];

        for (int i = 0; i < edgeCount; i++)
        {
            Line edgeLine = getEdgeLine(geometry, i);
            edges[i] = new LineCurve(edgeLine);
        }

        return edges;
    }

    private static Point3d[] ExtractTopologyMidpoints<T>(T geometry, Func<T, int> getCount,
        Func<T, int, Line> getEdgeLine)
    {
        int edgeCount = getCount(geometry);
        Point3d[] midpoints = new Point3d[edgeCount];

        for (int i = 0; i < edgeCount; i++)
        {
            Line edgeLine = getEdgeLine(geometry, i);
            midpoints[i] = edgeLine.PointAt(0.5);
        }

        return midpoints;
    }
}

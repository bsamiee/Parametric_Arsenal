using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Provides centroid and midpoint calculations for various geometry types.</summary>
public static class GeometryCentroids
{
    /// <summary>Gets the centroid or characteristic point for any geometry type using proper SDK methods.</summary>
    /// <remarks>Uses fallback chain: native center → mass properties → curve midpoint → bounding box center.</remarks>
    public static Point3d GetCentroid(GeometryBase? geometry)
    {
        if (geometry is null)
        {
            return Point3d.Origin;
        }

        // Try native center properties first
        if (TryGetNativeCenter(geometry, out Point3d center))
        {
            return center;
        }

        // Try mass properties for accurate centroids
        if (TryGetMassPropertiesCentroid(geometry, out Point3d massCentroid))
        {
            return massCentroid;
        }

        // Try curve-specific methods
        if (geometry is Curve curve)
        {
            return GetCurveCentroid(curve);
        }

        // Handle specific geometry types with specialized methods
        return geometry switch
        {
            Extrusion extrusion => GetExtrusionCentroid(extrusion),
            PointCloud pointCloud => GetPointCloudCentroid(pointCloud),
            AnnotationBase annotation => GetAnnotationCentroid(annotation),
            _ => GetBoundingBoxCenter(geometry)
        };
    }

    /// <summary>Gets the centroid for curve geometry using the most appropriate method.</summary>
    private static Point3d GetCurveCentroid(Curve curve)
    {
        // Try to get specific curve types for optimal centroid calculation
        if (curve.TryGetCircle(out Circle circle))
        {
            return circle.Center;
        }

        if (curve.TryGetArc(out Arc arc))
        {
            return arc.Center;
        }

        if (curve.TryGetPolyline(out Polyline polyline))
        {
            return GetPolylineCentroid(polyline);
        }

        // Check for line curves
        if (curve is LineCurve lineCurve)
        {
            return lineCurve.Line.PointAt(0.5);
        }

        // For general curves, use arc-length-based midpoint
        return curve.PointAtNormalizedLength(0.5);
    }

    /// <summary>Gets the centroid of a polyline using vertex averaging.</summary>
    private static Point3d GetPolylineCentroid(Polyline polyline)
    {
        if (polyline.Count == 0)
        {
            return Point3d.Origin;
        }

        Point3d sum = Point3d.Origin;
        foreach (Point3d vertex in polyline)
        {
            sum += vertex;
        }

        return sum / polyline.Count;
    }

    /// <summary>Attempts to get centroid using native center properties for geometries that have them.</summary>
    private static bool TryGetNativeCenter(GeometryBase geometry, out Point3d center)
    {
        center = Point3d.Origin;

        return geometry switch
        {
            Point point => SetAndReturn(point.Location, out center),
            _ => false
        };

        static bool SetAndReturn(Point3d value, out Point3d output)
        {
            output = value;
            return true;
        }
    }

    /// <summary>Attempts to get centroid using mass properties for accurate calculations.</summary>
    private static bool TryGetMassPropertiesCentroid(GeometryBase geometry, out Point3d centroid)
    {
        centroid = Point3d.Origin;

        try
        {
            // Try volume properties for closed solids first
            if (geometry is Brep { IsSolid: true } brep)
            {
                VolumeMassProperties? volumeProps = VolumeMassProperties.Compute(brep);
                if (volumeProps != null)
                {
                    centroid = volumeProps.Centroid;
                    volumeProps.Dispose();
                    return true;
                }
            }

            // Try area properties for surfaces, meshes, breps, and hatches
            AreaMassProperties? areaProps = geometry switch
            {
                Mesh mesh => AreaMassProperties.Compute(mesh),
                Surface surface => AreaMassProperties.Compute(surface),
                Brep b => AreaMassProperties.Compute(b),
                Hatch hatch => AreaMassProperties.Compute(hatch),
                _ => null
            };

            if (areaProps != null)
            {
                centroid = areaProps.Centroid;
                areaProps.Dispose();
                return true;
            }
        }
        catch
        {
            // Mass properties can fail for degenerate geometry
        }

        return false;
    }

    /// <summary>Gets the centroid of an extrusion using its profile curve centroid.</summary>
    private static Point3d GetExtrusionCentroid(Extrusion extrusion)
    {
        // Get the profile curve and find its centroid
        Curve? profile = extrusion.Profile3d(new ComponentIndex(ComponentIndexType.ExtrusionBottomProfile, 0));
        if (profile != null)
        {
            Point3d profileCentroid = GetCurveCentroid(profile);
            // Project the centroid to the middle of the extrusion height
            Vector3d extrusionVector = extrusion.PathTangent;
            double pathLength = extrusion.PathStart.DistanceTo(extrusion.PathEnd);
            return profileCentroid + extrusionVector * (pathLength * 0.5);
        }

        return GetBoundingBoxCenter(extrusion);
    }

    /// <summary>Gets the centroid of a point cloud using point averaging.</summary>
    private static Point3d GetPointCloudCentroid(PointCloud pointCloud)
    {
        if (pointCloud.Count == 0)
        {
            return Point3d.Origin;
        }

        Point3d sum = Point3d.Origin;
        foreach (PointCloudItem point in pointCloud)
        {
            sum += point.Location;
        }

        return sum / pointCloud.Count;
    }

    /// <summary>Gets the centroid of annotation geometry using its text position or bounding box.</summary>
    private static Point3d GetAnnotationCentroid(AnnotationBase annotation)
    {
        // For text entities, use the text position
        if (annotation is TextEntity textEntity)
        {
            return textEntity.Plane.Origin;
        }

        // For dimensions, convert 2D text position to 3D using the dimension plane
        if (annotation is Dimension dimension)
        {
            Point2d textPos2d = dimension.TextPosition;
            return dimension.Plane.PointAt(textPos2d.X, textPos2d.Y);
        }

        // For leaders, use the plane origin as the reference point
        if (annotation is Leader leader)
        {
            return leader.Plane.Origin;
        }

        // Fallback to bounding box center for other annotation types
        return GetBoundingBoxCenter(annotation);
    }

    /// <summary>Fallback method using bounding box center for any geometry type.</summary>
    private static Point3d GetBoundingBoxCenter(GeometryBase geometry)
    {
        BoundingBox bbox = geometry.GetBoundingBox(true);
        return bbox.IsValid ? bbox.Center : Point3d.Origin;
    }
}

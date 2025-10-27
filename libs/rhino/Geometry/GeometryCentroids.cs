using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Provides centroid calculations for any geometry type using the most appropriate SDK method.</summary>
public static class GeometryCentroids
{
    /// <summary>Gets the centroid for any geometry type using the most appropriate SDK method.</summary>
    /// <remarks>Uses GeometryDetection to classify geometry and select the optimal mass properties method.</remarks>
    public static Point3d GetCentroid(GeometryBase? geometry)
    {
        // Null geometry validation with clear fallback
        if (geometry is null)
        {
            return Point3d.Origin;
        }

        // Geometry validity checking before mass properties computation
        if (!geometry.IsValid)
        {
            return GetBoundingBoxCenterWithFallback(geometry);
        }

        // Use GeometryDetection to determine the appropriate mass properties method
        Result<MassPropertiesMethod> methodResult = GeometryDetection.GetMassPropertiesMethod(geometry);
        if (!methodResult.Ok)
        {
            return GetBoundingBoxCenterWithFallback(geometry);
        }

        // Apply the appropriate SDK method based on classification
        Point3d centroid = methodResult.Value switch
        {
            MassPropertiesMethod.Volume => GetVolumeCentroid(geometry),
            MassPropertiesMethod.Area => GetAreaCentroid(geometry),
            MassPropertiesMethod.Length => GetLengthCentroid(geometry),
            _ => GetBoundingBoxCenterWithFallback(geometry)
        };

        // Validate that computed centroid is a valid point
        return centroid.IsValid ? centroid : Point3d.Origin;
    }

    /// <summary>Gets the centroid using VolumeMassProperties for solid breps.</summary>
    private static Point3d GetVolumeCentroid(GeometryBase geometry)
    {
        try
        {
            VolumeMassProperties? volumeProps = geometry switch
            {
                Brep brep => VolumeMassProperties.Compute(brep),
                Mesh mesh => VolumeMassProperties.Compute(mesh),
                Surface surface => VolumeMassProperties.Compute(surface),
                _ => null
            };

            if (volumeProps != null)
            {
                using (volumeProps)
                {
                    if (volumeProps.Centroid.IsValid)
                    {
                        return volumeProps.Centroid;
                    }
                }
            }
        }
        catch
        {
            // Mass properties computation can fail for degenerate geometry
        }

        // Fallback to bounding box center when mass properties fail
        return GetBoundingBoxCenterWithFallback(geometry);
    }

    /// <summary>Gets the centroid using AreaMassProperties for surfaces, meshes, non-solid breps, and closed planar curves.</summary>
    private static Point3d GetAreaCentroid(GeometryBase geometry)
    {
        try
        {
            AreaMassProperties? areaProps = geometry switch
            {
                Brep brep => AreaMassProperties.Compute(brep),
                Surface surface => AreaMassProperties.Compute(surface),
                Mesh mesh => AreaMassProperties.Compute(mesh),
                Curve curve => AreaMassProperties.Compute(curve),
                Hatch hatch => AreaMassProperties.Compute(hatch),
                _ => null
            };

            if (areaProps != null)
            {
                using (areaProps)
                {
                    if (areaProps.Centroid.IsValid)
                    {
                        return areaProps.Centroid;
                    }
                }
            }
        }
        catch
        {
            // Mass properties computation can fail for degenerate geometry
        }

        // Fallback to bounding box center when mass properties fail
        return GetBoundingBoxCenterWithFallback(geometry);
    }

    /// <summary>Gets the centroid using LengthMassProperties for open curves.</summary>
    private static Point3d GetLengthCentroid(GeometryBase geometry)
    {
        try
        {
            if (geometry is Curve curve)
            {
                using LengthMassProperties? lengthProps = LengthMassProperties.Compute(curve);
                if (lengthProps is { Centroid.IsValid: true })
                {
                    return lengthProps.Centroid;
                }
            }
        }
        catch
        {
            // Mass properties computation can fail for degenerate geometry
        }

        // Fallback to bounding box center when mass properties fail
        return GetBoundingBoxCenterWithFallback(geometry);
    }

    /// <summary>Fallback method using bounding box center with final fallback to origin when bounding box is invalid.</summary>
    private static Point3d GetBoundingBoxCenterWithFallback(GeometryBase geometry)
    {
        try
        {
            BoundingBox bbox = geometry.GetBoundingBox(true);
            return bbox is { IsValid: true, Center.IsValid: true } ? bbox.Center : Point3d.Origin;
        }
        catch
        {
            // Bounding box computation can fail for some geometry types
            return Point3d.Origin;
        }
    }
}

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing algorithms using RhinoCommon RTree SDK with bitwise combinable operations.</summary>
[Flags]
public enum SpatialMethod {
    /// <summary>No spatial operation specified.</summary>
    None = 0,

    /// <summary>Point array range queries using RTree.CreateFromPointArray with spatial bounds search.</summary>
    PointsRange = 1,

    /// <summary>Point array proximity queries using RTree.Point3dKNeighbors and RTree.Point3dClosestPoints algorithms.</summary>
    PointsProximity = 2,

    /// <summary>PointCloud range queries using RTree.CreatePointCloudTree with spatial bounds search.</summary>
    PointCloudRange = 4,

    /// <summary>PointCloud proximity queries using RTree.PointCloudKNeighbors and RTree.PointCloudClosestPoints algorithms.</summary>
    PointCloudProximity = 8,

    /// <summary>Mesh face range queries using RTree.CreateMeshFaceTree with spatial bounds search.</summary>
    MeshRange = 16,

    /// <summary>Mesh overlap detection using RTree.CreateMeshFaceTree and RTree.SearchOverlaps with tolerance handling.</summary>
    MeshOverlap = 32,

    /// <summary>Curve range queries using manual RTree construction with bounding box insertion.</summary>
    CurveRange = 64,

    /// <summary>Surface range queries using manual RTree construction with bounding box insertion.</summary>
    SurfaceRange = 128,

    /// <summary>Brep range queries using manual RTree construction with bounding box insertion.</summary>
    BrepRange = 256,
}

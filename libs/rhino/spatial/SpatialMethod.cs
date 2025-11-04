namespace Arsenal.Rhino.Spatial;

/// <summary>Unified spatial method combining index types and operations into bitwise flags.</summary>
[Flags]
public enum SpatialMethod {
    /// <summary>No spatial operation specified.</summary>
    None = 0,

    /// <summary>Point3d array with range queries using RTree.CreateFromPointArray and RTree.Search methods.</summary>
    PointsRange = 1,

    /// <summary>Point3d array with proximity queries using RTree.Point3dKNeighbors and RTree.Point3dClosestPoints.</summary>
    PointsProximity = 2,

    /// <summary>PointCloud with range queries using RTree.CreatePointCloudTree and RTree.Search methods.</summary>
    PointCloudRange = 4,

    /// <summary>PointCloud with proximity queries using RTree.PointCloudKNeighbors and RTree.PointCloudClosestPoints.</summary>
    PointCloudProximity = 8,

    /// <summary>Mesh with range queries using RTree.CreateMeshFaceTree and RTree.Search methods.</summary>
    MeshRange = 16,

    /// <summary>Mesh with overlap queries using RTree.CreateMeshFaceTree and RTree.SearchOverlaps methods.</summary>
    MeshOverlap = 32,

    /// <summary>Curve with range queries using manual RTree.Insert and RTree.Search methods.</summary>
    CurveRange = 64,

    /// <summary>Surface with range queries using manual RTree.Insert and RTree.Search methods.</summary>
    SurfaceRange = 128,

    /// <summary>Brep with range queries using manual RTree.Insert and RTree.Search methods.</summary>
    BrepRange = 256,

    /// <summary>Dynamic tree building using RTree.Insert/Remove for incremental spatial indexing.</summary>
    DynamicInsertion = 512,
}

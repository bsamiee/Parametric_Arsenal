using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>RTree cache management with ConditionalWeakTable for automatic memory reclamation.</summary>
internal static class SpatialCache {
    /// <summary>Weak reference cache enabling automatic garbage collection when geometry objects are disposed.</summary>
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = [];

    /// <summary>Retrieves or constructs RTree for geometry with polymorphic type dispatch and automatic caching.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<RTree> GetTree<T>(T source) where T : notnull =>
        ResultFactory.Create(value: _treeCache.GetValue(source, ConstructTree));

    /// <summary>Constructs RTree using RhinoCommon factory methods with type-based dispatch for optimal tree structure.</summary>
    [Pure]
    private static RTree ConstructTree(object source) => source switch {
        Point3d[] points => RTree.CreateFromPointArray(points) ?? new RTree(),
        PointCloud cloud => RTree.CreatePointCloudTree(cloud) ?? new RTree(),
        Mesh mesh => RTree.CreateMeshFaceTree(mesh) ?? new RTree(),
        Curve[] curves => BuildGeometryArrayTree(curves),
        Surface[] surfaces => BuildGeometryArrayTree(surfaces),
        Brep[] breps => BuildGeometryArrayTree(breps),
        _ => new RTree(),
    };

    /// <summary>Constructs RTree from geometry array by inserting bounding boxes with index tracking.</summary>
    [Pure]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }
}

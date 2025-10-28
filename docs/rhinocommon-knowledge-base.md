# RhinoCommon SDK Knowledge Base for Arsenal.Rhino Enhancement

## Core RhinoCommon Namespaces and Classes

### Primary Geometry Namespace: `Rhino.Geometry`

#### Essential Geometry Classes
- `GeometryBase` - Base class for all geometry
- `Brep` - Boundary representation (solids/surfaces)
- `Mesh` - Triangulated/quad mesh geometry
- `Surface` - NURBS and other parametric surfaces
- `Curve` - All curve types (lines, arcs, NURBS, etc.)
- `Point3d` - 3D point structure
- `Vector3d` - 3D vector structure
- `Plane` - Plane definition with origin and axes
- `BoundingBox` - Axis-aligned bounding box

#### Specialized Analysis Classes
- `VolumeMassProperties` - Volume, centroid, moments of inertia
- `AreaMassProperties` - Area, centroid, moments for surfaces
- `LengthMassProperties` - Length, centroid for curves
- `MeshAnalysis` - Mesh quality and validation tools

#### Spatial and Intersection Classes
- `RTree` - Spatial indexing structure
- `Intersection` - Static methods for geometry intersections
- `Transform` - 4x4 transformation matrices

## Advanced RhinoCommon Capabilities

### 1. Mass Properties with Selective Computation

```csharp
// Full computation (current Arsenal.Rhino approach)
VolumeMassProperties props = VolumeMassProperties.Compute(brep);

// Selective computation for performance
VolumeMassProperties props = VolumeMassProperties.Compute(
    brep, 
    volume: true,           // Calculate volume
    firstMoments: true,     // Calculate centroid
    secondMoments: false,   // Skip second moments
    productMoments: false   // Skip product moments
);

// Fast area/volume without moments (Rhino 8 enhancement)
double area = brep.GetArea();
double volume = brep.GetVolume();
```

### 2. Advanced Mesh Operations

```csharp
// Mesh validation with detailed reporting
using (TextLog log = new TextLog())
{
    MeshCheckParameters parameters = new MeshCheckParameters();
    bool isValid = mesh.Check(log, ref parameters);
    string issues = log.ToString();
}

// Mesh repair operations
mesh.Vertices.CombineIdentical(tolerance, true);
mesh.Vertices.CullUnused();
mesh.FaceNormals.ComputeFaceNormals();
mesh.Normals.ComputeNormals();
mesh.Compact();

// Mesh tree for fast spatial queries
RTree meshTree = RTree.CreateMeshFaceTree(mesh);
```

### 3. Enhanced Intersection Capabilities

```csharp
// Brep-Brep intersections
Curve[] intersectionCurves;
Point3d[] intersectionPoints;
bool success = Intersection.BrepBrep(
    brepA, brepB, tolerance, 
    out intersectionCurves, 
    out intersectionPoints);

// Mesh-Plane intersections for sectioning
Polyline[] sections = Intersection.MeshPlane(mesh, plane);

// Batch point projections
Point3d[] projectedPoints = Intersection.ProjectPointsToBreps(
    points, breps, direction, tolerance);
```

### 4. Advanced RTree Usage Patterns

```csharp
// Specialized mesh face tree
RTree faceTree = RTree.CreateMeshFaceTree(mesh);

// K-nearest neighbors search
int[][] neighbors = RTree.Point3dKNeighbors(
    searchPoints, queryPoints, k: 5);

// Point cloud tree for large datasets
RTree pointTree = RTree.CreatePointCloudTree(pointCloud);

// Custom search with callback
tree.Search(boundingBox, (sender, args) => {
    // Custom logic for each found item
    int itemId = args.Id;
    // Process item...
});
```

### 5. Surface Analysis and Frame Computation

```csharp
// Surface evaluation with derivatives
bool success = surface.Evaluate(u, v, derivativeCount: 2, 
    out Point3d point, out Vector3d[] derivatives);

// Surface curvature analysis
SurfaceCurvature curvature = surface.CurvatureAt(u, v);
double gaussianCurvature = curvature.Gaussian;
double meanCurvature = curvature.Mean;

// Surface frame computation
Vector3d normal = surface.NormalAt(u, v);
Vector3d[] tangents = surface.TangentAt(u, v);
Plane frame = new Plane(point, tangents[0], tangents[1]);
```

### 6. Document and Tolerance Integration

```csharp
// Document property access
RhinoDoc doc = RhinoDoc.ActiveDoc;
double absoluteTolerance = doc.ModelAbsoluteTolerance;
double angleTolerance = doc.ModelAngleToleranceRadians;
UnitSystem units = doc.ModelUnitSystem;

// Document object management
Guid objectId = doc.Objects.Add(geometry);
RhinoObject rhinoObject = doc.Objects.Find(objectId);
doc.Objects.Delete(objectId, quiet: true);

// View management
doc.Views.Redraw();
RhinoView activeView = doc.Views.ActiveView;
```

### 7. Unit Conversion and Scaling

```csharp
// Unit system conversion
double scale = RhinoMath.UnitScale(
    UnitSystem.Meters, UnitSystem.Millimeters);

// Geometry scaling
Transform scaleTransform = Transform.Scale(Point3d.Origin, scale);
geometry.Transform(scaleTransform);

// Unit-aware distance calculations
double distance = point1.DistanceTo(point2);
string formatted = $"{distance:F3} {units}";
```

## Performance Optimization Techniques

### 1. Geometry Caching Patterns

```csharp
// Cache expensive computations
private readonly Dictionary<Guid, VolumeMassProperties> _massPropsCache 
    = new Dictionary<Guid, VolumeMassProperties>();

public VolumeMassProperties GetMassProperties(Brep brep)
{
    Guid id = brep.ObjectId;
    if (!_massPropsCache.TryGetValue(id, out var props))
    {
        props = VolumeMassProperties.Compute(brep);
        _massPropsCache[id] = props;
    }
    return props;
}
```

### 2. Batch Processing Patterns

```csharp
// Process multiple geometries efficiently
public Result<IReadOnlyList<Point3d>> BatchCentroids(
    IEnumerable<GeometryBase> geometries)
{
    var results = new List<Point3d>();
    
    // Group by type for optimized processing
    var breps = geometries.OfType<Brep>().ToList();
    var meshes = geometries.OfType<Mesh>().ToList();
    
    // Process each type with specialized methods
    foreach (var brep in breps)
    {
        var props = VolumeMassProperties.Compute(brep, 
            volume: false, firstMoments: true, 
            secondMoments: false, productMoments: false);
        results.Add(props.Centroid);
    }
    
    return Result<IReadOnlyList<Point3d>>.Success(results);
}
```

### 3. Memory Management Best Practices

```csharp
// Proper disposal of mass properties
using (var volumeProps = VolumeMassProperties.Compute(brep))
using (var areaProps = AreaMassProperties.Compute(brep))
{
    Point3d centroid = volumeProps.Centroid.IsValid 
        ? volumeProps.Centroid 
        : areaProps.Centroid;
    
    return Result<Point3d>.Success(centroid);
}

// Dispose geometry copies
GeometryBase copy = geometry.Duplicate();
try
{
    // Process copy...
    return result;
}
finally
{
    copy?.Dispose();
}
```

## Integration Patterns for Arsenal.Rhino

### 1. Enhanced Mass Properties Service

```csharp
public interface IMassPropertiesService
{
    Result<Point3d> Centroid(GeometryBase geometry, GeoContext context);
    Result<double> Volume(Brep brep, GeoContext context);
    Result<double> Area(GeometryBase geometry, GeoContext context);
    Result<MassPropertySummary> Summary(GeometryBase geometry, GeoContext context);
}

public sealed record MassPropertySummary(
    Point3d Centroid,
    double Volume,
    double Area,
    double Length,
    BoundingBox Bounds);
```

### 2. Advanced Intersection Service

```csharp
public interface IAdvancedIntersection
{
    Result<IReadOnlyList<Curve>> BrepBrep(
        IEnumerable<Brep> brepsA, IEnumerable<Brep> brepsB, GeoContext context);
    
    Result<IReadOnlyList<Polyline>> MeshPlane(
        Mesh mesh, IEnumerable<Plane> planes, GeoContext context);
    
    Result<IReadOnlyList<Point3d>> ProjectPoints(
        IEnumerable<Point3d> points, IEnumerable<Brep> targets, 
        Vector3d direction, GeoContext context);
}
```

### 3. Spatial Analysis Service

```csharp
public interface ISpatialAnalysis
{
    Result<IReadOnlyList<int>> KNearestNeighbors(
        IEnumerable<Point3d> searchPoints, IEnumerable<Point3d> queryPoints, 
        int k, GeoContext context);
    
    Result<IReadOnlyList<Point3d>> SpatialFilter(
        IEnumerable<Point3d> points, GeometryBase filter, GeoContext context);
    
    Result<RTree> CreateSpatialIndex(
        IEnumerable<GeometryBase> geometries, GeoContext context);
}
```

## Error Handling and Validation Patterns

### 1. RhinoCommon Exception Handling

```csharp
public Result<T> SafeRhinoOperation<T>(Func<T> operation)
{
    try
    {
        T result = operation();
        return Result<T>.Success(result);
    }
    catch (ArgumentException ex)
    {
        return Result<T>.Fail(new Failure("rhino.argument", ex.Message));
    }
    catch (InvalidOperationException ex)
    {
        return Result<T>.Fail(new Failure("rhino.operation", ex.Message));
    }
    catch (Exception ex)
    {
        return Result<T>.Fail(Failure.From(ex));
    }
}
```

### 2. Geometry Validation Patterns

```csharp
public static Result<T> ValidateGeometry<T>(T geometry) where T : GeometryBase
{
    if (geometry == null)
        return Result<T>.Fail(new Failure("geometry.null", "Geometry cannot be null"));
    
    if (!geometry.IsValid)
        return Result<T>.Fail(new Failure("geometry.invalid", 
            $"Geometry of type {typeof(T).Name} is not valid"));
    
    if (geometry.GetBoundingBox(false).Diagonal.Length < 1e-10)
        return Result<T>.Fail(new Failure("geometry.degenerate", 
            "Geometry appears to be degenerate"));
    
    return Result<T>.Success(geometry);
}
```

## Testing and Validation Utilities

### 1. Geometry Test Helpers

```csharp
public static class GeometryTestHelpers
{
    public static bool IsNearlyEqual(Point3d a, Point3d b, double tolerance)
        => a.EpsilonEquals(b, tolerance);
    
    public static bool IsNearlyEqual(Vector3d a, Vector3d b, double tolerance)
        => a.EpsilonEquals(b, tolerance);
    
    public static bool IsValidMesh(Mesh mesh, out string[] issues)
    {
        var issueList = new List<string>();
        
        if (!mesh.IsValid)
            issueList.Add("Mesh is geometrically invalid");
        
        if (mesh.DisjointMeshCount > 1)
            issueList.Add($"Mesh has {mesh.DisjointMeshCount} disjoint parts");
        
        issues = issueList.ToArray();
        return issues.Length == 0;
    }
}
```

This knowledge base provides comprehensive information about RhinoCommon SDK capabilities that can enhance the Arsenal.Rhino library implementation.
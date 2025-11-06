using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial analysis engine with UnifiedOperation-based dispatch and zero-null guarantees.</summary>
public static class SpatialAnalyzer {
    /// <summary>Spatial operation dispatch configuration mapping type signatures to validation and execution strategies.</summary>
    private static readonly FrozenDictionary<(Type Source, Type Query), (ValidationMode Mode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>> Operation)> _operations =
        new Dictionary<(Type, Type), (ValidationMode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>>)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (ValidationMode.Standard,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Point3d[])source, (Sphere)query, context, buffer, pts => RTree.CreateFromPointArray(pts))),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (ValidationMode.Standard,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Point3d[])source, (BoundingBox)query, context, buffer, pts => RTree.CreateFromPointArray(pts))),
            [(typeof(Point3d[]), typeof((Point3d, int)))] = (ValidationMode.Standard,
                (source, query, context, _) => SpatialOperations.ProximityQuery((Point3d[])source, ((Point3d, int))query, context)),
            [(typeof(Point3d[]), typeof((Point3d, double)))] = (ValidationMode.Standard,
                (source, query, context, _) => SpatialOperations.ProximityQuery((Point3d[])source, ((Point3d, double))query, context)),
            [(typeof(PointCloud), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((PointCloud)source, (Sphere)query, context, buffer, pc => RTree.CreatePointCloudTree(pc))),
            [(typeof(PointCloud), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((PointCloud)source, (BoundingBox)query, context, buffer, pc => RTree.CreatePointCloudTree(pc))),
            [(typeof(PointCloud), typeof((Point3d, int)))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, _) => SpatialOperations.ProximityQuery((PointCloud)source, ((Point3d, int))query, context)),
            [(typeof(PointCloud), typeof((Point3d, double)))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, _) => SpatialOperations.ProximityQuery((PointCloud)source, ((Point3d, double))query, context)),
            [(typeof(Mesh), typeof(Sphere))] = (ValidationMode.MeshSpecific,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Mesh)source, (Sphere)query, context, buffer, m => RTree.CreateMeshFaceTree(m))),
            [(typeof(Mesh), typeof(BoundingBox))] = (ValidationMode.MeshSpecific,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Mesh)source, (BoundingBox)query, context, buffer, m => RTree.CreateMeshFaceTree(m))),
            [(typeof((Mesh, Mesh)), typeof(IGeometryContext))] = (ValidationMode.MeshSpecific,
                (source, _, context, buffer) => SpatialOperations.OverlapQuery(((Mesh, Mesh))source, context, buffer)),
            [(typeof(Curve[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Curve[])source, (Sphere)query, context, buffer, treeFactory: null)),
            [(typeof(Curve[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Degeneracy,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Curve[])source, (BoundingBox)query, context, buffer, treeFactory: null)),
            [(typeof(Surface[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.BoundingBox,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Surface[])source, (Sphere)query, context, buffer, treeFactory: null)),
            [(typeof(Surface[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.BoundingBox,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Surface[])source, (BoundingBox)query, context, buffer, treeFactory: null)),
            [(typeof(Brep[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Topology,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Brep[])source, (Sphere)query, context, buffer, treeFactory: null)),
            [(typeof(Brep[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Topology,
                (source, query, context, buffer) => SpatialOperations.RangeQuery((Brep[])source, (BoundingBox)query, context, buffer, treeFactory: null)),
        }.ToFrozenDictionary();

    /// <summary>Performs spatial analysis with type-based polymorphic dispatch through UnifiedOperation pipeline.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TSource, TQuery>(
        TSource source,
        TQuery query,
        IGeometryContext context,
        double? toleranceBuffer = null) where TSource : notnull where TQuery : notnull =>
        source switch {
            System.Collections.IEnumerable collection when collection is not string and not GeometryBase =>
                UnifiedOperation.Apply(
                    collection.Cast<object>(),
                    (Func<object, Result<IReadOnlyList<int>>>)(item => Analyze(item, query, context, toleranceBuffer)),
                    new OperationConfig<object, int> { Context = context, ValidationMode = ValidationMode.None, AccumulateErrors = true }),
            _ => ExecuteOperation(source, query, context, toleranceBuffer),
        };

    /// <summary>Executes spatial operation with validation through UnifiedOperation framework.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteOperation<TSource, TQuery>(
        TSource source,
        TQuery query,
        IGeometryContext context,
        double? toleranceBuffer) where TSource : notnull where TQuery : notnull =>
        ResolveOperation(source, query) switch {
            (ValidationMode mode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>> op) =>
                UnifiedOperation.Apply(
                    source,
                    (Func<TSource, Result<IReadOnlyList<int>>>)(s => op(s, query, context, toleranceBuffer)),
                    new OperationConfig<TSource, int> { Context = context, ValidationMode = mode }),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.UnsupportedOperation),
        };

    /// <summary>Resolves operation configuration with GeometryBase runtime type support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (ValidationMode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>>)? ResolveOperation<TSource, TQuery>(
        TSource source,
        TQuery _) where TSource : notnull where TQuery : notnull =>
        _operations.TryGetValue((typeof(TSource), typeof(TQuery)), out (ValidationMode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>>) config) ? config :
        source switch {
            GeometryBase => _operations.TryGetValue((source.GetType(), typeof(TQuery)), out (ValidationMode, Func<object, object, IGeometryContext, double?, Result<IReadOnlyList<int>>>) gConfig) ? gConfig : null,
            _ => null,
        };
}

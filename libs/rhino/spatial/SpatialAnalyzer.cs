using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial analysis engine with type-based dispatch and RhinoCommon RTree integration.</summary>
public static class SpatialAnalyzer {
    /// <summary>Spatial operation configuration mapping (source, query) type pairs to validation modes and tree factories.</summary>
    private static readonly FrozenDictionary<(Type Source, Type Query), (ValidationMode Validation, Func<object, RTree?>? TreeFactory)> _operations =
        new Dictionary<(Type, Type), (ValidationMode, Func<object, RTree?>?)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s)),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s)),
            [(typeof(Point3d[]), typeof((Point3d, int)))] = (ValidationMode.Standard, null),
            [(typeof(Point3d[]), typeof((Point3d, double)))] = (ValidationMode.Standard, null),
            [(typeof(PointCloud), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => RTree.CreatePointCloudTree((PointCloud)s)),
            [(typeof(PointCloud), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => RTree.CreatePointCloudTree((PointCloud)s)),
            [(typeof(PointCloud), typeof((Point3d, int)))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(typeof(PointCloud), typeof((Point3d, double)))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(typeof(Mesh), typeof(Sphere))] = (ValidationMode.MeshSpecific, s => RTree.CreateMeshFaceTree((Mesh)s)),
            [(typeof(Mesh), typeof(BoundingBox))] = (ValidationMode.MeshSpecific, s => RTree.CreateMeshFaceTree((Mesh)s)),
            [(typeof((Mesh, Mesh)), typeof(IGeometryContext))] = (ValidationMode.MeshSpecific, null),
            [(typeof(Curve[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(typeof(Curve[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(typeof(Surface[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.BoundingBox, null),
            [(typeof(Surface[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.BoundingBox, null),
            [(typeof(Brep[]), typeof(Sphere))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(typeof(Brep[]), typeof(BoundingBox))] = (ValidationMode.Standard | ValidationMode.Topology, null),
        }.ToFrozenDictionary();

    /// <summary>Performs spatial analysis using polymorphic type-based dispatch with automatic validation and caching.</summary>
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
            _ => AnalyzeCore(source, query, context, toleranceBuffer),
        };

    /// <summary>Core spatial analysis with type pattern matching and RTree algorithm dispatch.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> AnalyzeCore<TSource, TQuery>(
        TSource source,
        TQuery query,
        IGeometryContext context,
        double? toleranceBuffer) where TSource : notnull where TQuery : notnull =>
        ((object)source, (object)query) switch {
            (Point3d[] pts, ValueTuple<Point3d, int> tuple) when tuple.Item2 > 0 =>
                ResultFactory.Create(value: source).Validate(args: [context, ValidationMode.Standard])
                    .Map(_ => (IReadOnlyList<int>)(SpatialOperations.ProximityK(pts, tuple.Item1, tuple.Item2) ?? []).AsReadOnly()),
            (Point3d[] pts, ValueTuple<Point3d, double> tuple) when tuple.Item2 > 0 =>
                ResultFactory.Create(value: source).Validate(args: [context, ValidationMode.Standard])
                    .Map(_ => (IReadOnlyList<int>)(SpatialOperations.ProximityDistance(pts, tuple.Item1, tuple.Item2) ?? []).AsReadOnly()),
            (PointCloud cloud, ValueTuple<Point3d, int> tuple) when tuple.Item2 > 0 =>
                ResultFactory.Create(value: source).Validate(args: [context, ValidationMode.Standard | ValidationMode.Degeneracy])
                    .Map(_ => (IReadOnlyList<int>)(SpatialOperations.ProximityK(cloud, tuple.Item1, tuple.Item2) ?? []).AsReadOnly()),
            (PointCloud cloud, ValueTuple<Point3d, double> tuple) when tuple.Item2 > 0 =>
                ResultFactory.Create(value: source).Validate(args: [context, ValidationMode.Standard | ValidationMode.Degeneracy])
                    .Map(_ => (IReadOnlyList<int>)(SpatialOperations.ProximityDistance(cloud, tuple.Item1, tuple.Item2) ?? []).AsReadOnly()),
            (ValueTuple<Mesh, Mesh> meshes, IGeometryContext) =>
                ResultFactory.Create(value: (meshes.Item1, meshes.Item2)).Validate(args: [context, ValidationMode.MeshSpecific])
                    .Map(__ => (IReadOnlyList<int>)(SpatialOperations.Overlap(meshes.Item1, meshes.Item2, context.AbsoluteTolerance + (toleranceBuffer ?? 0)) ?? []).AsReadOnly()),
            (Point3d[], ValueTuple<Point3d, int> tuple) when tuple.Item2 <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidCount),
            (Point3d[], ValueTuple<Point3d, double> tuple) when tuple.Item2 <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidDistance),
            (PointCloud, ValueTuple<Point3d, int> tuple) when tuple.Item2 <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidCount),
            (PointCloud, ValueTuple<Point3d, double> tuple) when tuple.Item2 <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidDistance),
            _ when GetOperationConfig(source, query) is (ValidationMode validation, Func<object, RTree?> factory) =>
                ResultFactory.Create(value: source).Validate(args: [context, validation])
                    .Map(_ => (IReadOnlyList<int>)(SpatialOperations.Range(source, query, context, factory, toleranceBuffer) ?? []).AsReadOnly()),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.UnsupportedOperation),
        };

    /// <summary>Retrieves operation configuration with type resolution for GeometryBase inheritance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (ValidationMode, Func<object, RTree?>)? GetOperationConfig<TSource, TQuery>(TSource source, TQuery _) where TSource : notnull where TQuery : notnull {
        (bool found, (ValidationMode, Func<object, RTree?>?) config) = (_operations.TryGetValue((typeof(TSource), typeof(TQuery)), out (ValidationMode, Func<object, RTree?>?) c1), c1);
        return found && config is (ValidationMode v, Func<object, RTree?> f) ? (v, f) :
            source switch {
                GeometryBase => _operations.TryGetValue((source.GetType(), typeof(TQuery)), out (ValidationMode, Func<object, RTree?>?) gConfig) && gConfig is (ValidationMode gv, Func<object, RTree?> gf) ? (gv, gf) : null,
                _ => null,
            };
    }
}

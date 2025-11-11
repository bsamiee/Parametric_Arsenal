using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Linq;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>RTree construction and queries with pooled buffers.</summary>
internal static class SpatialCore {
    private static readonly Func<object, IGeometryContext, Result<RTree>> _geometryArrayFactory = static (source, _) => source switch {
        GeometryBase[] geometries => {
            RTree tree = new RTree();
            for (int index = 0; index < geometries.Length; index++) {
                _ = tree.Insert(geometries[index].GetBoundingBox(accurate: true), index);
            }

            return ResultFactory.Create(value: tree);
        },
        _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
    };

    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<object>>> _queryValidators =
        new (Type Query, Func<object, IGeometryContext, Result<object>> Validator)[] {
            (typeof(Sphere), static (query, context) => query switch {
                Sphere { IsValid: true, Radius: > 0.0 } sphere when sphere.Radius > context.AbsoluteTolerance => ResultFactory.Create<object>(value: sphere),
                Sphere => ResultFactory.Create<object>(error: E.Validation.GeometryInvalid.WithContext("Sphere query invalid")),
                _ => ResultFactory.Create<object>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Expected Sphere query")),
            }),
            (typeof(BoundingBox), static (query, _) => query switch {
                BoundingBox { IsValid: true } box => ResultFactory.Create<object>(value: box),
                BoundingBox => ResultFactory.Create<object>(error: E.Validation.GeometryInvalid.WithContext("BoundingBox query invalid")),
                _ => ResultFactory.Create<object>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Expected BoundingBox query")),
            }),
            (typeof((Point3d[], int)), static (query, _) => query switch {
                (Point3d[] Needles, int K) payload when payload.K > 0 && payload.Needles.Length > 0 => ResultFactory
                    .Create(value: payload.Needles)
                    .Traverse(static point => point.IsValid ? ResultFactory.Create(value: point) : ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid))
                    .Map(validNeedles => (object)(validNeedles.ToArray(), payload.K)),
                (Point3d[], int k) when k <= 0 => ResultFactory.Create<object>(error: E.Spatial.InvalidK),
                (Point3d[] needles, int) when needles.Length == 0 => ResultFactory.Create<object>(error: E.Geometry.InvalidCount.WithContext("Needle set empty")),
                _ => ResultFactory.Create<object>(error: E.Spatial.UnsupportedTypeCombo),
            }),
            (typeof((Point3d[], double)), static (query, context) => query switch {
                (Point3d[] Needles, double Distance) payload when payload.Distance > context.AbsoluteTolerance && payload.Needles.Length > 0 => ResultFactory
                    .Create(value: payload.Needles)
                    .Traverse(static point => point.IsValid ? ResultFactory.Create(value: point) : ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid))
                    .Map(validNeedles => (object)(validNeedles.ToArray(), payload.Distance)),
                (Point3d[], double distance) when distance <= context.AbsoluteTolerance => ResultFactory.Create<object>(error: E.Spatial.InvalidDistance.WithContext("Distance must exceed tolerance")),
                (Point3d[] needles, double) when needles.Length == 0 => ResultFactory.Create<object>(error: E.Geometry.InvalidCount.WithContext("Needle set empty")),
                _ => ResultFactory.Create<object>(error: E.Spatial.UnsupportedTypeCombo),
            }),
            (typeof(double), static (query, _) => query switch {
                double value when value >= 0.0 => ResultFactory.Create<object>(value: value),
                double => ResultFactory.Create<object>(error: E.Spatial.InvalidDistance.WithContext("Tolerance must be non-negative")),
                _ => ResultFactory.Create<object>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Expected tolerance")),
            }),
        }.ToFrozenDictionary(entry => entry.Query, entry => entry.Validator);

    private static readonly Func<object, IGeometryContext, Func<object, IGeometryContext, Result<RTree>>, Result<RTree>> _treeResolver =
        static (source, context, factory) => Spatial.TreeCache.TryGetValue(source, out RTree cached)
            ? ResultFactory.Create(value: cached)
            : factory(source, context).Map(tree => {
                Spatial.TreeCache.Add(source, tree);
                return tree;
            });

    private static readonly Func<RTree, object, int, IGeometryContext, Result<IReadOnlyList<int>>> _rangeSearch =
        static (tree, query, bufferSize, context) => {
            int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
            int count = 0;
            bool overflow = false;
            bool unsupported = false;
            try {
                EventHandler<RTreeEventArgs> handler = (_, args) => {
                    bool hasCapacity = count < buffer.Length;
                    int index = hasCapacity ? count : buffer.Length - 1;
                    buffer[index] = args.Id;
                    count = hasCapacity ? count + 1 : count;
                    overflow = hasCapacity ? overflow : true;
                };
                Action search = query switch {
                    Sphere sphere => () => tree.Search(sphere, handler),
                    BoundingBox box => () => tree.Search(box, handler),
                    _ => () => unsupported = true,
                };
                search();
                return unsupported
                    ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)
                    : overflow
                        ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.BufferOverflow.WithContext($"BufferSize: {bufferSize}, Tolerance: {context.AbsoluteTolerance}"))
                        : ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
            } finally {
                ArrayPool<int>.Shared.Return(buffer, clearArray: true);
            }
        };

    private static readonly Func<object, Point3d[], object, Func<object, Point3d[], int, IEnumerable<int[]>>, Func<object, Point3d[], double, IEnumerable<int[]>>, Result<IReadOnlyList<int>>> _proximitySearch =
        static (source, needles, limit, kNearest, distLimited) => limit switch {
            int count => kNearest(source, needles, count).ToArray() is int[][] results && results.Length > 0
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed.WithContext("No neighbors within K")),
            double distance => distLimited(source, needles, distance).ToArray() is int[][] results && results.Length > 0
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed.WithContext("No neighbors within distance")),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Invalid proximity payload")),
        };

    private static readonly Func<RTree, RTree, double, int, Result<IReadOnlyList<int>>> _overlapSearch =
        static (treeA, treeB, tolerance, bufferSize) => {
            int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
            int count = 0;
            try {
                bool hasIntersections = RTree.SearchOverlaps(treeA, treeB, tolerance, (_, args) => {
                    bool hasCapacity = count + 1 < buffer.Length;
                    int index = hasCapacity ? count : buffer.Length - 2;
                    buffer[index] = args.Id;
                    buffer[index + 1] = args.IdB;
                    count = hasCapacity ? count + 2 : count;
                });
                return hasIntersections
                    ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : [])
                    : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed);
            } finally {
                ArrayPool<int>.Shared.Return(buffer, clearArray: true);
            }
        };

    /// <summary>(Input, Query) type pairs to (Factory, Mode, BufferSize, Execute) mapping.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, IGeometryContext, Result<RTree>>? Factory, Func<object, IGeometryContext, Result<object>> QueryValidator, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type Input, Func<object, IGeometryContext, Result<RTree>> Factory, V Mode)[] {
            (typeof(Point3d[]), SpatialConfig.RTreeFactories[typeof(Point3d[])], V.None),
            (typeof(PointCloud), SpatialConfig.RTreeFactories[typeof(PointCloud)], V.Standard),
            (typeof(Mesh), SpatialConfig.RTreeFactories[typeof(Mesh)], V.MeshSpecific),
            (typeof(Curve[]), _geometryArrayFactory, V.Degeneracy),
            (typeof(Surface[]), _geometryArrayFactory, V.BoundingBox),
            (typeof(Brep[]), _geometryArrayFactory, V.Topology),
        }
            .SelectMany(entry => new (Type Input, Type Query, Func<object, IGeometryContext, Result<RTree>>? Factory, Func<object, IGeometryContext, Result<object>> QueryValidator, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
                (entry.Input, typeof(Sphere), entry.Factory, _queryValidators[typeof(Sphere)], entry.Mode, SpatialConfig.DefaultBufferSize, (input, query, context, buffer) => _queryValidators[typeof(Sphere)](query, context).Bind(validQuery =>
                    _treeResolver(source: input, context: context, factory: entry.Factory).Bind(tree =>
                        _rangeSearch(tree: tree, query: validQuery, bufferSize: buffer, context: context)))),
                (entry.Input, typeof(BoundingBox), entry.Factory, _queryValidators[typeof(BoundingBox)], entry.Mode, SpatialConfig.DefaultBufferSize, (input, query, context, buffer) => _queryValidators[typeof(BoundingBox)](query, context).Bind(validQuery =>
                    _treeResolver(source: input, context: context, factory: entry.Factory).Bind(tree =>
                        _rangeSearch(tree: tree, query: validQuery, bufferSize: buffer, context: context)))),
            })
            .Concat(new (Type Input, Type Query, Func<object, IGeometryContext, Result<RTree>>? Factory, Func<object, IGeometryContext, Result<object>> QueryValidator, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
                (typeof(Point3d[]), typeof((Point3d[], int)), SpatialConfig.RTreeFactories[typeof(Point3d[])], _queryValidators[typeof((Point3d[], int))], V.None, SpatialConfig.DefaultBufferSize, (input, query, context, _) => input switch {
                    Point3d[] points => _queryValidators[typeof((Point3d[], int))](query, context).Bind(validQuery => validQuery switch {
                        (Point3d[] needles, int limit) => _proximitySearch(
                            source: points,
                            needles: needles,
                            limit: limit,
                            kNearest: static (source, payload, count) => RTree.Point3dKNeighbors((Point3d[])source, payload, count),
                            distLimited: static (source, payload, distance) => RTree.Point3dClosestPoints((Point3d[])source, payload, distance)
                        ),
                        _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                    }),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Validation.GeometryInvalid),
                }),
                (typeof(Point3d[]), typeof((Point3d[], double)), SpatialConfig.RTreeFactories[typeof(Point3d[])], _queryValidators[typeof((Point3d[], double))], V.None, SpatialConfig.DefaultBufferSize, (input, query, context, _) => input switch {
                    Point3d[] points => _queryValidators[typeof((Point3d[], double))](query, context).Bind(validQuery => validQuery switch {
                        (Point3d[] needles, double limit) => _proximitySearch(
                            source: points,
                            needles: needles,
                            limit: limit,
                            kNearest: static (source, payload, count) => RTree.Point3dKNeighbors((Point3d[])source, payload, count),
                            distLimited: static (source, payload, distance) => RTree.Point3dClosestPoints((Point3d[])source, payload, distance)
                        ),
                        _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                    }),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Validation.GeometryInvalid),
                }),
                (typeof(PointCloud), typeof((Point3d[], int)), SpatialConfig.RTreeFactories[typeof(PointCloud)], _queryValidators[typeof((Point3d[], int))], V.Standard, SpatialConfig.DefaultBufferSize, (input, query, context, _) => input switch {
                    PointCloud cloud => _queryValidators[typeof((Point3d[], int))](query, context).Bind(validQuery => validQuery switch {
                        (Point3d[] needles, int limit) => _proximitySearch(
                            source: cloud,
                            needles: needles,
                            limit: limit,
                            kNearest: static (source, payload, count) => RTree.PointCloudKNeighbors((PointCloud)source, payload, count),
                            distLimited: static (source, payload, distance) => RTree.PointCloudClosestPoints((PointCloud)source, payload, distance)
                        ),
                        _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                    }),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Validation.GeometryInvalid),
                }),
                (typeof(PointCloud), typeof((Point3d[], double)), SpatialConfig.RTreeFactories[typeof(PointCloud)], _queryValidators[typeof((Point3d[], double))], V.Standard, SpatialConfig.DefaultBufferSize, (input, query, context, _) => input switch {
                    PointCloud cloud => _queryValidators[typeof((Point3d[], double))](query, context).Bind(validQuery => validQuery switch {
                        (Point3d[] needles, double limit) => _proximitySearch(
                            source: cloud,
                            needles: needles,
                            limit: limit,
                            kNearest: static (source, payload, count) => RTree.PointCloudKNeighbors((PointCloud)source, payload, count),
                            distLimited: static (source, payload, distance) => RTree.PointCloudClosestPoints((PointCloud)source, payload, distance)
                        ),
                        _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                    }),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Validation.GeometryInvalid),
                }),
                (typeof((Mesh, Mesh)), typeof(double), null, _queryValidators[typeof(double)], V.MeshSpecific, SpatialConfig.LargeBufferSize, (input, query, context, buffer) => _queryValidators[typeof(double)](query, context).Bind(validTolerance => input switch {
                    (Mesh meshA, Mesh meshB) => _treeResolver(source: meshA, context: context, factory: SpatialConfig.RTreeFactories[typeof(Mesh)]).Bind(treeA =>
                        _treeResolver(source: meshB, context: context, factory: SpatialConfig.RTreeFactories[typeof(Mesh)]).Bind(treeB =>
                            _overlapSearch(treeA: treeA, treeB: treeB, tolerance: context.AbsoluteTolerance + (double)validTolerance, bufferSize: buffer))),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Expected mesh pair")),
                })),
            })
            .ToFrozenDictionary(entry => (entry.Input, entry.Query), entry => (entry.Factory, entry.QueryValidator, entry.Mode, entry.BufferSize, entry.Execute));
}

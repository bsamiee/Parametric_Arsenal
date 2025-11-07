using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Internal topology computation algorithms with FrozenDictionary-based type dispatch.</summary>
internal static class TopologyCore {
    private static readonly IReadOnlyList<int> EmptyIndices = [];

    [Pure]
    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(
        T input,
        IGeometryContext context,
        bool orderLoops,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.NakedEdgeData>>>)(g => g switch {
                Brep brep => brep.Edges.Count switch {
                    0 => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: [],
                            EdgeIndices: [],
                            Valences: [],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: 0,
                            TotalLength: 0.0),
                    ]),
                    _ => Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                        .ToArray() switch {
                            int[] nakedIndices => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                                new Topology.NakedEdgeData(
                                    EdgeCurves: [.. nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()),],
                                    EdgeIndices: [.. nakedIndices,],
                                    Valences: [.. Enumerable.Repeat(1, nakedIndices.Length),],
                                    IsOrdered: orderLoops,
                                    TotalEdgeCount: brep.Edges.Count,
                                    TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())),
                            ]),
                        },
                },
                Mesh mesh => (mesh.GetNakedEdges() ?? []) switch {
                    Polyline[] nakedPolylines => ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
                        new Topology.NakedEdgeData(
                            EdgeCurves: [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),],
                            EdgeIndices: [.. Enumerable.Range(0, nakedPolylines.Length),],
                            Valences: [.. Enumerable.Repeat(1, nakedPolylines.Length),],
                            IsOrdered: orderLoops,
                            TotalEdgeCount: mesh.TopologyEdges.Count,
                            TotalLength: nakedPolylines.Sum(pl => pl.Length)),
                    ]),
                },
                _ => ResultFactory.Create<IReadOnlyList<Topology.NakedEdgeData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.NakedEdgeData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetNakedEdges.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Topology.BoundaryLoopData> ExecuteBoundaryLoops<T>(
        T input,
        IGeometryContext context,
        double? tolerance,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.BoundaryLoopData>>>)(g => g switch {
                Brep brep => ExecuteBrepBoundaryLoops(brep: brep, tol: tolerance ?? context.AbsoluteTolerance),
                Mesh mesh => ExecuteMeshBoundaryLoops(mesh: mesh, tol: tolerance ?? context.AbsoluteTolerance),
                _ => ResultFactory.Create<IReadOnlyList<Topology.BoundaryLoopData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.BoundaryLoopData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetBoundaryLoops.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(
        T input,
        IGeometryContext context,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.NonManifoldData>>>)(g => g switch {
                Brep brep => ExecuteBrepNonManifold(brep: brep),
                Mesh mesh => ExecuteMeshNonManifold(mesh: mesh),
                _ => ResultFactory.Create<IReadOnlyList<Topology.NonManifoldData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.NonManifoldData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetNonManifold.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Topology.ConnectivityData> ExecuteConnectivity<T>(
        T input,
        IGeometryContext context,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.ConnectivityData>>>)(g => g switch {
                Brep brep => ExecuteBrepConnectivity(brep: brep),
                Mesh mesh => ExecuteMeshConnectivity(mesh: mesh),
                _ => ResultFactory.Create<IReadOnlyList<Topology.ConnectivityData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.ConnectivityData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetConnectivity.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Topology.EdgeClassificationData> ExecuteEdgeClassification<T>(
        T input,
        IGeometryContext context,
        Continuity? minimumContinuity = null,
        double? angleThreshold = null,
        bool enableDiagnostics = false) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.EdgeClassificationData>>>)(g => g switch {
                Brep brep => ExecuteBrepEdgeClassification(
                    brep: brep,
                    minContinuity: minimumContinuity ?? Continuity.G1_continuous,
                    angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                Mesh mesh => ExecuteMeshEdgeClassification(
                    mesh: mesh,
                    angleThreshold: angleThreshold ?? context.AngleToleranceRadians),
                _ => ResultFactory.Create<IReadOnlyList<Topology.EdgeClassificationData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.EdgeClassificationData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.ClassifyEdges.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(
        T input,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.AdjacencyData>>>)(g => g switch {
                Brep brep => edgeIndex >= 0 && edgeIndex < brep.Edges.Count
                    ? ExecuteBrepAdjacency(brep: brep, edgeIndex: edgeIndex)
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                        error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex.ToString(CultureInfo.InvariantCulture)}, Max: {(brep.Edges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                Mesh mesh => edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count
                    ? ExecuteMeshAdjacency(mesh: mesh, edgeIndex: edgeIndex)
                    : ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                        error: E.Geometry.InvalidEdgeIndex.WithContext(string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex.ToString(CultureInfo.InvariantCulture)}, Max: {(mesh.TopologyEdges.Count - 1).ToString(CultureInfo.InvariantCulture)}"))),
                _ => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                    error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")),
            }),
            config: new OperationConfig<T, Topology.AdjacencyData> {
                Context = context,
                ValidationMode = input switch {
                    Brep => V.Standard | V.Topology,
                    Mesh => V.Standard | V.MeshSpecific,
                    _ => V.None,
                },
                OperationName = $"Topology.GetAdjacency.{typeof(T).Name}",
                EnableDiagnostics = enableDiagnostics,
            })
            .Map(results => results[0]);

    [Pure]
    private static Result<IReadOnlyList<Topology.BoundaryLoopData>> ExecuteBrepBoundaryLoops(Brep brep, double tol) {
        Curve[] nakedCurves = [.. Enumerable.Range(0, brep.Edges.Count)
            .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
            .Select(i => brep.Edges[i].DuplicateCurve()),
        ];
        Curve[] joined = nakedCurves.Length > 0
            ? Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false)
            : [];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[
            new Topology.BoundaryLoopData(
                Loops: [.. joined,],
                EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),],
                LoopLengths: [.. joined.Select(c => c.GetLength()),],
                IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                JoinTolerance: tol,
                FailedJoins: nakedCurves.Length - joined.Length),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.BoundaryLoopData>> ExecuteMeshBoundaryLoops(Mesh mesh, double tol) {
        Polyline[] nakedPolylines = mesh.GetNakedEdges() ?? [];
        Curve[] nakedCurves = [.. nakedPolylines.Select(pl => pl.ToNurbsCurve()),];
        Curve[] joined = nakedCurves.Length > 0
            ? Curve.JoinCurves(nakedCurves, joinTolerance: tol, preserveDirection: false)
            : [];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[
            new Topology.BoundaryLoopData(
                Loops: [.. joined,],
                EdgeIndicesPerLoop: [.. joined.Select(_ => EmptyIndices),],
                LoopLengths: [.. joined.Select(c => c.GetLength()),],
                IsClosedPerLoop: [.. joined.Select(c => c.IsClosed),],
                JoinTolerance: tol,
                FailedJoins: nakedCurves.Length - joined.Length),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ExecuteBrepNonManifold(Brep brep) {
        IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, brep.Edges.Count)
            .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold),
        ];
        IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => (int)brep.Edges[i].Valence),];
        IReadOnlyList<Point3d> locations = [.. nonManifoldEdges.Select(i => brep.Edges[i].PointAtStart),];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
            new Topology.NonManifoldData(
                EdgeIndices: nonManifoldEdges,
                VertexIndices: [],
                Valences: valences,
                Locations: locations,
                IsManifold: nonManifoldEdges.Count == 0,
                IsOrientable: brep.IsSolid,
                MaxValence: valences.Count > 0 ? valences.Max() : 0),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ExecuteMeshNonManifold(Mesh mesh) {
        bool isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _);
        IReadOnlyList<int> nonManifoldEdges = [.. Enumerable.Range(0, mesh.TopologyEdges.Count)
            .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2),
        ];
        IReadOnlyList<int> valences = [.. nonManifoldEdges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length),];
        IReadOnlyList<Point3d> locations = [.. nonManifoldEdges.Select(i => {
            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
            Point3d p1 = mesh.TopologyVertices[verts.I];
            Point3d p2 = mesh.TopologyVertices[verts.J];
            return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
        }),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
            new Topology.NonManifoldData(
                EdgeIndices: nonManifoldEdges,
                VertexIndices: [],
                Valences: valences,
                Locations: locations,
                IsManifold: isManifold,
                IsOrientable: isOriented,
                MaxValence: valences.Count > 0 ? valences.Max() : 0),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteBrepConnectivity(Brep brep) {
        int[] componentIds = new int[brep.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < brep.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseBrepComponent(brep: brep, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c),]),
        ];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(
            BoundingBox.Empty,
            (union, fIdx) => union.IsValid
                ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false))
                : brep.Faces[fIdx].GetBoundingBox(accurate: false))),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: bounds,
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)[.. brep.Faces[f].AdjacentEdges()
                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                        .Where(adj => adj != f),
                    ]))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseBrepComponent(Brep brep, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new([seed,]);
        componentIds[seed] = componentId;
        while (queue.Count > 0) {
            int faceIdx = queue.Dequeue();
            foreach (int edgeIdx in brep.Faces[faceIdx].AdjacentEdges()) {
                foreach (int adjFace in brep.Edges[edgeIdx].AdjacentFaces().Where(f => componentIds[f] == -1)) {
                    componentIds[adjFace] = componentId;
                    queue.Enqueue(adjFace);
                }
            }
        }
        return componentId;
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ExecuteMeshConnectivity(Mesh mesh) {
        int[] componentIds = new int[mesh.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;
        for (int seed = 0; seed < mesh.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseMeshComponent(mesh: mesh, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }
        IReadOnlyList<IReadOnlyList<int>> components = [.. Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)[.. Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c),]),
        ];
        IReadOnlyList<BoundingBox> bounds = [.. components.Select(c => c.Aggregate(
            BoundingBox.Empty,
            (union, fIdx) => {
                MeshFace face = mesh.Faces[fIdx];
                BoundingBox fBox = face.IsQuad
                    ? new BoundingBox([
                        mesh.Vertices[face.A],
                        mesh.Vertices[face.B],
                        mesh.Vertices[face.C],
                        mesh.Vertices[face.D],
                    ])
                    : new BoundingBox([
                        mesh.Vertices[face.A],
                        mesh.Vertices[face.B],
                        mesh.Vertices[face.C],
                    ]);
                return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
            })),
        ];
        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: [.. components.Select(c => c.Count),],
                ComponentBounds: bounds,
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)[.. mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0),]))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseMeshComponent(Mesh mesh, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new([seed,]);
        componentIds[seed] = componentId;
        while (queue.Count > 0) {
            int faceIdx = queue.Dequeue();
            foreach (int adjFace in mesh.Faces.AdjacentFaces(faceIdx).Where(f => f >= 0 && componentIds[f] == -1)) {
                componentIds[adjFace] = componentId;
                queue.Enqueue(adjFace);
            }
        }
        return componentId;
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteBrepEdgeClassification(Brep brep, Continuity minContinuity, double angleThreshold) {
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, brep.Edges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => ClassifyBrepEdge(
            edge: brep.Edges[i],
            _: brep,
            minContinuity: minContinuity,
            angleThreshold: angleThreshold)),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => brep.Edges[i].GetLength()),];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
            .Select((idx, pos) => (idx, type: classifications[pos]))
            .GroupBy(x => x.type, x => x.idx)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: grouped,
                MinimumContinuity: minContinuity),
        ]);
    }

    [Pure]
    private static Topology.EdgeContinuityType ClassifyBrepEdge(BrepEdge edge, Brep _, Continuity minContinuity, double angleThreshold) =>
        edge.Valence switch {
            EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
            EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
            EdgeAdjacency.Interior => edge.EdgeCurve switch {
                Curve crv when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Curvature,
                Curve crv when edge.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) || crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) || crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) => Topology.EdgeContinuityType.Smooth,
                _ when minContinuity >= Continuity.G1_continuous => Topology.EdgeContinuityType.Sharp,
                _ => Topology.EdgeContinuityType.Interior,
            },
            _ => Topology.EdgeContinuityType.Sharp,
        };

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ExecuteMeshEdgeClassification(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * 0.1;
        IReadOnlyList<int> edgeIndices = [.. Enumerable.Range(0, mesh.TopologyEdges.Count),];
        IReadOnlyList<Topology.EdgeContinuityType> classifications = [.. edgeIndices.Select(i => {
            int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
            return connectedFaces.Length switch {
                1 => Topology.EdgeContinuityType.Boundary,
                > 2 => Topology.EdgeContinuityType.NonManifold,
                2 => CalculateDihedralAngle(mesh: mesh, faceIdx1: connectedFaces[0], faceIdx2: connectedFaces[1]) switch {
                    double angle when Math.Abs(angle) < curvatureThreshold => Topology.EdgeContinuityType.Curvature,
                    double angle when Math.Abs(angle) < angleThreshold => Topology.EdgeContinuityType.Smooth,
                    _ => Topology.EdgeContinuityType.Sharp,
                },
                _ => Topology.EdgeContinuityType.Sharp,
            };
        }),
        ];
        IReadOnlyList<double> measures = [.. edgeIndices.Select(i => {
            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
            return mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]);
        }),
        ];
        FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped = edgeIndices
            .Select((idx, pos) => (idx, type: classifications[pos]))
            .GroupBy(x => x.type, x => x.idx)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g,]);
        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: grouped,
                MinimumContinuity: Continuity.C0_continuous),
        ]);
    }

    [Pure]
    private static double CalculateDihedralAngle(Mesh mesh, int faceIdx1, int faceIdx2) {
        Vector3d n1 = mesh.FaceNormals[faceIdx1];
        Vector3d n2 = mesh.FaceNormals[faceIdx2];
        return n1.IsValid && n2.IsValid ? Vector3d.VectorAngle(n1, n2) : Math.PI;
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ExecuteBrepAdjacency(Brep brep, int edgeIndex) {
        BrepEdge edge = brep.Edges[edgeIndex];
        IReadOnlyList<int> adjFaces = [.. edge.AdjacentFaces(),];
        IReadOnlyList<Vector3d> normals = [.. adjFaces.Select(i => {
            BrepFace face = brep.Faces[i];
            double uMid = face.Domain(0).Mid;
            double vMid = face.Domain(1).Mid;
            return face.NormalAt(uMid, vMid);
        }),
        ];
        double dihedralAngle = normals.Count == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;
        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
            new Topology.AdjacencyData(
                EdgeIndex: edgeIndex,
                AdjacentFaceIndices: adjFaces,
                FaceNormals: normals,
                DihedralAngle: dihedralAngle,
                IsManifold: edge.Valence == EdgeAdjacency.Interior,
                IsBoundary: edge.Valence == EdgeAdjacency.Naked),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ExecuteMeshAdjacency(Mesh mesh, int edgeIndex) {
        int[] adjFaces = mesh.TopologyEdges.GetConnectedFaces(edgeIndex);
        IReadOnlyList<Vector3d> normals = [.. adjFaces.Select(i => mesh.FaceNormals[i]),];
        double dihedralAngle = normals.Count == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;
        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
            new Topology.AdjacencyData(
                EdgeIndex: edgeIndex,
                AdjacentFaceIndices: [.. adjFaces,],
                FaceNormals: normals,
                DihedralAngle: dihedralAngle,
                IsManifold: adjFaces.Length == 2,
                IsBoundary: adjFaces.Length == 1),
        ]);
    }
}

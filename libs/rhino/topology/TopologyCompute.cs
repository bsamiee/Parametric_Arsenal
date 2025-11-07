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

/// <summary>Internal topology computation algorithms with dense polymorphic dispatch and zero-duplication design.</summary>
internal static class TopologyCompute {
    private static readonly IReadOnlyList<int> EmptyIndices = Array.Empty<int>();

    [Pure]
    internal static Result<Topology.NakedEdgeData> ExecuteNakedEdges<T>(
        T input,
        IGeometryContext context,
        bool orderLoops,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.NakedEdgeData>>>)(g => g switch {
                Brep brep => ComputeBrepNakedEdges(brep: brep, orderLoops: orderLoops),
                Mesh mesh => ComputeMeshNakedEdges(mesh: mesh, orderLoops: orderLoops),
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
        bool enableDiagnostics) where T : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.BoundaryLoopData>>>)(g => g switch {
                Brep brep => ComputeBoundaryLoops(
                    nakedCurves: Enumerable.Range(0, brep.Edges.Count)
                        .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                        .Select(i => brep.Edges[i].DuplicateCurve())
                        .ToArray(),
                    joinTolerance: tol),
                Mesh mesh => ComputeBoundaryLoops(
                    nakedCurves: (mesh.GetNakedEdges() ?? Array.Empty<Polyline>())
                        .Select(pl => pl.ToNurbsCurve())
                        .ToArray(),
                    joinTolerance: tol),
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
    }

    [Pure]
    internal static Result<Topology.NonManifoldData> ExecuteNonManifold<T>(
        T input,
        IGeometryContext context,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.NonManifoldData>>>)(g => g switch {
                Brep brep => ComputeBrepNonManifold(brep: brep),
                Mesh mesh => ComputeMeshNonManifold(mesh: mesh),
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
                Brep brep => ComputeBrepConnectivity(brep: brep),
                Mesh mesh => ComputeMeshConnectivity(mesh: mesh),
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
        bool enableDiagnostics = false) where T : notnull {
        Continuity minCont = minimumContinuity ?? Continuity.G1_continuous;
        double angleThresh = angleThreshold ?? context.AngleToleranceRadians;
        return UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.EdgeClassificationData>>>)(g => g switch {
                Brep brep => ComputeBrepEdgeClassification(brep: brep, minContinuity: minCont, angleThreshold: angleThresh),
                Mesh mesh => ComputeMeshEdgeClassification(mesh: mesh, angleThreshold: angleThresh),
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
    }

    [Pure]
    internal static Result<Topology.AdjacencyData> ExecuteAdjacency<T>(
        T input,
        IGeometryContext context,
        int edgeIndex,
        bool enableDiagnostics) where T : notnull =>
        UnifiedOperation.Apply(
            input: input,
            operation: (Func<T, Result<IReadOnlyList<Topology.AdjacencyData>>>)(g => g switch {
                Brep brep when edgeIndex >= 0 && edgeIndex < brep.Edges.Count =>
                    ComputeBrepAdjacency(brep: brep, edgeIndex: edgeIndex),
                Brep brep => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(
                        string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {brep.Edges.Count - 1}"))),
                Mesh mesh when edgeIndex >= 0 && edgeIndex < mesh.TopologyEdges.Count =>
                    ComputeMeshAdjacency(mesh: mesh, edgeIndex: edgeIndex),
                Mesh mesh => ResultFactory.Create<IReadOnlyList<Topology.AdjacencyData>>(
                    error: E.Geometry.InvalidEdgeIndex.WithContext(
                        string.Create(CultureInfo.InvariantCulture, $"EdgeIndex: {edgeIndex}, Max: {mesh.TopologyEdges.Count - 1}"))),
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
    private static Result<IReadOnlyList<Topology.NakedEdgeData>> ComputeBrepNakedEdges(Brep brep, bool orderLoops) {
        int[] nakedIndices = brep.Edges.Count == 0
            ? Array.Empty<int>()
            : Enumerable.Range(0, brep.Edges.Count)
                .Where(i => brep.Edges[i].Valence == EdgeAdjacency.Naked)
                .ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
            new Topology.NakedEdgeData(
                EdgeCurves: nakedIndices.Select(i => brep.Edges[i].DuplicateCurve()).ToArray(),
                EdgeIndices: nakedIndices,
                Valences: nakedIndices.Select(_ => 1).ToArray(),
                IsOrdered: orderLoops,
                TotalEdgeCount: brep.Edges.Count,
                TotalLength: nakedIndices.Sum(i => brep.Edges[i].GetLength())),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NakedEdgeData>> ComputeMeshNakedEdges(Mesh mesh, bool orderLoops) {
        Polyline[] nakedPolylines = mesh.GetNakedEdges() ?? Array.Empty<Polyline>();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.NakedEdgeData>)[
            new Topology.NakedEdgeData(
                EdgeCurves: nakedPolylines.Select(pl => pl.ToNurbsCurve()).ToArray(),
                EdgeIndices: Enumerable.Range(0, nakedPolylines.Length).ToArray(),
                Valences: Enumerable.Repeat(1, nakedPolylines.Length).ToArray(),
                IsOrdered: orderLoops,
                TotalEdgeCount: mesh.TopologyEdges.Count,
                TotalLength: nakedPolylines.Sum(pl => pl.Length)),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.BoundaryLoopData>> ComputeBoundaryLoops(Curve[] nakedCurves, double joinTolerance) {
        Curve[] joined = nakedCurves.Length > 0
            ? Curve.JoinCurves(nakedCurves, joinTolerance: joinTolerance, preserveDirection: false)
            : Array.Empty<Curve>();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.BoundaryLoopData>)[
            new Topology.BoundaryLoopData(
                Loops: joined,
                EdgeIndicesPerLoop: joined.Select(_ => EmptyIndices).ToArray(),
                LoopLengths: joined.Select(c => c.GetLength()).ToArray(),
                IsClosedPerLoop: joined.Select(c => c.IsClosed).ToArray(),
                JoinTolerance: joinTolerance,
                FailedJoins: nakedCurves.Length - joined.Length),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ComputeBrepNonManifold(Brep brep) {
        int[] nonManifoldEdges = Enumerable.Range(0, brep.Edges.Count)
            .Where(i => brep.Edges[i].Valence == EdgeAdjacency.NonManifold)
            .ToArray();
        int[] valences = nonManifoldEdges.Select(i => (int)brep.Edges[i].Valence).ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
            new Topology.NonManifoldData(
                EdgeIndices: nonManifoldEdges,
                VertexIndices: Array.Empty<int>(),
                Valences: valences,
                Locations: nonManifoldEdges.Select(i => brep.Edges[i].PointAtStart).ToArray(),
                IsManifold: nonManifoldEdges.Length == 0,
                IsOrientable: brep.IsSolid,
                MaxValence: valences.Length > 0 ? valences.Max() : 0),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.NonManifoldData>> ComputeMeshNonManifold(Mesh mesh) {
        bool isManifold = mesh.IsManifold(topologicalTest: true, out bool isOriented, out bool _);
        int[] nonManifoldEdges = Enumerable.Range(0, mesh.TopologyEdges.Count)
            .Where(i => mesh.TopologyEdges.GetConnectedFaces(i).Length > 2)
            .ToArray();
        int[] valences = nonManifoldEdges.Select(i => mesh.TopologyEdges.GetConnectedFaces(i).Length).ToArray();
        Point3d[] locations = nonManifoldEdges.Select(i => {
            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
            Point3d p1 = mesh.TopologyVertices[verts.I];
            Point3d p2 = mesh.TopologyVertices[verts.J];
            return new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, (p1.Z + p2.Z) / 2.0);
        }).ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.NonManifoldData>)[
            new Topology.NonManifoldData(
                EdgeIndices: nonManifoldEdges,
                VertexIndices: Array.Empty<int>(),
                Valences: valences,
                Locations: locations,
                IsManifold: isManifold,
                IsOrientable: isOriented,
                MaxValence: valences.Length > 0 ? valences.Max() : 0),
        ]);
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ComputeBrepConnectivity(Brep brep) {
        int[] componentIds = new int[brep.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;

        for (int seed = 0; seed < brep.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseBrepComponent(brep: brep, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }

        IReadOnlyList<int>[] components = Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)Enumerable.Range(0, brep.Faces.Count).Where(f => componentIds[f] == c).ToArray())
            .ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: components.Select(c => c.Count).ToArray(),
                ComponentBounds: components.Select(c => c.Aggregate(
                    BoundingBox.Empty,
                    (union, fIdx) => union.IsValid
                        ? BoundingBox.Union(union, brep.Faces[fIdx].GetBoundingBox(accurate: false))
                        : brep.Faces[fIdx].GetBoundingBox(accurate: false))).ToArray(),
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, brep.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)brep.Faces[f].AdjacentEdges()
                        .SelectMany(e => brep.Edges[e].AdjacentFaces())
                        .Where(adj => adj != f)
                        .ToArray()))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseBrepComponent(Brep brep, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new(new[] { seed, });
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
    private static Result<IReadOnlyList<Topology.ConnectivityData>> ComputeMeshConnectivity(Mesh mesh) {
        int[] componentIds = new int[mesh.Faces.Count];
        Array.Fill(componentIds, -1);
        int componentCount = 0;

        for (int seed = 0; seed < mesh.Faces.Count; seed++) {
            componentCount = componentIds[seed] != -1
                ? componentCount
                : TraverseMeshComponent(mesh: mesh, componentIds: componentIds, seed: seed, componentId: componentCount) + 1;
        }

        IReadOnlyList<int>[] components = Enumerable.Range(0, componentCount)
            .Select(c => (IReadOnlyList<int>)Enumerable.Range(0, mesh.Faces.Count).Where(f => componentIds[f] == c).ToArray())
            .ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
            new Topology.ConnectivityData(
                ComponentIndices: components,
                ComponentSizes: components.Select(c => c.Count).ToArray(),
                ComponentBounds: components.Select(c => c.Aggregate(
                    BoundingBox.Empty,
                    (union, fIdx) => {
                        MeshFace face = mesh.Faces[fIdx];
                        BoundingBox fBox = face.IsQuad
                            ? new BoundingBox(new Point3d[] {
                                new Point3d(mesh.Vertices[face.A]),
                                new Point3d(mesh.Vertices[face.B]),
                                new Point3d(mesh.Vertices[face.C]),
                                new Point3d(mesh.Vertices[face.D]),
                            })
                            : new BoundingBox(new Point3d[] {
                                new Point3d(mesh.Vertices[face.A]),
                                new Point3d(mesh.Vertices[face.B]),
                                new Point3d(mesh.Vertices[face.C]),
                            });
                        return union.IsValid ? BoundingBox.Union(union, fBox) : fBox;
                    })).ToArray(),
                TotalComponents: componentCount,
                IsFullyConnected: componentCount == 1,
                AdjacencyGraph: Enumerable.Range(0, mesh.Faces.Count)
                    .Select(f => (f, (IReadOnlyList<int>)mesh.Faces.AdjacentFaces(f).Where(adj => adj >= 0).ToArray()))
                    .ToFrozenDictionary(x => x.f, x => x.Item2)),
        ]);
    }

    private static int TraverseMeshComponent(Mesh mesh, int[] componentIds, int seed, int componentId) {
        Queue<int> queue = new(new[] { seed, });
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
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ComputeBrepEdgeClassification(
        Brep brep,
        Continuity minContinuity,
        double angleThreshold) {
        int[] edgeIndices = Enumerable.Range(0, brep.Edges.Count).ToArray();
        Topology.EdgeContinuityType[] classifications = edgeIndices.Select(i => ClassifyBrepEdge(
            edge: brep.Edges[i],
            minContinuity: minContinuity,
            angleThreshold: angleThreshold)).ToArray();
        double[] measures = edgeIndices.Select(i => brep.Edges[i].GetLength()).ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: edgeIndices
                    .Select((idx, pos) => (idx, type: classifications[pos]))
                    .GroupBy(x => x.type, x => x.idx)
                    .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)g.ToArray()),
                MinimumContinuity: minContinuity),
        ]);
    }

    [Pure]
    private static Topology.EdgeContinuityType ClassifyBrepEdge(BrepEdge edge, Continuity minContinuity, double angleThreshold) =>
        edge.Valence switch {
            EdgeAdjacency.Naked => Topology.EdgeContinuityType.Boundary,
            EdgeAdjacency.NonManifold => Topology.EdgeContinuityType.NonManifold,
            EdgeAdjacency.Interior => edge.EdgeCurve switch {
                Curve crv when crv.IsContinuous(continuityType: Continuity.G2_continuous, t: crv.Domain.Mid) ||
                    crv.IsContinuous(continuityType: Continuity.G2_locus_continuous, t: crv.Domain.Mid) =>
                    Topology.EdgeContinuityType.Curvature,
                Curve crv when edge.IsSmoothManifoldEdge(angleToleranceRadians: angleThreshold) ||
                    crv.IsContinuous(continuityType: Continuity.G1_continuous, t: crv.Domain.Mid) ||
                    crv.IsContinuous(continuityType: Continuity.G1_locus_continuous, t: crv.Domain.Mid) =>
                    Topology.EdgeContinuityType.Smooth,
                _ when minContinuity >= Continuity.G1_continuous => Topology.EdgeContinuityType.Sharp,
                _ => Topology.EdgeContinuityType.Interior,
            },
            _ => Topology.EdgeContinuityType.Sharp,
        };

    [Pure]
    private static Result<IReadOnlyList<Topology.EdgeClassificationData>> ComputeMeshEdgeClassification(Mesh mesh, double angleThreshold) {
        double curvatureThreshold = angleThreshold * 0.1;
        int[] edgeIndices = Enumerable.Range(0, mesh.TopologyEdges.Count).ToArray();
        Topology.EdgeContinuityType[] classifications = edgeIndices.Select(i => {
            int[] connectedFaces = mesh.TopologyEdges.GetConnectedFaces(i);
            return connectedFaces.Length switch {
                1 => Topology.EdgeContinuityType.Boundary,
                > 2 => Topology.EdgeContinuityType.NonManifold,
                2 => ComputeDihedralAngle(mesh: mesh, faceIdx1: connectedFaces[0], faceIdx2: connectedFaces[1]) switch {
                    double angle when Math.Abs(angle) < curvatureThreshold => Topology.EdgeContinuityType.Curvature,
                    double angle when Math.Abs(angle) < angleThreshold => Topology.EdgeContinuityType.Smooth,
                    _ => Topology.EdgeContinuityType.Sharp,
                },
                _ => Topology.EdgeContinuityType.Sharp,
            };
        }).ToArray();
        double[] measures = edgeIndices.Select(i => {
            IndexPair verts = mesh.TopologyEdges.GetTopologyVertices(i);
            return mesh.TopologyVertices[verts.I].DistanceTo(mesh.TopologyVertices[verts.J]);
        }).ToArray();

        return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeClassificationData>)[
            new Topology.EdgeClassificationData(
                EdgeIndices: edgeIndices,
                Classifications: classifications,
                ContinuityMeasures: measures,
                GroupedByType: edgeIndices
                    .Select((idx, pos) => (idx, type: classifications[pos]))
                    .GroupBy(x => x.type, x => x.idx)
                    .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<int>)g.ToArray()),
                MinimumContinuity: Continuity.C0_continuous),
        ]);
    }

    [Pure]
    private static double ComputeDihedralAngle(Mesh mesh, int faceIdx1, int faceIdx2) {
        Vector3d n1 = new Vector3d(mesh.FaceNormals[faceIdx1]);
        Vector3d n2 = new Vector3d(mesh.FaceNormals[faceIdx2]);
        return n1.IsValid && n2.IsValid ? Vector3d.VectorAngle(n1, n2) : Math.PI;
    }

    [Pure]
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ComputeBrepAdjacency(Brep brep, int edgeIndex) {
        BrepEdge edge = brep.Edges[edgeIndex];
        int[] adjFaces = edge.AdjacentFaces().ToArray();
        Vector3d[] normals = adjFaces.Select(i => {
            BrepFace face = brep.Faces[i];
            double uMid = face.Domain(0).Mid;
            double vMid = face.Domain(1).Mid;
            return face.NormalAt(uMid, vMid);
        }).ToArray();
        double dihedralAngle = normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;

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
    private static Result<IReadOnlyList<Topology.AdjacencyData>> ComputeMeshAdjacency(Mesh mesh, int edgeIndex) {
        int[] adjFaces = mesh.TopologyEdges.GetConnectedFaces(edgeIndex);
        Vector3d[] normals = adjFaces.Select(i => new Vector3d(mesh.FaceNormals[i])).ToArray();
        double dihedralAngle = normals.Length == 2 ? Vector3d.VectorAngle(normals[0], normals[1]) : 0.0;

        return ResultFactory.Create(value: (IReadOnlyList<Topology.AdjacencyData>)[
            new Topology.AdjacencyData(
                EdgeIndex: edgeIndex,
                AdjacentFaceIndices: adjFaces,
                FaceNormals: normals,
                DihedralAngle: dihedralAngle,
                IsManifold: adjFaces.Length == 2,
                IsBoundary: adjFaces.Length == 1),
        ]);
    }
}

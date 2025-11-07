using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology engine with type-driven FrozenDictionary dispatch and zero-allocation query execution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
    public interface IResult { }

    /// <summary>Topology analysis mode enumeration for operation dispatch.</summary>
    public enum TopologyMode : byte {
        NakedEdges = 0,
        BoundaryLoops = 1,
        NonManifold = 2,
        Connectivity = 3,
        EdgeClassification = 4,
        Adjacency = 5,
    }

    /// <summary>Edge continuity classification enumeration.</summary>
    public enum EdgeContinuityType : byte {
        Sharp = 0,
        Smooth = 1,
        Curvature = 2,
        Interior = 3,
        Boundary = 4,
        NonManifold = 5,
    }

    /// <summary>Executes topology operations using type-driven dispatch with mode parameter controlling operation type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TResult> Analyze<TGeometry, TResult>(
        TGeometry geometry,
        IGeometryContext context,
        TopologyMode mode,
        params object[] args)
        where TGeometry : notnull
        where TResult : IResult =>
        TopologyCompute.StrategyConfig.TryGetValue((typeof(TGeometry), mode), out (V validationMode, Func<object, IGeometryContext, object[], Result<IResult>> compute) strategy) switch {
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<TGeometry, Result<IReadOnlyList<IResult>>>)(g => strategy.compute(g, context, args).Map(r => (IReadOnlyList<IResult>)[r,])),
                config: new OperationConfig<TGeometry, IResult> {
                    Context = context,
                    ValidationMode = strategy.validationMode,
                    OperationName = $"Topology.{typeof(TGeometry).Name}.{mode}",
                    EnableDiagnostics = args.Length > 0 && args[^1] is bool diag && diag,
                })
                .Map(results => (TResult)results[0]),
            false => ResultFactory.Create<TResult>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(TGeometry).Name}, Mode: {mode}")),
        };

    /// <summary>Naked edge analysis result containing edge curves and indices.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NakedEdgeData(
        IReadOnlyList<Curve> EdgeCurves,
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> Valences,
        bool IsOrdered,
        int TotalEdgeCount,
        double TotalLength) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"NakedEdges: {this.EdgeCurves.Count}/{this.TotalEdgeCount} | L={this.TotalLength:F3} | Ordered={this.IsOrdered}");
    }

    /// <summary>Boundary loop analysis result with closed loop curves.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BoundaryLoopData(
        IReadOnlyList<Curve> Loops,
        IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
        IReadOnlyList<double> LoopLengths,
        IReadOnlyList<bool> IsClosedPerLoop,
        double JoinTolerance,
        int FailedJoins) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"BoundaryLoops: {this.Loops.Count} | FailedJoins={this.FailedJoins} | Tol={this.JoinTolerance:E2}");
    }

    /// <summary>Non-manifold topology analysis result with diagnostic data.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record NonManifoldData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<int> VertexIndices,
        IReadOnlyList<int> Valences,
        IReadOnlyList<Point3d> Locations,
        bool IsManifold,
        bool IsOrientable,
        int MaxValence) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsManifold
            ? "Manifold: No issues detected"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NonManifold: Edges={this.EdgeIndices.Count} | Verts={this.VertexIndices.Count} | MaxVal={this.MaxValence}");
    }

    /// <summary>Connected component analysis result with adjacency graph data.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ConnectivityData(
        IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
        IReadOnlyList<int> ComponentSizes,
        IReadOnlyList<BoundingBox> ComponentBounds,
        int TotalComponents,
        bool IsFullyConnected,
        FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsFullyConnected
            ? "Connectivity: Single connected component"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Connectivity: {this.TotalComponents} components | Largest={this.ComponentSizes.Max()}");
    }

    /// <summary>Edge classification result by continuity type.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record EdgeClassificationData(
        IReadOnlyList<int> EdgeIndices,
        IReadOnlyList<EdgeContinuityType> Classifications,
        IReadOnlyList<double> ContinuityMeasures,
        FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
        Continuity MinimumContinuity) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"EdgeClassification: Total={this.EdgeIndices.Count} | Sharp={this.GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
    }

    /// <summary>Face adjacency query result with neighbor data.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record AdjacencyData(
        int EdgeIndex,
        IReadOnlyList<int> AdjacentFaceIndices,
        IReadOnlyList<Vector3d> FaceNormals,
        double DihedralAngle,
        bool IsManifold,
        bool IsBoundary) : IResult {
        [Pure]
        private string DebuggerDisplay => this.IsBoundary
            ? $"Edge[{this.EdgeIndex}]: Boundary (valence=1)"
            : this.IsManifold
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Edge[{this.EdgeIndex}]: Manifold | Angle={this.DihedralAngle * 180.0 / Math.PI:F1}°")
                : $"Edge[{this.EdgeIndex}]: NonManifold (valence={this.AdjacentFaceIndices.Count})";
    }
}

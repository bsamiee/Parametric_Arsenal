using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Rhino.Topology;

/// <summary>Topology analysis mode enumeration for operation dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "byte enum for performance and memory efficiency")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Enums grouped with primary API type")]
public enum TopologyMode : byte {
    /// <summary>Extract naked (boundary) edges with valence=1.</summary>
    NakedEdges = 0,
    /// <summary>Construct closed boundary loops from naked edges.</summary>
    BoundaryLoops = 1,
    /// <summary>Detect non-manifold vertices and edges.</summary>
    NonManifold = 2,
    /// <summary>Analyze connected components and adjacency graph.</summary>
    Connectivity = 3,
    /// <summary>Classify edges by continuity type (sharp, smooth, curvature).</summary>
    EdgeClassification = 4,
}

/// <summary>Edge continuity classification enumeration for geometric analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "byte enum for performance and memory efficiency")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Enums grouped with primary API type")]
public enum EdgeContinuityType : byte {
    /// <summary>G0 discontinuous or below minimum continuity threshold.</summary>
    Sharp = 0,
    /// <summary>G1 continuous (tangent continuity).</summary>
    Smooth = 1,
    /// <summary>G2 continuous (curvature continuity).</summary>
    Curvature = 2,
    /// <summary>Interior manifold edge (valence=2, meets continuity requirement).</summary>
    Interior = 3,
    /// <summary>Boundary naked edge (valence=1).</summary>
    Boundary = 4,
    /// <summary>Non-manifold edge (valence>2).</summary>
    NonManifold = 5,
}

/// <summary>Polymorphic topology engine with type-driven FrozenDictionary dispatch for structural and connectivity analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult { }

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
                })
                .Map(results => (TResult)results[0]),
            false => ResultFactory.Create<TResult>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(TGeometry).Name}, Mode: {mode}")),
        };
}

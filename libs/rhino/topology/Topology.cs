using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Rhino.Topology;

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

/// <summary>Polymorphic topology engine with type-driven FrozenDictionary dispatch and zero-allocation query execution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Topology is the primary API entry point for the Topology namespace")]
public static class Topology {
    /// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
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
                    EnableDiagnostics = args.Length > 0 && args[^1] is bool diag && diag,
                })
                .Map(results => (TResult)results[0]),
            false => ResultFactory.Create<TResult>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(TGeometry).Name}, Mode: {mode}")),
        };
}

using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Scope = "namespace", Target = "~N:Arsenal.Rhino.Boolean", Justification = "Boolean is the established name for this domain operation namespace")]

namespace Arsenal.Rhino.Boolean;

/// <summary>Unified boolean operations for Brep, Mesh, and planar Curve geometry.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Boolean is the primary API entry point for the Boolean namespace")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Boolean is the established name for this domain operation")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0104:Do not create a type with a name from the BCL", Justification = "Boolean is the domain-specific name for boolean operations")]
public static class Boolean {
    /// <summary>Boolean operation type selector.</summary>
    public enum OperationType {
        Union = 0,
        Intersection = 1,
        Difference = 2,
        Split = 3,
    }

    /// <summary>Boolean operation configuration options.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct BooleanOptions(
        double? ToleranceOverride = null,
        bool ManifoldOnly = false,
        bool CombineCoplanarFaces = true,
        bool ValidateResult = true);

    /// <summary>Boolean operation result containing geometry arrays and metadata.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct BooleanOutput(
        IReadOnlyList<Brep> Breps,
        IReadOnlyList<Mesh> Meshes,
        IReadOnlyList<Curve> Curves,
        double ToleranceUsed) {
        /// <summary>Empty result for non-intersecting or failed operations.</summary>
        public static readonly BooleanOutput Empty = new([], [], [], 0.0);
    }

    /// <summary>Executes type-detected boolean operation with automatic validation and dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BooleanOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        OperationType operation,
        IGeometryContext context,
        BooleanOptions? options = null) where T1 : notnull where T2 : notnull =>
        BooleanCore.OperationRegistry.TryGetValue(
            key: (typeof(T1), typeof(T2), operation),
            value: out (V ValidationMode, Func<object, object, OperationType, BooleanOptions, IGeometryContext, Result<BooleanOutput>> Executor) config) switch {
            true => UnifiedOperation.Apply(
                input: geometryA,
                operation: (Func<T1, Result<IReadOnlyList<BooleanOutput>>>)(itemA => config.Executor(
                    itemA,
                    geometryB,
                    operation,
                    options ?? new BooleanOptions(),
                    context)
                    .Map(output => (IReadOnlyList<BooleanOutput>)[output,])),
                config: new OperationConfig<T1, BooleanOutput> {
                    Context = context,
                    ValidationMode = config.ValidationMode,
                    OperationName = $"Boolean.{operation}.{typeof(T1).Name}.{typeof(T2).Name}",
                    EnableDiagnostics = false,
                })
                .Map(outputs => outputs.Count > 0 ? outputs[0] : BooleanOutput.Empty),
            false => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext(
                    $"Operation: {operation}, Types: {typeof(T1).Name}, {typeof(T2).Name}")),
        };
}

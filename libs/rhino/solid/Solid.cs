using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Solid;

/// <summary>Solid boolean operations for Brep and Mesh geometry.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Solid is the primary API entry point for the Solid namespace")]
public static class Solid {
    /// <summary>Boolean operation configuration options.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct SolidOptions(
        double? ToleranceOverride = null,
        bool ManifoldOnly = false,
        bool ValidateResult = true,
        bool CombineCoplanarFaces = true);

    /// <summary>Boolean operation result containing geometry arrays and tolerance metadata.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct SolidOutput(
        IReadOnlyList<Brep> Breps,
        IReadOnlyList<Mesh> Meshes,
        double ToleranceUsed) {
        /// <summary>Empty result for non-intersecting or failed operations.</summary>
        public static readonly SolidOutput Empty = new([], [], 0.0);
    }

    /// <summary>Combines geometries by adding overlapping volumes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SolidOutput> Union<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        SolidOptions? options = null) where T1 : notnull where T2 : notnull =>
        SolidCore.ExecuteOperation(geometryA, geometryB, SolidConfig.UnionOp, context, options ?? new SolidOptions());

    /// <summary>Extracts shared volume between geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SolidOutput> Intersection<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        SolidOptions? options = null) where T1 : notnull where T2 : notnull =>
        SolidCore.ExecuteOperation(geometryA, geometryB, SolidConfig.IntersectionOp, context, options ?? new SolidOptions());

    /// <summary>Subtracts second geometry from first.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SolidOutput> Difference<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        SolidOptions? options = null) where T1 : notnull where T2 : notnull =>
        SolidCore.ExecuteOperation(geometryA, geometryB, SolidConfig.DifferenceOp, context, options ?? new SolidOptions());

    /// <summary>Divides first geometry using second as cutting tool.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SolidOutput> Split<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        SolidOptions? options = null) where T1 : notnull where T2 : notnull =>
        SolidCore.ExecuteOperation(geometryA, geometryB, SolidConfig.SplitOp, context, options ?? new SolidOptions());

    /// <summary>Trims Brep using oriented cutter retaining portions inside cutter normal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SolidOutput> Trim(
        Brep target,
        Brep cutter,
        IGeometryContext context,
        SolidOptions? options = null) =>
        UnifiedOperation.Apply(
            input: target,
            operation: (Func<Brep, Result<IReadOnlyList<SolidOutput>>>)(item => SolidCompute.BrepTrim(
                item,
                cutter,
                options ?? new SolidOptions(),
                context)
                .Map(output => (IReadOnlyList<SolidOutput>)[output])),
            config: new OperationConfig<Brep, SolidOutput> {
                Context = context,
                ValidationMode = V.Standard | V.Topology,
                OperationName = "Solid.Trim",
                EnableDiagnostics = false,
            })
            .Map(outputs => outputs.Count > 0 ? outputs[0] : SolidOutput.Empty);
}

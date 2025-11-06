using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction with singular API.</summary>
public static class Extract {
    /// <summary>Semantic extraction marker for parameterless methods.</summary>
    public readonly struct Semantic(byte kind) {
        internal readonly byte Kind = kind;
        public static readonly Semantic Analytical = new(1);
        public static readonly Semantic Extremal = new(2);
        public static readonly Semantic Greville = new(3);
        public static readonly Semantic Inflection = new(4);
        public static readonly Semantic Quadrant = new(5);
        public static readonly Semantic EdgeMidpoints = new(6);
        public static readonly Semantic FaceCentroids = new(7);
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : notnull =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength),
            Vector3d dir when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidDirection),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity or Semantic =>
                UnifiedOperation.Apply(
                    input,
                    (Func<object, Result<IReadOnlyList<Point3d>>>)(item => item switch {
                        GeometryBase g => ExtractionCore.Execute(g, spec, context),
                        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ValidationErrors.Geometry.Invalid),
                    }),
                    new OperationConfig<object, Point3d> { Context = context, ValidationMode = ValidationMode.None }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        };
}

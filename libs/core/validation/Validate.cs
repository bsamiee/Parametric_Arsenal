using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;

namespace Arsenal.Core.Validation;

/// <summary>Unified validation entry point using polymorphic parameter detection for single clean API.</summary>
public static class Validate {
    /// <summary>Polymorphic validation dispatcher with automatic parameter type detection and config composition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> For<T>(T value, params object[] args) where T : notnull =>
        (value, args) switch {
            (var v, [IGeometryContext ctx, ValidationConfig config]) when IsGeometryType(typeof(T)) =>
                ResultFactory.Create(value: v).Bind(g =>
                    ValidationRules.GetOrCompileValidator(g!.GetType(), config)(g, ctx) switch {
                        SystemError[] { Length: 0 } => ResultFactory.Create(value: g),
                        SystemError[] errors => ResultFactory.Create<T>(errors: errors),
                    }),
            (var v, [IGeometryContext ctx]) when IsGeometryType(typeof(T)) =>
                For(v, ctx, ValidationConfig.Standard),
            (double absoluteTolerance, [double relativeTolerance, double angleToleranceRadians]) =>
                (Result<T>)(object)(
                    [.. (!(Rhino.RhinoMath.IsValidDouble(absoluteTolerance) && absoluteTolerance > Rhino.RhinoMath.ZeroTolerance) ?
                        [ErrorRegistry.Validation.ToleranceInvalidAbsolute,] : []),
                        .. (!(Rhino.RhinoMath.IsValidDouble(relativeTolerance) && relativeTolerance is >= 0d and < 1d) ?
                        [ErrorRegistry.Validation.ToleranceInvalidRelative,] : []),
                        .. (!(Rhino.RhinoMath.IsValidDouble(angleToleranceRadians) && angleToleranceRadians is > 0d and <= (2d * System.Math.PI)) ?
                        [ErrorRegistry.Validation.ToleranceInvalidAngle,] : []),
                    ] switch {
                        SystemError[] { Length: 0 } => ResultFactory.Create(value: (double)(object)absoluteTolerance),
                        SystemError[] errors => ResultFactory.Create<double>(errors: errors),
                    }),
            (var v, [Func<T, bool> predicate, SystemError error]) =>
                predicate(v) switch {
                    true => ResultFactory.Create(value: v),
                    false => ResultFactory.Create<T>(error: error),
                },
            (var v, [(Func<T, bool>, SystemError)[] validations]) when validations.Length > 0 =>
                validations.Where(pair => !pair.Item1(v)).Select(pair => pair.Item2).ToArray() switch {
                    SystemError[] { Length: 0 } => ResultFactory.Create(value: v),
                    SystemError[] errors => ResultFactory.Create<T>(errors: errors),
                },
            (var v, []) => ResultFactory.Create(value: v),
            _ => ResultFactory.Create(value: value),
        };

    /// <summary>Checks if type is Geometry without loading Rhino assembly using string comparison.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometryType(Type type) =>
        type.FullName?.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) ?? false;
}

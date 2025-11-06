using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;

namespace Arsenal.Core.Validation;

/// <summary>Polymorphic validation API with unified 'That' method for all validation scenarios.</summary>
public static class Validate {
    /// <summary>Polymorphic validation dispatcher with automatic detection of validation type and parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> That<T>(
        T? value = default,
        Func<T, bool>? predicate = null,
        SystemError? error = null,
        IGeometryContext? context = null,
        ValidationMode mode = ValidationMode.None,
        Func<T, bool>? unless = null,
        Func<T, bool>? premise = null,
        Func<T, bool>? conclusion = null,
        Func<T, bool>[]? validations = null,
        object[]? args = null) where T : notnull =>
        (value, predicate, error, context, mode, unless, premise, conclusion, validations, args) switch {
            // Geometry validation with context and mode
            (T v, null, null, IGeometryContext ctx, ValidationMode m, null, null, null, null, null) when m is not ValidationMode.None && IsGeometryType<T>() =>
                ResultFactory.Create(value: v).Bind(x => {
                    SystemError[] errors = ValidationRules.GetOrCompileValidator(runtimeType: typeof(T), mode: m)(x, ctx);
                    return errors.Length > 0
                        ? ResultFactory.Create<T>(errors: errors)
                        : ResultFactory.Create(value: x);
                }),

            // Predicate validation with error
            (T v, Func<T, bool> pred, SystemError err, null, ValidationMode.None, null, null, null, null, null) =>
                ResultFactory.Create(value: v).Ensure(predicate: pred, error: err),

            // Predicate validation with unless guard
            (T v, Func<T, bool> pred, SystemError err, null, ValidationMode.None, Func<T, bool> unlessGuard, null, null, null, null) =>
                ResultFactory.Create(value: v).Ensure(predicate: x => !unlessGuard(x) && pred(x), error: err),

            // Implication validation (premise => conclusion)
            (T v, null, SystemError err, null, ValidationMode.None, null, Func<T, bool> prem, Func<T, bool> concl, null, null) =>
                ResultFactory.Create(value: v).Ensure(predicate: x => !prem(x) || concl(x), error: err),

            // Multiple predicate validation with error accumulation
            (T v, null, SystemError err, null, ValidationMode.None, null, null, null, Func<T, bool>[] preds, null) =>
                preds.Aggregate(
                    seed: ResultFactory.Create(value: v),
                    func: (result, pred) => result.Ensure(predicate: pred, error: err)),

            // Tolerance validation for context values
            (T v, null, null, null, ValidationMode.None, null, null, null, null, object[] a) when typeof(T) == typeof(double) =>
                ValidationRules.For(input: v, args: a).Length is int len && len > 0
                    ? ResultFactory.Create<T>(errors: ValidationRules.For(input: v, args: a))
                    : ResultFactory.Create(value: v),

            // Invalid parameters
            _ => ResultFactory.Create<T>(error: CoreErrors.Results.InvalidValidateParameters),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometryType<T>() {
        string typeName = typeof(T).FullName ?? string.Empty;
        return typeName.StartsWith("Rhino.Geometry.", StringComparison.Ordinal);
    }
}

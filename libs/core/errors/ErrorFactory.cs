using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Polymorphic error factory with context injection and batch creation patterns.</summary>
public static class ErrorFactory {
    /// <summary>Creates error with polymorphic parameter detection for context or domain specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Create(
        int code,
        string? context = null,
        ErrorDomain? domain = null,
        string? message = null) =>
        (domain, message, context) switch {
            ({ } d, { } m, null) => new(d, code, m),
            ({ } d, { } m, { } c) => new(d, code, $"{m} (Context: {c})"),
            (null, { } m, null) => new(ErrorRegistry.Get(code).Domain, code, m),
            (null, { } m, { } c) => new(ErrorRegistry.Get(code).Domain, code, $"{m} (Context: {c})"),
            _ => ErrorRegistry.Get(code, context),
        };

    /// <summary>Batch creates errors from code/context pairs for validation accumulation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] CreateBatch(params (int Code, string? Context)[] errors) =>
        errors.Length switch {
            0 => [],
            1 => [ErrorRegistry.Get(errors[0].Code, errors[0].Context),],
            _ => [.. errors.Select(e => ErrorRegistry.Get(e.Code, e.Context)),],
        };

    /// <summary>Creates error with format string context for structured error reporting.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError WithFormat(int code, FormattableString context) =>
        ErrorRegistry.Get(code, context.ToString());

    /// <summary>Wraps exception as SystemError with stack trace context in diagnostics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError FromException(Exception exception, int fallbackCode = 1100) =>
        ErrorRegistry.Get(fallbackCode, $"{exception.GetType().Name}: {exception.Message}");

    /// <summary>Creates conditional error based on predicate result for validation chains.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When(bool condition, int code, string? context = null) =>
        condition ? [ErrorRegistry.Get(code, context),] : [];

    /// <summary>Creates conditional error with polymorphic predicate dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When<T>(T value, Func<T, bool> predicate, int code, string? context = null) =>
        predicate(value) ? [ErrorRegistry.Get(code, context),] : [];
}

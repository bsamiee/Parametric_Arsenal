using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Centralized error registry with duplicate code detection and optional introspection.</summary>
public static class ErrorRegistry {
    private static readonly ConcurrentDictionary<(ErrorDomain, int), SystemError> _registry = new();

    /// <summary>Registers error with duplicate code detection.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Register(ErrorDomain domain, int code, string message) =>
        _registry.GetOrAdd((domain, code), static (key, msg) => new(key.Item1, key.Item2, msg), message);

    /// <summary>Gets all registered errors for introspection or diagnostic purposes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyDictionary<(ErrorDomain Domain, int Code), SystemError> GetAll() => _registry;
}

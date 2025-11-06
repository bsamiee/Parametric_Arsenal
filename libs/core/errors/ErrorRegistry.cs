using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Centralized error registry with FrozenDictionary dispatch for zero-allocation lookups and automatic code allocation.</summary>
public static class ErrorRegistry {
    /// <summary>Error specification for registration with automatic code allocation within domain ranges.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ErrorSpec(ErrorDomain domain, int code, string message) : IEquatable<ErrorSpec> {
        public readonly ErrorDomain Domain = domain;
        public readonly int Code = code;
        public readonly string Message = message;

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is ErrorSpec other && this.Equals(other);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(this.Domain, this.Code);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ErrorSpec other) => this.Domain == other.Domain && this.Code == other.Code;
    }

    /// <summary>Composite key for error lookups by domain and code.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ErrorKey(ErrorDomain domain, int code) : IEquatable<ErrorKey> {
        public readonly ErrorDomain Domain = domain;
        public readonly int Code = code;

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => obj is ErrorKey other && this.Equals(other);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(this.Domain, this.Code);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ErrorKey other) => this.Domain == other.Domain && this.Code == other.Code;
    }

    private static readonly Dictionary<ErrorKey, SystemError> _registry = new();
    private static FrozenDictionary<ErrorKey, SystemError>? _frozen;

    /// <summary>Registers error with automatic domain-based code allocation and validation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Register(ErrorDomain domain, int code, string message) {
        SystemError error = new(domain, code, message);
        ErrorKey key = new(domain, code);
        _ = _frozen is null
            ? _registry.TryAdd(key, error)
            : throw new InvalidOperationException("Cannot register errors after registry is frozen");
        return error;
    }

    /// <summary>Freezes registry to FrozenDictionary for zero-allocation runtime lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Freeze() => _frozen ??= _registry.ToFrozenDictionary();

    /// <summary>Polymorphic error lookup by domain and code or direct error passthrough.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(ErrorDomain domain = ErrorDomain.None, int code = 0, SystemError? error = null) =>
        error ?? (_frozen ?? _registry.ToFrozenDictionary()).GetValueOrDefault(new ErrorKey(domain, code));
}

using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Error domain discriminator using value-based dispatch instead of enum for extensibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Domain(byte Value) {
    public static readonly Domain None = new(0);
    public static readonly Domain Results = new(10);
    public static readonly Domain Geometry = new(20);
    public static readonly Domain Validation = new(30);
    public static readonly Domain Diagnostics = new(50);
}

using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>3D grid sampling configuration.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly struct SampleGrid {
    internal readonly Point3d Origin;
    internal readonly Vector3d Delta;
    internal readonly int Resolution;
    internal readonly int TotalSamples;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SampleGrid(BoundingBox bounds, int resolution) {
        this.Origin = bounds.Min;
        this.Delta = (bounds.Max - bounds.Min) / (resolution - 1);
        this.Resolution = resolution;
        this.TotalSamples = resolution * resolution * resolution;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Point3d GetPoint(int i, int j, int k) =>
        new(
            this.Origin.X + (i * this.Delta.X),
            this.Origin.Y + (j * this.Delta.Y),
            this.Origin.Z + (k * this.Delta.Z));
}

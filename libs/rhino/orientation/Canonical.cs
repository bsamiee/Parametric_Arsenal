namespace Arsenal.Rhino.Orientation;

/// <summary>Semantic marker for canonical positioning modes.</summary>
public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;

    public static readonly Canonical WorldXY = new(1);
    public static readonly Canonical WorldYZ = new(2);
    public static readonly Canonical WorldXZ = new(3);
    public static readonly Canonical AreaCentroid = new(4);
    public static readonly Canonical VolumeCentroid = new(5);
}

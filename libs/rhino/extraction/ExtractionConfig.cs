using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants and validation modes for extraction operations.</summary>
internal static class ExtractionConfig {
    /// <summary>Validation mode configuration mapping extraction kind and geometry type to validation strategies.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(GeometryBase))] = V.Standard,
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(1, typeof(PointCloud))] = V.Standard,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(NurbsCurve))] = V.Standard,
            [(3, typeof(NurbsSurface))] = V.Standard,
            [(3, typeof(Curve))] = V.Standard,
            [(3, typeof(Surface))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(6, typeof(Curve))] = V.Standard,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(12, typeof(Curve))] = V.Standard,
            [(13, typeof(Curve))] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Kind code mapping for extraction semantic operations.</summary>
    internal const byte KindAnalytical = 1;
    internal const byte KindExtremal = 2;
    internal const byte KindGreville = 3;
    internal const byte KindInflection = 4;
    internal const byte KindQuadrant = 5;
    internal const byte KindEdgeMidpoints = 6;
    internal const byte KindFaceCentroids = 7;
    internal const byte KindDivideCount = 10;
    internal const byte KindDivideLength = 11;
    internal const byte KindExtremeDirection = 12;
    internal const byte KindDiscontinuity = 13;
}

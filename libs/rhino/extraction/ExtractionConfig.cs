using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration for semantic extraction: type IDs, thresholds, validation modes.</summary>
internal static class ExtractionConfig {
    /// <summary>Constant-radius fillet feature type identifier.</summary>
    internal const byte FeatureTypeFillet = 0;
    /// <summary>Chamfer feature type identifier.</summary>
    internal const byte FeatureTypeChamfer = 1;
    /// <summary>Hole feature type identifier.</summary>
    internal const byte FeatureTypeHole = 2;
    /// <summary>Generic edge feature type identifier.</summary>
    internal const byte FeatureTypeGenericEdge = 3;
    /// <summary>Variable-radius fillet feature type identifier.</summary>
    internal const byte FeatureTypeVariableRadiusFillet = 4;

    /// <summary>Planar surface primitive type identifier.</summary>
    internal const byte PrimitiveTypePlane = 0;
    /// <summary>Cylindrical surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeCylinder = 1;
    /// <summary>Spherical surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeSphere = 2;
    /// <summary>Unknown surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeUnknown = 3;
    /// <summary>Conical surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeCone = 4;
    /// <summary>Toroidal surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeTorus = 5;
    /// <summary>Extrusion surface primitive type identifier.</summary>
    internal const byte PrimitiveTypeExtrusion = 6;

    /// <summary>Linear array pattern type identifier.</summary>
    internal const byte PatternTypeLinear = 0;
    /// <summary>Radial array pattern type identifier.</summary>
    internal const byte PatternTypeRadial = 1;
    /// <summary>Grid array pattern type identifier.</summary>
    internal const byte PatternTypeGrid = 2;
    /// <summary>Scaling pattern type identifier.</summary>
    internal const byte PatternTypeScaling = 3;

    /// <summary>Curvature variation threshold for detecting constant-radius fillets.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;
    /// <summary>Sample count for fillet edge curvature analysis.</summary>
    internal const int FilletCurvatureSampleCount = 5;
    /// <summary>Angle tolerance for G2 continuity detection in smooth edges.</summary>
    internal const double G2ContinuityTolerance = 0.01;
    /// <summary>Dihedral angle threshold for sharp edge classification.</summary>
    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);
    /// <summary>Dihedral angle threshold for smooth edge classification.</summary>
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);
    /// <summary>Minimum polyline sides for circular hole approximation.</summary>
    internal const int MinHolePolySides = 16;
    /// <summary>Sample count for primitive surface residual calculation.</summary>
    internal const int PrimitiveResidualSampleCount = 20;
    /// <summary>Minimum instance count for pattern detection.</summary>
    internal const int PatternMinInstances = 3;
    /// <summary>Distance variation threshold for radial pattern detection.</summary>
    internal const double RadialDistanceVariationThreshold = 0.05;
    /// <summary>Angular variation threshold for radial pattern detection.</summary>
    internal const double RadialAngleVariationThreshold = 0.05;
    /// <summary>Dot product threshold for grid basis orthogonality detection.</summary>
    internal const double GridOrthogonalityThreshold = 0.1;
    /// <summary>Deviation threshold for integer coordinate validation in grids.</summary>
    internal const double GridPointDeviationThreshold = 0.1;
    /// <summary>Variance threshold for scaling ratio consistency detection.</summary>
    internal const double ScalingVarianceThreshold = 0.1;
    /// <summary>Angle threshold for sharp feature edge detection.</summary>
    internal static readonly double FeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);
    /// <summary>Sample count for principal curvature-based surface classification.</summary>
    internal const int CurvatureSampleCount = 16;
    /// <summary>Minimum valid curvature samples required for surface classification.</summary>
    internal const int MinCurvatureSamples = 4;
    /// <summary>Variation threshold for principal curvature constancy testing.</summary>
    internal const double CurvatureVariationThreshold = 0.05;
    /// <summary>Minimum isocurve count for valid surface extraction.</summary>
    internal const int MinIsocurveCount = 2;
    /// <summary>Maximum isocurve count to prevent excessive memory allocation.</summary>
    internal const int MaxIsocurveCount = 100;
    /// <summary>Default sample count for osculating frame extraction along curves.</summary>
    internal const int DefaultOsculatingFrameCount = 10;
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(1, typeof(Extrusion))] = V.Standard | V.MassProperties,
            [(1, typeof(SubD))] = V.Standard | V.Topology,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(GeometryBase))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(6, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(8, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(10, typeof(Brep))] = V.Standard | V.Topology,
            [(10, typeof(Extrusion))] = V.Standard | V.Topology,
            [(10, typeof(SubD))] = V.Standard | V.Topology,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(11, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(11, typeof(Brep))] = V.Standard | V.Topology,
            [(12, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(12, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(12, typeof(Brep))] = V.Standard | V.Topology,
            [(13, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(13, typeof(PolyCurve))] = V.Standard | V.Degeneracy,
            [(20, typeof(Surface))] = V.Standard | V.UVDomain,
            [(20, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(20, typeof(Brep))] = V.Standard | V.Topology,
            [(21, typeof(Surface))] = V.Standard | V.UVDomain,
            [(21, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(22, typeof(Surface))] = V.Standard | V.UVDomain,
            [(22, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(23, typeof(Surface))] = V.Standard | V.UVDomain,
            [(23, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(24, typeof(Brep))] = V.Standard | V.Topology,
            [(30, typeof(Surface))] = V.Standard | V.UVDomain,
            [(30, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(31, typeof(Surface))] = V.Standard | V.UVDomain,
            [(31, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(32, typeof(Surface))] = V.Standard | V.UVDomain,
            [(32, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(33, typeof(Surface))] = V.Standard | V.UVDomain,
            [(33, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(34, typeof(Brep))] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Gets validation mode with fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact) ? exact : ValidationModes.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType)).OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0)).Select(kv => kv.Value).DefaultIfEmpty(V.Standard).First();
}

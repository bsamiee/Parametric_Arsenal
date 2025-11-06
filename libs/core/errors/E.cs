using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Consolidated error registry with FrozenDictionary dispatch for zero-allocation lookups across all domains.</summary>
public static class E {
    private static readonly FrozenDictionary<int, string> _m =
        new Dictionary<int, string> {
            [1001] = "No value provided",
            [1002] = "Invalid Create parameters",
            [1003] = "Invalid validation parameters",
            [1004] = "Invalid Lift parameters",
            [1100] = "Cannot access value in error state or error in success state",
            [2000] = "Invalid extraction method specified",
            [2001] = "Insufficient parameters for extraction operation",
            [2002] = "Count parameter must be positive",
            [2003] = "Length parameter must be greater than zero tolerance",
            [2004] = "Direction parameter required for positional extrema",
            [2200] = "Intersection method not supported for geometry types",
            [2201] = "Intersection computation failed",
            [2202] = "Projection direction vector is invalid or zero-length",
            [2204] = "Ray direction vector is invalid or zero-length",
            [2205] = "Maximum hit count must be positive",
            [2300] = "Geometry type not supported for analysis",
            [2310] = "Curve analysis computation failed",
            [2311] = "Surface analysis computation failed",
            [2312] = "Brep analysis computation failed",
            [2313] = "Mesh analysis computation failed",
            [3000] = "Geometry must be valid",
            [3100] = "Curve must be closed and planar for area centroid",
            [3200] = "Bounding box is invalid",
            [3300] = "Mass properties computation failed",
            [3400] = "Geometry has invalid topology",
            [3500] = "Geometry is degenerate",
            [3600] = "Geometry is self-intersecting",
            [3700] = "Mesh has non-manifold edges",
            [3800] = "Surface has positional discontinuity (G0)",
            [3900] = "Absolute tolerance must be greater than zero",
            [3901] = "Relative tolerance must be in range [0,1)",
            [3902] = "Angle tolerance must be in range (0, 2π]",
            [3903] = "Geometry exceeds tolerance threshold",
            [3920] = "Invalid unit conversion scale",
            [4000] = "Unsupported operation type",
            [4001] = "Input filtered",
            [4002] = "K-nearest neighbor count must be positive",
            [4003] = "Distance limit must be positive",
            [4004] = "Input and query type combination not supported",
            [4005] = "Proximity search operation failed",
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SystemError At(int code) => new(code, _m[code]);

    /// <summary>Result system errors (1000-1999).</summary>
    public static class Results {
        public static SystemError NoValue => At(1001);
        public static SystemError InvalidCreate => At(1002);
        public static SystemError InvalidValidate => At(1003);
        public static SystemError InvalidLift => At(1004);
        public static SystemError InvalidAccess => At(1100);
    }

    /// <summary>Geometry operation errors (2000-2999).</summary>
    public static class Geometry {
        public static SystemError InvalidMethod => At(2000);
        public static SystemError InsufficientParameters => At(2001);
        public static SystemError InvalidCount => At(2002);
        public static SystemError InvalidLength => At(2003);
        public static SystemError InvalidDirection => At(2004);
        public static SystemError UnsupportedMethod => At(2200);
        public static SystemError ComputationFailed => At(2201);
        public static SystemError InvalidProjectionDirection => At(2202);
        public static SystemError InvalidRayDirection => At(2204);
        public static SystemError InvalidMaxHitCount => At(2205);
        public static SystemError UnsupportedGeometry => At(2300);
        public static SystemError CurveAnalysisFailed => At(2310);
        public static SystemError SurfaceAnalysisFailed => At(2311);
        public static SystemError BrepAnalysisFailed => At(2312);
        public static SystemError MeshAnalysisFailed => At(2313);
    }

    /// <summary>Validation errors (3000-3999).</summary>
    public static class Validation {
        public static SystemError Invalid => At(3000);
        public static SystemError CurveNotClosedOrPlanar => At(3100);
        public static SystemError BoundingBoxInvalid => At(3200);
        public static SystemError MassPropertiesFailed => At(3300);
        public static SystemError TopologyInvalid => At(3400);
        public static SystemError Degenerate => At(3500);
        public static SystemError SelfIntersecting => At(3600);
        public static SystemError MeshNonManifold => At(3700);
        public static SystemError SurfaceDiscontinuous => At(3800);
        public static SystemError ToleranceAbsoluteInvalid => At(3900);
        public static SystemError ToleranceRelativeInvalid => At(3901);
        public static SystemError ToleranceAngleInvalid => At(3902);
        public static SystemError ToleranceExceeded => At(3903);
        public static SystemError UnitConversionInvalid => At(3920);
    }

    /// <summary>Operation and spatial errors (4000-4999).</summary>
    public static class Operations {
        public static SystemError UnsupportedType => At(4000);
        public static SystemError InputFiltered => At(4001);
        public static SystemError InvalidK => At(4002);
        public static SystemError InvalidDistance => At(4003);
        public static SystemError UnsupportedTypeCombo => At(4004);
        public static SystemError ProximityFailed => At(4005);
    }
}

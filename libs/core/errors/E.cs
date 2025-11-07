using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>
/// Consolidated error registry with FrozenDictionary dispatch for zero-allocation error retrieval.
///
/// <para><b>Error Code Ranges:</b></para>
/// <list type="bullet">
/// <item>1000-1999: Results system errors</item>
/// <item>2000-2099: Geometry extraction errors</item>
/// <item>2200-2299: Geometry intersection errors</item>
/// <item>2300-2399: Geometry analysis errors</item>
/// <item>3000-3999: Validation errors</item>
/// <item>4000-4099: Spatial indexing errors</item>
/// </list>
///
/// <para><b>Domain Computation:</b></para>
/// <para>Error domain is automatically computed from code range, eliminating need for separate domain storage.</para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// SystemError error = E.Results.NoValueProvided;
/// SystemError withContext = E.Get(1001, context: "MethodName");
/// Result&lt;T&gt; result = ResultFactory.Create&lt;T&gt;(error: E.Validation.GeometryInvalid);
/// </code>
///
/// <para><b>Extensibility:</b></para>
/// <para>1. Add error code and message to _m dictionary</para>
/// <para>2. Add property to appropriate nested class</para>
/// </summary>
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
            [3930] = "Unsupported operation type",
            [3931] = "Input filtered by predicate",
            [4001] = "K-nearest neighbor count must be positive",
            [4002] = "Distance limit must be positive",
            [4003] = "Input and query type combination not supported",
            [4004] = "Proximity search operation failed",
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Domain GetDomain(int code) => code switch {
        >= 1000 and < 2000 => Domain.Results,
        >= 2000 and < 3000 => Domain.Geometry,
        >= 3000 and < 4000 => Domain.Validation,
        >= 4000 and < 5000 => Domain.Spatial,
        _ => Domain.Unknown,
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) {
        Domain domain = GetDomain(code);
        string message = _m.TryGetValue(code, out string? msg)
            ? msg
            : $"Unknown error code: {code}";

        return context switch {
            null => new SystemError(domain, code, message),
            string ctx => new SystemError(domain, code, message).WithContext(ctx),
        };
    }

    /// <summary>Results system errors (1000-1999)</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(1001);
        public static readonly SystemError InvalidCreate = Get(1002);
        public static readonly SystemError InvalidValidate = Get(1003);
        public static readonly SystemError InvalidLift = Get(1004);
        public static readonly SystemError InvalidAccess = Get(1100);
    }

    /// <summary>Geometry errors (2000-2999) - all geometry operations</summary>
    public static class Geometry {
        public static readonly SystemError InvalidExtraction = Get(2000);
        public static readonly SystemError InsufficientParameters = Get(2001);
        public static readonly SystemError InvalidCount = Get(2002);
        public static readonly SystemError InvalidLength = Get(2003);
        public static readonly SystemError InvalidDirection = Get(2004);
        public static readonly SystemError UnsupportedIntersection = Get(2200);
        public static readonly SystemError IntersectionFailed = Get(2201);
        public static readonly SystemError InvalidProjection = Get(2202);
        public static readonly SystemError InvalidRay = Get(2204);
        public static readonly SystemError InvalidMaxHits = Get(2205);
        public static readonly SystemError UnsupportedAnalysis = Get(2300);
        public static readonly SystemError CurveAnalysisFailed = Get(2310);
        public static readonly SystemError SurfaceAnalysisFailed = Get(2311);
        public static readonly SystemError BrepAnalysisFailed = Get(2312);
        public static readonly SystemError MeshAnalysisFailed = Get(2313);
    }

    /// <summary>Validation errors (3000-3999)</summary>
    public static class Validation {
        public static readonly SystemError GeometryInvalid = Get(3000);
        public static readonly SystemError CurveNotClosedOrPlanar = Get(3100);
        public static readonly SystemError BoundingBoxInvalid = Get(3200);
        public static readonly SystemError MassPropertiesComputationFailed = Get(3300);
        public static readonly SystemError InvalidTopology = Get(3400);
        public static readonly SystemError DegenerateGeometry = Get(3500);
        public static readonly SystemError SelfIntersecting = Get(3600);
        public static readonly SystemError NonManifoldEdges = Get(3700);
        public static readonly SystemError PositionalDiscontinuity = Get(3800);
        public static readonly SystemError ToleranceAbsoluteInvalid = Get(3900);
        public static readonly SystemError ToleranceRelativeInvalid = Get(3901);
        public static readonly SystemError ToleranceAngleInvalid = Get(3902);
        public static readonly SystemError ToleranceExceeded = Get(3903);
        public static readonly SystemError InvalidUnitConversion = Get(3920);
        public static readonly SystemError UnsupportedOperationType = Get(3930);
        public static readonly SystemError InputFiltered = Get(3931);
    }

    /// <summary>Spatial indexing errors (4000-4099)</summary>
    public static class Spatial {
        public static readonly SystemError InvalidK = Get(4001);
        public static readonly SystemError InvalidDistance = Get(4002);
        public static readonly SystemError UnsupportedTypeCombo = Get(4003);
        public static readonly SystemError ProximityFailed = Get(4004);
    }
}

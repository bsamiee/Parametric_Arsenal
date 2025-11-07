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
/// <para>Error domain is automatically computed from code range, eliminating need for separate ErrorDomain lookups.</para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// SystemError error = E.Results.NoValueProvided;
/// SystemError withContext = E.Get(code: 1001, context: "MethodName");
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
            // Results (1000-1999)
            [1001] = "No value provided",
            [1002] = "Invalid Create parameters",
            [1003] = "Invalid validation parameters",
            [1004] = "Invalid Lift parameters",
            [1100] = "Cannot access value in error state or error in success state",

            // Geometry Extraction (2000-2099)
            [2000] = "Invalid extraction method specified",
            [2001] = "Insufficient parameters for extraction operation",
            [2002] = "Count parameter must be positive",
            [2003] = "Length parameter must be greater than zero tolerance",
            [2004] = "Direction parameter required for positional extrema",

            // Geometry Intersection (2200-2299)
            [2200] = "Intersection method not supported for geometry types",
            [2201] = "Intersection computation failed",
            [2202] = "Projection direction vector is invalid or zero-length",
            [2204] = "Ray direction vector is invalid or zero-length",
            [2205] = "Maximum hit count must be positive",

            // Geometry Analysis (2300-2399)
            [2300] = "Geometry type not supported for analysis",
            [2310] = "Curve analysis computation failed",
            [2311] = "Surface analysis computation failed",
            [2312] = "Brep analysis computation failed",
            [2313] = "Mesh analysis computation failed",

            // Validation (3000-3999)
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

            // Spatial (4001-4099)
            [4001] = "K-nearest neighbor count must be positive",
            [4002] = "Distance limit must be positive",
            [4003] = "Input and query type combination not supported",
            [4004] = "Proximity search operation failed",

            // Operation/Validation errors (4000, 4005+ in 4000 range)
            [4000] = "Unsupported operation type",
            [4005] = "Input filtered",
        }.ToFrozenDictionary();

    /// <summary>Computes error domain from code range for automatic domain classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ErrorDomain GetDomain(int code) => code switch {
        >= 1000 and < 2000 => ErrorDomain.Results,
        >= 2000 and < 3000 => ErrorDomain.Geometry,
        >= 3000 and < 4000 => ErrorDomain.Validation,
        >= 4000 and < 5000 => ErrorDomain.Geometry,
        _ => ErrorDomain.None,
    };

    /// <summary>Core factory method for creating errors with optional context.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) {
        ErrorDomain domain = GetDomain(code);
        string message = _m.TryGetValue(code, out string? msg)
            ? msg
            : $"Unknown error code: {code.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return context switch {
            null => new(domain, code, message),
            string ctx => new SystemError(domain, code, message).WithContext(ctx),
        };
    }

    /// <summary>Results system errors (1000-1999).</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(code: 1001);
        public static readonly SystemError InvalidCreate = Get(code: 1002);
        public static readonly SystemError InvalidValidate = Get(code: 1003);
        public static readonly SystemError InvalidLift = Get(code: 1004);
        public static readonly SystemError InvalidAccess = Get(code: 1100);
    }

    /// <summary>Geometry errors (2000-2999) - extraction, intersection, analysis.</summary>
    public static class Geometry {
        // Extraction (2000-2099)
        public static readonly SystemError InvalidExtraction = Get(code: 2000);
        public static readonly SystemError InsufficientParameters = Get(code: 2001);
        public static readonly SystemError InvalidCount = Get(code: 2002);
        public static readonly SystemError InvalidLength = Get(code: 2003);
        public static readonly SystemError InvalidDirection = Get(code: 2004);

        // Intersection (2200-2299)
        public static readonly SystemError UnsupportedIntersection = Get(code: 2200);
        public static readonly SystemError IntersectionFailed = Get(code: 2201);
        public static readonly SystemError InvalidProjection = Get(code: 2202);
        public static readonly SystemError InvalidRay = Get(code: 2204);
        public static readonly SystemError InvalidMaxHits = Get(code: 2205);

        // Analysis (2300-2399)
        public static readonly SystemError UnsupportedAnalysis = Get(code: 2300);
        public static readonly SystemError CurveAnalysisFailed = Get(code: 2310);
        public static readonly SystemError SurfaceAnalysisFailed = Get(code: 2311);
        public static readonly SystemError BrepAnalysisFailed = Get(code: 2312);
        public static readonly SystemError MeshAnalysisFailed = Get(code: 2313);
    }

    /// <summary>Validation errors (3000-3999).</summary>
    public static class Validation {
        public static readonly SystemError GeometryInvalid = Get(code: 3000);
        public static readonly SystemError CurveNotClosedOrPlanar = Get(code: 3100);
        public static readonly SystemError BoundingBoxInvalid = Get(code: 3200);
        public static readonly SystemError MassPropertiesComputationFailed = Get(code: 3300);
        public static readonly SystemError InvalidTopology = Get(code: 3400);
        public static readonly SystemError DegenerateGeometry = Get(code: 3500);
        public static readonly SystemError SelfIntersecting = Get(code: 3600);
        public static readonly SystemError NonManifoldEdges = Get(code: 3700);
        public static readonly SystemError PositionalDiscontinuity = Get(code: 3800);
        public static readonly SystemError ToleranceAbsoluteInvalid = Get(code: 3900);
        public static readonly SystemError ToleranceRelativeInvalid = Get(code: 3901);
        public static readonly SystemError ToleranceAngleInvalid = Get(code: 3902);
        public static readonly SystemError ToleranceExceeded = Get(code: 3903);
        public static readonly SystemError InvalidUnitConversion = Get(code: 3920);
        public static readonly SystemError UnsupportedOperationType = Get(code: 4000);
        public static readonly SystemError InputFiltered = Get(code: 4005);
        public static readonly SystemError UnsupportedTypeCombo = Get(code: 4003);
    }

    /// <summary>Spatial indexing errors (4001-4002, 4004) - code 4003 shared with Validation.</summary>
    public static class Spatial {
        public static readonly SystemError InvalidK = Get(code: 4001);
        public static readonly SystemError InvalidDistance = Get(code: 4002);
        public static readonly SystemError ProximityFailed = Get(code: 4004);
    }
}

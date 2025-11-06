using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Dense error allocation system using FrozenDictionary dispatch for hundreds of errors in tight footprint.</summary>
public static class ErrorRegistry {
    private static readonly FrozenDictionary<(byte Domain, int Code), string> _messages =
        new Dictionary<(byte, int), string> {
            [(10, 1001)] = "No value provided",
            [(10, 1002)] = "Invalid Create parameters",
            [(10, 1003)] = "Invalid validation parameters",
            [(10, 1004)] = "Invalid Lift parameters",
            [(10, 1100)] = "Cannot access value in error state or error in success state",
            [(30, 3000)] = "Geometry must be valid",
            [(30, 3100)] = "Curve must be closed and planar for area centroid",
            [(30, 3200)] = "Bounding box is invalid",
            [(30, 3300)] = "Mass properties computation failed",
            [(30, 3400)] = "Geometry has invalid topology",
            [(30, 3500)] = "Geometry is degenerate",
            [(30, 3600)] = "Geometry is self-intersecting",
            [(30, 3700)] = "Mesh has non-manifold edges",
            [(30, 3800)] = "Surface has positional discontinuity (G0)",
            [(30, 3900)] = "Absolute tolerance must be greater than zero",
            [(30, 3901)] = "Relative tolerance must be in range [0,1)",
            [(30, 3902)] = "Angle tolerance must be in range (0, 2π]",
            [(30, 3903)] = "Geometry exceeds tolerance threshold",
            [(30, 3920)] = "Invalid unit conversion scale",
            [(30, 4000)] = "Unsupported operation type",
            [(30, 4001)] = "Input filtered",
            [(20, 2000)] = "Invalid extraction method specified",
            [(20, 2001)] = "Insufficient parameters for extraction operation",
            [(20, 2002)] = "Count parameter must be positive",
            [(20, 2003)] = "Length parameter must be greater than zero tolerance",
            [(20, 2004)] = "Direction parameter required for positional extrema",
            [(20, 2200)] = "Intersection method not supported for geometry types",
            [(20, 2201)] = "Intersection computation failed",
            [(20, 2202)] = "Projection direction vector is invalid or zero-length",
            [(20, 2204)] = "Ray direction vector is invalid or zero-length",
            [(20, 2205)] = "Maximum hit count must be positive",
            [(20, 2300)] = "Geometry type not supported for analysis",
            [(20, 2310)] = "Curve analysis computation failed",
            [(20, 2311)] = "Surface analysis computation failed",
            [(20, 2312)] = "Brep analysis computation failed",
            [(20, 2313)] = "Mesh analysis computation failed",
            [(20, 4001)] = "K-nearest neighbor count must be positive",
            [(20, 4002)] = "Distance limit must be positive",
            [(30, 4003)] = "Input and query type combination not supported",
            [(20, 4004)] = "Proximity search operation failed",
        }.ToFrozenDictionary();

    /// <summary>Allocates SystemError using dense tuple dispatch with domain value and code.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(Domain domain, int code) =>
        _messages.TryGetValue((domain.Value, code), out string? message) switch {
            true => new(domain, code, message),
            false => new(domain, code, $"Unknown error: Domain={domain.Value}, Code={code}"),
        };

    /// <summary>Polymorphic error retrieval using tuple dispatch for inline error allocation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(byte domainValue, int code) =>
        Get(domain: new Domain(domainValue), code: code);

    /// <summary>Results domain errors (1000-1999).</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(Domain.Results, 1001);
        public static readonly SystemError InvalidCreateParameters = Get(Domain.Results, 1002);
        public static readonly SystemError InvalidValidateParameters = Get(Domain.Results, 1003);
        public static readonly SystemError InvalidLiftParameters = Get(Domain.Results, 1004);
        public static readonly SystemError InvalidAccess = Get(Domain.Results, 1100);
    }

    /// <summary>Geometry domain errors (2000-2999) - flattened structure for type count compliance.</summary>
    public static class Geometry {
        public static readonly SystemError Invalid = Get(Domain.Validation, 3000);
        public static readonly SystemError CurveNotClosedOrPlanar = Get(Domain.Validation, 3100);
        public static readonly SystemError BoundingBoxInvalid = Get(Domain.Validation, 3200);
        public static readonly SystemError PropertiesComputationFailed = Get(Domain.Validation, 3300);
        public static readonly SystemError TopologyInvalidTopology = Get(Domain.Validation, 3400);
        public static readonly SystemError DegeneracyDegenerateGeometry = Get(Domain.Validation, 3500);
        public static readonly SystemError SelfIntersectionSelfIntersecting = Get(Domain.Validation, 3600);
        public static readonly SystemError MeshTopologyNonManifoldEdges = Get(Domain.Validation, 3700);
        public static readonly SystemError ContinuityPositionalDiscontinuity = Get(Domain.Validation, 3800);
        public static readonly SystemError ExtractionInvalidMethod = Get(Domain.Geometry, 2000);
        public static readonly SystemError ExtractionInsufficientParameters = Get(Domain.Geometry, 2001);
        public static readonly SystemError ExtractionInvalidCount = Get(Domain.Geometry, 2002);
        public static readonly SystemError ExtractionInvalidLength = Get(Domain.Geometry, 2003);
        public static readonly SystemError ExtractionInvalidDirection = Get(Domain.Geometry, 2004);
        public static readonly SystemError IntersectionUnsupportedMethod = Get(Domain.Geometry, 2200);
        public static readonly SystemError IntersectionComputationFailed = Get(Domain.Geometry, 2201);
        public static readonly SystemError IntersectionInvalidProjectionDirection = Get(Domain.Geometry, 2202);
        public static readonly SystemError IntersectionInvalidRayDirection = Get(Domain.Geometry, 2204);
        public static readonly SystemError IntersectionInvalidMaxHitCount = Get(Domain.Geometry, 2205);
        public static readonly SystemError AnalysisUnsupportedGeometry = Get(Domain.Geometry, 2300);
        public static readonly SystemError AnalysisCurveFailed = Get(Domain.Geometry, 2310);
        public static readonly SystemError AnalysisSurfaceFailed = Get(Domain.Geometry, 2311);
        public static readonly SystemError AnalysisBrepFailed = Get(Domain.Geometry, 2312);
        public static readonly SystemError AnalysisMeshFailed = Get(Domain.Geometry, 2313);
        public static readonly SystemError SpatialInvalidK = Get(Domain.Geometry, 4001);
        public static readonly SystemError SpatialInvalidDistance = Get(Domain.Geometry, 4002);
        public static readonly SystemError SpatialProximityFailed = Get(Domain.Geometry, 4004);
    }

    /// <summary>Validation domain errors (3000-3999).</summary>
    public static class Validation {
        public static readonly SystemError ToleranceInvalidAbsolute = Get(Domain.Validation, 3900);
        public static readonly SystemError ToleranceInvalidRelative = Get(Domain.Validation, 3901);
        public static readonly SystemError ToleranceInvalidAngle = Get(Domain.Validation, 3902);
        public static readonly SystemError ToleranceExceeded = Get(Domain.Validation, 3903);
        public static readonly SystemError InvalidUnitConversion = Get(Domain.Validation, 3920);
        public static readonly SystemError UnsupportedOperationType = Get(Domain.Validation, 4000);
        public static readonly SystemError InputFiltered = Get(Domain.Validation, 4001);
        public static readonly SystemError SpatialUnsupportedTypeCombo = Get(Domain.Validation, 4003);
    }
}

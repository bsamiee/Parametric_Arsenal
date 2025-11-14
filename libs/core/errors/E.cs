using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Error registry with O(1) lookup via FrozenDictionary.</summary>
public static class E {
    internal const byte UnknownDomain = 0;
    internal const byte ResultsDomain = 1;
    internal const byte GeometryDomain = 2;
    internal const byte ValidationDomain = 3;
    internal const byte SpatialDomain = 4;
    internal const byte TopologyDomain = 5;

    private static readonly FrozenDictionary<int, string> _m =
        new Dictionary<int, string> {
            // Results System Errors (1000-1999)
            [1001] = "No value provided",
            [1002] = "Invalid Create parameters",
            [1003] = "Invalid validation parameters",
            [1004] = "Invalid Lift parameters",
            [1005] = "Cannot access value in error state or error in success state",

            // Geometry Operation Errors (2000-2999)
            // Extraction Operations (2000-2006)
            [2000] = "Invalid extraction method specified",
            [2001] = "Insufficient parameters for extraction operation",
            [2002] = "Count parameter must be positive",
            [2003] = "Length parameter must be greater than zero tolerance",
            [2004] = "Direction parameter required for positional extrema",
            [2005] = "Parameters array must not be empty",
            [2006] = "Angle threshold must be positive",

            // Intersection Operations (2200-2207)
            [2200] = "Intersection method not supported for geometry types",
            [2201] = "Intersection computation failed",
            [2202] = "Projection direction vector is invalid or zero-length",
            [2203] = "Maximum hit count must be positive",
            [2204] = "Intersection classification failed",
            [2205] = "Insufficient intersection data for classification",
            [2206] = "Near-miss search failed",
            [2207] = "Invalid search radius for near-miss detection",

            // Analysis Operations (2300-2304)
            [2300] = "Geometry type not supported for analysis",
            [2301] = "Curve analysis computation failed",
            [2302] = "Surface analysis computation failed",
            [2303] = "Brep analysis computation failed",
            [2304] = "Mesh analysis computation failed",

            // Topology Operations (2400-2401)
            [2400] = "Invalid edge index",
            [2401] = "Invalid vertex index",

            // Orientation Operations (2500-2511)
            [2500] = "Unsupported orientation geometry type",
            [2501] = "Invalid orientation plane",
            [2502] = "Geometry transformation failed",
            [2503] = "Centroid extraction failed",
            [2504] = "Invalid orientation mode",
            [2505] = "Parallel vectors cannot be aligned",
            [2506] = "Invalid orientation vectors",
            [2507] = "Invalid curve parameter for orientation",
            [2508] = "Invalid surface UV parameters",
            [2509] = "Frame extraction failed",
            [2510] = "Relative orientation computation failed",
            [2511] = "Pattern detection failed",

            // Feature Extraction Operations (2600-2603)
            [2600] = "Feature extraction failed",
            [2601] = "Primitive decomposition failed",
            [2602] = "No primitives detected in geometry",
            [2603] = "No pattern detected in geometry collection",

            // Distance Field Operations (2700-2709)
            [2700] = "Field resolution must be positive integer",
            [2701] = "Field bounds invalid or degenerate",
            [2702] = "Distance field computation failed",
            [2703] = "Inside/outside determination failed for signed distance",

            // Gradient Field Operations (2720-2729)
            [2720] = "Gradient computation failed",
            [2721] = "Invalid gradient field sampling parameters",
            [2722] = "Zero gradient magnitude in critical region",

            // Streamline Operations (2730-2739)
            [2730] = "Streamline integration failed to converge",
            [2731] = "Invalid seed points for streamline tracing",
            [2732] = "Streamline divergence detected - unstable flow",
            [2733] = "Maximum integration steps exceeded",

            // Isosurface Operations (2740-2749)
            [2740] = "Isosurface extraction failed",
            [2741] = "Isovalue outside scalar field range",
            [2742] = "Marching cubes algorithm failure",
            [2743] = "Scalar field data invalid or insufficient",

            // Validation Errors (3000-3999)
            // Core Validation (3000-3800)
            [3000] = "Geometry must be valid",
            [3100] = "Curve must be closed and planar for area centroid",
            [3200] = "Bounding box is invalid",
            [3300] = "Mass properties computation failed",
            [3400] = "Geometry has invalid topology",
            [3500] = "Geometry is degenerate",
            [3600] = "Geometry is self-intersecting",
            [3700] = "Mesh has non-manifold edges",
            [3800] = "Surface has positional discontinuity (G0)",

            // Tolerance Validation (3900-3911)
            [3900] = "Absolute tolerance must be greater than zero",
            [3901] = "Relative tolerance must be in range [0,1)",
            [3902] = "Angle tolerance must be in range (0, 2π]",
            [3903] = "Geometry exceeds tolerance threshold",
            [3904] = "Polycurve has segment gaps exceeding tolerance",
            [3905] = "NURBS control point count below minimum",
            [3906] = "Extrusion profile curve invalid",
            [3907] = "Surface UV domain has singularity",
            [3908] = "Brep topology is invalid",
            [3909] = "Brep geometry is invalid",
            [3910] = "Brep tolerances and flags are invalid",
            [3911] = "Mesh validation failed",

            // Operation Validation (3920-3931)
            [3920] = "Invalid unit conversion scale",
            [3930] = "Unsupported operation type",
            [3931] = "Input filtered by predicate",

            // Spatial Indexing Errors (4000-4999)
            // Proximity Operations (4001-4004)
            [4001] = "K-nearest neighbor count must be positive",
            [4002] = "Distance limit must be positive",
            [4003] = "Input and query type combination not supported",
            [4004] = "Proximity search operation failed",

            // Clustering Operations (4100-4104)
            [4100] = "Spatial clustering operation failed",
            [4101] = "K-means k parameter must be positive",
            [4102] = "DBSCAN epsilon parameter must be positive",
            [4103] = "Cluster count exceeds point count",
            [4104] = "Direction vector is zero-length",

            // Medial Axis Operations (4200-4201)
            [4200] = "Medial axis computation failed",
            [4201] = "Non-planar medial axis not supported",

            // Proximity Field Operations (4300)
            [4300] = "Proximity field direction vector is invalid",

            // Topology Analysis Errors (5000-5999)
            [5001] = "Topology diagnosis failed",
            [5002] = "Topology is too complex for diagnosis",
            [5010] = "Topology healing failed",
            [5020] = "Topological feature extraction failed",
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetDomain(int code) => code switch {
        >= 1000 and < 2000 => ResultsDomain,
        >= 2000 and < 3000 => GeometryDomain,
        >= 3000 and < 4000 => ValidationDomain,
        >= 4000 and < 5000 => SpatialDomain,
        >= 5000 and < 6000 => TopologyDomain,
        _ => UnknownDomain,
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) {
        (byte domain, string message) = (GetDomain(code), _m.TryGetValue(code, out string? msg) ? msg : $"Unknown error code: {code.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return context is null ? new SystemError(domain, code, message) : new SystemError(domain, code, message).WithContext(context);
    }

    /// <summary>Results system errors (1000-1999).</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(1001);
        public static readonly SystemError InvalidCreate = Get(1002);
        public static readonly SystemError InvalidValidate = Get(1003);
        public static readonly SystemError InvalidLift = Get(1004);
        public static readonly SystemError InvalidAccess = Get(1005);
    }

    /// <summary>Geometry errors (2000-2999).</summary>
    public static class Geometry {
        // Extraction Operations (2000-2006)
        public static readonly SystemError InvalidExtraction = Get(2000);
        public static readonly SystemError InsufficientParameters = Get(2001);
        public static readonly SystemError InvalidCount = Get(2002);
        public static readonly SystemError InvalidLength = Get(2003);
        public static readonly SystemError InvalidDirection = Get(2004);
        public static readonly SystemError InvalidParameters = Get(2005);
        public static readonly SystemError InvalidAngle = Get(2006);

        // Intersection Operations (2200-2207)
        public static readonly SystemError UnsupportedIntersection = Get(2200);
        public static readonly SystemError IntersectionFailed = Get(2201);
        public static readonly SystemError InvalidProjection = Get(2202);
        public static readonly SystemError InvalidMaxHits = Get(2203);
        public static readonly SystemError ClassificationFailed = Get(2204);
        public static readonly SystemError InsufficientIntersectionData = Get(2205);
        public static readonly SystemError NearMissSearchFailed = Get(2206);
        public static readonly SystemError InvalidSearchRadius = Get(2207);

        // Analysis Operations (2300-2304)
        public static readonly SystemError UnsupportedAnalysis = Get(2300);
        public static readonly SystemError CurveAnalysisFailed = Get(2301);
        public static readonly SystemError SurfaceAnalysisFailed = Get(2302);
        public static readonly SystemError BrepAnalysisFailed = Get(2303);
        public static readonly SystemError MeshAnalysisFailed = Get(2304);

        // Topology Operations (2400-2401)
        public static readonly SystemError InvalidEdgeIndex = Get(2400);
        public static readonly SystemError InvalidVertexIndex = Get(2401);

        // Orientation Operations (2500-2511)
        public static readonly SystemError UnsupportedOrientationType = Get(2500);
        public static readonly SystemError InvalidOrientationPlane = Get(2501);
        public static readonly SystemError TransformFailed = Get(2502);
        public static readonly SystemError CentroidExtractionFailed = Get(2503);
        public static readonly SystemError InvalidOrientationMode = Get(2504);
        public static readonly SystemError ParallelVectorAlignment = Get(2505);
        public static readonly SystemError InvalidOrientationVectors = Get(2506);
        public static readonly SystemError InvalidCurveParameter = Get(2507);
        public static readonly SystemError InvalidSurfaceUV = Get(2508);
        public static readonly SystemError FrameExtractionFailed = Get(2509);
        public static readonly SystemError OrientationFailed = Get(2510);
        public static readonly SystemError PatternDetectionFailed = Get(2511);

        // Feature Extraction Operations (2600-2603)
        public static readonly SystemError FeatureExtractionFailed = Get(2600);
        public static readonly SystemError DecompositionFailed = Get(2601);
        public static readonly SystemError NoPrimitivesDetected = Get(2602);
        public static readonly SystemError NoPatternDetected = Get(2603);

        // Distance Field Operations (2700-2709)
        public static readonly SystemError InvalidFieldResolution = Get(2700);
        public static readonly SystemError InvalidFieldBounds = Get(2701);
        public static readonly SystemError DistanceFieldComputationFailed = Get(2702);
        public static readonly SystemError InsideOutsideTestFailed = Get(2703);

        // Gradient Field Operations (2720-2729)
        public static readonly SystemError GradientComputationFailed = Get(2720);
        public static readonly SystemError InvalidGradientSampling = Get(2721);
        public static readonly SystemError ZeroGradientRegion = Get(2722);

        // Streamline Operations (2730-2739)
        public static readonly SystemError StreamlineIntegrationFailed = Get(2730);
        public static readonly SystemError InvalidStreamlineSeeds = Get(2731);
        public static readonly SystemError StreamlineDivergence = Get(2732);
        public static readonly SystemError MaxStepsExceeded = Get(2733);

        // Isosurface Operations (2740-2749)
        public static readonly SystemError IsosurfaceExtractionFailed = Get(2740);
        public static readonly SystemError InvalidIsovalue = Get(2741);
        public static readonly SystemError MarchingCubesFailed = Get(2742);
        public static readonly SystemError InvalidScalarField = Get(2743);
    }

    /// <summary>Validation errors (3000-3999).</summary>
    public static class Validation {
        // Core Validation (3000-3800)
        public static readonly SystemError GeometryInvalid = Get(3000);
        public static readonly SystemError CurveNotClosedOrPlanar = Get(3100);
        public static readonly SystemError BoundingBoxInvalid = Get(3200);
        public static readonly SystemError MassPropertiesComputationFailed = Get(3300);
        public static readonly SystemError InvalidTopology = Get(3400);
        public static readonly SystemError DegenerateGeometry = Get(3500);
        public static readonly SystemError SelfIntersecting = Get(3600);
        public static readonly SystemError NonManifoldEdges = Get(3700);
        public static readonly SystemError PositionalDiscontinuity = Get(3800);

        // Tolerance Validation (3900-3911)
        public static readonly SystemError ToleranceAbsoluteInvalid = Get(3900);
        public static readonly SystemError ToleranceRelativeInvalid = Get(3901);
        public static readonly SystemError ToleranceAngleInvalid = Get(3902);
        public static readonly SystemError ToleranceExceeded = Get(3903);
        public static readonly SystemError PolycurveGaps = Get(3904);
        public static readonly SystemError NurbsControlPointCount = Get(3905);
        public static readonly SystemError ExtrusionProfileInvalid = Get(3906);
        public static readonly SystemError UVDomainSingularity = Get(3907);
        public static readonly SystemError BrepTopologyInvalid = Get(3908);
        public static readonly SystemError BrepGeometryInvalid = Get(3909);
        public static readonly SystemError BrepTolerancesAndFlagsInvalid = Get(3910);
        public static readonly SystemError MeshInvalid = Get(3911);

        // Operation Validation (3920-3931)
        public static readonly SystemError InvalidUnitConversion = Get(3920);
        public static readonly SystemError UnsupportedOperationType = Get(3930);
        public static readonly SystemError InputFiltered = Get(3931);
    }

    /// <summary>Spatial indexing errors (4000-4999).</summary>
    public static class Spatial {
        // Proximity Operations (4001-4004)
        public static readonly SystemError InvalidK = Get(4001);
        public static readonly SystemError InvalidDistance = Get(4002);
        public static readonly SystemError UnsupportedTypeCombo = Get(4003);
        public static readonly SystemError ProximityFailed = Get(4004);

        // Clustering Operations (4100-4104)
        public static readonly SystemError ClusteringFailed = Get(4100);
        public static readonly SystemError InvalidClusterK = Get(4101);
        public static readonly SystemError InvalidEpsilon = Get(4102);
        public static readonly SystemError KExceedsPointCount = Get(4103);
        public static readonly SystemError ZeroLengthDirection = Get(4104);

        // Medial Axis Operations (4200-4201)
        public static readonly SystemError MedialAxisFailed = Get(4200);
        public static readonly SystemError NonPlanarNotSupported = Get(4201);

        // Proximity Field Operations (4300)
        public static readonly SystemError InvalidDirection = Get(4300);
    }

    /// <summary>Topology analysis errors (5000-5999).</summary>
    public static class Topology {
        public static readonly SystemError DiagnosisFailed = Get(5001);
        public static readonly SystemError TopologyTooComplex = Get(5002);
        public static readonly SystemError HealingFailed = Get(5010);
        public static readonly SystemError FeatureExtractionFailed = Get(5020);
    }
}

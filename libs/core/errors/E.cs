using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Error registry with O(1) lookup via FrozenDictionary.</summary>
public static class E {
    private const byte DResults = 1;
    private const byte DGeometry = 2;
    private const byte DValidation = 3;
    private const byte DSpatial = 4;
    private const byte DTopology = 5;

    private static readonly FrozenDictionary<int, (byte D, string M)> _e =
        new Dictionary<int, (byte, string)> {
            [1001] = (DResults, "No value provided"),
            [1002] = (DResults, "Invalid Create parameters"),
            [1003] = (DResults, "Invalid validation parameters"),
            [1004] = (DResults, "Invalid Lift parameters"),
            [1100] = (DResults, "Cannot access value in error state or error in success state"),
            [2000] = (DGeometry, "Invalid extraction method specified"),
            [2001] = (DGeometry, "Insufficient parameters for extraction operation"),
            [2002] = (DGeometry, "Count parameter must be positive"),
            [2003] = (DGeometry, "Length parameter must be greater than zero tolerance"),
            [2004] = (DGeometry, "Direction parameter required for positional extrema"),
            [2005] = (DGeometry, "Parameters array must not be empty"),
            [2006] = (DGeometry, "Angle threshold must be positive"),
            [2200] = (DGeometry, "Intersection method not supported for geometry types"),
            [2201] = (DGeometry, "Intersection computation failed"),
            [2202] = (DGeometry, "Projection direction vector is invalid or zero-length"),
            [2204] = (DGeometry, "Ray direction vector is invalid or zero-length"),
            [2205] = (DGeometry, "Maximum hit count must be positive"),
            [2210] = (DGeometry, "Intersection classification failed"),
            [2211] = (DGeometry, "Insufficient intersection data for classification"),
            [2220] = (DGeometry, "Near-miss search failed"),
            [2221] = (DGeometry, "Invalid search radius for near-miss detection"),
            [2230] = (DGeometry, "Intersection stability analysis failed"),
            [2300] = (DGeometry, "Geometry type not supported for analysis"),
            [2310] = (DGeometry, "Curve analysis computation failed"),
            [2311] = (DGeometry, "Surface analysis computation failed"),
            [2312] = (DGeometry, "Brep analysis computation failed"),
            [2313] = (DGeometry, "Mesh analysis computation failed"),
            [2320] = (DGeometry, "Surface quality analysis failed"),
            [2321] = (DGeometry, "Curve fairness analysis failed"),
            [2322] = (DGeometry, "Mesh FEA quality analysis failed"),
            [2400] = (DGeometry, "Naked edge extraction failed"),
            [2401] = (DGeometry, "Boundary loop construction failed"),
            [2402] = (DGeometry, "Non-manifold edge detected"),
            [2403] = (DGeometry, "Non-manifold vertex detected"),
            [2404] = (DGeometry, "Connected component analysis failed"),
            [2405] = (DGeometry, "Edge classification failed"),
            [2406] = (DGeometry, "Invalid edge index"),
            [2407] = (DGeometry, "Edge curve extraction failed"),
            [2408] = (DGeometry, "Boundary loop join failed"),
            [2409] = (DGeometry, "Invalid edge topology"),
            [2410] = (DGeometry, "Adjacency query failed"),
            [2411] = (DGeometry, "Invalid vertex index"),
            [2500] = (DGeometry, "Unsupported orientation geometry type"),
            [2501] = (DGeometry, "Invalid orientation plane"),
            [2502] = (DGeometry, "Geometry transformation failed"),
            [2503] = (DGeometry, "Centroid extraction failed"),
            [2504] = (DGeometry, "Invalid orientation mode"),
            [2505] = (DGeometry, "Parallel vectors cannot be aligned"),
            [2506] = (DGeometry, "Invalid orientation vectors"),
            [2507] = (DGeometry, "Invalid curve parameter for orientation"),
            [2508] = (DGeometry, "Invalid surface UV parameters"),
            [2509] = (DGeometry, "Frame extraction failed"),
            [2520] = (DGeometry, "Relative orientation computation failed"),
            [2521] = (DGeometry, "Geometries too dissimilar for orientation"),
            [2530] = (DGeometry, "Pattern detection failed"),
            [2531] = (DGeometry, "No recognizable pattern found"),
            [2600] = (DGeometry, "Feature extraction failed"),
            [2601] = (DGeometry, "Feature classification failed"),
            [2610] = (DGeometry, "Primitive decomposition failed"),
            [2611] = (DGeometry, "No primitives detected in geometry"),
            [2620] = (DGeometry, "Pattern extraction failed"),
            [2621] = (DGeometry, "No pattern detected in geometry collection"),
            [3000] = (DValidation, "Geometry must be valid"),
            [3100] = (DValidation, "Curve must be closed and planar for area centroid"),
            [3200] = (DValidation, "Bounding box is invalid"),
            [3300] = (DValidation, "Mass properties computation failed"),
            [3400] = (DValidation, "Geometry has invalid topology"),
            [3500] = (DValidation, "Geometry is degenerate"),
            [3600] = (DValidation, "Geometry is self-intersecting"),
            [3700] = (DValidation, "Mesh has non-manifold edges"),
            [3800] = (DValidation, "Surface has positional discontinuity (G0)"),
            [3900] = (DValidation, "Absolute tolerance must be greater than zero"),
            [3901] = (DValidation, "Relative tolerance must be in range [0,1)"),
            [3902] = (DValidation, "Angle tolerance must be in range (0, 2π]"),
            [3903] = (DValidation, "Geometry exceeds tolerance threshold"),
            [3904] = (DValidation, "Polycurve has segment gaps exceeding tolerance"),
            [3905] = (DValidation, "NURBS control point count below minimum"),
            [3906] = (DValidation, "Extrusion profile curve invalid"),
            [3907] = (DValidation, "Surface UV domain has singularity"),
            [3908] = (DValidation, "Brep topology is invalid"),
            [3909] = (DValidation, "Brep geometry is invalid"),
            [3910] = (DValidation, "Brep tolerances and flags are invalid"),
            [3911] = (DValidation, "Mesh validation failed"),
            [3920] = (DValidation, "Invalid unit conversion scale"),
            [3930] = (DValidation, "Unsupported operation type"),
            [3931] = (DValidation, "Input filtered by predicate"),
            [4001] = (DSpatial, "K-nearest neighbor count must be positive"),
            [4002] = (DSpatial, "Distance limit must be positive"),
            [4003] = (DSpatial, "Input and query type combination not supported"),
            [4004] = (DSpatial, "Proximity search operation failed"),
            [4005] = (DSpatial, "Spatial query exceeded buffer capacity"),
            [4100] = (DSpatial, "Spatial clustering operation failed"),
            [4101] = (DSpatial, "K-means k parameter must be positive"),
            [4102] = (DSpatial, "DBSCAN epsilon parameter must be positive"),
            [4103] = (DSpatial, "Cluster count exceeds point count"),
            [4104] = (DSpatial, "Point set is degenerate (all points coincident)"),
            [4105] = (DSpatial, "Direction vector is zero-length"),
            [4106] = (DSpatial, "Input points are collinear"),
            [4107] = (DSpatial, "Input points are coplanar (3D operation requires non-coplanar)"),
            [4108] = (DSpatial, "Point distribution insufficient for operation"),
            [4200] = (DSpatial, "Medial axis computation failed"),
            [4201] = (DSpatial, "Non-planar medial axis not supported"),
            [4300] = (DSpatial, "Proximity field computation failed"),
            [4301] = (DSpatial, "Proximity field direction vector is invalid"),
            [5001] = (DTopology, "Topology diagnosis failed"),
            [5002] = (DTopology, "Topology is too complex for diagnosis"),
            [5010] = (DTopology, "Topology healing failed"),
            [5011] = (DTopology, "Topology healing made geometry worse"),
            [5020] = (DTopology, "Topological feature extraction failed"),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) {
        (byte domain, string message) = _e.TryGetValue(code, out (byte D, string M) v) ? (v.D, v.M) : ((byte)0, $"Unknown error code: {code.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return context is null ? new SystemError(domain, code, message) : new SystemError(domain, code, message).WithContext(context);
    }

    /// <summary>Results system errors (1000-1999).</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = Get(1001);
        public static readonly SystemError InvalidCreate = Get(1002);
        public static readonly SystemError InvalidValidate = Get(1003);
        public static readonly SystemError InvalidLift = Get(1004);
        public static readonly SystemError InvalidAccess = Get(1100);
    }

    /// <summary>Geometry errors (2000-2999).</summary>
    public static class Geometry {
        public static readonly SystemError InvalidExtraction = Get(2000);
        public static readonly SystemError InsufficientParameters = Get(2001);
        public static readonly SystemError InvalidCount = Get(2002);
        public static readonly SystemError InvalidLength = Get(2003);
        public static readonly SystemError InvalidDirection = Get(2004);
        public static readonly SystemError InvalidParameters = Get(2005);
        public static readonly SystemError InvalidAngle = Get(2006);
        public static readonly SystemError UnsupportedIntersection = Get(2200);
        public static readonly SystemError IntersectionFailed = Get(2201);
        public static readonly SystemError InvalidProjection = Get(2202);
        public static readonly SystemError InvalidRay = Get(2204);
        public static readonly SystemError InvalidMaxHits = Get(2205);
        public static readonly SystemError ClassificationFailed = Get(2210);
        public static readonly SystemError InsufficientIntersectionData = Get(2211);
        public static readonly SystemError NearMissSearchFailed = Get(2220);
        public static readonly SystemError InvalidSearchRadius = Get(2221);
        public static readonly SystemError StabilityAnalysisFailed = Get(2230);
        public static readonly SystemError UnsupportedAnalysis = Get(2300);
        public static readonly SystemError CurveAnalysisFailed = Get(2310);
        public static readonly SystemError SurfaceAnalysisFailed = Get(2311);
        public static readonly SystemError BrepAnalysisFailed = Get(2312);
        public static readonly SystemError MeshAnalysisFailed = Get(2313);
        public static readonly SystemError SurfaceQualityFailed = Get(2320);
        public static readonly SystemError CurveFairnessFailed = Get(2321);
        public static readonly SystemError MeshFEAAnalysisFailed = Get(2322);
        public static readonly SystemError NakedEdgeFailed = Get(2400);
        public static readonly SystemError BoundaryLoopFailed = Get(2401);
        public static readonly SystemError NonManifoldEdge = Get(2402);
        public static readonly SystemError NonManifoldVertex = Get(2403);
        public static readonly SystemError ConnectivityFailed = Get(2404);
        public static readonly SystemError EdgeClassificationFailed = Get(2405);
        public static readonly SystemError InvalidEdgeIndex = Get(2406);
        public static readonly SystemError EdgeCurveExtractionFailed = Get(2407);
        public static readonly SystemError BoundaryLoopJoinFailed = Get(2408);
        public static readonly SystemError InvalidEdge = Get(2409);
        public static readonly SystemError AdjacencyFailed = Get(2410);
        public static readonly SystemError InvalidVertexIndex = Get(2411);
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
        public static readonly SystemError OrientationFailed = Get(2520);
        public static readonly SystemError GeometriesTooDissimilar = Get(2521);
        public static readonly SystemError PatternDetectionFailed = Get(2530);
        public static readonly SystemError NoPatternFound = Get(2531);
        public static readonly SystemError FeatureExtractionFailed = Get(2600);
        public static readonly SystemError FeatureClassificationFailed = Get(2601);
        public static readonly SystemError DecompositionFailed = Get(2610);
        public static readonly SystemError NoPrimitivesDetected = Get(2611);
        public static readonly SystemError PatternExtractionFailed = Get(2620);
        public static readonly SystemError NoPatternDetected = Get(2621);
    }

    /// <summary>Validation errors (3000-3999).</summary>
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
        public static readonly SystemError PolycurveGaps = Get(3904);
        public static readonly SystemError NurbsControlPointCount = Get(3905);
        public static readonly SystemError ExtrusionProfileInvalid = Get(3906);
        public static readonly SystemError UVDomainSingularity = Get(3907);
        public static readonly SystemError BrepTopologyInvalid = Get(3908);
        public static readonly SystemError BrepGeometryInvalid = Get(3909);
        public static readonly SystemError BrepTolerancesAndFlagsInvalid = Get(3910);
        public static readonly SystemError MeshInvalid = Get(3911);
        public static readonly SystemError InvalidUnitConversion = Get(3920);
        public static readonly SystemError UnsupportedOperationType = Get(3930);
        public static readonly SystemError InputFiltered = Get(3931);
    }

    /// <summary>Spatial indexing errors (4000-4999).</summary>
    public static class Spatial {
        public static readonly SystemError InvalidK = Get(4001);
        public static readonly SystemError InvalidDistance = Get(4002);
        public static readonly SystemError UnsupportedTypeCombo = Get(4003);
        public static readonly SystemError ProximityFailed = Get(4004);
        public static readonly SystemError BufferOverflow = Get(4005);
        public static readonly SystemError ClusteringFailed = Get(4100);
        public static readonly SystemError InvalidClusterK = Get(4101);
        public static readonly SystemError InvalidEpsilon = Get(4102);
        public static readonly SystemError KExceedsPointCount = Get(4103);
        public static readonly SystemError DegeneratePointSet = Get(4104);
        public static readonly SystemError ZeroLengthDirection = Get(4105);
        public static readonly SystemError CollinearPoints = Get(4106);
        public static readonly SystemError CoplanarPoints = Get(4107);
        public static readonly SystemError InsufficientPointDistribution = Get(4108);
        public static readonly SystemError MedialAxisFailed = Get(4200);
        public static readonly SystemError NonPlanarNotSupported = Get(4201);
        public static readonly SystemError ProximityFieldFailed = Get(4300);
        public static readonly SystemError InvalidDirection = Get(4301);
    }

    /// <summary>Topology analysis errors (5000-5999).</summary>
    public static class Topology {
        public static readonly SystemError DiagnosisFailed = Get(5001);
        public static readonly SystemError TopologyTooComplex = Get(5002);
        public static readonly SystemError HealingFailed = Get(5010);
        public static readonly SystemError HealingMadeWorse = Get(5011);
        public static readonly SystemError FeatureExtractionFailed = Get(5020);
    }
}

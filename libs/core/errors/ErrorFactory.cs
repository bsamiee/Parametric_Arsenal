using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Polymorphic error factory with compile-time optimized dispatch for zero-allocation error retrieval.</summary>
public static class ErrorFactory {
    /// <summary>Creates SystemError using polymorphic parameter detection via tuple dispatch pattern.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Create(ErrorDomain domain, int code, string? context = null) =>
        context switch {
            null => ErrorCatalog.Get(domain, code),
            string ctx => ErrorCatalog.Get(domain, code).WithContext(ctx),
        };

    /// <summary>Results system errors (1000-1199).</summary>
    public static class Results {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError NoValueProvided() => Create(ErrorDomain.Results, 1001);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidCreateParameters() => Create(ErrorDomain.Results, 1002);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidValidateParameters() => Create(ErrorDomain.Results, 1003);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidLiftParameters() => Create(ErrorDomain.Results, 1004);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidAccess() => Create(ErrorDomain.Results, 1100);
    }

    /// <summary>Extraction operation errors (2000-2099).</summary>
    public static class Extraction {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidMethod() => Create(ErrorDomain.Geometry, 2000);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InsufficientParameters() => Create(ErrorDomain.Geometry, 2001);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidCount() => Create(ErrorDomain.Geometry, 2002);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidLength() => Create(ErrorDomain.Geometry, 2003);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidDirection() => Create(ErrorDomain.Geometry, 2004);
    }

    /// <summary>Intersection operation errors (2200-2299).</summary>
    public static class Intersection {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError UnsupportedMethod() => Create(ErrorDomain.Geometry, 2200);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError ComputationFailed() => Create(ErrorDomain.Geometry, 2201);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidProjectionDirection() => Create(ErrorDomain.Geometry, 2202);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidRayDirection() => Create(ErrorDomain.Geometry, 2204);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidMaxHitCount() => Create(ErrorDomain.Geometry, 2205);
    }

    /// <summary>Analysis operation errors (2300-2399).</summary>
    public static class Analysis {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError UnsupportedGeometry() => Create(ErrorDomain.Geometry, 2300);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError CurveAnalysisFailed() => Create(ErrorDomain.Geometry, 2310);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError SurfaceAnalysisFailed() => Create(ErrorDomain.Geometry, 2311);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError BrepAnalysisFailed() => Create(ErrorDomain.Geometry, 2312);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError MeshAnalysisFailed() => Create(ErrorDomain.Geometry, 2313);
    }

    /// <summary>Spatial indexing errors (4000-4099).</summary>
    public static class Spatial {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidK() => Create(ErrorDomain.Geometry, 4001);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidDistance() => Create(ErrorDomain.Geometry, 4002);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError UnsupportedTypeCombo() => Create(ErrorDomain.Validation, 4003);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError ProximityFailed() => Create(ErrorDomain.Geometry, 4004);
    }

    /// <summary>Validation errors (3000-3999).</summary>
    public static class Validation {
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError GeometryInvalid() => Create(ErrorDomain.Validation, 3000);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError CurveNotClosedOrPlanar() => Create(ErrorDomain.Validation, 3100);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError BoundingBoxInvalid() => Create(ErrorDomain.Validation, 3200);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError MassPropertiesComputationFailed() => Create(ErrorDomain.Validation, 3300);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidTopology() => Create(ErrorDomain.Validation, 3400);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError DegenerateGeometry() => Create(ErrorDomain.Validation, 3500);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError SelfIntersecting() => Create(ErrorDomain.Validation, 3600);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError NonManifoldEdges() => Create(ErrorDomain.Validation, 3700);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError PositionalDiscontinuity() => Create(ErrorDomain.Validation, 3800);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidAbsoluteTolerance() => Create(ErrorDomain.Validation, 3900);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidRelativeTolerance() => Create(ErrorDomain.Validation, 3901);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidAngleTolerance() => Create(ErrorDomain.Validation, 3902);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError ToleranceExceeded() => Create(ErrorDomain.Validation, 3903);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InvalidUnitConversion() => Create(ErrorDomain.Validation, 3920);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError UnsupportedOperationType() => Create(ErrorDomain.Validation, 3930);

        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemError InputFiltered() => Create(ErrorDomain.Validation, 3931);
    }
}

using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection operation error definitions - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Intersection instead", error: false)]
public static class IntersectionErrors {
    public static class Operation {
        public static readonly SystemError UnsupportedMethod = ErrorFactory.Intersection.UnsupportedMethod();
        public static readonly SystemError ComputationFailed = ErrorFactory.Intersection.ComputationFailed();
    }

    public static class Parameters {
        public static readonly SystemError InvalidProjectionDirection = ErrorFactory.Intersection.InvalidProjectionDirection();
        public static readonly SystemError InvalidRayDirection = ErrorFactory.Intersection.InvalidRayDirection();
        public static readonly SystemError InvalidMaxHitCount = ErrorFactory.Intersection.InvalidMaxHitCount();
    }
}

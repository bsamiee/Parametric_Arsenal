using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection operation error definitions - aliases to E.Geometry for backward compatibility during transition.</summary>
public static class IntersectionErrors {
    public static class Operation {
        public static readonly SystemError UnsupportedMethod = E.Geometry.UnsupportedIntersection;
        public static readonly SystemError ComputationFailed = E.Geometry.IntersectionFailed;
    }

    public static class Parameters {
        public static readonly SystemError InvalidProjectionDirection = E.Geometry.InvalidProjection;
        public static readonly SystemError InvalidRayDirection = E.Geometry.InvalidRay;
        public static readonly SystemError InvalidMaxHitCount = E.Geometry.InvalidMaxHits;
    }
}

using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection analysis error definitions with hierarchical categorization.</summary>
public static class IntersectionErrors {
    public static class Operation {
        public static readonly SystemError UnsupportedMethod = new(ErrorDomain.Geometry, 2200, "Intersection not supported for geometry type combination");
        public static readonly SystemError ComputationFailed = new(ErrorDomain.Geometry, 2201, "Intersection computation failed in RhinoCommon SDK");
    }

    public static class Parameters {
        public static readonly SystemError InvalidProjectionDirection = new(ErrorDomain.Geometry, 2202, "Projection direction vector invalid or zero-length");
        public static readonly SystemError InvalidRayDirection = new(ErrorDomain.Geometry, 2204, "Ray direction vector invalid or zero-length");
        public static readonly SystemError InvalidMaxHitCount = new(ErrorDomain.Geometry, 2205, "Maximum hit count must be positive integer");
    }
}

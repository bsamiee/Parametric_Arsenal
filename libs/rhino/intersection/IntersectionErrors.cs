using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection operation error definitions.</summary>
public static class IntersectionErrors {
    public static class Operation {
        public static readonly SystemError UnsupportedMethod =
            new(ErrorDomain.Geometry, 2200, "Intersection method not supported for geometry types");

        public static readonly SystemError ComputationFailed =
            new(ErrorDomain.Geometry, 2201, "Intersection computation failed");
    }
}

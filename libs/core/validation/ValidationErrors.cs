using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>DEPRECATED: Use CoreErrors instead. Maintained for backward compatibility only.</summary>
[Obsolete("Use CoreErrors.Geometry.* instead. This type will be removed in a future version.", error: false)]
public static class ValidationErrors {
    /// <summary>DEPRECATED: Use CoreErrors.Geometry instead.</summary>
    [Obsolete("Use CoreErrors.Geometry.* instead.", error: false)]
    public static class Geometry {
        public static readonly SystemError Invalid = CoreErrors.Geometry.Invalid;

        [Obsolete("Use CoreErrors.Geometry.Curve.* instead.", error: false)]
        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = CoreErrors.Geometry.Curve.NotClosedOrPlanar;
        }

        [Obsolete("Use CoreErrors.Geometry.BoundingBox.* instead.", error: false)]
        public static class BoundingBox {
            public static readonly SystemError Invalid = CoreErrors.Geometry.BoundingBox.Invalid;
        }

        [Obsolete("Use CoreErrors.Geometry.Properties.* instead.", error: false)]
        public static class Properties {
            public static readonly SystemError ComputationFailed = CoreErrors.Geometry.Properties.ComputationFailed;
        }

        [Obsolete("Use CoreErrors.Geometry.Topology.* instead.", error: false)]
        public static class Topology {
            public static readonly SystemError InvalidTopology = CoreErrors.Geometry.Topology.InvalidTopology;
        }

        [Obsolete("Use CoreErrors.Geometry.Degeneracy.* instead.", error: false)]
        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = CoreErrors.Geometry.Degeneracy.DegenerateGeometry;
        }

        [Obsolete("Use CoreErrors.Geometry.SelfIntersection.* instead.", error: false)]
        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = CoreErrors.Geometry.SelfIntersection.SelfIntersecting;
        }

        [Obsolete("Use CoreErrors.Geometry.MeshTopology.* instead.", error: false)]
        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = CoreErrors.Geometry.MeshTopology.NonManifoldEdges;
        }

        [Obsolete("Use CoreErrors.Geometry.Continuity.* instead.", error: false)]
        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = CoreErrors.Geometry.Continuity.PositionalDiscontinuity;
        }
    }

    /// <summary>DEPRECATED: Use CoreErrors.Context instead.</summary>
    [Obsolete("Use CoreErrors.Context.* instead.", error: false)]
    public static class Context {
        public static readonly SystemError InvalidUnitConversion = CoreErrors.Context.InvalidUnitConversion;

        [Obsolete("Use CoreErrors.Context.Tolerance.* instead.", error: false)]
        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = CoreErrors.Context.Tolerance.InvalidAbsolute;
            public static readonly SystemError InvalidRelative = CoreErrors.Context.Tolerance.InvalidRelative;
            public static readonly SystemError InvalidAngle = CoreErrors.Context.Tolerance.InvalidAngle;
            public static readonly SystemError ToleranceExceeded = CoreErrors.Context.Tolerance.ToleranceExceeded;
        }
    }

    /// <summary>DEPRECATED: Use CoreErrors.Operations instead.</summary>
    [Obsolete("Use CoreErrors.Operations.* instead.", error: false)]
    public static class Operations {
        public static readonly SystemError UnsupportedOperationType = CoreErrors.Operations.UnsupportedOperationType;
        public static readonly SystemError InputFiltered = CoreErrors.Operations.InputFiltered;
    }
}

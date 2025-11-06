using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Ultra-dense analysis dispatcher using FrozenSet method selection and Result monad eliminating null references.</summary>
internal static class AnalysisStrategies {
    /// <summary>Maximum discontinuity buffer size for ArrayPool allocations in discontinuity detection.</summary>
    private const int MaxDiscontinuities = 20;

    /// <summary>Validation configuration mapping geometry types to required validation modes with method-specific overrides.</summary>
    private static readonly FrozenDictionary<Type, ValidationMode> _validationConfig =
        new Dictionary<Type, ValidationMode> {
            [typeof(Curve)] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [typeof(Surface)] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [typeof(Brep)] = ValidationMode.Standard | ValidationMode.Topology,
            [typeof(Mesh)] = ValidationMode.MeshSpecific,
            [typeof(SubD)] = ValidationMode.Standard,
            [typeof(PointCloud)] = ValidationMode.Standard,
        }.ToFrozenDictionary();

    /// <summary>Polymorphic analysis with direct SDK dispatch using Result monad for all optional fields.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "Dense polymorphic dispatch requires comprehensive pattern matching")]
    internal static Result<AnalysisData> Analyze(
        object source,
        FrozenSet<AnalysisMethod> methods,
        IGeometryContext context,
        (double? Curve, (double, double)? Surface, int? Mesh)? parameters = null,
        int derivativeOrder = 2) =>
        (source, parameters, derivativeOrder) switch {
            (Curve cv, var p, int o) => ResultFactory.Create(value: cv)
                .Validate(args: [context, _validationConfig.GetValueOrDefault(typeof(Curve), ValidationMode.Standard)])
                .Map(c => {
                    double t = p?.Curve ?? c.Domain.ParameterAt(0.5);
                    ArrayPool<double> pool = ArrayPool<double>.Shared; double[] buffer = pool.Rent(MaxDiscontinuities);
                    try {
                        int dc = 0; double s = c.Domain.Min;
                        while (dc < MaxDiscontinuities && c.GetNextDiscontinuity(Continuity.C1_continuous, s, c.Domain.Max, out double td)) { buffer[dc++] = td; s = td + context.AbsoluteTolerance; }
                        return new AnalysisData(
                            c.PointAt(t),
                            methods.Contains(AnalysisMethod.Derivatives) ? ResultFactory.Create(value: c.DerivativeAt(t, o) ?? []) : ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.DerivativeComputationFailed),
                            methods.Contains(AnalysisMethod.Frame) && c.FrameAt(t, out Plane f) ? ResultFactory.Create(value: f) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Curvature) && c.CurvatureAt(t) is Vector3d k && k.IsValid ? ResultFactory.Create(value: (0d, 0d, k.Length, 0d, Vector3d.Zero, Vector3d.Zero)) : ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Discontinuity) && dc > 0 ? ResultFactory.Create(value: (buffer[..dc].ToArray(), buffer[..dc].Select(dp => c.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous).ToArray())) : ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                            ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                            ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                            methods.Contains(AnalysisMethod.Metrics) ? ResultFactory.Create(value: (c.GetLength(), 0d, 0d, AreaMassProperties.Compute(c)?.Centroid ?? Point3d.Origin)) : ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Domains) ? ResultFactory.Create<Interval[]>(value: [c.Domain]) : ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                            (t, null, null));
                    } finally { pool.Return(buffer, clearArray: true); }
                }),

            (Surface sf, var p, int o) => ResultFactory.Create(value: sf)
                .Validate(args: [context, _validationConfig.GetValueOrDefault(typeof(Surface), ValidationMode.Standard)])
                .Map(s => {
                    (double u, double v) = p?.Surface ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5));
                    ArrayPool<double> pool = ArrayPool<double>.Shared; double[] buffer = pool.Rent(MaxDiscontinuities);
                    try {
                        int dc = 0;
                        foreach (int dir in Enumerable.Range(0, 2)) {
                            double st = s.Domain(dir).Min;
                            while (dc < MaxDiscontinuities && s.GetNextDiscontinuity(dir, Continuity.C1_continuous, st, s.Domain(dir).Max, out double td)) { buffer[dc++] = td; st = td + context.AbsoluteTolerance; }
                        }
                        return new AnalysisData(
                            s.PointAt(u, v),
                            methods.Contains(AnalysisMethod.Derivatives) && s.Evaluate(u, v, o, out Point3d _, out Vector3d[] d) ? ResultFactory.Create(value: d) : ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.SurfaceEvaluationFailed),
                            methods.Contains(AnalysisMethod.Frame) && s.FrameAt(u, v, out Plane f) ? ResultFactory.Create(value: f) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Curvature) && s.CurvatureAt(u, v) is SurfaceCurvature sc ? ResultFactory.Create(value: (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1))) : ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Discontinuity) && dc > 0 ? ResultFactory.Create(value: (buffer[..dc].ToArray(), buffer[..dc].Select(_ => Continuity.C1_continuous).ToArray())) : ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                            ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                            ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                            methods.Contains(AnalysisMethod.Metrics) ? ResultFactory.Create(value: (0d, AreaMassProperties.Compute(s)?.Area ?? 0d, 0d, AreaMassProperties.Compute(s)?.Centroid ?? Point3d.Origin)) : ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                            methods.Contains(AnalysisMethod.Domains) ? ResultFactory.Create<Interval[]>(value: [s.Domain(0), s.Domain(1)]) : ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                            (null, (u, v), null));
                    } finally { pool.Return(buffer, clearArray: true); }
                }),

            (Brep brep, var p, int o) => ResultFactory.Create(value: brep)
                .Validate(args: [context, _validationConfig.GetValueOrDefault(typeof(Brep), ValidationMode.Standard)])
                .Map(b => {
                    int fIdx = Math.Max(0, Math.Min(p?.Mesh ?? 0, b.Faces.Count - 1));
                    using Surface sf = b.Faces[fIdx].UnderlyingSurface();
                    (double u, double v) = p?.Surface ?? (sf.Domain(0).ParameterAt(0.5), sf.Domain(1).ParameterAt(0.5)); Point3d pt = sf.PointAt(u, v);
                    return new AnalysisData(
                        pt,
                        methods.Contains(AnalysisMethod.Derivatives) && sf.Evaluate(u, v, o, out Point3d _, out Vector3d[] d) ? ResultFactory.Create(value: d) : ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.SurfaceEvaluationFailed),
                        methods.Contains(AnalysisMethod.Frame) && sf.FrameAt(u, v, out Plane f) ? ResultFactory.Create(value: f) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        methods.Contains(AnalysisMethod.Curvature) && sf.CurvatureAt(u, v) is SurfaceCurvature sc ? ResultFactory.Create(value: (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1))) : ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                        methods.Contains(AnalysisMethod.Topology) ? ResultFactory.Create(value: (b.Vertices.Select((v, i) => (i, v.Location)).ToArray(), b.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))).ToArray(), b.IsManifold, b.IsSolid)) : ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                        methods.Contains(AnalysisMethod.Proximity) && b.ClosestPoint(pt, out Point3d cp, out ComponentIndex _, out double _, out double _, context.AbsoluteTolerance * 100, out Vector3d _) ? ResultFactory.Create(value: (cp, pt.DistanceTo(cp))) : ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                        methods.Contains(AnalysisMethod.Metrics) ? ResultFactory.Create(value: (0d, AreaMassProperties.Compute(b)?.Area ?? 0d, VolumeMassProperties.Compute(b)?.Volume ?? 0d, VolumeMassProperties.Compute(b)?.Centroid ?? Point3d.Origin)) : ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                        (null, (u, v), fIdx));
                }),

            (Mesh mesh, var p, int o) => ResultFactory.Create(value: mesh)
                .Validate(args: [context, _validationConfig.GetValueOrDefault(typeof(Mesh), ValidationMode.MeshSpecific)])
                .Map(m => {
                    int vIdx = Math.Max(0, Math.Min(p?.Mesh ?? 0, m.Vertices.Count - 1));
                    return new AnalysisData(
                        m.Vertices[vIdx],
                        ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.DerivativeComputationFailed),
                        methods.Contains(AnalysisMethod.Frame) ? ResultFactory.Create(value: new Plane(m.Vertices[vIdx], m.Normals.Count > vIdx ? m.Normals[vIdx] : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                        methods.Contains(AnalysisMethod.Topology) ? ResultFactory.Create(value: (Enumerable.Range(0, m.TopologyVertices.Count).Select(i => (i, (Point3d)m.TopologyVertices[i])).ToArray(), Enumerable.Range(0, m.TopologyEdges.Count).Select(i => (i, m.TopologyEdges.EdgeLine(i))).ToArray(), m.IsManifold(topologicalTest: true, out bool _, out bool _), m.IsClosed)) : ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                        methods.Contains(AnalysisMethod.Proximity) && m.ClosestMeshPoint(m.Vertices[vIdx], context.AbsoluteTolerance * 100) is MeshPoint mp ? ResultFactory.Create(value: (m.PointAt(mp), 0d)) : ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                        methods.Contains(AnalysisMethod.Metrics) ? ResultFactory.Create(value: (0d, AreaMassProperties.Compute(m)?.Area ?? 0d, VolumeMassProperties.Compute(m)?.Volume ?? 0d, Point3d.Origin)) : ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                        methods.Contains(AnalysisMethod.Domains) ? ResultFactory.Create<Interval[]>(value: [new Interval(0, m.Vertices.Count)]) : ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                        (null, null, vIdx));
                }),

            (SubD subd, var p, int o) => ResultFactory.Create(deferred: () => {
                using Brep br = subd.ToBrep();
                return br is not null ? Analyze(br, methods, context, p, o) :
                    ResultFactory.Create<AnalysisData>(error: AnalysisErrors.Operation.UnsupportedGeometry);
            }),

            (PointCloud cloud, var p, int o) when cloud.Count > 0 => ResultFactory.Create(value: cloud.GetBoundingBox(accurate: true).Center)
                .Map(center => new AnalysisData(
                    center,
                    ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.DerivativeComputationFailed),
                    methods.Contains(AnalysisMethod.Frame) && Plane.FitPlaneToPoints(cloud.GetPoints(), out Plane f) == PlaneFitResult.Success ? ResultFactory.Create(value: f) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                    ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                    ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                    ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                    ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                    methods.Contains(AnalysisMethod.Metrics) ? ResultFactory.Create(value: (0d, 0d, 0d, center)) : ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                    methods.Contains(AnalysisMethod.Domains) ? ResultFactory.Create<Interval[]>(value: [new Interval(0, cloud.Count)]) : ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                    (null, null, null))),

            (Point3d pt, _, int o) => ResultFactory.Create(value: AnalysisData.Empty(pt)),

            (Vector3d vec, var p, int o) => ResultFactory.Create(value: new AnalysisData(
                Point3d.Origin,
                methods.Contains(AnalysisMethod.Derivatives) ? ResultFactory.Create<Vector3d[]>(value: [vec]) : ResultFactory.Create<Vector3d[]>(error: AnalysisErrors.Evaluation.DerivativeComputationFailed),
                methods.Contains(AnalysisMethod.Frame) ? ResultFactory.Create(value: new Plane(Point3d.Origin, vec)) : ResultFactory.Create<Plane>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                ResultFactory.Create<(double, double, double, double, Vector3d, Vector3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                ResultFactory.Create<(double[], Continuity[])>(error: AnalysisErrors.Discontinuity.NoneFound),
                ResultFactory.Create<((int, Point3d)[], (int, Line)[], bool, bool)>(error: AnalysisErrors.Topology.NoTopologyData),
                ResultFactory.Create<(Point3d, double)>(error: AnalysisErrors.Proximity.ClosestPointFailed),
                ResultFactory.Create<(double, double, double, Point3d)>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                ResultFactory.Create<Interval[]>(error: AnalysisErrors.Parameters.ParameterOutOfDomain),
                (null, null, null))),

            _ => ResultFactory.Create<AnalysisData>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        };
}

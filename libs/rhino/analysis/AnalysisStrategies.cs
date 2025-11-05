using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense analysis strategy dispatcher leveraging ResultFactory and Unified validation.</summary>
internal static class AnalysisStrategies {
    private static readonly FrozenDictionary<Type, ValidationMode> _validation =
        new Dictionary<Type, ValidationMode> {
            [typeof(Curve)] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [typeof(Surface)] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [typeof(Brep)] = ValidationMode.Standard | ValidationMode.Topology | ValidationMode.SurfaceContinuity,
            [typeof(SubD)] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [typeof(Mesh)] = ValidationMode.MeshSpecific,
            [typeof(PointCloud)] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [typeof(Point3d[])] = ValidationMode.None,
            [typeof(Point3d)] = ValidationMode.None,
            [typeof(Vector3d)] = ValidationMode.None,
        }.ToFrozenDictionary();

    private static readonly ConditionalWeakTable<SubD, SubDEvaluator> _subdEvaluators = new();
    private static readonly ConditionalWeakTable<Mesh, MeshCurvatureList> _meshCurvatures = new();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<AnalysisPacket>> Analyze(object source, IGeometryContext context, AnalysisParameters parameters) {
        Type runtime = source switch {
            BrepFace => typeof(Surface),
            Surface => typeof(Surface),
            GeometryBase geometry => geometry.GetType(),
            Point3d[] => typeof(Point3d[]),
            PointCloud => typeof(PointCloud),
            Point3d => typeof(Point3d),
            Vector3d => typeof(Vector3d),
            _ => source.GetType(),
        };
        ValidationMode mode = _validation.GetValueOrDefault(runtime);
        return ResultFactory.Create(value: source)
            .Validate(args: mode == ValidationMode.None ? null : [context, mode])
            .Bind(_ => AnalyzeCore(source, context, parameters));
    }

    [Pure]
    private static Result<IReadOnlyList<AnalysisPacket>> AnalyzeCore(object source, IGeometryContext context, AnalysisParameters parameters) =>
        source switch {
            Curve curve => ResultFactory.Create(value: (Domain: curve.Domain, Parameter: parameters.CurveParameter ?? curve.Domain.ParameterAt(0.5), Order: parameters.DerivativeOrder < 0 ? 0 : parameters.DerivativeOrder))
                .Validate(predicate: data => data.Order >= 0, error: AnalysisErrors.Parameters.InvalidDerivativeOrder)
                .Validate(predicate: data => data.Domain.IncludesParameter(data.Parameter), error: AnalysisErrors.Parameters.ParameterOutOfDomain)
                .Bind(data => curve.Evaluate(data.Parameter, data.Order is 0 ? 1 : data.Order, out Point3d location, out Vector3d[] derivatives) switch {
                    true => ResultFactory.Create(value: (Location: location, Derivatives: derivatives, Parameter: data.Parameter, Curvature: curve.CurvatureAt(data.Parameter), Frame: curve.FrameAt(data.Parameter, out Plane computed) ? computed : new Plane(location, Vector3d.ZAxis)))
                        .Map(tuple => new AnalysisPacket(
                            tuple.Location,
                            Array.AsReadOnly<Vector3d>(tuple.Derivatives),
                            tuple.Frame,
                            tuple.Curvature.IsValid ? new AnalysisCurvature(null, null, null, null, null, null, tuple.Curvature) : null,
                            new AnalysisMetrics(parameters.IncludeGlobalMetrics ? curve.GetLength() : null, null, null),
                            parameters.IncludeDomains ? new Interval[] { data.Domain } : Array.Empty<Interval>(),
                            parameters.IncludeOrientation ? new AnalysisOrientation(new Vector3d[] { tuple.Frame.XAxis, tuple.Frame.YAxis }, tuple.Frame.ZAxis, tuple.Frame) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                            parameters with { CurveParameter = tuple.Parameter }))
                        .Map(packet => (IReadOnlyList<AnalysisPacket>)[packet]),
                    _ => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                }),
            Surface surface => ResultFactory.Create(value: (DomainU: surface.Domain(0), DomainV: surface.Domain(1), Requested: parameters.SurfaceParameters ?? (surface.Domain(0).ParameterAt(0.5), surface.Domain(1).ParameterAt(0.5)), Order: parameters.DerivativeOrder < 0 ? 0 : parameters.DerivativeOrder))
                .Validate(predicate: data => data.Order >= 0, error: AnalysisErrors.Parameters.InvalidDerivativeOrder)
                .Map(data => (data.DomainU, data.DomainV, Actual: (data.Requested.Item1 ?? data.DomainU.ParameterAt(0.5), data.Requested.Item2 ?? data.DomainV.ParameterAt(0.5)), Order: data.Order))
                .Validate(predicate: data => data.DomainU.IncludesParameter(data.Actual.Item1) && data.DomainV.IncludesParameter(data.Actual.Item2), error: AnalysisErrors.Parameters.ParameterOutOfDomain)
                .Bind(data => surface.Evaluate(data.Actual.Item1, data.Actual.Item2, data.Order is 0 ? 1 : data.Order, out Point3d location, out Vector3d[] derivatives) switch {
                    true => ResultFactory.Create(value: (Location: location, Derivatives: derivatives, U: data.Actual.Item1, V: data.Actual.Item2, Curvature: SurfaceCurvature.Compute(surface, data.Actual.Item1, data.Actual.Item2), Frame: surface.FrameAt(data.Actual.Item1, data.Actual.Item2, out Plane computed) ? computed : new Plane(location, surface.NormalAt(data.Actual.Item1, data.Actual.Item2))))
                        .Map(tuple => new AnalysisPacket(
                            tuple.Location,
                            Array.AsReadOnly<Vector3d>(tuple.Derivatives),
                            tuple.Frame,
                            tuple.Curvature is { IsValid: true } c ? new AnalysisCurvature(c.Gaussian, c.Mean, c.K1, c.K2, c.Direction1, c.Direction2, null) : null,
                            new AnalysisMetrics(null, parameters.IncludeGlobalMetrics ? surface switch { BrepFace face => AreaMassProperties.Compute(face)?.Area, _ => AreaMassProperties.Compute(surface)?.Area } : null, parameters.IncludeGlobalMetrics && surface is BrepFace faceVolume ? VolumeMassProperties.Compute(faceVolume.Brep)?.Volume : null),
                            parameters.IncludeDomains ? new Interval[] { surface.Domain(0), surface.Domain(1) } : Array.Empty<Interval>(),
                            parameters.IncludeOrientation ? new AnalysisOrientation(tuple.Derivatives is [Vector3d du, Vector3d dv, ..] ? new Vector3d[] { du, dv } : Array.Empty<Vector3d>(), tuple.Frame.ZAxis, tuple.Frame) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                            parameters with { SurfaceParameters = (tuple.U, tuple.V) }))
                        .Map(packet => (IReadOnlyList<AnalysisPacket>)[packet]),
                    _ => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                }),
            Brep brep => ResultFactory.Create(value: (brep, Index: parameters.MeshElementIndex ?? 0))
                .Validate(predicate: data => data.Index >= 0 && data.Index < data.brep.Faces.Count, error: AnalysisErrors.Parameters.InvalidMeshElement)
                .Bind(data => AnalyzeCore(brep.Faces[data.Index], context, parameters with { MeshElementIndex = data.Index })
                    .Map(packets => packets.Count == 0 ? packets : (IReadOnlyList<AnalysisPacket>)[packets[0] with {
                        Metrics = new AnalysisMetrics(
                            packets[0].Metrics.Length,
                            parameters.IncludeGlobalMetrics ? AreaMassProperties.Compute(brep)?.Area : packets[0].Metrics.Area,
                            parameters.IncludeGlobalMetrics ? VolumeMassProperties.Compute(brep)?.Volume : packets[0].Metrics.Volume),
                        EvaluatedParameters = packets[0].EvaluatedParameters with { SurfaceParameters = parameters.SurfaceParameters ?? packets[0].EvaluatedParameters.SurfaceParameters }
                    }])),
            SubD subd => ResultFactory.Create(value: (Requested: parameters.SurfaceParameters ?? (0.5, 0.5), Order: parameters.DerivativeOrder < 0 ? 0 : parameters.DerivativeOrder, Brep: parameters.IncludeGlobalMetrics ? subd.ToBrep() : null))
                .Validate(predicate: data => data.Order >= 0, error: AnalysisErrors.Parameters.InvalidDerivativeOrder)
                .Bind(data => _subdEvaluators.GetValue(subd, static geometry => geometry.CreateEvaluator()) switch {
                    SubDEvaluator evaluator when evaluator.Evaluate(data.Requested.Item1 ?? 0.5, data.Requested.Item2 ?? 0.5, data.Order is 0 ? 1 : data.Order, out Point3d location, out Vector3d[] derivatives) => ResultFactory.Create(value: (Location: location, Derivatives: derivatives, Parameters: (data.Requested.Item1 ?? 0.5, data.Requested.Item2 ?? 0.5), Normal: evaluator.Normal, Evaluator: evaluator, Brep: data.Brep))
                        .Map(tuple => new AnalysisPacket(
                            tuple.Location,
                            Array.AsReadOnly<Vector3d>(tuple.Derivatives),
                            new Plane(tuple.Location, tuple.Normal),
                            tuple.Evaluator.Curvature(tuple.Parameters.Item1, tuple.Parameters.Item2, out double k1, out double k2, out Vector3d dir1, out Vector3d dir2) ? new AnalysisCurvature(null, null, k1, k2, dir1, dir2, null) : null,
                            new AnalysisMetrics(null, tuple.Brep is Brep areaSource ? AreaMassProperties.Compute(areaSource)?.Area : null, tuple.Brep is Brep volumeSource ? VolumeMassProperties.Compute(volumeSource)?.Volume : null),
                            parameters.IncludeDomains ? new Interval[] { new Interval(0, 1), new Interval(0, 1) } : Array.Empty<Interval>(),
                            parameters.IncludeOrientation ? new AnalysisOrientation(tuple.Derivatives is [Vector3d du, Vector3d dv, ..] ? new Vector3d[] { du, dv } : Array.Empty<Vector3d>(), tuple.Normal, new Plane(tuple.Location, tuple.Normal)) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                            parameters with { SurfaceParameters = tuple.Parameters }))
                        .Map(packet => (IReadOnlyList<AnalysisPacket>)[packet]),
                    _ => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                }),
            Mesh mesh => ResultFactory.Create(value: parameters.MeshElementIndex ?? 0)
                .Validate(predicate: index => index >= 0 && index < mesh.Vertices.Count, error: AnalysisErrors.Parameters.InvalidMeshElement)
                .Map(index => (Index: index, Point: mesh.Vertices.Point3dAt(index), Normal: mesh.Normals.Count > index ? mesh.Normals[index] : Vector3d.Unset))
                .Map(tuple => tuple.Normal.IsValid ? tuple : tuple with { Normal = mesh.Normals.ComputeNormals() && mesh.Normals.Count > tuple.Index ? mesh.Normals[tuple.Index] : Vector3d.ZAxis })
                .Bind(tuple => ResultFactory.Create(value: _meshCurvatures.GetValue(mesh, static geometry => MeshCurvatureList.Compute(geometry) ?? new MeshCurvatureList()))
                    .Map(list => list.Count > tuple.Index ? list[tuple.Index] : null)
                    .Map(curvature => new AnalysisPacket(
                        tuple.Point,
                        (IReadOnlyList<Vector3d>)(tuple.Normal.IsValid ? new Vector3d[] { tuple.Normal } : Array.Empty<Vector3d>()),
                        new Plane(tuple.Point, tuple.Normal.IsValid ? tuple.Normal : Vector3d.ZAxis),
                        curvature is MeshCurvature entry ? new AnalysisCurvature(entry.Gaussian, entry.Mean, entry.Minimum, entry.Maximum, entry.MinimumDirection, entry.MaximumDirection, null) : null,
                        new AnalysisMetrics(null, parameters.IncludeGlobalMetrics ? AreaMassProperties.Compute(mesh)?.Area : null, parameters.IncludeGlobalMetrics ? VolumeMassProperties.Compute(mesh)?.Volume : null),
                        parameters.IncludeDomains ? new Interval[] { new Interval(0, mesh.Vertices.Count) } : Array.Empty<Interval>(),
                        parameters.IncludeOrientation ? new AnalysisOrientation(tuple.Normal.IsValid ? new Vector3d[] { tuple.Normal } : Array.Empty<Vector3d>(), tuple.Normal.IsValid ? tuple.Normal : null, new Plane(tuple.Point, tuple.Normal.IsValid ? tuple.Normal : Vector3d.ZAxis)) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                        parameters with { MeshElementIndex = tuple.Index }))
                    .Map(packet => (IReadOnlyList<AnalysisPacket>)[packet])),
            PointCloud cloud => cloud.Count switch {
                0 => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                _ => ResultFactory.Create(value: cloud.GetPoints())
                    .Map(points => (Points: points, Box: cloud.GetBoundingBox(true)))
                    .Map(tuple => (tuple.Points, Plane: Plane.FitPlaneToPoints(tuple.Points, out Plane fitted) ? fitted : new Plane(tuple.Box.Center, Vector3d.ZAxis), tuple.Box.Center))
                    .Map(tuple => new AnalysisPacket(
                        tuple.Center,
                        (IReadOnlyList<Vector3d>)Array.Empty<Vector3d>(),
                        new Plane(tuple.Center, tuple.Plane.XAxis, tuple.Plane.YAxis),
                        null,
                        new AnalysisMetrics(null, null, null),
                        parameters.IncludeDomains ? new Interval[] { new Interval(0, cloud.Count) } : Array.Empty<Interval>(),
                        parameters.IncludeOrientation ? new AnalysisOrientation(new Vector3d[] { tuple.Plane.XAxis, tuple.Plane.YAxis }, tuple.Plane.ZAxis, tuple.Plane) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                        parameters))
                    .Map(packet => (IReadOnlyList<AnalysisPacket>)[packet]),
            },
            Point3d[] points => points.Length switch {
                0 => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(value: Array.Empty<AnalysisPacket>()),
                _ => ResultFactory.Create(value: (points, Plane: Plane.FitPlaneToPoints(points, out Plane fitted) ? fitted : new Plane(points[0], Vector3d.ZAxis)))
                    .Map(tuple => (IReadOnlyList<AnalysisPacket>)Array.ConvertAll(tuple.points, point => new AnalysisPacket(
                        point,
                        (IReadOnlyList<Vector3d>)Array.Empty<Vector3d>(),
                        new Plane(point, tuple.Plane.XAxis, tuple.Plane.YAxis),
                        null,
                        new AnalysisMetrics(null, null, null),
                        parameters.IncludeDomains ? new Interval[] { new Interval(0, tuple.points.Length) } : Array.Empty<Interval>(),
                        parameters.IncludeOrientation ? new AnalysisOrientation(new Vector3d[] { tuple.Plane.XAxis, tuple.Plane.YAxis }, tuple.Plane.ZAxis, tuple.Plane) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null),
                        parameters)))
                    .Map(packets => (IReadOnlyList<AnalysisPacket>)packets),
            },
            Point3d point => ResultFactory.Create(value: (IReadOnlyList<AnalysisPacket>)[new AnalysisPacket(point, (IReadOnlyList<Vector3d>)Array.Empty<Vector3d>(), new Plane(point, Vector3d.XAxis, Vector3d.YAxis), null, new AnalysisMetrics(null, null, null), parameters.IncludeDomains ? new Interval[] { new Interval(0, 1) } : Array.Empty<Interval>(), parameters.IncludeOrientation ? new AnalysisOrientation(new Vector3d[] { Vector3d.XAxis, Vector3d.YAxis }, Vector3d.ZAxis, new Plane(point, Vector3d.XAxis, Vector3d.YAxis)) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null), parameters)]),
            Vector3d vector => ResultFactory.Create(value: (IReadOnlyList<AnalysisPacket>)[new AnalysisPacket(Point3d.Origin, (IReadOnlyList<Vector3d>)new Vector3d[] { vector }, new Plane(Point3d.Origin, vector), null, new AnalysisMetrics(null, null, null), Array.Empty<Interval>(), parameters.IncludeOrientation ? new AnalysisOrientation(new Vector3d[] { vector }, vector, new Plane(Point3d.Origin, vector)) : new AnalysisOrientation(Array.Empty<Vector3d>(), null, null), parameters)]),
            _ => ResultFactory.Create<IReadOnlyList<AnalysisPacket>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        };
}

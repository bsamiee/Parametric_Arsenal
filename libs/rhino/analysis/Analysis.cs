using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic differential geometry and quality analysis with unified algebraic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    /// <summary>Analysis result marker for polymorphic dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface pattern for polymorphic result dispatch")]
    public interface IResult;

    /// <summary>Base type for differential geometry analysis requests.</summary>
    public abstract record DifferentialRequest(GeometryBase Geometry, int DerivativeOrder);

    /// <summary>Base type for geometric quality analysis requests.</summary>
    public abstract record QualityRequest(GeometryBase Geometry);

    /// <summary>Extrusion differential analysis request (converts to Brep internally).</summary>
    public sealed record ExtrusionAnalysis(Extrusion Extrusion, int FaceIndex, double U, double V, Point3d TestPoint, int DerivativeOrder) : DifferentialRequest(Extrusion, DerivativeOrder);

    /// <summary>Batch differential analysis request.</summary>
    public sealed record BatchAnalysis<T>(IReadOnlyList<T> Geometries, double? Parameter, (double U, double V)? UV, int? Index, Point3d? TestPoint, int DerivativeOrder) : DifferentialRequest(default!, DerivativeOrder) where T : GeometryBase;

    /// <summary>Surface quality analysis request (curvature uniformity, singularities).</summary>
    public sealed record SurfaceQualityAnalysis(Surface Surface) : QualityRequest(Surface);

    /// <summary>Curve fairness analysis request (smoothness, inflections, bending energy).</summary>
    public sealed record CurveFairnessAnalysis(Curve Curve) : QualityRequest(Curve);

    /// <summary>Mesh quality analysis request (FEA metrics: aspect ratio, skewness, Jacobian).</summary>
    public sealed record MeshQualityAnalysis(Mesh Mesh) : QualityRequest(Mesh);

    /// <summary>Mesh topology analysis request.</summary>
    public sealed record MeshAnalysis(Mesh Mesh, int VertexIndex) : DifferentialRequest(Mesh, 0) {
        public MeshAnalysis(Mesh mesh) : this(mesh, 0) { }
    }

    /// <summary>Curve differential analysis request.</summary>
    public sealed record CurveAnalysis(Curve Curve, double Parameter, int DerivativeOrder) : DifferentialRequest(Curve, DerivativeOrder) {
        public CurveAnalysis(Curve curve, int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder)
            : this(curve, curve.Domain.Mid, derivativeOrder) { }
    }

    /// <summary>Surface differential analysis request.</summary>
    public sealed record SurfaceAnalysis(Surface Surface, double U, double V, int DerivativeOrder) : DifferentialRequest(Surface, DerivativeOrder) {
        public SurfaceAnalysis(Surface surface, int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder)
            : this(surface, surface.Domain(0).Mid, surface.Domain(1).Mid, derivativeOrder) { }
    }

    /// <summary>Brep surface and topology analysis request.</summary>
    public sealed record BrepAnalysis(Brep Brep, int FaceIndex, double U, double V, Point3d TestPoint, int DerivativeOrder) : DifferentialRequest(Brep, DerivativeOrder) {
        public BrepAnalysis(Brep brep, int faceIndex = 0, int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder)
            : this(
                brep,
                faceIndex,
                brep.Faces.Count > faceIndex ? brep.Faces[faceIndex].Domain(0).Mid : 0.5,
                brep.Faces.Count > faceIndex ? brep.Faces[faceIndex].Domain(1).Mid : 0.5,
                brep.GetBoundingBox(accurate: false).Center,
                derivativeOrder) { }
    }

    /// <summary>Surface quality metrics: curvature samples, singularity locations, uniformity score.</summary>
    [DebuggerDisplay("SurfaceQuality | Uniformity={UniformityScore:F3} | Singularities={SingularityLocations.Length}")]
    public sealed record SurfaceQualityResult(
        double[] GaussianCurvatures,
        double[] MeanCurvatures,
        (double U, double V)[] SingularityLocations,
        double UniformityScore) : IResult;

    /// <summary>Curve fairness metrics: smoothness score, curvature samples, inflection points, bending energy.</summary>
    [DebuggerDisplay("CurveFairness | Smoothness={SmoothnessScore:F3} | Inflections={InflectionPoints.Length} | Energy={BendingEnergy:F3}")]
    public sealed record CurveFairnessResult(
        double SmoothnessScore,
        double[] CurvatureValues,
        (double Parameter, bool IsSharp)[] InflectionPoints,
        double BendingEnergy) : IResult;

    /// <summary>Mesh quality metrics for FEA: aspect ratios, skewness, Jacobians, problematic faces, quality flags.</summary>
    [DebuggerDisplay("MeshQuality | Warnings={QualityFlags.Warning} | Critical={QualityFlags.Critical} | Problematic={ProblematicFaceIndices.Length}")]
    public sealed record MeshQualityResult(
        double[] AspectRatios,
        double[] Skewness,
        double[] Jacobians,
        int[] ProblematicFaceIndices,
        (int Warning, int Critical) QualityFlags) : IResult;

    /// <summary>Mesh topology: vertices, edges, manifold state, closure, area, volume.</summary>
    [DebuggerDisplay("Mesh @ {Location} | V={Volume:F3} | A={Area:F3}{(IsClosed ? \" [closed]\" : \"\")}{(IsManifold ? \" [manifold]\" : \"\")}")]
    public sealed record MeshData(
        Point3d Location,
        Plane Frame,
        Vector3d Normal,
        (int Index, Point3d Point)[] TopologyVertices,
        (int Index, Line Geometry)[] TopologyEdges,
        bool IsManifold,
        bool IsClosed,
        double Area,
        double Volume) : IResult;

    /// <summary>Curve differential geometry: derivatives, curvature, frames, discontinuities, length, centroid.</summary>
    [DebuggerDisplay("Curve @ {Location} | κ={Curvature:F3} | L={Length:F3} | Disc={DiscontinuityParameters.Length}")]
    public sealed record CurveData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Curvature,
        Plane Frame,
        Plane[] PerpendicularFrames,
        double Torsion,
        double[] DiscontinuityParameters,
        Continuity[] DiscontinuityTypes,
        double Length,
        Point3d Centroid) : IResult;

    /// <summary>Surface differential geometry: Gaussian/mean curvature, principal directions, singularities, area, centroid.</summary>
    [DebuggerDisplay("Surface @ {Location} | K={Gaussian:F3} | H={Mean:F3} | A={Area:F3}{(AtSingularity ? \" [singular]\" : \"\")}")]
    public sealed record SurfaceData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Gaussian,
        double Mean,
        double K1,
        double K2,
        Vector3d PrincipalDir1,
        Vector3d PrincipalDir2,
        Plane Frame,
        Vector3d Normal,
        bool AtSeam,
        bool AtSingularity,
        double Area,
        Point3d Centroid) : IResult;

    /// <summary>Brep topology: vertices, edges, manifold state, closest point, volume, area, centroid.</summary>
    [DebuggerDisplay("Brep @ {Location} | V={Volume:F3} | A={Area:F3}{(IsSolid ? \" [solid]\" : \"\")}{(IsManifold ? \" [manifold]\" : \"\")}")]
    public sealed record BrepData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Gaussian,
        double Mean,
        double K1,
        double K2,
        Vector3d PrincipalDir1,
        Vector3d PrincipalDir2,
        Plane Frame,
        Vector3d Normal,
        (int Index, Point3d Point)[] Vertices,
        (int Index, Line Geometry)[] Edges,
        bool IsManifold,
        bool IsSolid,
        Point3d ClosestPoint,
        double Distance,
        ComponentIndex Component,
        (double U, double V) SurfaceUV,
        double Area,
        double Volume,
        Point3d Centroid) : IResult;

    /// <summary>Analyzes surface quality via curvature sampling and singularity detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceQualityResult> AnalyzeSurfaceQuality(
        Surface surface,
        IGeometryContext context) =>
        AnalysisCore.ExecuteQuality<SurfaceQualityResult>(request: new SurfaceQualityAnalysis(Surface: surface), context: context);

    /// <summary>Analyzes curve fairness via curvature variation and inflection detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFairnessResult> AnalyzeCurveFairness(
        Curve curve,
        IGeometryContext context) =>
        AnalysisCore.ExecuteQuality<CurveFairnessResult>(request: new CurveFairnessAnalysis(Curve: curve), context: context);

    /// <summary>Analyzes mesh quality for FEA via aspect ratio, skewness, and Jacobian metrics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshQualityResult> AnalyzeMeshForFEA(
        Mesh mesh,
        IGeometryContext context) =>
        AnalysisCore.ExecuteQuality<MeshQualityResult>(request: new MeshQualityAnalysis(Mesh: mesh), context: context);

    /// <summary>Analyzes mesh topology and manifold properties at vertex.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(
        Mesh mesh,
        IGeometryContext context,
        int vertexIndex = 0) =>
        AnalysisCore.Execute<MeshData>(request: new MeshAnalysis(
            Mesh: mesh,
            VertexIndex: vertexIndex), context: context);

    /// <summary>Analyzes curve differential geometry at specified parameter.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        Curve curve,
        IGeometryContext context,
        double? parameter = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute<CurveData>(request: new CurveAnalysis(
            Curve: curve,
            Parameter: parameter ?? curve.Domain.Mid,
            DerivativeOrder: derivativeOrder), context: context);

    /// <summary>Analyzes surface differential geometry at specified UV parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(
        Surface surface,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute<SurfaceData>(request: new SurfaceAnalysis(
            Surface: surface,
            U: uvParameter?.u ?? surface.Domain(0).Mid,
            V: uvParameter?.v ?? surface.Domain(1).Mid,
            DerivativeOrder: derivativeOrder), context: context);

    /// <summary>Analyzes Brep surface, topology, and proximity to test point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(
        Brep brep,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int faceIndex = 0,
        Point3d? testPoint = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute<BrepData>(request: new BrepAnalysis(
            Brep: brep,
            FaceIndex: faceIndex,
            U: uvParameter?.u ?? (brep.Faces.Count > faceIndex ? brep.Faces[faceIndex].Domain(0).Mid : 0.5),
            V: uvParameter?.v ?? (brep.Faces.Count > faceIndex ? brep.Faces[faceIndex].Domain(1).Mid : 0.5),
            TestPoint: testPoint ?? brep.GetBoundingBox(accurate: false).Center,
            DerivativeOrder: derivativeOrder), context: context);

    /// <summary>Batch analysis for multiple geometry instances with unified error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context,
        double? parameter = null,
        (double u, double v)? uvParameter = null,
        int? index = null,
        Point3d? testPoint = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) where T : GeometryBase =>
        AnalysisCore.ExecuteBatch(
            geometries: geometries,
            context: context,
            parameter: parameter,
            uv: uvParameter,
            index: index,
            testPoint: testPoint,
            derivativeOrder: derivativeOrder);
}

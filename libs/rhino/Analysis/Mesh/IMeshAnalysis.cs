using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Analysis.Mesh;

/// <summary>Mesh analysis operations.</summary>
public interface IMeshAnalysis
{
    /// <summary>Analyzes mesh face planarity.</summary>
    Result<PlanarityReport> Planarity(global::Rhino.Geometry.Mesh mesh, GeoContext context);

    /// <summary>Computes mesh quality metrics.</summary>
    Result<MeshMetrics> Metrics(global::Rhino.Geometry.Mesh mesh, GeoContext context);

    /// <summary>Validates mesh integrity.</summary>
    Result<MeshValidation> Validate(global::Rhino.Geometry.Mesh mesh, GeoContext context);
}

using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Analysis.Mesh;

/// <summary>Mesh quality metrics and validation operations.</summary>
public interface IMeshAnalysis
{
    /// <summary>Analyzes mesh planarity and returns planarity report.</summary>
    /// <param name="mesh">The mesh to analyze for planarity.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the planarity report or a failure.</returns>
    Result<PlanarityReport> Planarity(global::Rhino.Geometry.Mesh mesh, GeoContext context);

    /// <summary>Calculates mesh quality metrics.</summary>
    /// <param name="mesh">The mesh to calculate metrics for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the mesh metrics or a failure.</returns>
    Result<MeshMetrics> Metrics(global::Rhino.Geometry.Mesh mesh, GeoContext context);

    /// <summary>Validates mesh integrity and quality.</summary>
    /// <param name="mesh">The mesh to validate.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the validation report or a failure.</returns>
    Result<MeshValidation> Validate(global::Rhino.Geometry.Mesh mesh, GeoContext context);
}

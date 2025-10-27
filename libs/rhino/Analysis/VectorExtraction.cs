using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Arsenal.Rhino.Geometry;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Main extraction orchestrator for directional vectors from geometry using existing GeometryTraversal pattern.</summary>
public static class VectorExtraction
{
    /// <summary>
    /// Extracts all directional vectors from geometry using GeometryTraversal.Extract pattern.
    /// Supports transformation, filtering, and deduplication through function composition.
    /// </summary>
    public static Result<VectorExtractionResult[]> ExtractVectors(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract<VectorExtractionResult, VectorExtractionResult>(
            geometry,
            GetVectorExtractionAdapter);
    }

    /// <summary>
    /// Extracts tangent vectors from geometry using GeometryTraversal.Extract with filtering pipeline.
    /// </summary>
    public static Result<Vector3d[]> ExtractTangentVectors(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract(
            geometry,
            GetVectorExtractionAdapter,
            pipeline: results => results
                .Where(r => r.TangentVector.HasValue)
                .Select(r => r.TangentVector!.Value));
    }

    /// <summary>
    /// Extracts normal vectors from geometry using GeometryTraversal.Extract with filtering pipeline.
    /// </summary>
    public static Result<Vector3d[]> ExtractNormalVectors(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract(
            geometry,
            GetVectorExtractionAdapter,
            pipeline: results => results
                .Where(r => r.NormalVector.HasValue)
                .Select(r => r.NormalVector!.Value));
    }

    /// <summary>
    /// Extracts U direction vectors from surfaces using GeometryTraversal.Extract with filtering pipeline.
    /// </summary>
    public static Result<Vector3d[]> ExtractUDirectionVectors(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract(
            geometry,
            GetVectorExtractionAdapter,
            pipeline: results => results
                .Where(r => r.UDirection.HasValue)
                .Select(r => r.UDirection!.Value));
    }

    /// <summary>
    /// Extracts V direction vectors from surfaces using GeometryTraversal.Extract with filtering pipeline.
    /// </summary>
    public static Result<Vector3d[]> ExtractVDirectionVectors(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract(
            geometry,
            GetVectorExtractionAdapter,
            pipeline: results => results
                .Where(r => r.VDirection.HasValue)
                .Select(r => r.VDirection!.Value));
    }

    /// <summary>
    /// Extracts extraction points from geometry using GeometryTraversal.Extract with filtering pipeline.
    /// </summary>
    public static Result<Point3d[]> ExtractVectorPoints(GeometryBase? geometry)
    {
        return GeometryTraversal.Extract(
            geometry,
            GetVectorExtractionAdapter,
            pipeline: results => results.Select(r => r.ExtractionPoint));
    }

    /// <summary>
    /// Geometry adapter that routes to appropriate vector extraction method based on geometry type.
    /// Follows existing GeometryAdapters pattern for geometry type switching.
    /// </summary>
    private static VectorExtractionResult[] GetVectorExtractionAdapter(GeometryBase geometry)
    {
        Result<VectorExtractionResult[]> result = geometry switch
        {
            // Derived surface types - must come before Surface base class
            Brep brep => ExtractBrepVectors(brep),
            BrepFace brepFace => VectorAdapters.GetBrepFaceVectors(brepFace),
            Extrusion extrusion => VectorAdapters.GetExtrusionVectors(extrusion),
            NurbsSurface nurbsSurface => VectorAdapters.GetSurfaceVectors(nurbsSurface),

            // Base surface type - catches remaining Surface types
            Surface surface => VectorAdapters.GetSurfaceVectors(surface),

            // Curve geometry - use SDK TangentAt methods (includes all curve types)
            Curve curve => VectorAdapters.GetCurveVectors(curve),

            // Mesh geometry - use existing normal computation pattern
            Mesh mesh => VectorAdapters.GetMeshVectors(mesh),

            // SubD geometry - convert to Brep and extract face normals
            SubD subd => ExtractSubDVectors(subd),

            // Geometry with no meaningful directional information
            Point => Result<VectorExtractionResult[]>.Success([]),
            Hatch => Result<VectorExtractionResult[]>.Success([]),
            TextEntity => Result<VectorExtractionResult[]>.Success([]),
            Dimension => Result<VectorExtractionResult[]>.Success([]),

            // Unknown geometry types
            _ => Result<VectorExtractionResult[]>.Fail($"Unsupported geometry type: {geometry.GetType().Name}")
        };

        // Return empty array on failure to allow GeometryTraversal to continue processing
        return result.Ok ? result.Value! : [];
    }

    /// <summary>Extracts vectors from all faces of a Brep using existing BrepFace pattern.</summary>
    private static Result<VectorExtractionResult[]> ExtractBrepVectors(Brep brep)
    {
        Result<Brep> brepValidation = Guard.RequireNonNull(brep, nameof(brep));
        if (!brepValidation.Ok)
        {
            return Result<VectorExtractionResult[]>.Fail(brepValidation.Error!);
        }

        if (!brep.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Brep is not valid");
        }

        try
        {
            List<VectorExtractionResult> allResults = [];

            // Extract vectors from each face using existing BrepFace pattern
            foreach (BrepFace face in brep.Faces)
            {
                Result<VectorExtractionResult[]> faceResult = VectorAdapters.GetBrepFaceVectors(face);
                if (faceResult is { Ok: true, Value.Length: > 0 })
                {
                    allResults.AddRange(faceResult.Value);
                }
            }

            if (allResults.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("No valid vectors could be extracted from Brep faces");
            }

            return Result<VectorExtractionResult[]>.Success([.. allResults]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Brep vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts vectors from SubD by converting to Brep and extracting face normals.</summary>
    private static Result<VectorExtractionResult[]> ExtractSubDVectors(SubD subd)
    {
        Result<SubD> subdValidation = Guard.RequireNonNull(subd, nameof(subd));
        if (!subdValidation.Ok)
        {
            return Result<VectorExtractionResult[]>.Fail(subdValidation.Error!);
        }

        if (!subd.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("SubD is not valid");
        }

        try
        {
            // Convert SubD to Brep to access face information
            Brep? subdBrep = subd.ToBrep();
            if (subdBrep == null || !subdBrep.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert SubD to Brep");
            }

            // Use existing Brep extraction pattern
            return ExtractBrepVectors(subdBrep);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"SubD vector extraction failed: {ex.Message}");
        }
    }
}

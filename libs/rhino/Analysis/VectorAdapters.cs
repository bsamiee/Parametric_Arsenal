using System;
using System.Collections.Generic;
using Arsenal.Core;
using Arsenal.Rhino.Surfaces;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Geometry-specific vector extraction adapters following existing GeometryAdapters pattern.</summary>
public static class VectorAdapters
{
    /// <summary>Extracts directional vectors from Surface geometry using existing SurfaceEvaluationResult pattern.</summary>
    public static Result<VectorExtractionResult[]> GetSurfaceVectors(Surface surface)
    {
        Guard.RequireNonNull(surface, nameof(surface));

        if (!surface.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Surface is not valid");
        }

        try
        {
            // Use domain center extraction points following existing domain handling patterns
            Interval uDomain = surface.Domain(0);
            Interval vDomain = surface.Domain(1);
            double u = uDomain.Mid;
            double v = vDomain.Mid;

            // Leverage existing SurfaceClosestPoint.EvaluateAt() pattern
            Result<SurfaceEvaluationResult> evaluationResult = SurfaceClosestPoint.EvaluateAt(surface, u, v);
            if (!evaluationResult.Ok)
            {
                return Result<VectorExtractionResult[]>.Fail($"Surface evaluation failed: {evaluationResult.Error}");
            }

            SurfaceEvaluationResult evaluation = evaluationResult.Value;

            // Create VectorExtractionResult using existing SurfaceEvaluationResult data
            VectorExtractionResult result = new(
                ExtractionPoint: evaluation.Point,
                TangentVector: null, // Surfaces don't have single tangent vectors
                NormalVector: evaluation.Normal,
                UDirection: evaluation.TangentU,
                VDirection: evaluation.TangentV
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Surface vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts directional vectors from BrepFace geometry using BrepFace.NormalAt() and BrepFace.Evaluate().</summary>
    public static Result<VectorExtractionResult[]> GetBrepFaceVectors(BrepFace brepFace)
    {
        Guard.RequireNonNull(brepFace, nameof(brepFace));

        if (!brepFace.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("BrepFace is not valid");
        }

        try
        {
            // Use domain center extraction points following existing domain handling patterns
            Interval uDomain = brepFace.Domain(0);
            Interval vDomain = brepFace.Domain(1);
            double u = uDomain.Mid;
            double v = vDomain.Mid;

            // Validate parameters are within domain
            if (!uDomain.IncludesParameter(u) || !vDomain.IncludesParameter(v))
            {
                return Result<VectorExtractionResult[]>.Fail("Domain center parameters are outside BrepFace domain");
            }

            // Use BrepFace.NormalAt() for orientation-correct normals
            Vector3d normal = brepFace.NormalAt(u, v);

            // Use BrepFace.Evaluate() for U/V directions
            bool evaluateSuccess = brepFace.Evaluate(u, v, 1, out Point3d point, out Vector3d[] derivatives);
            if (!evaluateSuccess || derivatives.Length < 2)
            {
                return Result<VectorExtractionResult[]>.Fail("BrepFace evaluation failed to compute derivatives");
            }

            Vector3d tangentU = derivatives[0]; // First derivative in U direction
            Vector3d tangentV = derivatives[1]; // First derivative in V direction

            // Validate vectors
            if (!normal.IsValid || !tangentU.IsValid || !tangentV.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Invalid vectors computed from BrepFace evaluation");
            }

            VectorExtractionResult result = new(
                ExtractionPoint: point,
                TangentVector: null, // BrepFaces don't have single tangent vectors
                NormalVector: normal,
                UDirection: tangentU,
                VDirection: tangentV
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"BrepFace vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts tangent vectors from Curve geometry using SDK TangentAt() at domain midpoint.</summary>
    public static Result<VectorExtractionResult[]> GetCurveVectors(Curve curve)
    {
        Result<Curve> curveValidation = Guard.RequireNonNull(curve, nameof(curve));
        if (!curveValidation.Ok)
        {
            return Result<VectorExtractionResult[]>.Fail(curveValidation.Error!);
        }

        Result curveStateValidation = ValidateCurveState(curve);
        if (!curveStateValidation.Ok)
        {
            return Result<VectorExtractionResult[]>.Fail(curveStateValidation.Error!);
        }

        try
        {
            // Handle PolyCurve segment-wise tangent extraction
            if (curve is PolyCurve polyCurve)
            {
                return GetPolyCurveVectors(polyCurve);
            }

            // For all other curve types, extract tangent at domain midpoint
            Interval domain = curve.Domain;
            double midParameter = domain.Mid;

            Point3d extractionPoint = curve.PointAt(midParameter);
            Vector3d tangentVector = curve.TangentAt(midParameter);

            // Validate results
            if (!extractionPoint.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Invalid extraction point computed from curve");
            }

            if (!tangentVector.IsValid || tangentVector.Length <= RhinoMath.ZeroTolerance)
            {
                return Result<VectorExtractionResult[]>.Fail(
                    "Invalid or zero-length tangent vector computed from curve");
            }

            VectorExtractionResult result = new(
                ExtractionPoint: extractionPoint,
                TangentVector: tangentVector,
                NormalVector: null, // Curves don't have normal vectors
                UDirection: null, // Curves don't have U direction
                VDirection: null // Curves don't have V direction
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Curve vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts tangent vectors from PolyCurve segments at each segment midpoint.</summary>
    private static Result<VectorExtractionResult[]> GetPolyCurveVectors(PolyCurve polyCurve)
    {
        try
        {
            List<VectorExtractionResult> results = [];

            for (int i = 0; i < polyCurve.SegmentCount; i++)
            {
                Curve? segment = polyCurve.SegmentCurve(i);
                if (segment == null)
                {
                    continue; // Skip invalid segments
                }

                // Validate segment state
                Result segmentValidation = ValidateCurveState(segment);
                if (!segmentValidation.Ok)
                {
                    continue; // Skip invalid segments and continue processing
                }

                // Get segment domain and compute midpoint parameter
                Interval segmentDomain = segment.Domain;
                double midParameter = segmentDomain.Mid;

                Point3d extractionPoint = segment.PointAt(midParameter);
                Vector3d tangentVector = segment.TangentAt(midParameter);

                // Validate segment results
                if (!extractionPoint.IsValid || !tangentVector.IsValid ||
                    tangentVector.Length <= RhinoMath.ZeroTolerance)
                {
                    continue; // Skip invalid results and continue processing
                }

                VectorExtractionResult result = new(
                    ExtractionPoint: extractionPoint,
                    TangentVector: tangentVector,
                    NormalVector: null, // Curves don't have normal vectors
                    UDirection: null, // Curves don't have U direction
                    VDirection: null // Curves don't have V direction
                );

                results.Add(result);
            }

            if (results.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail(
                    "No valid tangent vectors could be extracted from PolyCurve segments");
            }

            return Result<VectorExtractionResult[]>.Success([.. results]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"PolyCurve vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Mesh geometry using exact same mesh.FaceNormals.ComputeFaceNormals() pattern.</summary>
    public static Result<VectorExtractionResult[]> GetMeshVectors(Mesh mesh)
    {
        Guard.RequireNonNull(mesh, nameof(mesh));

        if (!mesh.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Mesh is not valid");
        }

        try
        {
            // Ensure mesh has face normals using exact same pattern as Intersect.cs
            if (mesh.FaceNormals.Count == 0)
            {
                mesh.FaceNormals.ComputeFaceNormals();
            }

            List<VectorExtractionResult> results = [];

            // Iterate through mesh faces and extract normals at face centers
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                MeshFace face = mesh.Faces[i];
                Vector3f faceNormal = mesh.FaceNormals[i];

                // Validate face normal
                if (!faceNormal.IsValid || faceNormal.Length <= RhinoMath.ZeroTolerance)
                {
                    continue; // Skip invalid normals and continue processing
                }

                // Calculate face center (centroid) for extraction point
                Point3d faceCenter = CalculateFaceCenter(mesh, face);
                if (!faceCenter.IsValid)
                {
                    continue; // Skip invalid face centers and continue processing
                }

                // Convert Vector3f to Vector3d for consistency with other vector types
                Vector3d normalVector = new(faceNormal.X, faceNormal.Y, faceNormal.Z);

                VectorExtractionResult result = new(
                    ExtractionPoint: faceCenter,
                    TangentVector: null, // Meshes don't have tangent vectors
                    NormalVector: normalVector,
                    UDirection: null, // Meshes don't have U direction
                    VDirection: null // Meshes don't have V direction
                );

                results.Add(result);
            }

            if (results.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("No valid face normals could be extracted from mesh");
            }

            return Result<VectorExtractionResult[]>.Success([.. results]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Mesh vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Calculates face center (centroid) for mesh faces, handling both triangles and quads.</summary>
    private static Point3d CalculateFaceCenter(Mesh mesh, MeshFace face)
    {
        // Get face vertices
        Point3f v0 = mesh.Vertices[face.A];
        Point3f v1 = mesh.Vertices[face.B];
        Point3f v2 = mesh.Vertices[face.C];

        if (face.IsQuad)
        {
            // Quad face - average of 4 vertices
            Point3f v3 = mesh.Vertices[face.D];
            return new Point3d(
                (v0.X + v1.X + v2.X + v3.X) / 4.0,
                (v0.Y + v1.Y + v2.Y + v3.Y) / 4.0,
                (v0.Z + v1.Z + v2.Z + v3.Z) / 4.0
            );
        }
        else
        {
            // Triangle face - average of 3 vertices
            return new Point3d(
                (v0.X + v1.X + v2.X) / 3.0,
                (v0.Y + v1.Y + v2.Y) / 3.0,
                (v0.Z + v1.Z + v2.Z) / 3.0
            );
        }
    }

    /// <summary>Extracts direction vector from Line geometry using Line.Direction property.</summary>
    public static Result<VectorExtractionResult[]> GetLineVectors(Line line)
    {
        if (!line.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Line is not valid");
        }

        try
        {
            // Use Line.Direction property which returns vector from Line.From to Line.To
            Vector3d directionVector = line.Direction;

            // Validate direction vector
            if (!directionVector.IsValid || directionVector.Length <= RhinoMath.ZeroTolerance)
            {
                return Result<VectorExtractionResult[]>.Fail("Invalid or zero-length direction vector from line");
            }

            // Extract at line midpoint for consistent positioning
            Point3d extractionPoint = line.PointAt(0.5);

            VectorExtractionResult result = new(
                ExtractionPoint: extractionPoint,
                TangentVector: directionVector, // Line direction serves as tangent
                NormalVector: null, // Lines don't have normal vectors
                UDirection: null, // Lines don't have U direction
                VDirection: null // Lines don't have V direction
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Line vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts direction vector from Extrusion geometry using PathStart and PathEnd points.</summary>
    public static Result<VectorExtractionResult[]> GetExtrusionVectors(Extrusion extrusion)
    {
        Guard.RequireNonNull(extrusion, nameof(extrusion));

        if (!extrusion.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Extrusion is not valid");
        }

        try
        {
            // Compute direction vector from PathStart to PathEnd since no Direction property exists
            Vector3d directionVector = extrusion.PathEnd - extrusion.PathStart;

            // Validate direction vector
            if (!directionVector.IsValid || directionVector.Length <= RhinoMath.ZeroTolerance)
            {
                return Result<VectorExtractionResult[]>.Fail(
                    "Invalid or zero-length direction vector from extrusion path");
            }

            // Extract at path midpoint for consistent positioning
            Point3d extractionPoint = (extrusion.PathStart + extrusion.PathEnd) / 2.0;

            VectorExtractionResult result = new(
                ExtractionPoint: extractionPoint,
                TangentVector: directionVector, // Extrusion direction serves as tangent
                NormalVector: null, // Extrusion path doesn't have a single normal
                UDirection: null, // Extrusions don't have U direction
                VDirection: null // Extrusions don't have V direction
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Extrusion vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vector from Plane geometry using Plane.Normal property.</summary>
    public static Result<VectorExtractionResult[]> GetPlaneVectors(Plane plane)
    {
        if (!plane.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Plane is not valid");
        }

        try
        {
            // Use Plane.Normal property which returns unit normal vector
            Vector3d normalVector = plane.Normal;

            // Validate normal vector
            if (!normalVector.IsValid || normalVector.Length <= RhinoMath.ZeroTolerance)
            {
                return Result<VectorExtractionResult[]>.Fail("Invalid or zero-length normal vector from plane");
            }

            // Extract at plane origin for consistent positioning
            Point3d extractionPoint = plane.Origin;

            VectorExtractionResult result = new(
                ExtractionPoint: extractionPoint,
                TangentVector: null, // Planes don't have tangent vectors
                NormalVector: normalVector,
                UDirection: plane.XAxis, // Plane X axis serves as U direction
                VDirection: plane.YAxis // Plane Y axis serves as V direction
            );

            return Result<VectorExtractionResult[]>.Success([result]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Plane vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Box geometry by converting to Brep and extracting face normals.</summary>
    public static Result<VectorExtractionResult[]> GetBoxVectors(Box box)
    {
        if (!box.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Box is not valid");
        }

        try
        {
            // Convert box to Brep to access face information
            Brep? boxBrep = box.ToBrep();
            if (boxBrep == null || !boxBrep.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert box to Brep");
            }

            List<VectorExtractionResult> results = [];

            // Extract normal from each face using existing BrepFace pattern
            foreach (BrepFace face in boxBrep.Faces)
            {
                Result<VectorExtractionResult[]> faceResult = GetBrepFaceVectors(face);
                if (faceResult is { Ok: true, Value.Length: > 0 })
                {
                    results.AddRange(faceResult.Value);
                }
            }

            if (results.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("No valid face normals could be extracted from box");
            }

            return Result<VectorExtractionResult[]>.Success([.. results]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Box vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Sphere geometry by converting to Brep and extracting surface normals.</summary>
    public static Result<VectorExtractionResult[]> GetSphereVectors(Sphere sphere)
    {
        if (!sphere.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Sphere is not valid");
        }

        try
        {
            // Convert sphere to Brep to access surface information
            Brep? sphereBrep = sphere.ToBrep();
            if (sphereBrep == null || !sphereBrep.IsValid || sphereBrep.Faces.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert sphere to Brep or no faces found");
            }

            // Extract normal from the sphere surface at center point using existing BrepFace pattern
            BrepFace sphereFace = sphereBrep.Faces[0]; // Sphere has one face
            return GetBrepFaceVectors(sphereFace);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Sphere vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Cylinder geometry by converting to Brep and extracting face normals.</summary>
    public static Result<VectorExtractionResult[]> GetCylinderVectors(Cylinder cylinder)
    {
        if (!cylinder.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Cylinder is not valid");
        }

        try
        {
            // Convert cylinder to Brep to access face information
            Brep? cylinderBrep = cylinder.ToBrep(true, true); // Include caps
            if (cylinderBrep == null || !cylinderBrep.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert cylinder to Brep");
            }

            List<VectorExtractionResult> results = [];

            // Extract normal from each face using existing BrepFace pattern
            foreach (BrepFace face in cylinderBrep.Faces)
            {
                Result<VectorExtractionResult[]> faceResult = GetBrepFaceVectors(face);
                if (faceResult is { Ok: true, Value.Length: > 0 })
                {
                    results.AddRange(faceResult.Value);
                }
            }

            if (results.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("No valid face normals could be extracted from cylinder");
            }

            return Result<VectorExtractionResult[]>.Success([.. results]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Cylinder vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Cone geometry by converting to Brep and extracting face normals.</summary>
    public static Result<VectorExtractionResult[]> GetConeVectors(Cone cone)
    {
        if (!cone.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Cone is not valid");
        }

        try
        {
            // Convert cone to Brep to access face information
            Brep? coneBrep = cone.ToBrep(true); // Include cap
            if (coneBrep == null || !coneBrep.IsValid)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert cone to Brep");
            }

            List<VectorExtractionResult> results = [];

            // Extract normal from each face using existing BrepFace pattern
            foreach (BrepFace face in coneBrep.Faces)
            {
                Result<VectorExtractionResult[]> faceResult = GetBrepFaceVectors(face);
                if (faceResult is { Ok: true, Value.Length: > 0 })
                {
                    results.AddRange(faceResult.Value);
                }
            }

            if (results.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("No valid face normals could be extracted from cone");
            }

            return Result<VectorExtractionResult[]>.Success([.. results]);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Cone vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts normal vectors from Torus geometry by converting to Brep and extracting surface normals.</summary>
    public static Result<VectorExtractionResult[]> GetTorusVectors(Torus torus)
    {
        if (!torus.IsValid)
        {
            return Result<VectorExtractionResult[]>.Fail("Torus is not valid");
        }

        try
        {
            // Convert torus to Brep to access surface information
            Brep? torusBrep = torus.ToBrep();
            if (torusBrep == null || !torusBrep.IsValid || torusBrep.Faces.Count == 0)
            {
                return Result<VectorExtractionResult[]>.Fail("Failed to convert torus to Brep or no faces found");
            }

            // Extract normal from the torus surface at center point using existing BrepFace pattern
            BrepFace torusFace = torusBrep.Faces[0]; // Torus has one face
            return GetBrepFaceVectors(torusFace);
        }
        catch (Exception ex)
        {
            return Result<VectorExtractionResult[]>.Fail($"Torus vector extraction failed: {ex.Message}");
        }
    }

    /// <summary>Validates curve state using comprehensive RhinoCommon validation methods following existing patterns.</summary>
    private static Result ValidateCurveState(Curve curve)
    {
        if (!curve.IsValid)
        {
            return Result.Fail("Curve is not valid");
        }

        if (!curve.Domain.IsIncreasing)
        {
            return Result.Fail("Curve domain is degenerate or invalid");
        }

        return Result.Success();
    }
}

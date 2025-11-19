using System;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Configuration constants for fields operations.</summary>
[Pure]
internal static class FieldsConfig {
    /// <summary>Distance field metadata containing validation mode, operation name, and buffer size.</summary>
    internal sealed record DistanceFieldMetadata(
        V ValidationMode,
        string DistanceOperationName,
        string GradientOperationName,
        int BufferSize);

    internal sealed record FieldOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Distance field configuration by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, DistanceFieldMetadata> DistanceFields =
        new Dictionary<Type, DistanceFieldMetadata> {
            [typeof(Mesh)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                DistanceOperationName: "Fields.MeshDistance",
                GradientOperationName: "Fields.MeshDistanceGradient",
                BufferSize: 4096),
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology,
                DistanceOperationName: "Fields.BrepDistance",
                GradientOperationName: "Fields.BrepDistanceGradient",
                BufferSize: 8192),
            [typeof(Curve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                DistanceOperationName: "Fields.CurveDistance",
                GradientOperationName: "Fields.CurveDistanceGradient",
                BufferSize: 2048),
            [typeof(Surface)] = new(
                ValidationMode: V.Standard | V.BoundingBox,
                DistanceOperationName: "Fields.SurfaceDistance",
                GradientOperationName: "Fields.SurfaceDistanceGradient",
                BufferSize: 4096),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<Type, FieldOperationMetadata> Operations =
        new Dictionary<Type, FieldOperationMetadata> {
            [typeof(Fields.CurlFieldRequest)] = new(V.None, "Fields.VectorField.Curl"),
            [typeof(Fields.DivergenceFieldRequest)] = new(V.None, "Fields.VectorField.Divergence"),
            [typeof(Fields.LaplacianFieldRequest)] = new(V.None, "Fields.ScalarField.Laplacian"),
            [typeof(Fields.VectorPotentialFieldRequest)] = new(V.None, "Fields.VectorField.VectorPotential"),
            [typeof(Fields.ScalarInterpolationRequest)] = new(V.None, "Fields.Interpolation.Scalar"),
            [typeof(Fields.VectorInterpolationRequest)] = new(V.None, "Fields.Interpolation.Vector"),
            [typeof(Fields.StreamlineRequest)] = new(V.None, "Fields.Streamlines"),
            [typeof(Fields.IsosurfaceRequest)] = new(V.None, "Fields.Isosurfaces"),
            [typeof(Fields.HessianFieldRequest)] = new(V.None, "Fields.ScalarField.Hessian"),
            [typeof(Fields.DirectionalDerivativeRequest)] = new(V.None, "Fields.VectorField.DirectionalDerivative"),
            [typeof(Fields.FieldMagnitudeRequest)] = new(V.None, "Fields.VectorField.Magnitude"),
            [typeof(Fields.NormalizeFieldRequest)] = new(V.None, "Fields.VectorField.Normalize"),
            [typeof(Fields.ScalarVectorProductRequest)] = new(V.None, "Fields.FieldComposition.ScalarVectorProduct"),
            [typeof(Fields.VectorDotProductRequest)] = new(V.None, "Fields.FieldComposition.VectorDotProduct"),
            [typeof(Fields.CriticalPointsRequest)] = new(V.None, "Fields.CriticalPoints"),
            [typeof(Fields.FieldStatisticsRequest)] = new(V.None, "Fields.FieldStatistics"),
        }.ToFrozenDictionary();

    /// <summary>Field sampling resolution limits: default 32, range [8, 256].</summary>
    internal const int DefaultResolution = 32;
    internal const int MinResolution = 8;
    internal const int MaxResolution = 256;

    /// <summary>Integration step size parameters: default 0.01, range [√ε, 1.0].</summary>
    internal const double DefaultStepSize = 0.01;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double MinStepSize = RhinoMath.SqrtEpsilon;
    internal const double MaxStepSize = 1.0;

    /// <summary>Streamline integration limits: max 10000 steps, min field magnitude 1e-10.</summary>
    internal const int MaxStreamlineSteps = 10000;
    internal const double MinFieldMagnitude = 1e-10;

    /// <summary>Runge-Kutta integration weights and step coefficients.</summary>
    internal static readonly double[] RK4Weights = [1.0 / 6.0, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 6.0,];
    internal static readonly double[] RK4HalfSteps = [0.5, 0.5, 1.0,];
    internal const double RK2HalfStep = 0.5;

    /// <summary>Vector potential solver: 512 max iterations, √ε convergence tolerance.</summary>
    internal const int VectorPotentialIterations = 512;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double VectorPotentialTolerance = RhinoMath.SqrtEpsilon;

    /// <summary>Detection thresholds for spatial queries and eigenvalue classification.</summary>
    internal const int FieldRTreeThreshold = 100;
    internal const double InsideOutsideToleranceMultiplier = 10.0;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double EigenvalueThreshold = RhinoMath.SqrtEpsilon;

    internal static readonly (int V1, int V2)[] EdgeVertexPairs = [
        (0, 1),
        (1, 2),
        (2, 3),
        (3, 0),
        (4, 5),
        (5, 6),
        (6, 7),
        (7, 4),
        (0, 4),
        (1, 5),
        (2, 6),
        (3, 7),
    ];

    internal static readonly int[][] MarchingCubesTable = GenerateMarchingCubesTable();

    private static int[][] GenerateMarchingCubesTable() {
        int[][] table = new int[256][];
        table[0] = [];
        table[1] = [0, 8, 3,];
        table[2] = [0, 1, 9,];
        table[3] = [1, 8, 3, 9, 8, 1,];
        table[4] = [1, 2, 10,];
        table[5] = [0, 8, 3, 1, 2, 10,];
        table[6] = [9, 2, 10, 0, 2, 9,];
        table[7] = [2, 8, 3, 2, 10, 8, 10, 9, 8,];
        table[8] = [3, 11, 2,];
        table[9] = [0, 11, 2, 8, 11, 0,];
        table[10] = [1, 9, 0, 2, 3, 11,];
        table[11] = [1, 11, 2, 1, 9, 11, 9, 8, 11,];
        table[12] = [3, 10, 1, 11, 10, 3,];
        table[13] = [0, 10, 1, 0, 8, 10, 8, 11, 10,];
        table[14] = [3, 9, 0, 3, 11, 9, 11, 10, 9,];
        table[15] = [9, 8, 10, 10, 8, 11,];
        table[16] = [4, 7, 8,];
        table[17] = [4, 3, 0, 7, 3, 4,];
        table[18] = [0, 1, 9, 8, 4, 7,];
        table[19] = [4, 1, 9, 4, 7, 1, 7, 3, 1,];
        table[20] = [1, 2, 10, 8, 4, 7,];
        table[21] = [3, 4, 7, 3, 0, 4, 1, 2, 10,];
        table[22] = [9, 2, 10, 9, 0, 2, 8, 4, 7,];
        table[23] = [2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4,];
        table[24] = [8, 4, 7, 3, 11, 2,];
        table[25] = [11, 4, 7, 11, 2, 4, 2, 0, 4,];
        table[26] = [9, 0, 1, 8, 4, 7, 2, 3, 11,];
        table[27] = [4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1,];
        table[28] = [3, 10, 1, 3, 11, 10, 7, 8, 4,];
        table[29] = [1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4,];
        table[30] = [4, 7, 8, 9, 0, 11, 9, 11, 10, 11, 0, 3,];
        table[31] = [4, 7, 11, 4, 11, 9, 9, 11, 10,];
        table[32] = [9, 5, 4,];
        table[33] = [9, 5, 4, 0, 8, 3,];
        table[34] = [0, 5, 4, 1, 5, 0,];
        table[35] = [8, 5, 4, 8, 3, 5, 3, 1, 5,];
        table[36] = [1, 2, 10, 9, 5, 4,];
        table[37] = [3, 0, 8, 1, 2, 10, 4, 9, 5,];
        table[38] = [5, 2, 10, 5, 4, 2, 4, 0, 2,];
        table[39] = [2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8,];
        table[40] = [9, 5, 4, 2, 3, 11,];
        table[41] = [0, 11, 2, 0, 8, 11, 4, 9, 5,];
        table[42] = [0, 5, 4, 0, 1, 5, 2, 3, 11,];
        table[43] = [2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5,];
        table[44] = [10, 3, 11, 10, 1, 3, 9, 5, 4,];
        table[45] = [4, 9, 5, 0, 8, 1, 8, 10, 1, 8, 11, 10,];
        table[46] = [5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3,];
        table[47] = [5, 4, 8, 5, 8, 10, 10, 8, 11,];
        table[48] = [9, 7, 8, 5, 7, 9,];
        table[49] = [9, 3, 0, 9, 5, 3, 5, 7, 3,];
        table[50] = [0, 7, 8, 0, 1, 7, 1, 5, 7,];
        table[51] = [1, 5, 3, 3, 5, 7,];
        table[52] = [9, 7, 8, 9, 5, 7, 10, 1, 2,];
        table[53] = [10, 1, 2, 9, 5, 0, 5, 3, 0, 5, 7, 3,];
        table[54] = [8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2,];
        table[55] = [2, 10, 5, 2, 5, 3, 3, 5, 7,];
        table[56] = [7, 9, 5, 7, 8, 9, 3, 11, 2,];
        table[57] = [9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11,];
        table[58] = [2, 3, 11, 0, 1, 8, 1, 7, 8, 1, 5, 7,];
        table[59] = [11, 2, 1, 11, 1, 7, 7, 1, 5,];
        table[60] = [9, 5, 8, 8, 5, 7, 10, 1, 3, 10, 3, 11,];
        table[61] = [5, 7, 0, 5, 0, 9, 7, 11, 0, 1, 0, 10, 11, 10, 0,];
        table[62] = [11, 10, 0, 11, 0, 3, 10, 5, 0, 8, 0, 7, 5, 7, 0,];
        table[63] = [11, 10, 5, 7, 11, 5,];
        table[64] = [10, 6, 5,];
        table[65] = [0, 8, 3, 5, 10, 6,];
        table[66] = [9, 0, 1, 5, 10, 6,];
        table[67] = [1, 8, 3, 1, 9, 8, 5, 10, 6,];
        table[68] = [1, 6, 5, 2, 6, 1,];
        table[69] = [1, 6, 5, 1, 2, 6, 3, 0, 8,];
        table[70] = [9, 6, 5, 9, 0, 6, 0, 2, 6,];
        table[71] = [5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8,];
        table[72] = [2, 3, 11, 10, 6, 5,];
        table[73] = [11, 0, 8, 11, 2, 0, 10, 6, 5,];
        table[74] = [0, 1, 9, 2, 3, 11, 5, 10, 6,];
        table[75] = [5, 10, 6, 1, 9, 2, 9, 11, 2, 9, 8, 11,];
        table[76] = [6, 3, 11, 6, 5, 3, 5, 1, 3,];
        table[77] = [0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6,];
        table[78] = [3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9,];
        table[79] = [6, 5, 9, 6, 9, 11, 11, 9, 8,];
        table[80] = [5, 10, 6, 4, 7, 8,];
        table[81] = [4, 3, 0, 4, 7, 3, 6, 5, 10,];
        table[82] = [1, 9, 0, 5, 10, 6, 8, 4, 7,];
        table[83] = [10, 6, 5, 1, 9, 7, 1, 7, 3, 7, 9, 4,];
        table[84] = [6, 1, 2, 6, 5, 1, 4, 7, 8,];
        table[85] = [1, 2, 5, 5, 2, 6, 3, 0, 4, 3, 4, 7,];
        table[86] = [8, 4, 7, 9, 0, 5, 0, 6, 5, 0, 2, 6,];
        table[87] = [7, 3, 9, 7, 9, 4, 3, 2, 9, 5, 9, 6, 2, 6, 9,];
        table[88] = [3, 11, 2, 7, 8, 4, 10, 6, 5,];
        table[89] = [5, 10, 6, 4, 7, 2, 4, 2, 0, 2, 7, 11,];
        table[90] = [0, 1, 9, 4, 7, 8, 2, 3, 11, 5, 10, 6,];
        table[91] = [9, 2, 1, 9, 11, 2, 9, 4, 11, 7, 11, 4, 5, 10, 6,];
        table[92] = [8, 4, 7, 3, 11, 5, 3, 5, 1, 5, 11, 6,];
        table[93] = [5, 1, 11, 5, 11, 6, 1, 0, 11, 7, 11, 4, 0, 4, 11,];
        table[94] = [0, 5, 9, 0, 6, 5, 0, 3, 6, 11, 6, 3, 8, 4, 7,];
        table[95] = [6, 5, 9, 6, 9, 11, 4, 7, 9, 7, 11, 9,];
        table[96] = [11, 9, 4, 6, 11, 4,];
        table[97] = [4, 6, 11, 4, 11, 9, 0, 3, 8,];
        table[98] = [11, 1, 0, 11, 0, 6, 6, 0, 4,];
        table[99] = [8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 11, 1,];
        table[100] = [1, 9, 4, 1, 4, 2, 2, 4, 6,];
        table[101] = [3, 8, 0, 1, 9, 2, 2, 9, 4, 2, 4, 6,];
        table[102] = [0, 4, 2, 4, 6, 2,];
        table[103] = [8, 2, 3, 8, 4, 2, 4, 6, 2,];
        table[104] = [11, 9, 4, 11, 4, 6, 10, 3, 2,];
        table[105] = [0, 2, 8, 2, 10, 8, 4, 11, 9, 4, 6, 11,];
        table[106] = [3, 2, 10, 0, 6, 1, 0, 4, 6, 6, 11, 1,];
        table[107] = [6, 1, 4, 6, 11, 1, 4, 1, 8, 2, 10, 1, 8, 1, 10,];
        table[108] = [9, 4, 6, 9, 6, 3, 9, 3, 1, 10, 3, 6,];
        table[109] = [8, 1, 10, 8, 0, 1, 10, 1, 6, 9, 4, 1, 6, 1, 4,];
        table[110] = [3, 6, 10, 3, 0, 6, 0, 4, 6,];
        table[111] = [6, 8, 4, 10, 8, 6,];
        table[112] = [7, 6, 11, 7, 11, 8, 8, 11, 9,];
        table[113] = [0, 3, 7, 0, 7, 11, 0, 11, 9, 6, 11, 7,];
        table[114] = [11, 7, 6, 1, 7, 11, 1, 8, 7, 1, 0, 8,];
        table[115] = [11, 7, 6, 11, 1, 7, 1, 3, 7,];
        table[116] = [1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6,];
        table[117] = [2, 9, 6, 2, 1, 9, 6, 9, 7, 0, 3, 9, 7, 9, 3,];
        table[118] = [7, 0, 8, 7, 6, 0, 6, 2, 0,];
        table[119] = [7, 2, 3, 6, 2, 7,];
        table[120] = [2, 10, 3, 11, 8, 6, 11, 9, 8, 8, 7, 6,];
        table[121] = [2, 7, 0, 2, 10, 7, 0, 7, 9, 6, 11, 7, 9, 7, 11,];
        table[122] = [1, 0, 8, 1, 8, 7, 1, 7, 11, 6, 11, 7, 2, 10, 3,];
        table[123] = [10, 1, 2, 10, 7, 1, 11, 1, 6, 6, 1, 7,];
        table[124] = [8, 6, 9, 8, 7, 6, 9, 6, 1, 10, 3, 6, 1, 6, 3,];
        table[125] = [0, 1, 9, 10, 7, 6,];
        table[126] = [7, 0, 8, 7, 6, 0, 3, 0, 10, 10, 0, 6,];
        table[127] = [7, 6, 10,];

        for (int i = 128; i < 256; i++) {
            int complement = 255 - i;
            int[] baseCase = table[complement];
            table[i] = baseCase.Length > 0
                ? [.. Enumerable.Range(0, baseCase.Length / 3)
                    .SelectMany(t => new[] { baseCase[(t * 3) + 2], baseCase[(t * 3) + 1], baseCase[t * 3], }),
                ]
                : [];
        }

        return table;
    }
}

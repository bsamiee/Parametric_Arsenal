using System.Diagnostics.Contracts;
using Rhino;

namespace Arsenal.Rhino.Fields;

/// <summary>Configuration constants, byte operation codes, and unified dispatch registry for fields operations.</summary>
[Pure]
internal static class FieldsConfig {
    internal const byte OperationDistance = 0;
    internal const byte IntegrationRK4 = 2;
    internal const byte InterpolationNearest = 0;
    internal const byte InterpolationTrilinear = 1;

    internal const int DefaultResolution = 32;
    internal const int MinResolution = 8;
    internal const int MaxResolution = 256;

    internal const double DefaultStepSize = 0.01;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double MinStepSize = RhinoMath.SqrtEpsilon;
    internal const double MaxStepSize = 1.0;
    internal const int MaxStreamlineSteps = 10000;
    internal const double MinFieldMagnitude = 1e-10;

    internal static readonly double[] RK4Weights = [1.0 / 6.0, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 6.0,];
    internal static readonly double[] RK4HalfSteps = [0.5, 0.5, 1.0,];
    internal const double RK2HalfStep = 0.5;

    internal const int StreamlineRTreeThreshold = 1000;
    internal const int FieldRTreeThreshold = 100;

    internal const byte CriticalPointMinimum = 0;
    internal const byte CriticalPointMaximum = 1;
    internal const byte CriticalPointSaddle = 2;

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

        for (int i = 96; i < 256; i++) {
            int complement = 255 - i;
            int[] baseCase = table[complement];
            table[i] = baseCase.Length > 0 ? InvertTriangles(baseCase) : [];
        }

        return table;
    }

    private static int[] InvertTriangles(int[] edges) {
        int length = edges.Length;
        int[] inverted = new int[length];
        for (int i = 0; i < length; i += 3) {
            inverted[i] = edges[i + 2];
            inverted[i + 1] = edges[i + 1];
            inverted[i + 2] = edges[i];
        }
        return inverted;
    }

    internal const double InsideOutsideToleranceMultiplier = 10.0;

    /// <summary>Critical point detection: eigenvalue threshold for classification.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double EigenvalueThreshold = RhinoMath.SqrtEpsilon;
}

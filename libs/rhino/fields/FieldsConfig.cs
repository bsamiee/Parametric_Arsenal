using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Configuration constants, byte operation codes, and unified dispatch registry for fields operations.</summary>
[Pure]
internal static class FieldsConfig {
    // OPERATION TYPE IDENTIFIERS

    /// <summary>Distance field operation identifier.</summary>
    internal const byte OperationDistance = 0;
    /// <summary>Gradient field operation identifier.</summary>
    internal const byte OperationGradient = 1;
    /// <summary>Streamline tracing operation identifier.</summary>
    internal const byte OperationStreamline = 2;
    /// <summary>Isosurface extraction operation identifier.</summary>
    internal const byte OperationIsosurface = 3;
    /// <summary>Curl field operation identifier.</summary>
    internal const byte OperationCurl = 4;
    /// <summary>Divergence field operation identifier.</summary>
    internal const byte OperationDivergence = 5;
    /// <summary>Laplacian field operation identifier.</summary>
    internal const byte OperationLaplacian = 6;
    /// <summary>Vector potential field operation identifier.</summary>
    internal const byte OperationVectorPotential = 7;

    // INTEGRATION METHOD IDENTIFIERS

    /// <summary>Euler forward integration method.</summary>
    internal const byte IntegrationEuler = 0;
    /// <summary>Second-order Runge-Kutta integration method.</summary>
    internal const byte IntegrationRK2 = 1;
    /// <summary>Fourth-order Runge-Kutta integration method.</summary>
    internal const byte IntegrationRK4 = 2;
    /// <summary>Adaptive fourth-order Runge-Kutta with error control (Dormand-Prince RK45).</summary>
    internal const byte IntegrationAdaptiveRK4 = 3;

    // INTERPOLATION METHOD IDENTIFIERS

    /// <summary>Nearest neighbor interpolation (fastest, lowest quality).</summary>
    internal const byte InterpolationNearest = 0;
    /// <summary>Trilinear interpolation (balanced speed and quality).</summary>
    internal const byte InterpolationTrilinear = 1;
    /// <summary>Tricubic interpolation (slowest, highest quality).</summary>
    internal const byte InterpolationTricubic = 2;

    // GRID RESOLUTION CONSTANTS

    /// <summary>Default grid resolution (32³ = 32,768 samples).</summary>
    internal const int DefaultResolution = 32;
    /// <summary>Minimum grid resolution (8³ = 512 samples).</summary>
    internal const int MinResolution = 8;
    /// <summary>Maximum grid resolution (256³ = 16,777,216 samples).</summary>
    internal const int MaxResolution = 256;

    // INTEGRATION STEP PARAMETERS

    /// <summary>Default integration step size (1% of typical geometry scale).</summary>
    internal const double DefaultStepSize = 0.01;
    /// <summary>Minimum step size (RhinoMath.SqrtEpsilon for numerical stability).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double MinStepSize = RhinoMath.SqrtEpsilon;
    /// <summary>Maximum step size (prevents overshooting field features).</summary>
    internal const double MaxStepSize = 1.0;
    /// <summary>Maximum streamline integration steps (prevents infinite loops).</summary>
    internal const int MaxStreamlineSteps = 10000;
    /// <summary>Adaptive step tolerance (RhinoMath.SqrtEpsilon for error control).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double AdaptiveStepTolerance = RhinoMath.SqrtEpsilon;
    /// <summary>Finite difference step for gradient/curl/divergence/laplacian computation (RhinoMath.SqrtEpsilon for numerical derivatives).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Value depends on RhinoMath constant")]
    internal static readonly double FiniteDifferenceStep = RhinoMath.SqrtEpsilon;

    // RK4 INTEGRATION COEFFICIENTS

    /// <summary>RK4 final stage weights: [k1/6, k2/3, k3/3, k4/6].</summary>
    internal static readonly double[] RK4Weights = [1.0 / 6.0, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 6.0,];
    /// <summary>RK4 intermediate stage half-step multipliers: [0.5, 0.5, 1.0].</summary>
    internal static readonly double[] RK4HalfSteps = [0.5, 0.5, 1.0,];

    // DORMANT-PRINCE RK45 COEFFICIENTS (for adaptive integration)

    /// <summary>Dormant-Prince RK45 Butcher tableau a coefficients.</summary>
    internal static readonly double[][] RK45A = [
        [],
        [1.0 / 5.0,],
        [3.0 / 40.0, 9.0 / 40.0,],
        [44.0 / 45.0, -56.0 / 15.0, 32.0 / 9.0,],
        [19372.0 / 6561.0, -25360.0 / 2187.0, 64448.0 / 6561.0, -212.0 / 729.0,],
        [9017.0 / 3168.0, -355.0 / 33.0, 46732.0 / 5247.0, 49.0 / 176.0, -5103.0 / 18656.0,],
    ];
    /// <summary>Dormant-Prince RK45 Butcher tableau b coefficients (5th order).</summary>
    internal static readonly double[] RK45B = [35.0 / 384.0, 0.0, 500.0 / 1113.0, 125.0 / 192.0, -2187.0 / 6784.0, 11.0 / 84.0,];
    /// <summary>Dormant-Prince RK45 Butcher tableau b* coefficients (4th order for error estimate).</summary>
    internal static readonly double[] RK45BStar = [5179.0 / 57600.0, 0.0, 7571.0 / 16695.0, 393.0 / 640.0, -92097.0 / 339200.0, 187.0 / 2100.0, 1.0 / 40.0,];

    // SPATIAL INDEXING THRESHOLDS (RTree vs linear search tradeoffs)

    /// <summary>Grid size threshold for RTree usage in streamline integration (RTree overhead justified above this size).</summary>
    internal const int StreamlineRTreeThreshold = 1000;
    /// <summary>RTree threshold for switching from linear to spatial search in field operations.</summary>
    internal const int FieldRTreeThreshold = 100;

    // MARCHING CUBES CONSTANTS

    /// <summary>Marching cubes edge vertex pairs: edge index → (vertex1, vertex2).</summary>
    internal static readonly (int V1, int V2)[] EdgeVertexPairs = [
        (0, 1),
        (1, 2),
        (2, 3),
        (3, 0),  // Bottom face edges 0-3
        (4, 5),
        (5, 6),
        (6, 7),
        (7, 4),  // Top face edges 4-7
        (0, 4),
        (1, 5),
        (2, 6),
        (3, 7),  // Vertical edges 8-11
    ];

    /// <summary>Marching cubes triangle configuration table (simplified - 256 cases compressed).</summary>
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

        // Cases 96-255 (remaining cases - use complement symmetry)
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

    // DISTANCE FIELD PARAMETERS

    /// <summary>Inside/outside ray casting tolerance multiplier.</summary>
    internal const double InsideOutsideToleranceMultiplier = 10.0;
}

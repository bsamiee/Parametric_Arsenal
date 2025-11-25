---
version: 1.0
last_updated: 2025-11-20
category: rhino-testing
difficulty: advanced
target: libs/rhino
prerequisites:
  - CLAUDE.md
  - AGENTS.md
  - copilot-instructions.md
  - test/shared/Test.cs
  - test/rhino/Rhino.Testing.Configs.xml
  - libs/rhino/rhino_math_class.md
  - libs/rhino/rhino_math_reference.md
---

# Rhino Headless Testing

Design and implement complete test coverage for Rhino geometry operations using NUnit + Rhino.Testing headless mode with property-based testing, RTree verification, and tolerance-based comparisons.

## Task Description

Comprehensive NUnit testing for RhinoCommon geometry operations. Use headless Rhino.Testing, CsCheck property-based testing, RTree spatial indexing verification, and RhinoMath tolerance-based comparisons.

## Inputs

- **Target Folder**: `libs/rhino/<<TARGET_FOLDER>>/`
- **Test Project**: `test/rhino/`
- **Testing Focus**: RhinoCommon geometry operations, spatial indexing, mesh/curve/surface manipulation

## Success Criteria

[PASS] Complete NUnit test coverage for Rhino geometry operations  
[PASS] Headless Rhino.Testing configured and functional (Rhino.Testing.Configs.xml)  
[PASS] Property-based tests with geometry generators (CsCheck + RhinoCommon)  
[PASS] RTree spatial indexing tests with performance validation  
[PASS] Tolerance-based geometry comparisons (RhinoMath.ZeroTolerance, etc.)  
[PASS] All tests pass in headless mode with zero warnings  
[PASS] Test utilities leverage test/shared/Test.cs patterns

## Constraints

Follow all rules in CLAUDE.md. Use NUnit framework with Rhino.Testing headless mode. Study test/shared/Test.cs utilities. Use RhinoMath constants for tolerances (ZeroTolerance, SqrtEpsilon).

**Rhino Testing**: NUnit + Rhino.Testing + RhinoCommon types + RhinoMath constants. Tolerance-based comparisons, validity checks, RTree verification.

## Methodology

---

### Phase 1: Rhino Testing Environment Setup (No Code Changes)

**Goal**: Understand Rhino.Testing infrastructure and headless mode.

### 1.1 Study Rhino.Testing Configuration

**Read** `test/rhino/Rhino.Testing.Configs.xml`:
- Headless Rhino configuration
- Plugin loading settings
- SDK initialization parameters

**Verify test project** `test/rhino/Arsenal.Rhino.Tests.csproj`:
- NUnit package references
- Rhino.Testing package
- RhinoCommon references
- CsCheck for property-based testing

### 1.2 Understand RhinoCommon Geometry Types

**Study primary types**:
- **Point3d, Vector3d** - Fundamental 3D primitives
- **Line** - Two-point line segment
- **Curve** - Abstract curve (LineCurve, NurbsCurve, PolyCurve, etc.)
- **Surface** - Abstract surface (NurbsSurface, PlaneSurface, etc.)
- **Brep** - Boundary representation solid
- **Mesh** - Triangulated/quad mesh
- **RTree** - Spatial indexing structure (O(log n) queries)

**Key methods**:
- Validity checks: `IsValid`, `IsValidWithLog`, `IsDegenerate`
- Distance queries: `DistanceTo`, `ClosestPoint`, `ClosestParameters`
- Transformations: `Transform`, `Rotate`, `Translate`, `Scale`
- Boolean operations: `CreateBooleanUnion`, `CreateBooleanDifference`, `CreateBooleanIntersection`

### 1.3 Study RhinoMath Constants

**From** `libs/rhino/rhino_math_class.md`:
- **ZeroTolerance** (1.0e-12) - Default tolerance for zero checks
- **SqrtEpsilon** (~1.49e-8) - Square root of epsilon
- **UnsetValue** (≈-1.23432101234321e+308) - Sentinel for unset values
- **SqrtTwo**, **Pi**, **TwoPi** - Mathematical constants

**Usage**:
```csharp
// Tolerance-based comparison
Test.EqualWithin(actual, expected, tolerance: RhinoMath.ZeroTolerance);

// Zero check
bool isZero = Math.Abs(value) < RhinoMath.ZeroTolerance;

// Unset value check
bool isUnset = Math.Abs(value - RhinoMath.UnsetValue) < RhinoMath.ZeroTolerance;
```

### Phase 2: Target Implementation Analysis (No Code Changes)

**Goal**: Map Rhino module operations to test scenarios.

### 2.1 Identify Operations to Test

**For target folder** `libs/rhino/<<TARGET>>/`:
1. **List public APIs** and their geometry types
2. **Map RhinoCommon operations** used in Compute layer
3. **Identify spatial indexing** (RTree usage)
4. **Trace validation points** (V flags, E error codes)
5. **Document tolerances** (default vs custom)

### 2.2 Map Test Categories

**For each operation**:
1. **Valid geometry tests**: Standard inputs, expected outputs
2. **Degenerate geometry tests**: Zero-length, coincident, collapsed
3. **Boundary tests**: Edge cases, limits, extremes
4. **Tolerance tests**: Precision, rounding, floating-point
5. **Spatial tests**: RTree correctness, performance, edge cases
6. **Property-based tests**: Invariants, transformations, laws

### 2.3 Identify RTree Operations

**If module uses spatial indexing**:
- RTree construction from geometry
- Insert/Search/Remove operations
- Nearest neighbor queries
- Radius/box/sphere searches
- Performance characteristics (O(log n) vs O(n))

### Phase 3: Geometry Generator Design (No Code Changes)

**Goal**: Design CsCheck generators for RhinoCommon types.

### 3.1 Primitive Generators

**Point3d generator**:
```csharp
public static Gen<Point3d> ValidPoint3dGen =>
    Gen.Double[-1000, 1000][3].Select(static arr =>
        new Point3d(arr[0], arr[1], arr[2]));

public static Gen<Point3d> OriginPointGen =>
    Gen.Const(Point3d.Origin);

public static Gen<Point3d> DegeneratePointGen =>
    Gen.OneOf(
        Gen.Const(new Point3d(RhinoMath.UnsetValue, 0, 0)),
        Gen.Const(new Point3d(double.NaN, 0, 0)),
        Gen.Const(new Point3d(double.PositiveInfinity, 0, 0)));
```

**Vector3d generator**:
```csharp
public static Gen<Vector3d> ValidVectorGen =>
    Gen.Double[-100, 100][3]
        .Where(static arr => Math.Sqrt(arr[0]*arr[0] + arr[1]*arr[1] + arr[2]*arr[2]) > RhinoMath.ZeroTolerance)
        .Select(static arr => new Vector3d(arr[0], arr[1], arr[2]));

public static Gen<Vector3d> UnitVectorGen =>
    ValidVectorGen.Select(static v => {
        v.Unitize();
        return v;
    });

public static Gen<Vector3d> ZeroVectorGen =>
    Gen.Const(Vector3d.Zero);
```

### 3.2 Line Generators

**Valid line generator**:
```csharp
public static Gen<Line> ValidLineGen =>
    from start in ValidPoint3dGen
    from end in ValidPoint3dGen.Where(p => start.DistanceTo(p) > RhinoMath.ZeroTolerance)
    select new Line(start, end);

public static Gen<Line> LongLineGen =>
    from start in ValidPoint3dGen
    from direction in UnitVectorGen
    from length in Gen.Double[100, 10000]
    select new Line(start, start + direction * length);
```

**Degenerate line generator**:
```csharp
public static Gen<Line> DegenerateLineGen =>
    ValidPoint3dGen.Select(static p => new Line(p, p));

public static Gen<Line> NearlyDegenerateLineGen =>
    from start in ValidPoint3dGen
    from offset in Gen.Double[-RhinoMath.SqrtEpsilon, RhinoMath.SqrtEpsilon][3]
    select new Line(start, new Point3d(start.X + offset[0], start.Y + offset[1], start.Z + offset[2]));
```

### 3.3 Curve Generators

**LineCurve generator**:
```csharp
public static Gen<LineCurve> ValidLineCurveGen =>
    ValidLineGen.Select(static line => new LineCurve(line));
```

**NurbsCurve generator**:
```csharp
public static Gen<NurbsCurve> ValidNurbsCurveGen =>
    from pointCount in Gen.Int[4, 20]
    from points in ValidPoint3dGen.List[pointCount, pointCount]
    select NurbsCurve.Create(
        periodic: false,
        degree: 3,
        points: points);

public static Gen<NurbsCurve> ClosedNurbsCurveGen =>
    from pointCount in Gen.Int[4, 20]
    from points in ValidPoint3dGen.List[pointCount, pointCount]
    select NurbsCurve.Create(
        periodic: true,
        degree: 3,
        points: points);
```

**Degenerate curve generator**:
```csharp
public static Gen<Curve> DegenerateCurveGen =>
    ValidPoint3dGen.Select(static p => (Curve)new LineCurve(p, p));
```

### 3.4 Mesh Generators

**Simple mesh generator**:
```csharp
public static Gen<Mesh> ValidMeshGen =>
    from rows in Gen.Int[3, 20]
    from cols in Gen.Int[3, 20]
    let mesh = new Mesh()
    select CreateGridMesh(mesh, rows, cols);

private static Mesh CreateGridMesh(Mesh mesh, int rows, int cols) {
    // Create grid vertices
    for (int i = 0; i < rows; i++) {
        for (int j = 0; j < cols; j++) {
            mesh.Vertices.Add(i * 10.0, j * 10.0, 0);
        }
    }

    // Create faces
    for (int i = 0; i < rows - 1; i++) {
        for (int j = 0; j < cols - 1; j++) {
            int v1 = i * cols + j;
            int v2 = v1 + 1;
            int v3 = (i + 1) * cols + j + 1;
            int v4 = v3 - 1;
            mesh.Faces.AddFace(v1, v2, v3, v4);
        }
    }

    mesh.Normals.ComputeNormals();
    mesh.Compact();
    return mesh;
}
```

**Degenerate mesh generator**:
```csharp
public static Gen<Mesh> DegenerateMeshGen =>
    Gen.OneOf(
        // Empty mesh
        Gen.Const(new Mesh()),
        // Single vertex, no faces
        ValidPoint3dGen.Select(static p => {
            Mesh mesh = new();
            mesh.Vertices.Add(p);
            return mesh;
        }),
        // Vertices but degenerate faces (coincident points)
        ValidPoint3dGen.Select(static p => {
            Mesh mesh = new();
            mesh.Vertices.Add(p);
            mesh.Vertices.Add(p);
            mesh.Vertices.Add(p);
            mesh.Faces.AddFace(0, 1, 2);
            return mesh;
        }));
```

### Phase 4: NUnit Test Implementation

**Goal**: Implement comprehensive NUnit tests with Rhino.Testing.

### 4.1 Test Fixture Structure

**Basic test fixture**:
```csharp
using NUnit.Framework;
using Rhino.Geometry;
using Arsenal.Rhino.<<Module>>;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;

namespace Arsenal.Rhino.Tests.<<Module>>;

[TestFixture]
public sealed class <<Module>>Tests {
    [SetUp]
    public void Setup() {
        // Rhino.Testing automatically initializes headless Rhino
        // Additional setup if needed
    }

    [Test]
    public void TestName() {
        // Test implementation
    }

    [TearDown]
    public void TearDown() {
        // Cleanup if needed
    }
}
```

### 4.2 Valid Geometry Tests

**Standard operation tests**:
```csharp
[Test]
public void ProcessValidLine_ReturnsSuccess() {
    // Arrange
    Line line = new(start: new Point3d(0, 0, 0), end: new Point3d(10, 0, 0));

    // Act
    Result<Output> result = Module.Process(line);

    // Assert
    Test.Success(result, output => {
        Assert.That(output.Length, Is.EqualTo(10.0).Within(RhinoMath.ZeroTolerance));
        Assert.That(output.IsValid, Is.True);
        return true;
    });
}

[Test]
public void ProcessValidCurve_ComputesCorrectProperties() {
    // Arrange
    Point3d[] points = [
        new Point3d(0, 0, 0),
        new Point3d(10, 10, 0),
        new Point3d(20, 0, 0),
        new Point3d(30, 10, 0),
    ];
    NurbsCurve curve = NurbsCurve.Create(periodic: false, degree: 3, points: points);

    // Act
    Result<CurveAnalysis> result = Module.AnalyzeCurve(curve);

    // Assert
    Test.Success(result, analysis => {
        Assert.That(analysis.Length, Is.GreaterThan(0));
        Assert.That(analysis.IsPlanar, Is.True);
        Assert.That(analysis.Degree, Is.EqualTo(3));
        return true;
    });
}
```

**Mesh operation tests**:
```csharp
[Test]
public void ProcessValidMesh_ReturnsSuccess() {
    // Arrange
    Mesh mesh = CreateSimpleQuadMesh();
    Assert.That(mesh.IsValid, Is.True);

    // Act
    Result<MeshAnalysis> result = Module.AnalyzeMesh(mesh);

    // Assert
    Test.Success(result, analysis => {
        Assert.That(analysis.VertexCount, Is.EqualTo(mesh.Vertices.Count));
        Assert.That(analysis.FaceCount, Is.EqualTo(mesh.Faces.Count));
        Assert.That(analysis.Area, Is.GreaterThan(0));
        return true;
    });
}

private static Mesh CreateSimpleQuadMesh() {
    Mesh mesh = new();
    mesh.Vertices.Add(0, 0, 0);
    mesh.Vertices.Add(10, 0, 0);
    mesh.Vertices.Add(10, 10, 0);
    mesh.Vertices.Add(0, 10, 0);
    mesh.Faces.AddFace(0, 1, 2, 3);
    mesh.Normals.ComputeNormals();
    return mesh;
}
```

### 4.3 Degenerate Geometry Tests

**Degenerate input handling**:
```csharp
[Test]
public void ProcessDegenerateLine_ReturnsValidationError() {
    // Arrange
    Point3d point = new(5, 5, 5);
    Line degenerateLine = new(point, point);
    Assert.That(degenerateLine.Length, Is.LessThan(RhinoMath.ZeroTolerance));

    // Act
    Result<Output> result = Module.Process(degenerateLine);

    // Assert
    Test.Failure(result, errs =>
        errs.Any(e => e.Code == E.Validation.DegenerateGeometry.Code));
}

[Test]
public void ProcessDegenerateMesh_ReturnsValidationError() {
    // Arrange
    Mesh mesh = new(); // Empty mesh
    Assert.That(mesh.IsValid, Is.False);

    // Act
    Result<MeshAnalysis> result = Module.AnalyzeMesh(mesh);

    // Assert
    Test.Failure(result, errs =>
        errs.Any(e => e.Domain == ErrorDomain.Validation));
}
```

### 4.4 Property-Based Tests with CsCheck

**Invariant verification**:
```csharp
[Test]
public void ProcessLine_PreservesLength() {
    ValidLineGen.Run((Action<Line>)(line => {
        double originalLength = line.Length;

        Result<Output> result = Module.Process(line);

        Test.Success(result, output => {
            Test.EqualWithin(output.ComputedLength, originalLength, tolerance: RhinoMath.ZeroTolerance);
            return true;
        });
    }), 100);
}

[Test]
public void TransformGeometry_PreservesValidity() {
    ValidPoint3dGen.Select(ValidVectorGen).Run(
        (Action<Point3d, Vector3d>)((point, translation) => {
            Point3d transformed = point + translation;

            Result<Output> result = Module.Process(transformed);

            Test.Success(result, output => {
                Assert.That(output.IsValid, Is.True);
                return true;
            });
        }), 50);
}
```

**Transformation preservation**:
```csharp
[Test]
public void Transformation_PreservesDistance() {
    ValidLineGen.Select(ValidLineGen).Run(
        (Action<Line, Line>)((line1, line2) => {
            double originalDist = line1.From.DistanceTo(line2.From);

            Result<TransformOutput> result = Module.Transform(line1, line2);

            Test.Success(result, output => {
                double transformedDist = output.Line1.From.DistanceTo(output.Line2.From);
                Test.EqualWithin(originalDist, transformedDist, tolerance: RhinoMath.ZeroTolerance);
                return true;
            });
        }), 50);
}
```

### Phase 5: RTree Spatial Indexing Tests

**Goal**: Verify RTree correctness and performance.

### 5.1 RTree Construction Tests

**Basic RTree construction**:
```csharp
[Test]
public void RTree_Construction_FromPoints() {
    // Arrange
    Point3d[] points = Enumerable.Range(0, 100)
        .Select(i => new Point3d(i * 10.0, i * 5.0, 0))
        .ToArray();

    // Act
    Result<RTree> result = Spatial.BuildRTree(points);

    // Assert
    Test.Success(result, tree => {
        Assert.That(tree.Count, Is.EqualTo(points.Length));
        return true;
    });
}

[Test]
public void RTree_Construction_FromBoundingBoxes() {
    // Arrange
    Line[] lines = Enumerable.Range(0, 50)
        .Select(i => new Line(
            new Point3d(i * 10.0, 0, 0),
            new Point3d(i * 10.0 + 5, 5, 0)))
        .ToArray();
    BoundingBox[] boxes = lines.Select(l => l.BoundingBox).ToArray();

    // Act
    RTree tree = new();
    for (int i = 0; i < boxes.Length; i++) {
        tree.Insert(boxes[i], i);
    }

    // Assert
    Assert.That(tree.Count, Is.EqualTo(lines.Length));
}
```

### 5.2 RTree Query Tests

**Nearest neighbor search**:
```csharp
[Test]
public void RTree_NearestNeighbor_FindsClosest() {
    // Arrange
    Point3d[] points = [
        new Point3d(0, 0, 0),
        new Point3d(10, 0, 0),
        new Point3d(20, 0, 0),
        new Point3d(30, 0, 0),
    ];
    RTree tree = new();
    for (int i = 0; i < points.Length; i++) {
        tree.Insert(points[i], i);
    }
    Point3d query = new(12, 0, 0); // Closest to index 1 (10, 0, 0)

    // Act
    tree.Search(
        sphere: new Sphere(query, 5.0),
        callback: (sender, args) => { /* Collect results */ });

    // Assert - Closest point should be index 1
    // (Full implementation would collect indices and verify)
}

[Test]
public void RTree_RadiusSearch_FindsWithinRadius() {
    // Arrange
    Point3d[] points = Enumerable.Range(0, 100)
        .Select(i => new Point3d(i * 2.0, 0, 0))
        .ToArray();
    RTree tree = new();
    for (int i = 0; i < points.Length; i++) {
        tree.Insert(points[i], i);
    }
    Point3d query = new(50, 0, 0);
    double radius = 10.0;

    // Act
    int foundCount = 0;
    tree.Search(
        sphere: new Sphere(query, radius),
        callback: (sender, args) => { foundCount++; });

    // Assert - Should find ~5 points within radius
    Assert.That(foundCount, Is.GreaterThan(0));
    Assert.That(foundCount, Is.LessThanOrEqualTo(10)); // Max possible
}
```

### 5.3 RTree Performance Tests

**O(log n) vs O(n) comparison**:
```csharp
[Test]
public void RTree_Performance_LogarithmicSearch() {
    // Arrange
    int[] sizes = [100, 1000, 10000,];
    Dictionary<int, long> rtreeTimes = new();
    Dictionary<int, long> linearTimes = new();

    foreach (int size in sizes) {
        Point3d[] points = Enumerable.Range(0, size)
            .Select(i => new Point3d(i * 2.0, i * 3.0, 0))
            .ToArray();

        // RTree search
        RTree tree = new();
        for (int i = 0; i < points.Length; i++) {
            tree.Insert(points[i], i);
        }

        Stopwatch rtreeSw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) {
            Point3d query = new(size / 2.0 * 2.0, size / 2.0 * 3.0, 0);
            tree.Search(new Sphere(query, 10.0), (sender, args) => { });
        }
        rtreeSw.Stop();
        rtreeTimes[size] = rtreeSw.ElapsedTicks;

        // Linear search
        Stopwatch linearSw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) {
            Point3d query = new(size / 2.0 * 2.0, size / 2.0 * 3.0, 0);
            _ = points.Where(p => p.DistanceTo(query) <= 10.0).ToArray();
        }
        linearSw.Stop();
        linearTimes[size] = linearSw.ElapsedTicks;
    }

    // Assert - RTree should scale better than linear (logarithmic vs linear)
    double rtreeRatio = (double)rtreeTimes[10000] / rtreeTimes[100];
    double linearRatio = (double)linearTimes[10000] / linearTimes[100];
    Assert.That(rtreeRatio, Is.LessThan(linearRatio * 0.5),
        "RTree should scale better than linear search");
}
```

### Phase 6: Tolerance & Precision Tests

**Goal**: Verify numerical stability and tolerance handling.

### 6.1 Tolerance-Based Comparisons

**RhinoMath.ZeroTolerance usage**:
```csharp
[Test]
public void GeometryComparison_UsesTolerance() {
    // Arrange
    Point3d p1 = new(1.0, 2.0, 3.0);
    Point3d p2 = new(1.0 + RhinoMath.ZeroTolerance * 0.5, 2.0, 3.0);

    // Act
    double distance = p1.DistanceTo(p2);

    // Assert
    Assert.That(distance, Is.LessThan(RhinoMath.ZeroTolerance));
}

[Test]
public void LineLength_WithinTolerance() {
    // Arrange
    Line line = new(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
    double expected = 10.0;

    // Act
    double actual = line.Length;

    // Assert
    Test.EqualWithin(actual, expected, tolerance: RhinoMath.ZeroTolerance);
}
```

### 6.2 Floating-Point Edge Cases

**NaN and infinity handling**:
```csharp
[Test]
public void InvalidCoordinate_NaN_ReturnsValidationError() {
    // Arrange
    Point3d invalidPoint = new(double.NaN, 0, 0);

    // Act
    Result<Output> result = Module.Process(invalidPoint);

    // Assert
    Test.Failure(result, errs =>
        errs.Any(e => e.Domain == ErrorDomain.Validation));
}

[Test]
public void InvalidCoordinate_Infinity_ReturnsValidationError() {
    // Arrange
    Point3d invalidPoint = new(double.PositiveInfinity, 0, 0);

    // Act
    Result<Output> result = Module.Process(invalidPoint);

    // Assert
    Test.Failure(result, errs =>
        errs.Any(e => e.Domain == ErrorDomain.Validation));
}
```

### Phase 7: Final Quality Pass

**Goal**: Holistic verification of Rhino test quality.

### 7.1 Coverage Verification

**For each Rhino module operation**:
- [ ] Valid geometry tests (standard inputs)
- [ ] Degenerate geometry tests (zero-length, empty, etc.)
- [ ] Property-based tests (invariants, transformations)
- [ ] Tolerance tests (RhinoMath.ZeroTolerance)
- [ ] RTree tests (if spatial indexing used)
- [ ] Validation mode tests (V.None, V.Standard, V.Degeneracy, V.All)
- [ ] Error code tests (E.* registry verification)

### 7.2 Test Execution

```bash
# Run all Rhino tests in headless mode
dotnet test test/rhino/Arsenal.Rhino.Tests.csproj

# Run specific test fixture
dotnet test --filter "ClassName~SpatialTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed" test/rhino/

# Run specific test
dotnet test --filter "Name~RTree_NearestNeighbor"
```

### 7.3 Headless Mode Verification

**Confirm Rhino.Testing working**:
- Tests run without UI
- RhinoCommon types instantiate correctly
- Geometry operations execute properly
- No licensing/activation errors

---

## Editing Discipline

[PASS] **Do**:
- Use NUnit framework (`[TestFixture]`, `[Test]`)
- Leverage Rhino.Testing for headless mode
- Use RhinoMath constants for tolerances
- Generate valid and degenerate geometry with CsCheck
- Test RTree performance and correctness
- Use Test.cs utilities for assertions
- Verify geometry validity (IsValid, IsValidWithLog)

[FAIL] **Don't**:
- Use `var` or `if`/`else` statements
- Hardcode tolerance values (use RhinoMath.ZeroTolerance)
- Skip degenerate geometry tests
- Assume UI is available (headless mode only)
- Create tests that require user interaction
- Use magic numbers for coordinates/tolerances

## Verification

After implementation:
- Complete NUnit test coverage
- Headless Rhino.Testing functional
- Property-based tests with geometry generators
- RTree performance tests (O(log n) vs O(n))
- Tolerance-based comparisons (RhinoMath.ZeroTolerance)
- All tests pass with zero warnings

---

## Anti-Patterns to Avoid

1. **UI Dependency**: Tests requiring Rhino UI (must be headless)
2. **Hardcoded Tolerances**: Using 0.001 instead of RhinoMath.ZeroTolerance
3. **Validity Assumptions**: Not checking GeometryBase.IsValid
4. **RTree Neglect**: Not testing spatial indexing performance
5. **Degenerate Gaps**: Missing zero-length, empty, collapsed geometry tests
6. **Property-Based Omission**: Only testing concrete values, not invariants
7. **Tolerance Ignorance**: Exact comparisons instead of tolerance-based
8. **Framework Mixing**: Using xUnit instead of NUnit for Rhino tests

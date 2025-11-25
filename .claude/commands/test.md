---
name: test
description: Create comprehensive tests using testing-specialist patterns
---

Create tests using the testing-specialist agent approach.

## TEST FRAMEWORK SELECTION

| Location | Framework | Purpose |
|----------|-----------|---------|
| test/core/ | xUnit + CsCheck | Property-based tests, monad laws |
| test/rhino/ | NUnit + Rhino.Testing | Geometry integration tests |

## PROPERTY-BASED TESTING (test/core/)

```csharp
// Monad laws
[Fact]
public void Result_LeftIdentity_Law() =>
    Gen.Int.Sample(x => {
        Func<int, Result<int>> f = v => ResultFactory.Create(value: v * 2);
        Result<int> left = ResultFactory.Create(value: x).Bind(f);
        Result<int> right = f(x);
        Assert.Equal(left.Value, right.Value);
    });

// Algebraic properties
[Fact]
public void Operation_Commutative() =>
    Gen.Int.Sample(x => Gen.Int.Sample(y => {
        Assert.Equal(Op(x, y).Value, Op(y, x).Value);
    }));
```

## INTEGRATION TESTING (test/rhino/)

```csharp
[Test]
public void Feature_ValidInput_ReturnsExpected() {
    // Arrange
    Curve curve = new LineCurve(Point3d.Origin, new Point3d(10, 0, 0));
    IGeometryContext context = new GeometryContext();

    // Act
    Result<IReadOnlyList<Point3d>> result = Feature.Extract(curve, context);

    // Assert - use pattern matching
    result.Match(
        onSuccess: points => Assert.That(points.Count, Is.GreaterThan(0)),
        onFailure: errors => Assert.Fail($"Unexpected: {errors[0].Message}"));
}
```

## MANDATORY TEST PATTERNS

- ✅ Property-based for mathematical invariants
- ✅ Edge cases: null, empty, degenerate, boundary
- ✅ Result.Match for assertions (no if/else)
- ✅ NO var in test code
- ✅ Named parameters where not obvious
- ✅ Max 4 test files per folder

## VERIFICATION

```bash
dotnet test test/core/
dotnet test test/rhino/
```

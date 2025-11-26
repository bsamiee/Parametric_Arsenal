# Parametric_Arsenal: Comprehensive Agentic & Tooling Enhancement Guide

> **Purpose**: This document provides a complete roadmap for enhancing the Parametric_Arsenal monorepo with advanced agentic capabilities, automation, and tooling improvements.
>
> **Target Repository**: https://github.com/bsamiee/Parametric_Arsenal
>
> **For Agents**: Execute tasks in order of priority. Each section contains actionable items with file paths and content.

---

## Table of Contents

1. [Current State Assessment](#1-current-state-assessment)
2. [Priority Matrix](#2-priority-matrix)
3. [Automated Tasks - P0 Critical](#3-automated-tasks---p0-critical)
4. [Automated Tasks - P1 High](#4-automated-tasks---p1-high)
5. [Automated Tasks - P2 Medium](#5-automated-tasks---p2-medium)
6. [Automated Tasks - P3 Low](#6-automated-tasks---p3-low)
7. [Manual Tasks - Requires Human Action](#7-manual-tasks---requires-human-action)
8. [Reference: Existing Configuration Analysis](#8-reference-existing-configuration-analysis)

---

## 1. Current State Assessment

### Existing Infrastructure

| Component | Status | Files |
|-----------|--------|-------|
| CI/CD | ✅ Comprehensive | `.github/workflows/ci.yml` |
| Rhino Tests | ✅ Windows headless | `.github/workflows/rhino-tests.yml` |
| Claude Code | ✅ Multi-workflow | `claude.yml`, `claude-issues.yml`, `claude-code-review.yml`, `claude-maintenance.yml` |
| Dependabot | ⚠️ Basic config | `.github/dependabot.yml` |
| Pre-commit | ✅ Good foundation | `.pre-commit-config.yaml` |
| Analyzers | ✅ Excellent | `Directory.Build.props` (Roslynator, Meziantou, NetAnalyzers, AsyncFixer, ReflectionAnalyzers) |
| AI Reviews | ⚠️ Fragmented | Copilot, Gemini, Codex (no aggregation) |

### Identified Gaps

1. No unified AI review summary aggregation
2. No automated PR/issue labeling
3. No dependabot auto-merge
4. No structured agent context documentation (`docs/`)
5. No PR/Issue templates optimized for AI agents
6. No security scanning (CodeQL/SAST)
7. No coverage reporting integration

---

## 2. Priority Matrix

| Priority | Task | Effort | Impact | Automatable |
|----------|------|--------|--------|-------------|
| 🔴 P0 | Create `docs/AGENTS.md` | Low | High | ✅ Yes |
| 🔴 P0 | Create `.github/copilot-instructions.md` | Low | High | ✅ Yes |
| 🔴 P0 | Create dependabot auto-merge workflow | Low | Medium | ✅ Yes |
| 🔴 P0 | Create `docs/` directory structure | Low | High | ✅ Yes |
| 🟠 P1 | Create PR template | Low | Medium | ✅ Yes |
| 🟠 P1 | Create Issue templates | Low | Medium | ✅ Yes |
| 🟠 P1 | Add `actions/labeler` workflow | Low | Medium | ✅ Yes |
| 🟠 P1 | Add AI labeler workflow | Medium | Medium | ✅ Yes |
| 🟠 P1 | Enhanced dependabot.yml | Low | Medium | ✅ Yes |
| 🟡 P2 | Create review aggregation workflow | Medium | High | ✅ Yes |
| 🟡 P2 | Add CodeQL security scanning | Low | Medium | ✅ Yes |
| 🟡 P2 | Enhanced pre-commit config | Low | Low | ✅ Yes |
| 🟡 P2 | Add stale management workflow | Low | Low | ✅ Yes |
| 🟢 P3 | Create `docs/exemplars/` examples | Medium | High | ✅ Yes |
| 🟢 P3 | Add Codecov integration | Low | Low | ✅ Yes |
| 🟢 P3 | Create `.github/labeler.yml` config | Low | Medium | ✅ Yes |
| ⬛ Manual | Enable GitHub repo settings | N/A | High | ❌ No |
| ⬛ Manual | Add repository secrets | N/A | High | ❌ No |
| ⬛ Manual | Enable GitHub Advanced Security | N/A | Medium | ❌ No |

---

## 3. Automated Tasks - P0 Critical

### 3.1 Create Agent Context Documentation Structure

Create the following directory structure and files:

#### 3.1.1 `docs/AGENTS.md`

```markdown
# Arsenal Agent Context

> Master context file for AI agents working with Parametric_Arsenal.
> All agents should read this file before making any code changes.

## Quick Reference Commands

```bash
# Build with analyzers (MUST pass with zero warnings)
dotnet build Parametric_Arsenal.sln --configuration Release -p:TreatWarningsAsErrors=true -p:EnforceCodeStyleInBuild=true

# Run all tests
dotnet test Parametric_Arsenal.sln --configuration Release

# Run core tests only (cross-platform)
dotnet test test/core/Arsenal.Core.Tests.csproj --configuration Release

# Run Rhino tests (Windows only)
dotnet test test/rhino/Arsenal.Rhino.Tests.csproj --configuration Release --framework net8.0-windows

# Format check (must pass)
dotnet format Parametric_Arsenal.sln --verify-no-changes --verbosity quiet

# Restore dependencies
dotnet restore Parametric_Arsenal.sln
```

## Critical Style Rules

**These rules cause build failures if violated. NO EXCEPTIONS.**

### ❌ NEVER Use

```csharp
// var declarations
var x = GetValue();
var items = new List<int>();

// if/else blocks
if (condition) {
    DoA();
} else {
    DoB();
}

// new List<T>, new Dictionary<K,V>
var list = new List<int> { 1, 2, 3 };
var dict = new Dictionary<string, int>();

// Allman brace style
public void Method()
{
    // wrong
}

// Multiple types per file
// File: MyTypes.cs containing ClassA and ClassB
```

### ✅ ALWAYS Use

```csharp
// Explicit types
int x = GetValue();
List<int> items = [];

// Ternary operators, switch expressions, pattern matching
string result = condition ? "A" : "B";

string category = value switch {
    < 0 => "negative",
    0 => "zero",
    > 0 => "positive",
};

// Collection expressions
List<int> list = [1, 2, 3];
Dictionary<string, int> dict = new() { ["key"] = 1 };

// K&R brace style (opening brace on same line)
public void Method() {
    // correct
}

// Named parameters for non-obvious arguments
Result<T> result = Operation.Execute(
    input: data,
    timeout: TimeSpan.FromSeconds(30),
    retryCount: 3
);

// Trailing commas on multi-line collections
List<string> items = [
    "first",
    "second",
    "third",  // <-- trailing comma required
];

// One type per file
// File: MyClass.cs containing only MyClass

// File-scoped namespaces
namespace Arsenal.Core.Results;  // <-- semicolon, not braces

// Result<T> for failable operations
public Result<GeometryData> Analyze(Curve curve) {
    return curve.IsValid
        ? Result<GeometryData>.Success(ComputeData(curve))
        : Result<GeometryData>.Failure(E.InvalidGeometry("Curve is not valid"));
}
```

## Architecture Patterns

### Result<T> Pattern
All operations that can fail MUST return `Result<T>`:
- Reference: `libs/core/results/Result.cs`
- Errors created via `E.*` methods: `libs/core/errors/E.cs`
- Validation flags via `V.*`: `libs/core/validation/V.cs`

### UnifiedOperation Pattern
Polymorphic dispatch for geometry operations:
- Reference: `libs/core/operations/UnifiedOperation.cs`
- Handles Curve, Surface, Brep, Mesh uniformly

### Error Handling
```csharp
// Creating errors
E.InvalidGeometry("Description")
E.NullArgument(nameof(parameter))
E.OutOfRange(nameof(index), index, min, max)

// Validation
V.NotNull | V.NotEmpty | V.InRange
```

## Project Structure

```
Parametric_Arsenal/
├── libs/
│   ├── core/           # Framework-agnostic utilities
│   │   ├── results/    # Result<T>, Option<T>
│   │   ├── errors/     # E.* error factory
│   │   ├── operations/ # UnifiedOperation base
│   │   └── validation/ # V.* validation flags
│   └── rhino/          # RhinoCommon-dependent code
│       ├── analysis/   # Geometry analysis
│       ├── spatial/    # Spatial operations
│       └── topology/   # Topological operations
├── test/
│   ├── core/           # xUnit + CsCheck (cross-platform)
│   ├── rhino/          # NUnit + Rhino.Testing (Windows)
│   └── shared/         # Shared test utilities
├── docs/               # Agent context documentation
└── .github/
    ├── workflows/      # CI/CD workflows
    ├── agents/         # Agent prompt files
    └── ISSUE_TEMPLATE/ # Issue templates
```

## Pre-Submission Checklist

Before creating a PR, verify:

1. [ ] `dotnet build` passes with **zero warnings**
2. [ ] `dotnet test` passes (all tests green)
3. [ ] `dotnet format --verify-no-changes` passes
4. [ ] No `var` declarations anywhere in changed files
5. [ ] No `if/else` blocks - use ternary/switch/pattern matching
6. [ ] All multi-line collections have trailing commas
7. [ ] All non-obvious parameters use named arguments
8. [ ] One type per file
9. [ ] File-scoped namespaces used
10. [ ] K&R brace style (opening brace on same line)
11. [ ] `Result<T>` used for all failable operations

## Additional Context Files

- `docs/architecture/CODEBASE_MAP.md` - Detailed project structure
- `docs/architecture/PATTERNS.md` - Design patterns in use
- `docs/conventions/ERROR_HANDLING.md` - Result<T> patterns
- `docs/exemplars/IDEAL_CLASS.md` - Annotated ideal implementation
- `docs/exemplars/IDEAL_TEST.md` - Annotated ideal test file
- `docs/rhino/SDK_REFERENCE.md` - Critical RhinoCommon APIs

## For Claude Code Specifically

When implementing issues labeled `claude-implement`:

1. Read this file completely
2. Read `docs/exemplars/IDEAL_CLASS.md` for reference
3. Study existing code in the affected area
4. Make changes following all style rules
5. Run `dotnet build` and fix any warnings
6. Run `dotnet test` and ensure all pass
7. Create PR with `Closes #<issue-number>` in body
```

#### 3.1.2 `docs/architecture/CODEBASE_MAP.md`

```markdown
# Codebase Map

## Solution Structure

```
Parametric_Arsenal.sln
│
├── libs/core/Core.csproj (Arsenal.Core)
│   └── Target: net8.0
│   └── Dependencies: None (framework-agnostic)
│
├── libs/rhino/Rhino.csproj (Arsenal.Rhino)
│   └── Target: net8.0
│   └── Dependencies: Arsenal.Core, RhinoCommon
│
├── test/core/Arsenal.Core.Tests.csproj
│   └── Target: net8.0
│   └── Framework: xUnit + CsCheck
│   └── Dependencies: Arsenal.Core
│
├── test/rhino/Arsenal.Rhino.Tests.csproj
│   └── Target: net8.0-windows (Windows only)
│   └── Framework: NUnit + Rhino.Testing
│   └── Dependencies: Arsenal.Rhino
│
└── test/shared/Arsenal.Tests.Shared.csproj
    └── Shared test utilities
```

## Key Directories

### libs/core/
Framework-agnostic code. No RhinoCommon dependencies.

| Folder | Purpose |
|--------|---------|
| `results/` | `Result<T>`, `Option<T>` monadic types |
| `errors/` | `E.*` error factory methods |
| `operations/` | `UnifiedOperation` base infrastructure |
| `validation/` | `V.*` validation flag enums |

### libs/rhino/
RhinoCommon-dependent geometry operations.

| Folder | Purpose |
|--------|---------|
| `analysis/` | Differential geometry, curvature analysis |
| `spatial/` | Spatial queries, proximity, containment |
| `topology/` | Topological operations, connectivity |
| `intersection/` | Curve/surface/solid intersections |

## Dependency Graph

```
Arsenal.Core (no dependencies)
    ↑
Arsenal.Rhino (depends on Core + RhinoCommon)
    ↑
Arsenal.Rhino.Tests (depends on Rhino + Rhino.Testing)

Arsenal.Core.Tests (depends on Core + CsCheck)
```

## Build Configuration

All projects inherit from `Directory.Build.props`:
- `TreatWarningsAsErrors=true`
- `EnforceCodeStyleInBuild=true`
- Analyzers: Roslynator, Meziantou, NetAnalyzers, AsyncFixer, ReflectionAnalyzers
- Language: C# preview features enabled
- Nullable: enabled
```

#### 3.1.3 `docs/architecture/PATTERNS.md`

```markdown
# Design Patterns

## Result<T> - Railway-Oriented Programming

All operations that can fail return `Result<T>` instead of throwing exceptions.

```csharp
// Definition (simplified)
public readonly struct Result<T> {
    public bool IsSuccess { get; }
    public T Value { get; }        // Only valid if IsSuccess
    public Error Error { get; }    // Only valid if !IsSuccess

    public static Result<T> Success(T value);
    public static Result<T> Failure(Error error);

    // Monadic operations
    public Result<U> Map<U>(Func<T, U> mapper);
    public Result<U> Bind<U>(Func<T, Result<U>> binder);
    public T GetValueOrDefault(T defaultValue);
}
```

### Usage Pattern

```csharp
public Result<AnalysisData> AnalyzeCurve(Curve curve, AnalysisContext context) {
    return !curve.IsValid
        ? Result<AnalysisData>.Failure(E.InvalidGeometry("Curve is invalid"))
        : !context.IsValid
            ? Result<AnalysisData>.Failure(E.InvalidContext("Context is not configured"))
            : Result<AnalysisData>.Success(PerformAnalysis(curve, context));
}

// Chaining results
Result<Report> result = GetCurve(id)
    .Bind(curve => AnalyzeCurve(curve, context))
    .Map(data => GenerateReport(data));
```

## UnifiedOperation - Polymorphic Dispatch

Handle multiple geometry types uniformly without type-checking scattered throughout code.

```csharp
public abstract class UnifiedOperation<TInput, TOutput> {
    public Result<TOutput> Execute(TInput input) {
        return input switch {
            Curve curve => ExecuteCurve(curve),
            Surface surface => ExecuteSurface(surface),
            Brep brep => ExecuteBrep(brep),
            Mesh mesh => ExecuteMesh(mesh),
            _ => Result<TOutput>.Failure(E.UnsupportedType(input.GetType())),
        };
    }

    protected abstract Result<TOutput> ExecuteCurve(Curve curve);
    protected abstract Result<TOutput> ExecuteSurface(Surface surface);
    protected abstract Result<TOutput> ExecuteBrep(Brep brep);
    protected abstract Result<TOutput> ExecuteMesh(Mesh mesh);
}
```

## E.* Error Factory

Centralized error creation for consistency.

```csharp
public static class E {
    public static Error InvalidGeometry(string message) =>
        new Error(ErrorCode.InvalidGeometry, message);

    public static Error NullArgument(string paramName) =>
        new Error(ErrorCode.NullArgument, $"Argument '{paramName}' cannot be null");

    public static Error OutOfRange(string paramName, object value, object min, object max) =>
        new Error(ErrorCode.OutOfRange, $"'{paramName}' value {value} is outside range [{min}, {max}]");

    public static Error OperationFailed(string operation, string reason) =>
        new Error(ErrorCode.OperationFailed, $"{operation} failed: {reason}");
}
```

## V.* Validation Flags

Composable validation requirements.

```csharp
[Flags]
public enum V {
    None = 0,
    NotNull = 1 << 0,
    NotEmpty = 1 << 1,
    InRange = 1 << 2,
    IsValid = 1 << 3,
    // Combinations
    Required = NotNull | NotEmpty,
    ValidGeometry = NotNull | IsValid,
}
```
```

#### 3.1.4 `docs/conventions/ERROR_HANDLING.md`

```markdown
# Error Handling Conventions

## Never Throw Exceptions for Expected Failures

```csharp
// ❌ WRONG - Exceptions for control flow
public double GetCurvature(Curve curve, double t) {
    if (curve == null) throw new ArgumentNullException(nameof(curve));
    if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t));
    // ...
}

// ✅ CORRECT - Result<T> for expected failures
public Result<double> GetCurvature(Curve curve, double t) {
    return curve == null
        ? Result<double>.Failure(E.NullArgument(nameof(curve)))
        : t < 0 || t > 1
            ? Result<double>.Failure(E.OutOfRange(nameof(t), t, 0, 1))
            : Result<double>.Success(ComputeCurvature(curve, t));
}
```

## Error Categories

| Category | Use Case | Example |
|----------|----------|---------|
| `InvalidGeometry` | Geometry validation failures | Degenerate curve, invalid Brep |
| `NullArgument` | Null parameter passed | `E.NullArgument(nameof(curve))` |
| `OutOfRange` | Value outside valid range | Parameter t outside [0,1] |
| `OperationFailed` | Operation couldn't complete | Intersection failed |
| `UnsupportedType` | Geometry type not handled | Unknown GeometryBase subtype |
| `InvalidContext` | Context/settings invalid | Missing tolerance settings |

## Composing Results

```csharp
// Sequential operations
public Result<Report> GenerateFullReport(Curve curve) {
    return ValidateCurve(curve)
        .Bind(valid => AnalyzeCurvature(valid))
        .Bind(curvature => AnalyzeFrames(curve, curvature))
        .Map(frames => CreateReport(curve, frames));
}

// Parallel collection processing
public Result<IReadOnlyList<double>> GetAllCurvatures(IEnumerable<Curve> curves) {
    List<double> results = [];
    foreach (Curve curve in curves) {
        Result<double> result = GetCurvature(curve, 0.5);
        if (!result.IsSuccess) {
            return Result<IReadOnlyList<double>>.Failure(result.Error);
        }
        results.Add(result.Value);
    }
    return Result<IReadOnlyList<double>>.Success(results);
}
```
```

#### 3.1.5 `docs/exemplars/IDEAL_CLASS.md`

```markdown
# Ideal Class Implementation

This file demonstrates the ideal implementation pattern for Arsenal classes.

```csharp
// File: libs/rhino/analysis/CurvatureAnalyzer.cs
// ONE type per file, filename matches type name

namespace Arsenal.Rhino.Analysis;  // File-scoped namespace (semicolon, not braces)

using System.Collections.Frozen;   // Modern collection types
using Rhino.Geometry;
using Arsenal.Core.Results;
using Arsenal.Core.Errors;

/// <summary>
/// Analyzes curvature properties of curves.
/// </summary>
public sealed class CurvatureAnalyzer {  // K&R brace style
    // Static readonly for immutable lookup tables
    private static readonly FrozenDictionary<CurveType, Func<Curve, double, Result<double>>> Analyzers =
        new Dictionary<CurveType, Func<Curve, double, Result<double>>> {
            [CurveType.Line] = AnalyzeLine,
            [CurveType.Arc] = AnalyzeArc,
            [CurveType.NurbsCurve] = AnalyzeNurbs,
        }.ToFrozenDictionary();  // Trailing comma in multi-line

    // Private readonly fields for injected dependencies
    private readonly AnalysisContext _context;

    // Constructor with explicit types and named parameters
    public CurvatureAnalyzer(AnalysisContext context) {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Computes curvature at the specified parameter.
    /// </summary>
    /// <param name="curve">The curve to analyze.</param>
    /// <param name="parameter">Normalized parameter in [0, 1].</param>
    /// <returns>Result containing curvature value or error.</returns>
    public Result<double> GetCurvature(Curve curve, double parameter) {
        // Early returns using ternary chains (no if/else)
        return curve is null
            ? Result<double>.Failure(E.NullArgument(nameof(curve)))
            : !curve.IsValid
                ? Result<double>.Failure(E.InvalidGeometry("Curve is not valid"))
                : parameter < 0.0 || parameter > 1.0
                    ? Result<double>.Failure(E.OutOfRange(nameof(parameter), parameter, 0.0, 1.0))
                    : ComputeCurvatureCore(curve, parameter);
    }

    // Private implementation methods
    private Result<double> ComputeCurvatureCore(Curve curve, double parameter) {
        // Use switch expression for type dispatch
        CurveType curveType = ClassifyCurve(curve);

        return Analyzers.TryGetValue(curveType, out Func<Curve, double, Result<double>>? analyzer)
            ? analyzer(curve, parameter)
            : Result<double>.Failure(E.UnsupportedType(curve.GetType()));
    }

    // Static analysis methods with explicit return types
    private static Result<double> AnalyzeLine(Curve curve, double parameter) {
        // Lines have zero curvature everywhere
        return Result<double>.Success(0.0);
    }

    private static Result<double> AnalyzeArc(Curve curve, double parameter) {
        // Arc curvature is 1/radius
        return curve is ArcCurve arc
            ? Result<double>.Success(1.0 / arc.Arc.Radius)
            : Result<double>.Failure(E.InvalidGeometry("Expected ArcCurve"));
    }

    private static Result<double> AnalyzeNurbs(Curve curve, double parameter) {
        double t = curve.Domain.ParameterAt(parameter);
        Vector3d curvatureVector = curve.CurvatureAt(t);
        double curvature = curvatureVector.Length;
        return Result<double>.Success(curvature);
    }

    // Private helper with switch expression
    private static CurveType ClassifyCurve(Curve curve) {
        return curve switch {
            LineCurve => CurveType.Line,
            ArcCurve => CurveType.Arc,
            NurbsCurve => CurveType.NurbsCurve,
            PolylineCurve => CurveType.Polyline,
            PolyCurve => CurveType.PolyCurve,
            _ => CurveType.Unknown,
        };  // Trailing comma on last item
    }
}

// Enum in separate file: CurveType.cs
```

## Key Observations

1. **File-scoped namespace** - Uses semicolon, not braces
2. **K&R braces** - Opening brace on same line as declaration
3. **No var** - All types explicitly declared
4. **No if/else** - Ternary chains and switch expressions
5. **Trailing commas** - On all multi-line collections
6. **Named parameters** - When purpose isn't obvious
7. **Result<T>** - For all failable operations
8. **E.*** - For error creation
9. **FrozenDictionary** - For immutable lookup tables
10. **Sealed class** - Unless inheritance is intended
```

#### 3.1.6 `docs/exemplars/IDEAL_TEST.md`

```markdown
# Ideal Test Implementation

## Core Tests (xUnit + CsCheck)

```csharp
// File: test/core/results/ResultTests.cs

namespace Arsenal.Core.Tests.Results;

using Arsenal.Core.Results;
using Arsenal.Core.Errors;
using CsCheck;
using Xunit;

/// <summary>
/// Property-based tests for Result<T> monadic operations.
/// </summary>
public sealed class ResultTests {
    // Use descriptive test names: Method_Condition_ExpectedResult
    [Fact]
    public void Success_WithValue_ContainsValue() {
        // Arrange
        const int expected = 42;

        // Act
        Result<int> result = Result<int>.Success(expected);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Failure_WithError_ContainsError() {
        // Arrange
        Error error = E.InvalidGeometry("Test error");

        // Act
        Result<int> result = Result<int>.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    // Property-based test with CsCheck
    [Fact]
    public void Map_PreservesSuccess_WhenSuccessful() {
        Gen.Int.Sample(value => {
            Result<int> result = Result<int>.Success(value);
            Result<string> mapped = result.Map(v => v.ToString());

            return mapped.IsSuccess && mapped.Value == value.ToString();
        });
    }

    [Fact]
    public void Bind_PropagatesFailure_WhenFailed() {
        // Arrange
        Error error = E.OperationFailed("test", "reason");
        Result<int> failed = Result<int>.Failure(error);

        // Act
        Result<string> bound = failed.Bind(v => Result<string>.Success(v.ToString()));

        // Assert
        Assert.False(bound.IsSuccess);
        Assert.Equal(error, bound.Error);
    }

    // Theory for parameterized tests
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Success_WithAnyInt_IsSuccess(int value) {
        Result<int> result = Result<int>.Success(value);
        Assert.True(result.IsSuccess);
    }
}
```

## Rhino Tests (NUnit + Rhino.Testing)

```csharp
// File: test/rhino/analysis/AnalysisTests.cs

namespace Arsenal.Rhino.Tests.Analysis;

using Arsenal.Rhino.Analysis;
using Arsenal.Core.Results;
using NUnit.Framework;
using Rhino.Geometry;

/// <summary>
/// Tests for geometry analysis operations using Rhino.Testing.
/// </summary>
[TestFixture]
public sealed class AnalysisTests {
    private AnalysisContext _context = null!;

    [SetUp]
    public void SetUp() {
        _context = AnalysisContext.Default;
    }

    [Test]
    public void AnalyzeCurve_WithValidLine_ReturnsZeroCurvature() {
        // Arrange
        Line line = new(Point3d.Origin, new Point3d(10, 0, 0));
        LineCurve curve = new(line);
        CurvatureAnalyzer analyzer = new(_context);

        // Act
        Result<double> result = analyzer.GetCurvature(curve: curve, parameter: 0.5);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(0.0).Within(_context.AbsoluteTolerance));
    }

    [Test]
    public void AnalyzeCurve_WithValidArc_ReturnsInverseRadius() {
        // Arrange
        const double radius = 5.0;
        Arc arc = new(Plane.WorldXY, radius, Math.PI);
        ArcCurve curve = new(arc);
        CurvatureAnalyzer analyzer = new(_context);
        double expectedCurvature = 1.0 / radius;

        // Act
        Result<double> result = analyzer.GetCurvature(curve: curve, parameter: 0.5);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(expectedCurvature).Within(_context.AbsoluteTolerance));
    }

    [Test]
    public void AnalyzeCurve_WithNullCurve_ReturnsFailure() {
        // Arrange
        CurvatureAnalyzer analyzer = new(_context);

        // Act
        Result<double> result = analyzer.GetCurvature(curve: null!, parameter: 0.5);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.NullArgument));
    }

    [Test]
    [TestCase(-0.1)]
    [TestCase(1.1)]
    [TestCase(double.NaN)]
    public void AnalyzeCurve_WithInvalidParameter_ReturnsFailure(double parameter) {
        // Arrange
        LineCurve curve = new(new Line(Point3d.Origin, Point3d.Origin + Vector3d.XAxis));
        CurvatureAnalyzer analyzer = new(_context);

        // Act
        Result<double> result = analyzer.GetCurvature(curve: curve, parameter: parameter);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }
}
```

## Key Testing Patterns

1. **Arrange-Act-Assert** - Clear separation of test phases
2. **Descriptive names** - `Method_Condition_ExpectedResult`
3. **Named parameters** - For clarity in test calls
4. **Property-based tests** - CsCheck for core logic
5. **Theory/TestCase** - For parameterized edge cases
6. **Tolerance-aware assertions** - For floating point comparisons
7. **Null and edge case coverage** - Always test failure paths
```

#### 3.1.7 `docs/rhino/SDK_REFERENCE.md`

```markdown
# RhinoCommon SDK Quick Reference

## Critical APIs for Analysis

### Curve Analysis

```csharp
// Parameter domain
Interval domain = curve.Domain;
double t = domain.ParameterAt(normalizedParameter);  // [0,1] -> actual domain

// Point and tangent
Point3d point = curve.PointAt(t);
Vector3d tangent = curve.TangentAt(t);

// Curvature
Vector3d curvatureVector = curve.CurvatureAt(t);
double curvature = curvatureVector.Length;

// Derivatives (index 0 = point, 1 = first derivative, etc.)
Vector3d[] derivatives = curve.DerivativeAt(t, derivativeCount: 2, side: CurveEvaluationSide.Default);

// Frames
Plane frame;
bool success = curve.FrameAt(t, out frame);
```

### Surface Analysis

```csharp
// Parameter domains
Interval uDomain = surface.Domain(0);
Interval vDomain = surface.Domain(1);

// Evaluation
Point3d point = surface.PointAt(u, v);
Vector3d normal = surface.NormalAt(u, v);

// Curvature
SurfaceCurvature curvature = surface.CurvatureAt(u, v);
double gaussian = curvature.Gaussian;  // K = k1 * k2
double mean = curvature.Mean;          // H = (k1 + k2) / 2
double kappa1 = curvature.Kappa(0);    // Principal curvature 1
double kappa2 = curvature.Kappa(1);    // Principal curvature 2

// Frames
Plane frame;
bool success = surface.FrameAt(u, v, out frame);
```

### Brep Analysis

```csharp
// Topology access
BrepFaceList faces = brep.Faces;
BrepEdgeList edges = brep.Edges;
BrepVertexList vertices = brep.Vertices;

// Face as surface
Surface surface = face.UnderlyingSurface();

// Volume and area
VolumeMassProperties vmp = VolumeMassProperties.Compute(brep);
double volume = vmp.Volume;
Point3d centroid = vmp.Centroid;

AreaMassProperties amp = AreaMassProperties.Compute(brep);
double area = amp.Area;
```

### Mesh Analysis

```csharp
// Topology
MeshVertexList vertices = mesh.Vertices;
MeshFaceList faces = mesh.Faces;
MeshTopologyVertexList topoVertices = mesh.TopologyVertices;
MeshTopologyEdgeList topoEdges = mesh.TopologyEdges;

// Normals
mesh.Normals.ComputeNormals();
Vector3f vertexNormal = mesh.Normals[vertexIndex];

// Quality metrics
MeshQuads.GetQuadQuality(mesh, out double aspectRatio, out double skew);
```

## Geometric Tolerances

```csharp
// Document tolerances (when available)
double absoluteTolerance = doc.ModelAbsoluteTolerance;  // typically 0.001 for mm
double angleTolerance = doc.ModelAngleToleranceRadians;

// RhinoMath constants
double zeroTolerance = RhinoMath.ZeroTolerance;  // ~1e-12
double sqrtEpsilon = RhinoMath.SqrtEpsilon;      // ~1e-8
```

## Common Pitfalls

1. **Domain vs Normalized Parameters**
   - Many methods expect actual domain parameters, not [0,1]
   - Always use `domain.ParameterAt(t)` to convert

2. **Validity Checks**
   - Always check `IsValid` before operations
   - `Curve.IsValid`, `Surface.IsValid`, `Brep.IsValid`, `Mesh.IsValid`

3. **Degenerate Geometry**
   - Zero-length curves, collapsed surfaces
   - Check `curve.GetLength() > tolerance`

4. **Memory Management**
   - GeometryBase implements IDisposable
   - Dispose geometry created in loops
```

---

### 3.2 Create `.github/copilot-instructions.md`

```markdown
# GitHub Copilot Instructions for Parametric_Arsenal

## Repository Context

This is a C# monorepo for Rhino 8 plugin development with extremely strict coding standards.

## Critical Rules - Build Will Fail If Violated

### NEVER Use
- `var` keyword - always use explicit types
- `if/else` blocks - use ternary operators, switch expressions, or pattern matching
- `new List<T>()` or `new Dictionary<K,V>()` - use collection expressions `[]`
- Allman brace style - use K&R (opening brace on same line)
- Multiple types per file - one type per file only
- Block-scoped namespaces - use file-scoped with semicolon

### ALWAYS Use
- Explicit type declarations: `int x = 5;` not `var x = 5;`
- Ternary operators: `condition ? a : b`
- Switch expressions for multi-branch logic
- Collection expressions: `List<int> items = [1, 2, 3];`
- Trailing commas on multi-line collections
- Named parameters for non-obvious arguments
- `Result<T>` for operations that can fail
- `E.*` methods for creating errors

## Code Style Examples

```csharp
// ✅ Correct
public Result<double> Calculate(Curve curve, double parameter) {
    return curve is null
        ? Result<double>.Failure(E.NullArgument(nameof(curve)))
        : parameter < 0 || parameter > 1
            ? Result<double>.Failure(E.OutOfRange(nameof(parameter), parameter, 0, 1))
            : Result<double>.Success(curve.GetLength() * parameter);
}

// ❌ Wrong
public double Calculate(Curve curve, double parameter) {
    if (curve == null) {
        throw new ArgumentNullException(nameof(curve));
    }
    var length = curve.GetLength();
    return length * parameter;
}
```

## Reference Files

Before generating code, consider these reference implementations:
- `docs/AGENTS.md` - Complete style guide
- `docs/exemplars/IDEAL_CLASS.md` - Reference class implementation
- `libs/core/results/Result.cs` - Result<T> pattern

## Testing

- Core tests use xUnit + CsCheck (property-based testing)
- Rhino tests use NUnit + Rhino.Testing (Windows only)
- All tests must pass before PR can merge
```

---

### 3.3 Create Dependabot Auto-Merge Workflow

Create `.github/workflows/dependabot-auto-merge.yml`:

```yaml
name: Dependabot Auto-Merge

on:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: write
  pull-requests: write

jobs:
  dependabot-auto-merge:
    runs-on: ubuntu-latest
    if: github.actor == 'dependabot[bot]'

    steps:
      - name: Dependabot metadata
        id: metadata
        uses: dependabot/fetch-metadata@v2
        with:
          github-token: "${{ secrets.GITHUB_TOKEN }}"

      - name: Wait for CI to complete
        uses: lewagon/wait-on-check-action@v1.3.4
        with:
          ref: ${{ github.event.pull_request.head.sha }}
          check-name: 'Build & Test'
          repo-token: ${{ secrets.GITHUB_TOKEN }}
          wait-interval: 30

      - name: Auto-merge development dependency patches
        if: |
          steps.metadata.outputs.dependency-type == 'direct:development' &&
          (steps.metadata.outputs.update-type == 'version-update:semver-patch' ||
           steps.metadata.outputs.update-type == 'version-update:semver-minor')
        run: |
          gh pr review --approve "$PR_URL"
          gh pr merge --auto --squash "$PR_URL"
        env:
          PR_URL: ${{ github.event.pull_request.html_url }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Auto-merge production dependency patches only
        if: |
          steps.metadata.outputs.dependency-type == 'direct:production' &&
          steps.metadata.outputs.update-type == 'version-update:semver-patch'
        run: |
          gh pr review --approve "$PR_URL"
          gh pr merge --auto --squash "$PR_URL"
        env:
          PR_URL: ${{ github.event.pull_request.html_url }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Auto-merge GitHub Actions updates
        if: |
          steps.metadata.outputs.package-ecosystem == 'github-actions' &&
          (steps.metadata.outputs.update-type == 'version-update:semver-patch' ||
           steps.metadata.outputs.update-type == 'version-update:semver-minor')
        run: |
          gh pr review --approve "$PR_URL"
          gh pr merge --auto --squash "$PR_URL"
        env:
          PR_URL: ${{ github.event.pull_request.html_url }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Label major updates for manual review
        if: steps.metadata.outputs.update-type == 'version-update:semver-major'
        run: |
          gh pr edit "$PR_URL" --add-label "major-update,needs-review"
        env:
          PR_URL: ${{ github.event.pull_request.html_url }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

---

## 4. Automated Tasks - P1 High

### 4.1 Create PR Template

Create `.github/PULL_REQUEST_TEMPLATE.md`:

```markdown
## Summary

<!-- Brief description of changes -->

## Type of Change

- [ ] 🐛 Bug fix (non-breaking change fixing an issue)
- [ ] ✨ New feature (non-breaking change adding functionality)
- [ ] ♻️ Refactor (code change that neither fixes a bug nor adds a feature)
- [ ] 💥 Breaking change (fix or feature causing existing functionality to change)
- [ ] 📝 Documentation update
- [ ] 🧪 Test addition/update
- [ ] 🔧 CI/Build configuration

## CLAUDE.md Compliance Checklist

<!-- All items must be checked for PR to be merged -->

- [ ] No `var` declarations (explicit types only)
- [ ] No `if/else` blocks (ternary, switch expressions, pattern matching)
- [ ] Named parameters for non-obvious arguments
- [ ] Trailing commas on multi-line collections
- [ ] K&R brace style (opening brace on same line)
- [ ] File-scoped namespaces
- [ ] One type per file
- [ ] `Result<T>` for failable operations
- [ ] `E.*` for error creation

## Testing

- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` passes (all tests green)
- [ ] `dotnet format --verify-no-changes` passes
- [ ] New tests added for new functionality

## Related Issues

<!-- Use "Closes #123" to auto-close issues -->

Closes #

## Additional Notes

<!-- Any additional context, screenshots, or notes for reviewers -->

---

<!-- For AI Reviewers: Please evaluate this PR against the CLAUDE.md compliance checklist above. Flag any violations with specific line numbers and suggested fixes. -->
```

### 4.2 Create Issue Templates

Create `.github/ISSUE_TEMPLATE/bug_report.yml`:

```yaml
name: 🐛 Bug Report
description: Report a bug in Arsenal
labels: ["bug", "triage"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for taking the time to report a bug! Please fill out the form below.

  - type: dropdown
    id: area
    attributes:
      label: Affected Area
      description: Which part of the codebase is affected?
      options:
        - Core (libs/core)
        - Rhino (libs/rhino)
        - Grasshopper Components
        - Tests
        - CI/Build
        - Documentation
    validations:
      required: true

  - type: textarea
    id: description
    attributes:
      label: Bug Description
      description: A clear and concise description of the bug.
      placeholder: Describe the bug...
    validations:
      required: true

  - type: textarea
    id: reproduction
    attributes:
      label: Steps to Reproduce
      description: Steps to reproduce the behavior.
      placeholder: |
        1. Call `Analysis.Analyze()` with...
        2. Pass geometry type...
        3. Observe error...
    validations:
      required: true

  - type: textarea
    id: expected
    attributes:
      label: Expected Behavior
      description: What you expected to happen.
    validations:
      required: true

  - type: textarea
    id: actual
    attributes:
      label: Actual Behavior
      description: What actually happened.
    validations:
      required: true

  - type: input
    id: rhino-version
    attributes:
      label: Rhino Version
      description: If applicable, which version of Rhino?
      placeholder: "8.25.25328"

  - type: textarea
    id: environment
    attributes:
      label: Environment
      description: Any relevant environment details.
      placeholder: |
        - OS: Windows 11 / macOS 14
        - .NET: 8.0.x
        - IDE: VS 2022 / Rider

  - type: textarea
    id: logs
    attributes:
      label: Error Logs
      description: Any relevant error messages or stack traces.
      render: shell

  - type: checkboxes
    id: ai-appropriate
    attributes:
      label: AI Implementation
      description: Is this bug fix suitable for automated implementation?
      options:
        - label: This bug fix is suitable for Claude to implement automatically (add `claude-implement` label)
```

Create `.github/ISSUE_TEMPLATE/feature_request.yml`:

```yaml
name: ✨ Feature Request
description: Suggest a new feature for Arsenal
labels: ["enhancement"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for suggesting a new feature! Please describe what you'd like to see.

  - type: dropdown
    id: area
    attributes:
      label: Feature Area
      description: Which part of the codebase would this affect?
      options:
        - Core (libs/core)
        - Rhino (libs/rhino)
        - Grasshopper Components
        - Testing Infrastructure
        - CI/Build
        - Developer Experience
    validations:
      required: true

  - type: textarea
    id: problem
    attributes:
      label: Problem Statement
      description: What problem does this feature solve?
      placeholder: I'm always frustrated when...
    validations:
      required: true

  - type: textarea
    id: solution
    attributes:
      label: Proposed Solution
      description: Describe the solution you'd like.
    validations:
      required: true

  - type: textarea
    id: alternatives
    attributes:
      label: Alternatives Considered
      description: Any alternative solutions or features you've considered.

  - type: textarea
    id: api
    attributes:
      label: Proposed API (if applicable)
      description: If this involves new public APIs, sketch out what they might look like.
      render: csharp

  - type: dropdown
    id: complexity
    attributes:
      label: Estimated Complexity
      description: How complex do you think this feature would be?
      options:
        - Small (< 1 day)
        - Medium (1-3 days)
        - Large (1 week+)
        - Unknown

  - type: checkboxes
    id: ai-appropriate
    attributes:
      label: AI Implementation
      description: Is this feature suitable for automated implementation?
      options:
        - label: This feature is suitable for Claude to implement automatically (add `claude-implement` label)

  - type: checkboxes
    id: contribution
    attributes:
      label: Contribution
      options:
        - label: I'm willing to submit a PR for this feature
```

Create `.github/ISSUE_TEMPLATE/config.yml`:

```yaml
blank_issues_enabled: false
contact_links:
  - name: 💬 Discussions
    url: https://github.com/bsamiee/Parametric_Arsenal/discussions
    about: Use discussions for questions and general conversation
  - name: 📖 Documentation
    url: https://github.com/bsamiee/Parametric_Arsenal/tree/main/docs
    about: Check the documentation for usage guides
```

### 4.3 Create Labeler Workflow and Configuration

Create `.github/workflows/labeler.yml`:

```yaml
name: PR Labeler

on:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: read
  pull-requests: write

jobs:
  label:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Apply path-based labels
        uses: actions/labeler@v5
        with:
          repo-token: "${{ secrets.GITHUB_TOKEN }}"
          configuration-path: .github/labeler.yml
          sync-labels: false

      - name: Apply branch-based labels
        uses: TimonVS/pr-labeler-action@v5
        with:
          repo-token: ${{ secrets.GITHUB_TOKEN }}
          configuration-path: .github/pr-labeler.yml
```

Create `.github/labeler.yml`:

```yaml
# Path-based labeling for PRs

core:
  - changed-files:
    - any-glob-to-any-file: 'libs/core/**'

rhino:
  - changed-files:
    - any-glob-to-any-file: 'libs/rhino/**'

tests:
  - changed-files:
    - any-glob-to-any-file: 'test/**'

ci:
  - changed-files:
    - any-glob-to-any-file:
      - '.github/workflows/**'
      - '.github/actions/**'

config:
  - changed-files:
    - any-glob-to-any-file:
      - '*.props'
      - '*.targets'
      - '*.csproj'
      - '.editorconfig'
      - 'Directory.Build.props'

documentation:
  - changed-files:
    - any-glob-to-any-file:
      - 'docs/**'
      - '*.md'
      - 'README.md'

analyzer:
  - changed-files:
    - any-glob-to-any-file:
      - '**/.editorconfig'
      - '**/Directory.Build.props'
      - '**/Directory.Packages.props'

dependencies:
  - changed-files:
    - any-glob-to-any-file:
      - '**/packages.lock.json'
      - '**/*.csproj'
```

Create `.github/pr-labeler.yml`:

```yaml
# Branch-based labeling

feature: ['feature/*', 'feat/*']
bugfix: ['fix/*', 'bugfix/*', 'bug/*']
hotfix: ['hotfix/*']
refactor: ['refactor/*', 'cleanup/*']
documentation: ['docs/*', 'doc/*']
test: ['test/*', 'tests/*']
ci: ['ci/*', 'build/*']
claude-generated: ['claude/*']
copilot-generated: ['copilot/*']
dependabot: ['dependabot/*']
```

### 4.4 Enhanced dependabot.yml

Replace `.github/dependabot.yml`:

```yaml
version: 2

registries:
  nuget-org:
    type: nuget-feed
    url: https://api.nuget.org/v3/index.json

updates:
  # NuGet packages
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "daily"
      time: "06:00"
      timezone: "America/Chicago"
    open-pull-requests-limit: 10
    registries:
      - nuget-org
    labels:
      - "dependencies"
      - "nuget"
    commit-message:
      prefix: "deps(nuget)"
      include: "scope"
    groups:
      # Analyzer packages - safe to update together
      analyzers:
        patterns:
          - "Roslynator*"
          - "Meziantou*"
          - "*Analyzer*"
          - "AsyncFixer"
          - "ReflectionAnalyzers"
          - "Nullable.Extended*"
          - "Microsoft.CodeAnalysis*"
        update-types:
          - "minor"
          - "patch"
      # Testing packages
      testing:
        patterns:
          - "xunit*"
          - "NUnit*"
          - "CsCheck*"
          - "coverlet*"
          - "Microsoft.NET.Test.Sdk"
          - "Rhino.Testing"
        update-types:
          - "minor"
          - "patch"
      # Rhino SDK - handle carefully
      rhino-sdk:
        patterns:
          - "RhinoCommon"
          - "Grasshopper"
    ignore:
      # Major Rhino updates need manual review
      - dependency-name: "RhinoCommon"
        update-types: ["version-update:semver-major"]
      - dependency-name: "Grasshopper"
        update-types: ["version-update:semver-major"]

  # GitHub Actions
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "daily"
      time: "06:00"
      timezone: "America/Chicago"
    labels:
      - "dependencies"
      - "ci"
      - "github-actions"
    commit-message:
      prefix: "ci(actions)"
    groups:
      # Group all actions together
      actions:
        patterns:
          - "*"
        update-types:
          - "minor"
          - "patch"
```

---

## 5. Automated Tasks - P2 Medium

### 5.1 Create Review Aggregation Workflow

Create `.github/workflows/review-summary.yml`:

```yaml
name: AI Review Summary

on:
  # Trigger after reviews are submitted
  pull_request_review:
    types: [submitted]
  # Also allow manual trigger
  workflow_dispatch:
    inputs:
      pr_number:
        description: 'PR number to summarize'
        required: true
        type: number

concurrency:
  group: review-summary-${{ github.event.pull_request.number || inputs.pr_number }}
  cancel-in-progress: true

jobs:
  aggregate-reviews:
    runs-on: ubuntu-latest
    # Only run when we have multiple reviews or on manual trigger
    if: |
      github.event_name == 'workflow_dispatch' ||
      (github.event_name == 'pull_request_review' &&
       github.event.review.state != 'commented')

    permissions:
      pull-requests: write
      contents: read

    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Get PR number
        id: pr
        run: |
          if [ "${{ github.event_name }}" == "workflow_dispatch" ]; then
            echo "number=${{ inputs.pr_number }}" >> $GITHUB_OUTPUT
          else
            echo "number=${{ github.event.pull_request.number }}" >> $GITHUB_OUTPUT
          fi

      - name: Fetch all reviews
        id: reviews
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          PR_NUM=${{ steps.pr.outputs.number }}

          # Get all reviews
          gh api repos/${{ github.repository }}/pulls/${PR_NUM}/reviews \
            --jq '[.[] | {user: .user.login, state: .state, body: .body}]' > reviews.json

          # Get all review comments
          gh api repos/${{ github.repository }}/pulls/${PR_NUM}/comments \
            --jq '[.[] | {user: .user.login, body: .body, path: .path, line: .line}]' > comments.json

          # Count reviews by bot
          echo "review_count=$(cat reviews.json | jq length)" >> $GITHUB_OUTPUT
          echo "comment_count=$(cat comments.json | jq length)" >> $GITHUB_OUTPUT

      - name: Check if summary needed
        id: check
        run: |
          # Only create summary if we have reviews from multiple sources
          REVIEWERS=$(cat reviews.json | jq -r '[.[].user] | unique | length')
          if [ "$REVIEWERS" -ge 2 ]; then
            echo "should_summarize=true" >> $GITHUB_OUTPUT
          else
            echo "should_summarize=false" >> $GITHUB_OUTPUT
          fi

      - name: Generate summary with Claude
        if: steps.check.outputs.should_summarize == 'true'
        uses: anthropics/claude-code-action@v1
        with:
          claude_code_oauth_token: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}
          prompt: |
            Analyze the following PR reviews and comments, then create a consolidated summary.

            ## Reviews
            $(cat reviews.json)

            ## Review Comments
            $(cat comments.json)

            ## Task
            Create a summary comment for PR #${{ steps.pr.outputs.number }} that:

            1. **Consensus Issues** - Things multiple reviewers flagged
            2. **Critical Issues** - Blocking problems that must be fixed
            3. **Style Violations** - CLAUDE.md compliance issues
            4. **Suggestions** - Optional improvements
            5. **Conflicts** - Where reviewers disagree

            Format as a clear, actionable GitHub comment.
            Use `gh pr comment ${{ steps.pr.outputs.number }} --body "..."` to post.

          claude_args: |
            --model claude-sonnet-4-5-20250929
            --max-turns 5
            --allowedTools Bash(gh:*)
```

### 5.2 Add CodeQL Security Scanning

Create `.github/workflows/codeql.yml`:

```yaml
name: CodeQL Security Analysis

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
    paths:
      - '**/*.cs'
      - '**/*.csproj'
  schedule:
    # Run weekly on Monday at midnight
    - cron: '0 0 * * 1'

permissions:
  security-events: write
  contents: read
  actions: read

jobs:
  analyze:
    name: Analyze C#
    runs-on: ubuntu-latest
    timeout-minutes: 30

    steps:
      - name: Checkout repository
        uses: actions/checkout@v6

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: csharp
          queries: +security-extended,security-and-quality

      - name: Restore dependencies
        run: dotnet restore Parametric_Arsenal.sln

      - name: Build for CodeQL
        run: dotnet build Parametric_Arsenal.sln --configuration Release --no-restore

      - name: Perform CodeQL Analysis
        uses: github/codeql-action/analyze@v3
        with:
          category: "/language:csharp"
```

### 5.3 Enhanced Pre-commit Configuration

Replace `.pre-commit-config.yaml`:

```yaml
default_language_version:
  python: python3

fail_fast: false

repos:
  # Standard pre-commit hooks
  - repo: https://github.com/pre-commit/pre-commit-hooks
    rev: v5.0.0
    hooks:
      - id: trailing-whitespace
        args: ['--markdown-linebreak-ext=md']
      - id: end-of-file-fixer
      - id: mixed-line-ending
        args: ['--fix=lf']
      - id: check-yaml
        args: ['--unsafe']
      - id: check-json
      - id: check-toml
      - id: check-xml
      - id: check-merge-conflict
      - id: check-added-large-files
        args: ['--maxkb=500']
      - id: detect-private-key

  # .NET specific hooks
  - repo: local
    hooks:
      # Build with full analyzer enforcement
      - id: dotnet-build
        name: .NET Build with Analyzers
        entry: dotnet build
        args:
          - 'Parametric_Arsenal.sln'
          - '--configuration'
          - 'Release'
          - '-p:EnforceCodeStyleInBuild=true'
          - '-p:TreatWarningsAsErrors=true'
          - '-warnaserror'
        language: system
        files: '\.(cs|csproj|props|targets)$'
        pass_filenames: false
        stages: [pre-commit]

      # Format verification
      - id: dotnet-format
        name: .NET Format Check
        entry: dotnet format
        args:
          - 'Parametric_Arsenal.sln'
          - '--verify-no-changes'
          - '--verbosity'
          - 'quiet'
        language: system
        files: '\.cs$'
        pass_filenames: false
        stages: [pre-commit]

      # Custom check for 'var' keyword (critical violation)
      - id: no-var-check
        name: No 'var' declarations
        entry: bash -c 'if grep -rn --include="*.cs" "\\bvar\\s" libs/; then echo "ERROR: var keyword found - use explicit types"; exit 1; fi'
        language: system
        files: '\.cs$'
        pass_filenames: false
        stages: [pre-commit]

      # Custom check for if/else blocks
      - id: no-if-else-check
        name: No if/else blocks
        entry: bash -c 'if grep -Pzn --include="*.cs" "if\s*\([^)]+\)\s*\{[^}]*\}\s*else\s*\{" libs/; then echo "WARNING: if/else blocks found - prefer ternary/switch"; fi; exit 0'
        language: system
        files: '\.cs$'
        pass_filenames: false
        stages: [pre-commit]

  # Commitizen for conventional commits
  - repo: https://github.com/commitizen-tools/commitizen
    rev: v4.1.0
    hooks:
      - id: commitizen
        stages: [commit-msg]

ci:
  autofix_commit_msg: 'style: auto-fix pre-commit hooks'
  autofix_prs: true
  autoupdate_commit_msg: 'chore: update pre-commit hooks'
  autoupdate_schedule: monthly
  skip:
    # Skip .NET hooks in pre-commit.ci (no .NET SDK)
    - dotnet-build
    - dotnet-format
    - no-var-check
    - no-if-else-check
```

### 5.4 Add Stale Management Workflow

Create `.github/workflows/stale.yml`:

```yaml
name: Stale Issue/PR Management

on:
  schedule:
    # Run daily at midnight
    - cron: '0 0 * * *'
  workflow_dispatch:

permissions:
  issues: write
  pull-requests: write

jobs:
  stale:
    runs-on: ubuntu-latest
    steps:
      - name: Mark stale issues and PRs
        uses: actions/stale@v9
        with:
          # Issue configuration
          stale-issue-message: |
            This issue has been automatically marked as stale because it has not had recent activity.

            It will be closed in 7 days if no further activity occurs.

            If this issue is still relevant:
            - Add a comment with an update
            - Remove the `stale` label

            Thank you for your contributions! 🙏
          close-issue-message: |
            This issue has been automatically closed due to inactivity.

            Feel free to reopen if this is still relevant, or create a new issue with updated information.
          stale-issue-label: 'stale'
          days-before-issue-stale: 30
          days-before-issue-close: 7
          exempt-issue-labels: |
            pinned
            in-progress
            blocked
            claude-implement
            help-wanted
            good-first-issue

          # PR configuration
          stale-pr-message: |
            This PR has been automatically marked as stale because it has not had recent activity.

            It will be closed in 7 days if no further activity occurs.

            If this PR is still needed:
            - Rebase on main and push changes
            - Add a comment with a status update
            - Remove the `stale` label
          close-pr-message: |
            This PR has been automatically closed due to inactivity.

            Feel free to reopen or create a new PR if you'd like to continue this work.
          stale-pr-label: 'stale'
          days-before-pr-stale: 14
          days-before-pr-close: 7
          exempt-pr-labels: |
            in-progress
            blocked
            claude-working
            do-not-close

          # General configuration
          operations-per-run: 50
          remove-stale-when-updated: true
          delete-branch: false
```

---

## 6. Automated Tasks - P3 Low

### 6.1 Add Codecov Integration

Update `.github/workflows/ci.yml` to add coverage reporting:

Add these steps after the test step:

```yaml
      - name: Upload Coverage to Codecov
        if: always()
        uses: codecov/codecov-action@v4
        with:
          files: '**/TestResults/**/coverage.cobertura.xml'
          flags: unittests
          name: codecov-arsenal
          fail_ci_if_error: false
          verbose: true
        env:
          CODECOV_TOKEN: ${{ secrets.CODECOV_TOKEN }}

      - name: Generate Coverage Summary
        if: always()
        uses: irongut/CodeCoverageSummary@v1.3.0
        with:
          filename: '**/TestResults/**/coverage.cobertura.xml'
          badge: true
          format: markdown
          output: both
          indicators: true
          thresholds: '60 80'

      - name: Add Coverage to PR
        if: github.event_name == 'pull_request' && always()
        uses: marocchino/sticky-pull-request-comment@v2
        with:
          recreate: true
          path: code-coverage-results.md
```

### 6.2 Create Additional Documentation Files

#### `docs/workflows/PR_CHECKLIST.md`

```markdown
# PR Review Checklist

## For Human Reviewers

### Code Quality
- [ ] Logic is correct and handles edge cases
- [ ] No unnecessary complexity
- [ ] Appropriate error handling with Result<T>
- [ ] Performance considerations addressed

### Style Compliance (CLAUDE.md)
- [ ] No `var` declarations
- [ ] No `if/else` blocks (ternary/switch only)
- [ ] Trailing commas on multi-line collections
- [ ] Named parameters for non-obvious args
- [ ] K&R brace style
- [ ] File-scoped namespaces
- [ ] One type per file

### Architecture
- [ ] Result<T> for failable operations
- [ ] E.* for error creation
- [ ] UnifiedOperation for polymorphic dispatch
- [ ] Appropriate abstraction level

### Testing
- [ ] Tests cover new functionality
- [ ] Tests cover failure cases
- [ ] Property-based tests where applicable
- [ ] Tests follow naming convention

### Documentation
- [ ] XML docs for public APIs
- [ ] README updates if needed
- [ ] CHANGELOG entry if significant

## For AI Reviewers

When reviewing PRs, check for these specific patterns:

```csharp
// ❌ Flag these patterns
var x = ...;                    // Use explicit type
if (...) { } else { }          // Use ternary/switch
new List<T> { ... }            // Use collection expression []
public void Method()           // Check K&R braces
{
namespace X {                  // Use file-scoped namespace;
throw new Exception(...);      // Use Result<T>.Failure(E.*)
```

Provide specific line numbers and suggested fixes for each violation.
```

#### `docs/workflows/IMPLEMENTATION_FLOW.md`

```markdown
# Implementation Flow for Claude

When implementing issues labeled `claude-implement`, follow this workflow:

## 1. Preparation

```bash
# Ensure clean state
git fetch origin
git checkout main
git pull origin main

# Create feature branch
git checkout -b claude/issue-{number}-{brief-description}
```

## 2. Understand the Task

1. Read the issue description completely
2. Read `docs/AGENTS.md` for style rules
3. Read `docs/exemplars/IDEAL_CLASS.md` for patterns
4. Identify affected files and dependencies
5. Check existing tests for the area

## 3. Implementation

1. Make minimal, focused changes
2. Follow all CLAUDE.md style rules:
   - Explicit types (no var)
   - Ternary/switch (no if/else)
   - Result<T> for failable ops
   - Named parameters
   - Trailing commas
   - K&R braces

## 4. Verification

```bash
# Must pass with zero warnings
dotnet build Parametric_Arsenal.sln -c Release -p:TreatWarningsAsErrors=true

# Must pass all tests
dotnet test Parametric_Arsenal.sln -c Release

# Must pass format check
dotnet format Parametric_Arsenal.sln --verify-no-changes
```

## 5. Create PR

```bash
git add -A
git commit -m "feat(area): description

Closes #{issue_number}"

git push origin HEAD
```

Create PR with:
```bash
gh pr create \
  --title "feat(area): brief description" \
  --body "Closes #{issue_number}

## Changes
- List of changes

## Checklist
- [x] No var declarations
- [x] No if/else blocks
- [x] Tests added
- [x] Build passes
- [x] Format passes"
```

## 6. Address Review Feedback

If reviewers request changes:
1. Make the requested changes
2. Re-run verification steps
3. Push additional commits
4. Reply to review comments
```

---

## 7. Manual Tasks - Requires Human Action

These tasks cannot be automated and require manual configuration in GitHub settings or external services.

### 7.1 GitHub Repository Settings

#### Enable Auto-merge
1. Go to **Settings** → **General** → **Pull Requests**
2. Check **Allow auto-merge**
3. Check **Automatically delete head branches**

#### Enable Branch Protection
1. Go to **Settings** → **Branches** → **Add rule**
2. Branch name pattern: `main`
3. Enable:
   - **Require a pull request before merging**
   - **Require approvals** (1)
   - **Require status checks to pass**
     - Add: `Build & Test`
     - Add: `Rhino Tests` (if applicable)
   - **Require conversation resolution**
   - **Do not allow bypassing the above settings**

#### Enable GitHub Actions Permissions
1. Go to **Settings** → **Actions** → **General**
2. Under **Workflow permissions**:
   - Select **Read and write permissions**
   - Check **Allow GitHub Actions to create and approve pull requests**

### 7.2 Add Repository Secrets

Navigate to **Settings** → **Secrets and variables** → **Actions** → **New repository secret**

| Secret Name | Description | Required For |
|-------------|-------------|--------------|
| `CLAUDE_CODE_OAUTH_TOKEN` | Claude Code OAuth token | Claude workflows |
| `CODECOV_TOKEN` | Codecov upload token | Coverage reporting |
| `RHINO_TOKEN` | Rhino license token | Rhino tests |
| `RHINO_EMAIL` | Rhino account email | Rhino tests |

### 7.3 Enable GitHub Advanced Security (Optional)

If you have GitHub Enterprise or Advanced Security:

1. Go to **Settings** → **Code security and analysis**
2. Enable:
   - **Dependency graph** ✓
   - **Dependabot alerts** ✓
   - **Dependabot security updates** ✓
   - **Code scanning** ✓ (uses CodeQL workflow)
   - **Secret scanning** ✓

### 7.4 Configure External Services

#### Codecov Setup
1. Go to [codecov.io](https://codecov.io)
2. Sign in with GitHub
3. Add the repository
4. Copy the upload token
5. Add as `CODECOV_TOKEN` secret

#### Pre-commit.ci Setup (Optional)
1. Go to [pre-commit.ci](https://pre-commit.ci)
2. Sign in with GitHub
3. Enable for the repository
4. Auto-fixes will be applied on PRs

### 7.5 Create Required Labels

Create these labels in **Issues** → **Labels** → **New label**:

| Label | Color | Description |
|-------|-------|-------------|
| `claude-implement` | `#7057ff` | Issue suitable for Claude implementation |
| `claude-working` | `#a371f7` | Claude is actively working on this |
| `copilot-fix` | `#238636` | Copilot suggested fix |
| `needs-review` | `#d93f0b` | Needs human review |
| `major-update` | `#b60205` | Major version update |
| `stale` | `#ffffff` | Stale issue/PR |
| `core` | `#1d76db` | Affects libs/core |
| `rhino` | `#0e8a16` | Affects libs/rhino |
| `ci` | `#fbca04` | CI/CD related |
| `documentation` | `#0075ca` | Documentation changes |
| `dependencies` | `#0366d6` | Dependency updates |
| `breaking-change` | `#b60205` | Breaking change |

### 7.6 Update Claude Workflow Prompts

After creating the `docs/` structure, update your Claude workflow files to reference it:

In `claude.yml`, `claude-issues.yml`, `claude-code-review.yml`:

```yaml
claude_args: |
  --model claude-opus-4-5-20251101
  --max-turns 15
  --append-system-prompt "CRITICAL: Before any code changes:
  1. Read docs/AGENTS.md for complete style guide
  2. Read docs/exemplars/IDEAL_CLASS.md for reference implementation
  3. Follow CLAUDE.md exactly - build fails on style violations

  Key rules: NO var, NO if/else, explicit types, Result<T> for errors.

  Run 'dotnet build -c Release -p:TreatWarningsAsErrors=true' to verify."
```

---

## 8. Reference: Existing Configuration Analysis

### Current `Directory.Build.props` Strengths

Your existing configuration is excellent:

```xml
<!-- Already configured correctly -->
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<AnalysisLevel>latest-all</AnalysisLevel>
<AnalysisMode>All</AnalysisMode>
<LangVersion>preview</LangVersion>
<Nullable>enable</Nullable>
```

### Analyzer Stack (Already Configured)

- ✅ Roslynator.Analyzers
- ✅ Meziantou.Analyzer
- ✅ Microsoft.CodeAnalysis.NetAnalyzers
- ✅ AsyncFixer
- ✅ ReflectionAnalyzers
- ✅ Nullable.Extended.Analyzer

### Recommended Addition to `Directory.Build.props`

```xml
<!-- Add security analyzer -->
<PackageReference Include="SecurityCodeScan.VS2019" Version="5.6.7" PrivateAssets="all" />

<!-- Add code metrics -->
<PackageReference Include="Microsoft.CodeAnalysis.Metrics" Version="3.11.0" PrivateAssets="all" />
```

---

## Summary: File Creation Checklist

### New Files to Create

```
docs/
├── AGENTS.md                           ✅ P0
├── architecture/
│   ├── CODEBASE_MAP.md                 ✅ P0
│   └── PATTERNS.md                     ✅ P0
├── conventions/
│   └── ERROR_HANDLING.md               ✅ P0
├── exemplars/
│   ├── IDEAL_CLASS.md                  ✅ P0
│   └── IDEAL_TEST.md                   ✅ P3
├── rhino/
│   └── SDK_REFERENCE.md                ✅ P3
└── workflows/
    ├── PR_CHECKLIST.md                 ✅ P3
    └── IMPLEMENTATION_FLOW.md          ✅ P3

.github/
├── copilot-instructions.md             ✅ P0
├── PULL_REQUEST_TEMPLATE.md            ✅ P1
├── ISSUE_TEMPLATE/
│   ├── bug_report.yml                  ✅ P1
│   ├── feature_request.yml             ✅ P1
│   └── config.yml                      ✅ P1
├── labeler.yml                         ✅ P1
├── pr-labeler.yml                      ✅ P1
├── workflows/
│   ├── dependabot-auto-merge.yml       ✅ P0
│   ├── labeler.yml                     ✅ P1
│   ├── review-summary.yml              ✅ P2
│   ├── codeql.yml                      ✅ P2
│   └── stale.yml                       ✅ P2
└── dependabot.yml                      ✅ P1 (replace existing)

.pre-commit-config.yaml                 ✅ P2 (replace existing)
```

---

**End of Document**

*Generated for Parametric_Arsenal enhancement project*
*Last Updated: 2025-11-25*

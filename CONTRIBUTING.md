# Contributing to Parametric Arsenal

**Welcome!** Thank you for considering contributing to Parametric Arsenal. This document provides everything you need to know to contribute effectively to this project.

---

## 📋 Table of Contents

1. [Quick Start](#quick-start)
2. [Development Environment](#development-environment)
3. [Code Standards](#code-standards)
4. [Workflow Overview](#workflow-overview)
5. [Quality Gates](#quality-gates)
6. [Testing](#testing)
7. [Issue Templates](#issue-templates)
8. [Pull Request Process](#pull-request-process)
9. [Agentic Workflows](#agentic-workflows)
10. [Getting Help](#getting-help)

---

## Quick Start

```bash
# 1. Clone the repository
git clone https://github.com/bsamiee/Parametric_Arsenal.git
cd Parametric_Arsenal

# 2. Install .NET 8 SDK
# Download from https://dotnet.microsoft.com/download/dotnet/8.0

# 3. Restore dependencies
dotnet restore

# 4. Build the solution
dotnet build

# 5. Run tests
dotnet test

# 6. (Optional) Install pre-commit hooks
pip install pre-commit
pre-commit install
```

**That's it!** You're ready to start contributing.

---

## Development Environment

### Required Tools

| Tool | Version | Purpose |
|------|---------|---------|
| **.NET SDK** | 8.0+ | Core compilation and testing |
| **Git** | Latest | Version control |
| **Git LFS** | Latest | Large file storage |
| **Pre-commit** | Latest (optional) | Local quality gates |

### Optional Tools

- **Rhino 8** (for `libs/rhino` and `test/rhino` work)
- **Python 3.13+** (for Python plugin development)
- **Visual Studio 2022** or **JetBrains Rider** (IDE support)

### Project Structure

```
Parametric_Arsenal/
├── libs/                      # Core libraries
│   ├── core/                  # Pure C# primitives (Result, UnifiedOperation, etc.)
│   ├── rhino/                 # RhinoCommon geometry operations
│   └── grasshopper/           # Grasshopper components (future)
├── test/
│   ├── core/                  # xUnit + CsCheck property-based tests
│   └── rhino/                 # NUnit + Rhino.Testing integration tests
├── tools/                     # Build and generation tools
│   ├── ContextGen/            # Roslyn-based context generator
│   └── standards/             # STANDARDS.yaml + generator script
├── docs/                      # Documentation and agent context
│   ├── agent-context/         # Generated JSON context files
│   └── agentic-design/        # Strategic analysis documents
├── rhino/plugins/             # Python Rhino plugins
├── .github/
│   ├── agents/                # Custom agent definitions
│   ├── workflows/             # CI/CD automation
│   ├── ISSUE_TEMPLATE/        # Structured issue templates
│   └── prompts/               # Reusable prompt templates
├── CLAUDE.md                  # Comprehensive coding standards
└── AGENTS.md                  # Quick reference for agents
```

---

## Code Standards

**Parametric Arsenal enforces extremely strict code quality standards.** These are **non-negotiable** and enforced by build analyzers.

### Critical Rules (Build Failures)

1. **NO `var` EVER** → Always explicit types
2. **NO `if`/`else` STATEMENTS** → Use expressions (ternary, switch expressions, pattern matching)
3. **NO helper methods** → Algorithmic density (max 300 LOC/member)
4. **ONE type per file** → CA1050 analyzer enforces this
5. **ALWAYS named parameters** → For non-obvious arguments
6. **ALWAYS trailing commas** → Multi-line collections
7. **ALWAYS target-typed `new()`** → When type is known
8. **ALWAYS collection expressions `[]`** → Not `new List<T>()`
9. **ALWAYS file-scoped namespaces** → `namespace X;` not `namespace X { }`
10. **K&R brace style** → Opening brace on same line

### Organizational Limits

**ABSOLUTE MAXIMUMS** (violations are unacceptable):
- **4 files maximum** per folder
- **10 types maximum** per folder
- **300 LOC maximum** per member

**IDEAL TARGETS**:
- **2-3 files** per folder
- **6-8 types** per folder
- **150-250 LOC** per member

### Architecture Patterns

All code must follow these architectural patterns:

#### Result Monad (All Error Handling)
```csharp
// ✅ CORRECT
Result<Point3d> result = ResultFactory.Create(value: point);
return result
    .Ensure(p => p.IsValid, error: E.Validation.GeometryInvalid)
    .Map(p => Transform(p));

// ❌ WRONG - Never throw exceptions for control flow
throw new InvalidOperationException("Point is invalid");
```

#### UnifiedOperation (Polymorphic Dispatch)
```csharp
// ✅ CORRECT
return UnifiedOperation.Apply(
    input: data,
    operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)(item => item switch {
        Point3d p => ProcessPoint(p),
        Curve c => ProcessCurve(c),
        _ => ResultFactory.Create<IReadOnlyList<TOut>>(error: E.Geometry.Unsupported),
    }),
    config: new OperationConfig<TIn, TOut> {
        Context = context,
        ValidationMode = V.Standard,
    });
```

#### Conditional Expressions (Critical)
```csharp
// ✅ CORRECT - Ternary for binary choice
return count > 0
    ? ProcessItems(items)
    : ResultFactory.Create(error: E.Validation.Empty);

// ✅ CORRECT - Switch expression for multiple branches
return count switch {
    0 => ResultFactory.Create(error: E.Validation.Empty),
    1 => ProcessSingle(items[0]),
    _ => ProcessMultiple(items),
};

// ❌ WRONG - if/else statements forbidden
if (count > 0) {
    return ProcessItems(items);
} else {
    return ResultFactory.Create(error: E.Validation.Empty);
}
```

**For detailed patterns and examples, see [`CLAUDE.md`](CLAUDE.md).**

---

## Workflow Overview

### 1. Local Development

```bash
# Create feature branch
git checkout -b feature/your-feature-name

# Make changes (study exemplar files first!)
# See libs/core/results/Result.cs, libs/core/operations/UnifiedOperation.cs

# Run local checks
dotnet build
dotnet format --verify-no-changes
dotnet test

# Commit with conventional commit message
git commit -m "feat(core): add new validation mode"
```

### 2. Pre-commit Hooks (Optional)

We use `pre-commit` for local quality gates:

```bash
pip install pre-commit
pre-commit install
```

Hooks run automatically on `git commit`:
- Trailing whitespace removal
- YAML/JSON validation
- `dotnet build` with analyzers
- `dotnet format` verification

### 3. CI Pipeline

On push, GitHub Actions runs:

| Check | Tool | Timeout |
|-------|------|---------|
| **EditorConfig** | editorconfig-checker | 10min |
| **Formatting** | dotnet format | 10min |
| **Build** | dotnet build (Release) | 10min |
| **Analyzers** | 6 analyzer packages | 10min |
| **Core Tests** | xUnit + CsCheck | 10min |
| **Rhino Tests** | NUnit + Rhino.Testing | 20min |
| **Coverage** | XPlat Code Coverage | 10min |

---

## Quality Gates

### Build Analyzers (Enforced)

The following analyzer packages are enabled with `TreatWarningsAsErrors=true`:

1. **Roslynator.Analyzers** (4.14.1)
2. **Meziantou.Analyzer** (2.0.256)
3. **Microsoft.CodeAnalysis.NetAnalyzers** (10.0.100)
4. **AsyncFixer** (1.6.0)
5. **ReflectionAnalyzers** (0.3.1)
6. **Nullable.Extended.Analyzer** (1.15.6581)

All violations fail the build. No suppressions allowed without justification.

### Code Review Gates

PRs require:
1. **Passing CI** (all checks green)
2. **Agentic review** (`claude-code-review.yml` runs automatically)
3. **No analyzer violations**
4. **80% code coverage** (for new code)
5. **CLAUDE.md compliance** (verified by review bot)

### Auto-Fix Loop

If the agentic review requests changes:
1. `claude-autofix.yml` applies suggested fixes automatically
2. Commits and pushes changes
3. Re-triggers review
4. **Max 3 iterations** before requiring human intervention

### Auto-Merge

PRs are auto-merged when:
- Created by `claude[bot]` OR labeled `auto-merge`
- All required checks pass
- Review is approved
- Branch is up to date

---

## Testing

### Test Structure

| Project | Framework | Purpose |
|---------|-----------|---------|
| `test/core/` | xUnit + CsCheck | Property-based tests for pure functions |
| `test/rhino/` | NUnit + Rhino.Testing | Integration tests for RhinoCommon |

### Writing Tests

#### Core Tests (xUnit + CsCheck)
```csharp
[Fact]
public void Result_Map_Identity_Law() =>
    Gen.Int.Sample(x => {
        Result<int> result = ResultFactory.Create(value: x);
        Result<int> mapped = result.Map(v => v);
        Assert.Equal(result, mapped);
    });
```

#### Rhino Tests (NUnit)
```csharp
[Test]
public void Spatial_PointCloud_SphereQuery_ReturnsIndices() {
    // Arrange
    Point3d[] points = [
        new Point3d(0, 0, 0),
        new Point3d(1, 0, 0),
        new Point3d(10, 10, 10),
    ];
    PointCloud cloud = new(points);
    Sphere query = new(new Point3d(0, 0, 0), radius: 2.0);
    IGeometryContext context = new GeometryContext();

    // Act
    Result<IReadOnlyList<int>> result = Spatial.Analyze(cloud, query, context);

    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Count, Is.EqualTo(2));
}
```

### Running Tests

```bash
# All tests
dotnet test

# Core tests only
dotnet test test/core/Arsenal.Core.Tests.csproj

# Rhino tests only (requires Rhino 8)
dotnet test test/rhino/Arsenal.Rhino.Tests.csproj

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Specific test
dotnet test --filter "FullyQualifiedName~Result_Map"
```

---

## Issue Templates

We provide structured issue templates to enable agentic workflows.

### Feature Request (`feature-claude.yml`)

Use this template for new features. It includes dropdowns for:

- **Scope**: Which `libs/` domain (core, rhino, grasshopper, etc.)
- **Complexity**: trivial/medium/hard/expert
- **Agent**: Which specialist agent should handle this (auto-detect, csharp-advanced, etc.)
- **Validation Mode**: V.None through V.All

**Creating a feature request:**
1. Go to [Issues → New Issue](https://github.com/bsamiee/Parametric_Arsenal/issues/new/choose)
2. Select "Feature Request (Claude-Ready)"
3. Fill in all required fields
4. Submit

The `claude-issues` workflow will automatically:
- Assign the recommended agent
- Load relevant context files
- Generate an implementation plan
- Create a PR with changes

### Bug Report (`bug-report.yml`) - Coming Soon

For reporting bugs with structured information.

### Maintenance (`maintenance.yml`) - Coming Soon

For maintenance tasks like refactoring or documentation updates.

---

## Pull Request Process

### Creating a PR

1. **Push your feature branch** to GitHub
2. **Open a PR** against `main`
3. **Fill out the PR template** completely:
   - Summary of changes
   - Related issue number
   - Change type (checkboxes)
   - Verification checklist (CLAUDE.md compliance)
   - Test plan

### PR Template Structure

The template includes:

```markdown
## Summary
[Brief description]

## Related Issue
Closes #123

## Agent Metadata
<!-- AGENT_REVIEW_CONFIG {"auto_merge_eligible": false, ...} -->

## Change Type
- [ ] Feature
- [ ] Bug fix
- [ ] Refactoring
- [ ] Documentation

## Verification Checklist
### Build & Test
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` all pass
- [ ] No analyzer warnings

### CLAUDE.md Compliance
- [ ] No `var` usage
- [ ] No `if`/`else` statements
- [ ] Named parameters for non-obvious args
- [ ] Trailing commas in collections
- [ ] One type per file
- [ ] File-scoped namespaces
- [ ] K&R brace style
- [ ] Result<T> for error handling
- [ ] UnifiedOperation for polymorphic dispatch
- [ ] Members ≤ 300 LOC

### Architecture
- [ ] Operations return Result<T>
- [ ] Errors use E.* constants
- [ ] Validation uses V.* flags
- [ ] No handrolled validation

## Test Plan
[Describe how you tested]
```

### Review Process

1. **Automated Review**: `claude-code-review.yml` runs automatically
   - Checks CLAUDE.md compliance
   - Generates structured JSON output
   - Posts review comments or approval

2. **Auto-Fix** (if needed): `claude-autofix.yml` applies fixes
   - Reads violations from review JSON
   - Applies suggested changes
   - Commits and pushes
   - Re-triggers review (max 3 iterations)

3. **Auto-Merge**: `auto-merge.yml` merges when ready
   - All checks pass
   - Review approved
   - Branch up to date
   - Deletes branch after merge

### Review Iteration Limits

To prevent infinite loops:
- **Maximum 3 auto-fix iterations** per PR
- After 3 iterations, manual review required
- Tracked via `autofix-attempt-N` labels

---

## Agentic Workflows

Parametric Arsenal uses GitHub Actions + Claude AI for autonomous development.

### Available Workflows

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| **claude-issues** | Issue labeled `claude` | Implements feature from issue |
| **claude-code-review** | PR opened/updated | Reviews code against CLAUDE.md |
| **claude-autofix** | Review requests changes | Applies fixes automatically |
| **auto-merge** | PR ready | Merges approved PRs |
| **context-gen** | Push to `libs/` | Regenerates JSON context |
| **standards-sync** | Push to STANDARDS.yaml | Syncs protocol files |

### Custom Agents

11 specialist agents available for specific tasks:

1. **csharp-advanced** - Dense, algebraic C# code
2. **testing-specialist** - xUnit + CsCheck + Rhino.Testing
3. **refactoring-architect** - Holistic refactoring
4. **rhino-implementation** - RhinoCommon SDK integration
5. **grasshopper-implementation** - GH_Component patterns
6. **performance-analyst** - Optimization opportunities
7. **documentation-specialist** - Docs consistency
8. **integration-specialist** - libs/core integration
9. **cleanup-specialist** - Code cleanup and deduplication
10. **library-planner** - New libs/ folder planning
11. **plugin-architect** - Rhino plugin architecture

Agents are selected based on issue metadata or auto-detected from context.

### Agent Context Files

Generated JSON files in `docs/agent-context/`:

- **architecture.json** - Project structure, namespaces, types
- **error-catalog.json** - E.* error constants
- **validation-modes.json** - V.* validation flags
- **exemplar-metrics.json** - Exemplar file metrics
- **domain-map.json** - libs/rhino/* domain mapping

These files are regenerated automatically on code changes.

---

## Getting Help

### Documentation

1. **[CLAUDE.md](CLAUDE.md)** - Comprehensive coding standards (mandatory reading)
2. **[AGENTS.md](AGENTS.md)** - Quick reference for agents
3. **[.github/copilot-instructions.md](.github/copilot-instructions.md)** - IDE integration guide
4. **[docs/agentic-design/ANALYSIS.md](docs/agentic-design/ANALYSIS.md)** - Strategic analysis
5. **[docs/agentic-design/TASK_FINAL.md](docs/agentic-design/TASK_FINAL.md)** - Implementation plan

### Learning Progression

**Stage 1: Understanding Result Monad** (Required)
1. Read `libs/core/results/Result.cs`
2. Read `libs/core/results/ResultFactory.cs`
3. Practice chaining: Map → Bind → Ensure → Match

**Stage 2: Mastering Patterns** (Required)
1. Study `libs/core/operations/UnifiedOperation.cs`
2. Study `libs/core/validation/ValidationRules.cs`
3. Review `libs/rhino/spatial/Spatial.cs`
4. Practice: Write operation using UnifiedOperation

**Stage 3: Advanced Techniques** (Recommended)
1. Learn FrozenDictionary for constant lookups
2. Understand ConditionalWeakTable for caching
3. Study ArrayPool for zero-allocation buffers
4. Master expression tree compilation

**Stage 4: Contribution Ready**
1. Can write operations using UnifiedOperation
2. Can add new validation modes
3. Can add new error codes to E registry
4. Can write property-based tests with CsCheck

### Exemplar Files (Study These)

Before editing ANY code, study these exemplars:

1. **`libs/core/validation/ValidationRules.cs`** - Expression tree compilation, zero allocations
2. **`libs/core/results/ResultFactory.cs`** - Polymorphic parameter detection
3. **`libs/core/operations/UnifiedOperation.cs`** - 108 lines, complete dispatch engine
4. **`libs/core/results/Result.cs`** - Monadic composition, lazy evaluation
5. **`libs/rhino/spatial/Spatial.cs`** - FrozenDictionary dispatch, algorithmic density

### Community

- **GitHub Issues**: For bug reports and feature requests
- **GitHub Discussions**: For questions and discussions (coming soon)
- **Project Maintainer**: [@bsamiee](https://github.com/bsamiee)

### Common Mistakes

If build fails, check for these violations:

1. ❌ **Using `var`** → Replace with explicit type
2. ❌ **Missing trailing comma** → Add `,` at end of multi-line collections
3. ❌ **Unnamed parameter** → Add `parameter: value`
4. ❌ **Using `if`/`else`** → Replace with ternary/switch expression
5. ❌ **Multiple types in file** → Split into separate files
6. ❌ **Using `new List<T>()`** → Use collection expression `[]`
7. ❌ **Throwing exceptions** → Return `Result<T>` instead

---

## Code of Conduct

### Quality Standards

- **Zero tolerance** for analyzer violations
- **Zero tolerance** for failing tests
- **Zero tolerance** for uncommitted changes breaking build
- **Respect existing patterns** - Study before changing
- **Density over sprawl** - Improve algorithms, don't add helpers

### Commit Messages

Follow [Conventional Commits](https://conventionalcommits.org):

```
feat(core): add new validation mode for topology
fix(rhino): correct bounding box calculation
docs(contributing): add testing section
refactor(spatial): consolidate query methods
test(core): add property tests for Result.Bind
chore(ci): update analyzer versions
```

### Branch Naming

Use descriptive branch names:

```
feature/spatial-query-optimization
bugfix/result-map-null-handling
refactor/unified-operation-dispatch
docs/contributing-guide
```

---

## Autonomy Metrics

This project targets **>70% issue-to-merge autonomy** through agentic workflows.

Current metrics tracked:
- Bot-only PRs merged (count and %)
- Average review iterations
- Average time-to-merge
- Agent invocation counts by type
- Context file freshness

View current status in daily dashboard issues (auto-created at 00:00 UTC daily).

---

## License

By contributing to Parametric Arsenal, you agree that your contributions will be licensed under the [MIT License](LICENSE).

---

**Remember**: Quality is non-negotiable. Study the exemplars, follow the patterns, and never compromise on standards. Every line of code must exemplify excellence.

**Happy contributing!** 🚀

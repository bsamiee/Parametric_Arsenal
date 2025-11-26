# Prompt Library

This directory contains reusable prompt templates for common development tasks in Parametric Arsenal. These prompts are designed for use with Claude AI (via claude.ai/code or GitHub workflows) and enforce strict adherence to project standards.

---

## 📋 Available Prompts

| Prompt | Purpose | Scope | Estimated Time |
|--------|---------|-------|----------------|
| [code-cleanup.prompt.md](#code-cleanup) | Fix CLAUDE.md violations | Any .cs file | 5-15 min |
| [code-optimization.prompt.md](#code-optimization) | Performance improvements | Hot paths | 15-30 min |
| [code-organization.prompt.md](#code-organization) | Consolidate files/types | Any folder | 20-40 min |
| [integration-testing.prompt.md](#integration-testing) | RhinoCommon tests | libs/rhino | 15-30 min |
| [rhino-testing.prompt.md](#rhino-testing) | Rhino headless tests | test/rhino | 15-30 min |
| [sdk_and_logic.prompt.md](#sdk-and-logic) | RhinoCommon API usage | libs/rhino | 20-40 min |
| [testing.prompt.md](#testing) | Property-based tests | test/core | 10-20 min |

---

## 🎯 How to Use

### Method 1: Claude Web Interface (claude.ai/code)

1. **Open the repository** in claude.ai/code
2. **Select a prompt file** from this directory
3. **Copy the prompt content** and paste into Claude chat
4. **Specify the file/folder** you want to work on
5. **Review the changes** before accepting

**Example:**
```
@claude Please use the code-cleanup.prompt.md to fix all violations in libs/rhino/spatial/SpatialCore.cs
```

### Method 2: GitHub Workflows (Automated)

Workflows automatically inject relevant prompts based on issue metadata:

- **claude-issues.yml**: Uses prompts based on issue type and scope
- **claude-code-review.yml**: Uses validation prompts for reviews
- **claude-autofix.yml**: Uses cleanup prompts for fixing violations
- **claude-maintenance.yml**: Uses refactoring/optimization prompts

**Example Issue:**
```yaml
scope: libs/rhino/spatial
agent: cleanup-specialist
```
→ Workflow loads `code-cleanup.prompt.md` + agent file + context JSON

### Method 3: Command Line (via gh CLI)

```bash
# Fix violations in a file
gh issue create \
  --label "claude" \
  --title "Cleanup SpatialCore.cs violations" \
  --body "$(cat .github/prompts/code-cleanup.prompt.md)

Target file: libs/rhino/spatial/SpatialCore.cs
Violations: var usage, if/else statements, missing trailing commas"

# Optimize performance
gh issue create \
  --label "claude,performance" \
  --title "Optimize Spatial.Query hot path" \
  --body "$(cat .github/prompts/code-optimization.prompt.md)

Target: libs/rhino/spatial/SpatialCore.cs lines 127-189
Current: 50ms for 10k points
Goal: <20ms using RTree spatial indexing"
```

---

## 📖 Prompt Descriptions

### code-cleanup.prompt.md

**Purpose**: Fix CLAUDE.md standards violations

**Fixes**:
- `var` → explicit types
- `if`/`else` → ternary/switch expressions
- Missing trailing commas
- Unnamed parameters
- Multiple types per file
- Non-file-scoped namespaces
- Old C# patterns

**Usage**:
```
Target: libs/rhino/spatial/SpatialCore.cs
Violations:
- 3 instances of var (lines 45, 67, 89)
- 2 if/else statements (lines 127, 145)
- Missing trailing commas in FrozenDictionary (line 23)
```

**Output**: Clean code passing all analyzers, `dotnet build` succeeds with 0 warnings.

---

### code-optimization.prompt.md

**Purpose**: Improve performance while maintaining correctness

**Optimizations**:
- LINQ → `for` loops in hot paths
- Zero-allocation patterns (ArrayPool, Span<T>)
- FrozenDictionary for constant lookups
- ConditionalWeakTable for caching
- Expression tree compilation
- Parallel processing for large datasets

**Usage**:
```
Target: libs/rhino/spatial/SpatialCore.cs method QueryInternal
Current: 50ms for 10k points (LINQ-heavy)
Goal: <20ms using RTree + for loops
Profile: BenchmarkDotNet shows LINQ allocation overhead
```

**Output**: Optimized code with benchmark comparisons, maintained correctness.

---

### code-organization.prompt.md

**Purpose**: Consolidate files/types to meet organizational limits

**Consolidations**:
- Similar methods → parameterized operations
- Type switching → FrozenDictionary dispatch
- Separate files → dense implementations
- Loose helpers → inlined algorithms

**Limits Enforced**:
- **4 files max** per folder
- **10 types max** per folder
- **300 LOC max** per member

**Usage**:
```
Target folder: libs/rhino/spatial/
Current: 6 files, 14 types (exceeds limits)
Goal: 3 files, 8 types
Strategy: Merge query variants, use dispatch table
```

**Output**: Consolidated structure meeting all limits, improved cohesion.

---

### integration-testing.prompt.md

**Purpose**: Create NUnit integration tests for RhinoCommon operations

**Test Types**:
- Geometry creation/manipulation
- Validation flag combinations
- Result<T> error handling
- Edge cases (empty, null, degenerate)
- Performance benchmarks

**Framework**: NUnit + Rhino.Testing

**Usage**:
```
Target: libs/rhino/spatial/Spatial.cs
Operations: Analyze, Query, Index
Validation modes: V.Standard | V.BoundingBox
Coverage goal: 80%+
```

**Output**: Integration tests in `test/rhino/`, all passing, coverage verified.

---

### rhino-testing.prompt.md

**Purpose**: Rhino-specific headless testing patterns

**Covers**:
- Rhino 8 headless mode setup
- Geometry validation in tests
- RTree spatial indexing tests
- Tolerance handling
- Large dataset tests

**Framework**: NUnit + Rhino.Testing + RhinoCommon

**Usage**:
```
Target: test/rhino/SpatialTests.cs
New tests needed:
- PointCloud with 100k points
- Sphere query with varying radii
- RTree insertion/query performance
- Edge case: empty geometries
```

**Output**: Comprehensive Rhino integration tests, all passing in headless mode.

---

### sdk_and_logic.prompt.md

**Purpose**: Implement RhinoCommon SDK operations with proper patterns

**Patterns**:
- UnifiedOperation.Apply for all operations
- Result<T> for error handling
- V.* validation flags
- E.* error constants
- IGeometryContext threading

**Usage**:
```
Target: New feature in libs/rhino/analysis/
Operation: Curvature analysis
Input: Curve or Surface
Output: Result<IReadOnlyList<CurvatureData>>
Validation: V.Standard | V.Degeneracy
```

**Output**: Production-ready RhinoCommon implementation following all patterns.

---

### testing.prompt.md

**Purpose**: Create property-based tests for pure functions

**Framework**: xUnit + CsCheck

**Tests**:
- Monad laws (identity, associativity)
- Mathematical properties
- Functor laws
- Applicative laws
- Edge cases via random generation

**Usage**:
```
Target: libs/core/results/Result.cs
Properties to test:
- Map preserves structure
- Bind is associative
- Ensure accumulates errors
- Match is exhaustive
```

**Output**: Property-based tests in `test/core/`, all laws verified.

---

## 🔧 Customizing Prompts

### Prompt Structure

All prompts follow this structure:

```markdown
# [Prompt Title]

## Context
[Project background, standards reference]

## Objective
[What this prompt achieves]

## Critical Rules
[CLAUDE.md rules enforced]

## Process
[Step-by-step instructions]

## Verification
[How to verify success]

## Examples
[Code examples showing correct patterns]
```

### Adding New Prompts

1. **Create file**: `.github/prompts/[name].prompt.md`
2. **Follow structure** above
3. **Reference CLAUDE.md** for standards
4. **Include examples** with correct patterns
5. **Add to this README** in the table

### Prompt Best Practices

1. **Be specific**: Reference exact file paths, line numbers, violations
2. **Show examples**: Provide before/after code snippets
3. **Reference standards**: Always cite CLAUDE.md rules
4. **Verify success**: Include `dotnet build` and `dotnet test` steps
5. **Use exemplars**: Point to exemplar files for patterns

---

## 🧪 Testing Prompts

Before committing a new prompt:

1. **Test manually** via claude.ai/code
2. **Verify output** passes all analyzers
3. **Check limits** (files, types, LOC)
4. **Run tests** to ensure correctness
5. **Document results** in commit message

**Example Test Session:**
```bash
# 1. Apply prompt
@claude Use code-cleanup.prompt.md on libs/rhino/spatial/SpatialCore.cs

# 2. Verify build
dotnet build libs/rhino/Rhino.csproj

# 3. Run tests
dotnet test test/rhino/Arsenal.Rhino.Tests.csproj

# 4. Check violations fixed
git diff libs/rhino/spatial/SpatialCore.cs
```

---

## 📊 Prompt Performance Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Success Rate** | >90% | Issues resolved without human intervention |
| **Build Pass Rate** | 100% | All prompts produce buildable code |
| **Test Pass Rate** | 100% | All tests pass after prompt execution |
| **Standards Compliance** | 100% | Zero analyzer violations |
| **Time to Resolution** | <30 min | Average time from issue to merged PR |

Track metrics in `docs/agent-context/dashboard.json` (auto-generated).

---

## 🚀 Advanced Usage

### Chaining Prompts

For complex tasks, chain multiple prompts:

```
1. Use code-cleanup.prompt.md to fix violations
2. Use code-optimization.prompt.md to improve performance
3. Use testing.prompt.md to add missing tests
4. Use integration-testing.prompt.md for RhinoCommon tests
```

**Workflow Example:**
```bash
# Issue 1: Cleanup
@claude code-cleanup.prompt.md on libs/rhino/spatial/

# Issue 2: Optimize (after cleanup merged)
@claude code-optimization.prompt.md on libs/rhino/spatial/SpatialCore.cs

# Issue 3: Test (after optimization merged)
@claude testing.prompt.md + integration-testing.prompt.md for spatial/
```

### Prompt Templates in Issues

Use prompts as templates in issue descriptions:

```markdown
## Task
Cleanup violations in libs/rhino/spatial/

## Prompt Template
[Paste code-cleanup.prompt.md here]

## Specific Targets
- SpatialCore.cs: lines 45, 67, 89 (var usage)
- Spatial.cs: lines 127, 145 (if/else)
- SpatialConfig.cs: line 23 (missing trailing comma)
```

### Custom Agent + Prompt Combos

Combine specialist agents with relevant prompts:

| Agent | Recommended Prompts |
|-------|-------------------|
| **cleanup-specialist** | code-cleanup.prompt.md |
| **performance-analyst** | code-optimization.prompt.md |
| **refactoring-architect** | code-organization.prompt.md |
| **testing-specialist** | testing.prompt.md, integration-testing.prompt.md, rhino-testing.prompt.md |
| **rhino-implementation** | sdk_and_logic.prompt.md, integration-testing.prompt.md |
| **documentation-specialist** | (no prompts yet - add docs prompts) |

---

## 📚 Related Documentation

- **[CLAUDE.md](../../CLAUDE.md)** - Comprehensive coding standards (mandatory reading)
- **[AGENTS.md](../../AGENTS.md)** - Quick reference for agents
- **[CONTRIBUTING.md](../../CONTRIBUTING.md)** - Developer onboarding guide
- **[.github/agents/](../agents/)** - Custom agent definitions
- **[docs/agent-context/](../../docs/agent-context/)** - Generated JSON context

---

## 🔄 Maintenance

### Prompt Sync with STANDARDS.yaml

Prompts are manually maintained but should stay synchronized with `tools/standards/STANDARDS.yaml`:

1. **On STANDARDS.yaml update**: Review all prompts
2. **Check critical rules**: Ensure prompts enforce all current rules
3. **Update examples**: Reflect any pattern changes
4. **Test prompts**: Verify they still work correctly

**TODO**: Automate prompt validation via `standards-sync.yml` workflow.

### Deprecating Old Prompts

If a prompt is no longer needed:

1. **Move to archive**: `.github/prompts/archive/[name].prompt.md`
2. **Update README**: Remove from table, add to deprecated section
3. **Update workflows**: Remove references
4. **Document reason**: Add deprecation notice in prompt file

---

## 🤝 Contributing Prompts

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for general contribution guidelines.

### Prompt-Specific Guidelines

1. **Clarity**: Prompts must be unambiguous
2. **Standards**: Always reference CLAUDE.md
3. **Examples**: Include at least 2 code examples
4. **Verification**: Include build/test commands
5. **Scope**: Keep prompts focused on single task type

### Review Checklist

- [ ] Follows standard prompt structure
- [ ] References CLAUDE.md standards
- [ ] Includes code examples (correct/incorrect)
- [ ] Provides verification steps
- [ ] Tested manually on real code
- [ ] Documented in this README
- [ ] Added to appropriate workflow (if applicable)

---

**Last Updated**: 2025-11-26

**Prompt Count**: 7

**Total Coverage**: Core refactoring, testing, optimization, RhinoCommon integration

**Gaps**: Documentation generation, Python plugin prompts, Grasshopper component prompts (TODO)

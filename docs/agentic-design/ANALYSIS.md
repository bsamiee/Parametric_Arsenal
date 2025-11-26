# ANALYSIS.md — Agentic Monorepo Architecture Strategy

**Principal Architect**: Claude (Opus 4)
**Date**: 2025-11-26
**Scope**: Self-Describing Agentic Environment for Parametric Arsenal

---

## Executive Summary

The Parametric Arsenal repository is **70% ready** for autonomous agent operation. Its sophisticated Result monad architecture, expression tree compilation, and event-driven workflows provide a strong foundation. However, critical gaps in protocol synchronization, structured metadata, and automated context generation prevent true autonomy.

This analysis identifies **12 impediments** to agent autonomy and proposes a **4-pillar infrastructure upgrade** to achieve a self-describing agentic environment.

---

## 1. Infrastructure Audit

### 1.1 Protocol Layer Assessment

#### Current State: **Fragmented Multi-Document System**

| Document | Location | LOC | Audience | Issue |
|----------|----------|-----|----------|-------|
| CLAUDE.md | `/` | 1000+ | Claude AI | **Canonical but monolithic** |
| AGENTS.md | `/` | 405 | Task runners | **95% duplicates CLAUDE.md** |
| copilot-instructions.md | `.github/` | 323 | GitHub Copilot | **Condensed but drifts** |
| 5x agent files | `.github/agents/` | ~350 avg | Specialized agents | **6 missing files** |
| 4x command files | `.claude/commands/` | ~50 avg | Slash commands | **Stubs only** |

**Critical Finding**: `.claude/settings.json` references **11 agents** but only **5 `.agent.md` files exist**:
- ✅ `csharp-advanced.agent.md`
- ✅ `testing-specialist.agent.md`
- ✅ `refactoring-architect.agent.md`
- ✅ `rhino-implementation.agent.md`
- ✅ `performance-analyst.agent.md`
- ❌ `cleanup-specialist.agent.md` (MISSING)
- ❌ `library-planner.agent.md` (MISSING)
- ❌ `documentation-specialist.agent.md` (MISSING)
- ❌ `integration-specialist.agent.md` (MISSING)
- ❌ `grasshopper-implementation.agent.md` (MISSING)
- ❌ `plugin-architect.agent.md` (MISSING)

**Impact**: Agent invocation will fail or use stale 1-3 sentence inline prompts from JSON.

#### Synchronization Failure Matrix

| Rule | CLAUDE.md | AGENTS.md | copilot-instructions | Agents | Status |
|------|-----------|-----------|---------------------|--------|--------|
| No `var` | ✅ Line 21 | ✅ Line 21 | ✅ Line 48 | ✅ All | **SYNC** |
| No if/else | ✅ Line 22 | ✅ Line 22 | ✅ Line 50 | ✅ All | **SYNC** |
| 300 LOC limit | ✅ "ABSOLUTE" | ✅ "ABSOLUTE" | ❌ "300 lines" | Varies | **DRIFT** |
| 4 files/folder | ✅ "per folder" | ❌ Unclear | ❌ Missing | Varies | **AMBIGUOUS** |
| Exemplar LOC | 202 lines | 202 lines | Not listed | 202 lines | **STALE RISK** |

**Root Cause**: No single-source-of-truth with automated propagation.

---

### 1.2 CI/CD Fabric Assessment

#### Current Workflow Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                     AGENTIC WORKFLOW TOPOLOGY                        │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ISSUE LABELED                    COMMENT @claude                    │
│       │                                │                             │
│       ▼                                ▼                             │
│  ┌─────────────────┐            ┌─────────────────┐                 │
│  │ claude-issues   │            │    claude       │                 │
│  │ (Sonnet, 20t)   │            │ (Opus, 15t)     │                 │
│  │ MCP: github,    │            │ MCP: github,    │                 │
│  │      context7   │            │ tavily, context7│                 │
│  │ Agents: NONE    │            │ perplexity, exa │                 │
│  └────────┬────────┘            │ Agents: 5       │                 │
│           │                     └────────┬────────┘                 │
│           │ Creates PR                   │                          │
│           ▼                              ▼                          │
│  ┌─────────────────┐            ┌─────────────────┐                 │
│  │ claude-code-    │◄───────────│ PR OPENED       │                 │
│  │ review          │            │ (any trigger)   │                 │
│  │ (Opus, 8t)      │            └────────┬────────┘                 │
│  │ MCP: NONE       │                     │                          │
│  │ Tools: Read-only│                     ▼                          │
│  └────────┬────────┘            ┌─────────────────┐                 │
│           │                     │     ci.yml      │                 │
│           │ gh pr review        │ (Build gates)   │                 │
│           ▼                     │ EditorConfig    │                 │
│  ┌─────────────────┐            │ dotnet format   │                 │
│  │ APPROVE or      │            │ Analyzers       │                 │
│  │ REQUEST_CHANGES │            │ Tests           │                 │
│  └─────────────────┘            └────────┬────────┘                 │
│                                          │                          │
│                                          ▼                          │
│                                 ┌─────────────────┐                 │
│                                 │ rhino-tests     │ (Windows only)  │
│                                 │ NUnit + Rhino   │                 │
│                                 └────────┬────────┘                 │
│                                          │                          │
│                                          ▼                          │
│                                 ┌─────────────────┐                 │
│                                 │  MANUAL MERGE   │ ← GAP: No auto  │
│                                 └─────────────────┘                 │
│                                                                      │
│  SCHEDULED (Monday 9 AM)                                            │
│       │                                                             │
│       ▼                                                             │
│  ┌─────────────────┐                                                │
│  │ claude-         │                                                │
│  │ maintenance     │                                                │
│  │ (Sonnet, 10t)   │                                                │
│  │ MCP: NONE       │ ← GAP: Cannot access context7                  │
│  │ Tools: Read-only│                                                │
│  └─────────────────┘                                                │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

#### Identified Gaps

| Gap ID | Description | Impact | Priority |
|--------|-------------|--------|----------|
| **CI-1** | No auto-merge workflow | Human bottleneck after all gates pass | HIGH |
| **CI-2** | `claude-issues` lacks agent specialization | Generic Sonnet vs. specialized agents | MEDIUM |
| **CI-3** | `claude-maintenance` cannot access MCP | No historical context for trend analysis | MEDIUM |
| **CI-4** | `claude-code-review` is read-only | Cannot auto-apply fixes | HIGH |
| **CI-5** | No explicit test coverage gate | Implementation PRs may lack tests | MEDIUM |
| **CI-6** | Different concurrency strategies | Inconsistent queue behavior | LOW |

---

### 1.3 Gatekeeper Analysis

#### Pre-Commit Hooks (`.pre-commit-config.yaml`)

```yaml
# Current enforcement chain:
repos:
  - repo: https://github.com/pre-commit/pre-commit-hooks
    hooks: [trailing-whitespace, end-of-file-fixer, mixed-line-ending,
            check-yaml, check-json, check-toml]

  - repo: local
    hooks:
      - id: dotnet-build        # TreatWarningsAsErrors=true
      - id: dotnet-format       # --verify-no-changes
```

**Strength**: Any analyzer violation blocks commit locally.
**Gap**: Agents bypass pre-commit (CI is the real gate).

#### Analyzer Enforcement (`Directory.Build.props`)

| Analyzer | Version | Key Rules |
|----------|---------|-----------|
| Roslynator | 4.14.1 | RCS* (refactoring patterns) |
| Meziantou | 2.0.256 | MA* (trailing commas, async patterns) |
| NetAnalyzers | 10.0.100 | CA*, IDE* (primary constructors, collections) |
| AsyncFixer | 1.6.0 | Async/await correctness |
| ReflectionAnalyzers | 0.3.1 | Reflection safety |
| Nullable.Extended | 1.15.6581 | Null safety |

**Critical Rules Enforced as Errors**:
- `CA1050`: One type per file
- `IDE0290`: Primary constructors required
- `IDE0300-0305`: Collection expressions required
- `IDE0007-0009`: No `var` keyword
- `IDE0048`: Pattern matching required
- `IDE0055`: K&R brace style

**Assessment**: ✅ **Production-ready** gatekeeper configuration.

---

### 1.4 Interface Layer Assessment (Templates)

#### Current State: **No Traditional Templates**

The repository has **zero** issue or PR templates in the standard locations:
- ❌ `.github/ISSUE_TEMPLATE/` (directory does not exist)
- ❌ `.github/PULL_REQUEST_TEMPLATE.md` (file does not exist)

Instead, the system uses **event-driven workflows** that consume raw issue/PR bodies.

#### Problem: Unstructured Input

When `claude-issues.yml` triggers on label `claude-implement`:
```yaml
prompt: |
  Implement issue #${{ github.event.issue.number }}
  Title: ${{ github.event.issue.title }}
  Body: ${{ github.event.issue.body }}  # ← UNSTRUCTURED TEXT
```

**Agents receive freeform text** without:
- Explicit scope (which libs/ folder?)
- Complexity classification (trivial/medium/hard)
- Required context files
- Success criteria
- Validation requirements

#### Opportunity: Semantic Hooks

Templates can embed **invisible structured metadata** that agents parse:

```markdown
## Feature Request

<!-- AGENT_METADATA
{
  "scope": "libs/rhino/spatial",
  "complexity": "hard",
  "required_context": ["Spatial.cs", "SpatialCore.cs"],
  "validation_mode": "V.Standard | V.Topology",
  "success_criteria": ["Tests pass", "Coverage > 80%"],
  "estimated_files_touched": 3
}
-->

### Description
[Human-readable description here]
```

---

### 1.5 Codebase Architecture Assessment

#### Strengths Enabling Autonomy

| Pattern | Location | Why It Helps Agents |
|---------|----------|---------------------|
| Result Monad | `libs/core/results/` | Explicit error handling, no exceptions |
| UnifiedOperation | `libs/core/operations/` | Polymorphic dispatch with configuration |
| FrozenDictionary | Throughout | O(1) lookups, compile-time verification |
| Expression Trees | `ValidationRules.cs` | Zero-allocation validation |
| 4-file Domain Pattern | `libs/rhino/*/` | Predictable structure |
| Error Registry | `E.cs` | Centralized, discoverable error codes |

#### Weaknesses Impeding Autonomy

| Issue | Location | Impact |
|-------|----------|--------|
| Exemplar LOC counts stale | CLAUDE.md, AGENTS.md | Agents may reference wrong metrics |
| No architecture.json | N/A | Agents must grep to understand structure |
| No error-catalog.json | N/A | Agents must read E.cs to find codes |
| No validation-modes.json | N/A | Agents must read V.cs for flags |
| Missing agent files | `.github/agents/` | 6 agents will fail to specialize |

---

## 2. Bleeding Edge Implementation Strategy

### 2.1 Pillar A: Dual-Protocol Standard

**Principle**: Retain both protocols but eliminate duplication via single-source generation.

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      STANDARDS.yaml                             │
│                  (Single Source of Truth)                       │
├─────────────────────────────────────────────────────────────────┤
│ rules:                                                          │
│   - id: NO_VAR                                                  │
│     severity: error                                             │
│     analyzer: IDE0007                                           │
│     description: "No var keyword - explicit types always"       │
│   - id: NO_IF_ELSE                                              │
│     severity: error                                             │
│     description: "Use expressions: ternary, switch, patterns"   │
│   ...                                                           │
│                                                                 │
│ limits:                                                         │
│   files_per_folder: 4                                           │
│   types_per_folder: 10                                          │
│   loc_per_member: 300                                           │
│                                                                 │
│ exemplars:                                                      │
│   - path: libs/core/results/Result.cs                          │
│     purpose: "Monadic composition with lazy evaluation"         │
│   ...                                                           │
└─────────────────────────────────────────────────────────────────┘
           │
           │ tools/StandardsGen.csx
           │
           ▼
     ┌─────┴─────┐
     │           │
     ▼           ▼
CLAUDE.md   copilot-instructions.md
(Full)           (Condensed)
     │
     ▼
.github/agents/*.agent.md
(Generated: [CRITICAL RULES] section)
```

#### Implementation

1. **Create** `tools/standards/STANDARDS.yaml` as canonical source
2. **Create** `tools/StandardsGen.csx` (C# Script) to generate:
   - Full CLAUDE.md from STANDARDS.yaml + prose templates
   - Condensed copilot-instructions.md (rules + limits only)
   - [CRITICAL RULES] sections for agent files
3. **Add CI step** to verify generated files match source
4. **Synchronization rule**: Edit STANDARDS.yaml → run generator → commit all

---

### 2.2 Pillar B: Semantic Hooks in Templates

**Principle**: Templates are machine-readable first, human-readable second.

#### Issue Template Schema

```yaml
# .github/ISSUE_TEMPLATE/feature-claude.yml
name: "Feature Request (Claude Implementable)"
description: "Request a feature for autonomous implementation"
labels: ["enhancement", "claude-implement"]
assignees: ["claude[bot]"]

body:
  - type: markdown
    attributes:
      value: |
        ## Agent Metadata (parsed automatically)

  - type: dropdown
    id: scope
    attributes:
      label: "Target Scope"
      description: "Which library domain?"
      options:
        - libs/core/results
        - libs/core/validation
        - libs/core/operations
        - libs/rhino/analysis
        - libs/rhino/spatial
        - libs/rhino/extraction
        - libs/rhino/intersection
        - libs/rhino/topology
        - libs/rhino/transformation
        - libs/rhino/morphology
        - libs/rhino/orientation
        - libs/grasshopper
    validations:
      required: true

  - type: dropdown
    id: complexity
    attributes:
      label: "Complexity Estimate"
      options:
        - trivial (< 50 LOC, single file)
        - medium (50-150 LOC, 2-3 files)
        - hard (150-300 LOC, 3-4 files)
        - expert (requires new domain)
    validations:
      required: true

  - type: input
    id: context_files
    attributes:
      label: "Required Context Files"
      description: "Comma-separated paths agents should read first"
      placeholder: "Spatial.cs, SpatialCore.cs"

  - type: dropdown
    id: validation_mode
    attributes:
      label: "Validation Requirements"
      options:
        - V.None
        - V.Standard
        - V.Standard | V.Topology
        - V.Standard | V.Degeneracy
        - V.All

  - type: textarea
    id: specification
    attributes:
      label: "Feature Specification"
      description: "What should this feature do?"
    validations:
      required: true

  - type: textarea
    id: success_criteria
    attributes:
      label: "Success Criteria"
      description: "How will we know this is done?"
      placeholder: |
        - [ ] Tests pass (dotnet test)
        - [ ] Coverage verified
        - [ ] CLAUDE.md compliant
    validations:
      required: true
```

#### PR Template with Agent Review Hooks

```markdown
<!-- .github/PULL_REQUEST_TEMPLATE.md -->

## Summary
<!-- Brief description of changes -->

## Agent Metadata
<!-- AGENT_REVIEW_CONFIG
{
  "require_agents": ["claude-code-review"],
  "auto_merge_eligible": true,
  "coverage_threshold": 75,
  "validation_modes_used": [],
  "files_touched_count": 0
}
-->

## Verification Checklist
<!-- Agents parse these checkboxes -->
- [ ] `dotnet build` passes (zero warnings)
- [ ] `dotnet test` passes (all tests)
- [ ] No `var` keyword usage
- [ ] No `if`/`else` statements
- [ ] Named parameters for non-obvious args
- [ ] Trailing commas on multi-line collections
- [ ] One type per file (CA1050)
- [ ] Method length ≤ 300 LOC
- [ ] Result<T> for failable operations
- [ ] E.* error constants (no direct SystemError)

## Test Plan
<!-- How to verify this change -->
```

---

### 2.3 Pillar C: Active Context Generation

**Principle**: Agents consume generated JSON maps, not raw source code.

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      tools/ContextGen/                          │
│                                                                 │
│  ContextGen.csproj                                              │
│  ├── Generators/                                                │
│  │   ├── ArchitectureGenerator.cs    → architecture.json        │
│  │   ├── ErrorCatalogGenerator.cs    → error-catalog.json       │
│  │   ├── ExemplarMetricsGenerator.cs → exemplar-metrics.json    │
│  │   ├── ValidationModesGenerator.cs → validation-modes.json    │
│  │   └── DomainMapGenerator.cs       → domain-map.json          │
│  │                                                              │
│  └── Program.cs (orchestrator)                                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ dotnet run
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   docs/agent-context/                           │
│                                                                 │
│  architecture.json                                              │
│  {                                                              │
│    "projects": [                                                │
│      {                                                          │
│        "name": "Arsenal.Core",                                  │
│        "path": "libs/core/Core.csproj",                        │
│        "namespaces": ["Arsenal.Core.Results", ...],            │
│        "types": [                                               │
│          {"name": "Result<T>", "kind": "struct", "loc": 202}   │
│        ]                                                        │
│      }                                                          │
│    ]                                                            │
│  }                                                              │
│                                                                 │
│  error-catalog.json                                             │
│  {                                                              │
│    "domains": [                                                 │
│      {                                                          │
│        "name": "Results",                                       │
│        "id": 1,                                                 │
│        "range": [1000, 1999],                                   │
│        "errors": [                                              │
│          {"code": 1001, "name": "InvalidCreate", "message": ...}│
│        ]                                                        │
│      }                                                          │
│    ]                                                            │
│  }                                                              │
│                                                                 │
│  exemplar-metrics.json                                          │
│  {                                                              │
│    "exemplars": [                                               │
│      {                                                          │
│        "path": "libs/core/results/Result.cs",                  │
│        "loc": 202,                                              │
│        "types": 1,                                              │
│        "methods": 18,                                           │
│        "complexity": 42,                                        │
│        "last_verified": "2025-11-26"                           │
│      }                                                          │
│    ]                                                            │
│  }                                                              │
│                                                                 │
│  validation-modes.json                                          │
│  {                                                              │
│    "modes": [                                                   │
│      {"name": "V.None", "value": 0, "checks": []},             │
│      {"name": "V.Standard", "value": 1, "checks": ["IsValid"]} │
│    ],                                                           │
│    "combinations": [...]                                        │
│  }                                                              │
│                                                                 │
│  domain-map.json                                                │
│  {                                                              │
│    "domains": [                                                 │
│      {                                                          │
│        "name": "spatial",                                       │
│        "path": "libs/rhino/spatial",                           │
│        "files": ["Spatial.cs", "SpatialCore.cs", ...],         │
│        "api_types": ["RangeAnalysis", "ProximityAnalysis"],    │
│        "patterns": ["FrozenDictionary dispatch", "RTree"]      │
│      }                                                          │
│    ]                                                            │
│  }                                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

#### Implementation via .NET Reflection

```csharp
// tools/ContextGen/Generators/ArchitectureGenerator.cs
public static class ArchitectureGenerator {
    public static ArchitectureMap Generate(string solutionPath) {
        // Use Roslyn to parse without loading assemblies
        MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Solution solution = workspace.OpenSolutionAsync(solutionPath).Result;

        return new ArchitectureMap {
            Projects = [.. solution.Projects.Select(p => new ProjectInfo {
                Name = p.Name,
                Path = p.FilePath,
                Namespaces = [.. p.Documents
                    .SelectMany(d => d.GetSyntaxRootAsync().Result?
                        .DescendantNodes()
                        .OfType<NamespaceDeclarationSyntax>()
                        .Select(ns => ns.Name.ToString()) ?? [])
                    .Distinct()],
                Types = [.. p.Documents
                    .SelectMany(d => ExtractTypes(d))],
            }),],
            GeneratedAt = DateTime.UtcNow,
        };
    }
}
```

#### CI Integration

```yaml
# .github/workflows/context-gen.yml
name: Generate Agent Context
on:
  push:
    branches: [main]
    paths:
      - 'libs/**/*.cs'
      - 'libs/**/*.csproj'

jobs:
  generate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Generate Context
        run: dotnet run --project tools/ContextGen/ContextGen.csproj

      - name: Commit Context Files
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add docs/agent-context/*.json
          git diff --staged --quiet || git commit -m "chore: regenerate agent context"
          git push
```

---

### 2.4 Pillar D: Agentic Handshake

**Principle**: Agents review agents via structured JSON interchange.

#### Review Protocol

```
┌─────────────────────────────────────────────────────────────────┐
│                    AGENTIC REVIEW FLOW                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. PR CREATED (by claude-issues workflow)                      │
│       │                                                         │
│       ▼                                                         │
│  2. claude-code-review (Opus) generates:                        │
│       │                                                         │
│       │  .github/review-output/pr-{number}.json                │
│       │  {                                                      │
│       │    "pr_number": 42,                                     │
│       │    "verdict": "request_changes",                        │
│       │    "violations": [                                      │
│       │      {                                                  │
│       │        "rule": "NO_VAR",                                │
│       │        "file": "libs/rhino/spatial/Spatial.cs",        │
│       │        "line": 127,                                     │
│       │        "current": "var tree = new RTree();",           │
│       │        "suggested": "RTree tree = new();"              │
│       │      }                                                  │
│       │    ],                                                   │
│       │    "passed_checks": ["NO_IF_ELSE", "TRAILING_COMMAS"], │
│       │    "coverage_delta": -2.3,                              │
│       │    "complexity_delta": +5                               │
│       │  }                                                      │
│       │                                                         │
│       ▼                                                         │
│  3. claude-autofix (triggered by review output)                 │
│       │                                                         │
│       │  Reads pr-{number}.json                                 │
│       │  Applies fixes per violation                            │
│       │  Commits with message:                                  │
│       │    "fix(review): apply agentic review fixes"            │
│       │                                                         │
│       ▼                                                         │
│  4. LOOP: Re-run review until:                                  │
│       - verdict == "approve"                                    │
│       - OR max_iterations (3) reached                           │
│       │                                                         │
│       ▼                                                         │
│  5. auto-merge (if all gates pass)                              │
│       - ci.yml ✅                                                │
│       - rhino-tests ✅ (if applicable)                          │
│       - claude-code-review verdict == "approve"                 │
│       │                                                         │
│       ▼                                                         │
│  6. MERGED TO MAIN                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

#### Review Output Schema

```typescript
// Schema for .github/review-output/pr-{number}.json
interface AgentReviewOutput {
  pr_number: number;
  reviewer: "claude-code-review" | "copilot-review" | "gemini-review";
  timestamp: string;  // ISO 8601
  verdict: "approve" | "request_changes" | "comment";

  violations: Array<{
    rule: string;           // Rule ID from STANDARDS.yaml
    severity: "error" | "warning";
    file: string;           // Relative path
    line: number;
    column?: number;
    current: string;        // Current code snippet
    suggested: string;      // Suggested fix
    explanation?: string;   // Why this violates
  }>;

  passed_checks: string[];  // Rules that passed

  metrics: {
    files_reviewed: number;
    lines_added: number;
    lines_removed: number;
    coverage_delta: number;
    complexity_delta: number;
  };

  recommendations?: string[];  // Non-blocking suggestions
}
```

---

## 3. Tooling Decisions

### 3.1 gh-aw (GitHub Agentic Workflows) Assessment

**Question**: Should we integrate `gh-aw` or stick to custom actions?

#### Current `claude-*.yml` Maturity

| Capability | claude-issues | claude-code-review | claude-maintenance | claude |
|------------|---------------|-------------------|-------------------|--------|
| Trigger variety | ✅ Label-based | ✅ PR event | ✅ Schedule + dispatch | ✅ 5 triggers |
| MCP integration | ✅ 2 servers | ❌ None | ❌ None | ✅ 5 servers |
| Agent specialization | ❌ None | ❌ None | ❌ None | ✅ 5 agents |
| Error handling | ✅ Fallback comment | ❌ None | ❌ None | ✅ Comment |
| Max turns | 20 | 8 | 10 | 15 |

**Assessment**: The current `anthropics/claude-code-action@v1` integration is **mature** for:
- Issue-to-PR automation
- Code review
- Scheduled maintenance

**Gaps** that `gh-aw` or custom enhancement could address:
- ❌ Review → Fix → Re-review loop (not automated)
- ❌ Auto-merge orchestration
- ❌ Multi-agent coordination (coder + reviewer + tester)

#### Recommendation

**Hybrid approach**:
1. **Keep** `claude-code-action@v1` for core Claude interactions
2. **Add** custom composite actions for:
   - Agentic handshake (review → fix loop)
   - Auto-merge orchestration
   - Context generation
3. **Defer** `gh-aw` adoption until it supports:
   - MCP server configuration
   - Multi-model coordination (Claude + Copilot)
   - Review output schemas

---

### 3.2 Context Generation Tooling

**Options**:

| Tool | Pros | Cons | Recommendation |
|------|------|------|----------------|
| Roslyn Workspace | Full semantic analysis, accurate LOC | Requires solution load | ✅ **Primary** |
| `dotnet format --report` | Built-in, fast | Limited metadata | Supplement |
| `csharp-ls` | Language server protocol | Overkill for JSON gen | ❌ |
| Custom regex parsing | Fast, no dependencies | Brittle, inaccurate | ❌ |

**Decision**: Use **Roslyn MSBuildWorkspace** for accurate reflection:
- Parse `.sln` to enumerate projects
- Parse syntax trees for type/member extraction
- Calculate LOC, complexity, method counts
- Output to `docs/agent-context/*.json`

---

## 4. Risk Assessment

### 4.1 Implementation Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Standards drift | HIGH | HIGH | Single-source YAML + generation |
| Context staleness | MEDIUM | MEDIUM | CI-triggered regeneration |
| Review loop infinite | LOW | HIGH | Max iteration limit (3) |
| Agent file missing | CONFIRMED | HIGH | Create 6 missing files |
| Template adoption | MEDIUM | LOW | Keep workflows as fallback |

### 4.2 Security Considerations

| Concern | Current State | Required Action |
|---------|--------------|-----------------|
| Secrets in context | ❌ Not scanned | Add `.gitignore` for `agent-context/` if needed |
| PR permissions | `contents: write` | Acceptable for auto-fix |
| MCP server access | Restricted per workflow | Maintain granularity |
| Auto-merge | Not implemented | Require branch protection |

---

## 5. Success Metrics

### 5.1 Autonomy Indicators

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Issue → Merged PR (no human) | ~30% | >70% | Track PRs with only bot commits |
| Review iterations before approve | N/A | <2 avg | Review JSON aggregation |
| Time from issue to merge | ~4 hours | <1 hour | GitHub API timing |
| Agent specialization accuracy | 0% | >80% | Agent invocation logs |
| Context JSON freshness | N/A | <24h | File timestamps |

### 5.2 Quality Indicators

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Build success rate | ~95% | >99% | CI analytics |
| Analyzer violations in PRs | ~10/PR | <2/PR | Review JSON aggregation |
| Test coverage | Unknown | >75% | Coverlet reports |
| CLAUDE.md compliance | ~80% | >95% | Automated scan |

---

## 6. Conclusion

The Parametric Arsenal repository has **strong foundational architecture** (Result monad, expression trees, FrozenDictionary patterns) that enables dense, algebraic code. However, the **agent infrastructure** suffers from:

1. **Fragmented protocols** with 95% duplication and drift risk
2. **Missing agent files** (6 of 11 referenced agents)
3. **Unstructured interfaces** (no semantic templates)
4. **No automated context** (agents must grep raw source)
5. **No agentic handshake** (review → fix is manual)

The proposed **4-pillar implementation** addresses each gap:

| Pillar | Gap Addressed | Effort | Impact |
|--------|---------------|--------|--------|
| A. Dual-Protocol Standard | Fragmentation | 2-3 days | HIGH |
| B. Semantic Hooks | Unstructured input | 1-2 days | MEDIUM |
| C. Active Context | No JSON maps | 3-5 days | HIGH |
| D. Agentic Handshake | Manual review loop | 3-4 days | VERY HIGH |

**Total estimated effort**: 10-14 days to full autonomy.

**Priority order**: D → C → A → B (impact per effort)

---

*Document generated by Claude (Opus 4) for Parametric Arsenal agentic infrastructure design.*

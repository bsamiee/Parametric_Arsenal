# TASK_FINAL.md — Agentic Infrastructure Execution Checklist

**Principal Architect**: Claude (Opus 4)
**Date**: 2025-11-26
**Objective**: Transform Parametric Arsenal into a Self-Describing Agentic Environment

---

## Execution Overview

| Layer | Tasks | Priority | Dependencies |
|-------|-------|----------|--------------|
| **Protocol Layer** | 8 | HIGH | None |
| **Interface Layer** | 6 | MEDIUM | Protocol Layer |
| **Context Layer** | 7 | HIGH | None |
| **CI/CD Layer** | 9 | VERY HIGH | All other layers |

**Total Tasks**: 30
**Estimated Duration**: 10-14 days
**Autonomy Target**: >70% issue-to-merge without human intervention

---

## Protocol Layer

> Standardizing agent protocols, synchronization rules, and eliminating duplication.

### P-1: Create Single-Source Standards Definition
**Priority**: CRITICAL
**Effort**: 4 hours
**Deliverable**: `tools/standards/STANDARDS.yaml`

```yaml
# Target structure
rules:
  syntax:
    - id: NO_VAR
      severity: error
      analyzer: IDE0007
      description: "No var keyword - explicit types always"
      examples:
        wrong: "var x = 5;"
        correct: "int x = 5;"

    - id: NO_IF_ELSE
      severity: error
      description: "Use expressions: ternary, switch, pattern matching"
      examples:
        wrong: "if (x) { return a; } else { return b; }"
        correct: "return x ? a : b;"

  architecture:
    - id: RESULT_MONAD
      description: "All failable operations return Result<T>"
    - id: UNIFIED_OPERATION
      description: "Polymorphic dispatch via UnifiedOperation.Apply"
    - id: ERROR_REGISTRY
      description: "Errors via E.* constants, never direct SystemError"

limits:
  files_per_folder: 4
  types_per_folder: 10
  loc_per_member: 300

exemplars:
  - path: libs/core/results/Result.cs
    purpose: "Monadic composition with lazy evaluation"
    patterns: ["Map", "Bind", "Ensure", "Match"]
  - path: libs/core/validation/ValidationRules.cs
    purpose: "Expression tree compilation, zero allocations"
    patterns: ["Expression<>", "Compile()", "FrozenDictionary"]
```

**Verification**:
- [ ] All 12 critical rules from CLAUDE.md captured
- [ ] All 5 exemplar files documented
- [ ] All organizational limits explicit
- [ ] YAML validates against JSON Schema

---

### P-2: Create Standards Generator Tool
**Priority**: HIGH
**Effort**: 6 hours
**Deliverable**: `tools/standards/StandardsGen.csx`

**Implementation Requirements**:
```csharp
// C# Script (dotnet-script) for standards generation
#r "nuget: YamlDotNet, 15.1.0"

using YamlDotNet.Serialization;

// Load STANDARDS.yaml
var yaml = File.ReadAllText("STANDARDS.yaml");
var standards = new DeserializerBuilder().Build().Deserialize<Standards>(yaml);

// Generate CLAUDE.md (full version)
GenerateCLAUDEmd(standards, "../../CLAUDE.md");

// Generate copilot-instructions.md (condensed)
GenerateCopilotInstructions(standards, "../../.github/copilot-instructions.md");

// Generate agent file sections
foreach (var agentFile in Directory.GetFiles("../../.github/agents/", "*.agent.md")) {
    InjectCriticalRulesSection(standards, agentFile);
}
```

**Verification**:
- [ ] `dotnet script StandardsGen.csx` executes without errors
- [ ] Generated CLAUDE.md matches current content (modulo formatting)
- [ ] Generated copilot-instructions.md is ≤400 LOC
- [ ] Agent files have identical [CRITICAL RULES] sections

---

### P-3: Create Missing Agent Definition Files
**Priority**: CRITICAL
**Effort**: 8 hours
**Deliverables**: 6 new `.agent.md` files

| Agent | File | Primary Focus |
|-------|------|---------------|
| cleanup-specialist | `.github/agents/cleanup-specialist.agent.md` | Dead code removal, consolidation |
| library-planner | `.github/agents/library-planner.agent.md` | New domain architecture |
| documentation-specialist | `.github/agents/documentation-specialist.agent.md` | API docs, examples |
| integration-specialist | `.github/agents/integration-specialist.agent.md` | Cross-module patterns |
| grasshopper-implementation | `.github/agents/grasshopper-implementation.agent.md` | GH component development |
| plugin-architect | `.github/agents/plugin-architect.agent.md` | Rhino plugin patterns |

**Template Structure** (per file):
```markdown
---
name: {agent-name}
tier: {1-3}
description: {one-line purpose}
capabilities:
  - {capability-1}
  - {capability-2}
domains:
  - {target-folder-1}
  - {target-folder-2}
---

# [ROLE]
<!-- Generated from STANDARDS.yaml -->

# [CRITICAL RULES]
<!-- Generated from STANDARDS.yaml -->

# [PATTERNS]
<!-- Agent-specific code patterns -->

# [EXEMPLARS]
<!-- Agent-specific file references -->

# [WORKFLOW]
<!-- Step-by-step task execution -->
```

**Verification**:
- [ ] All 6 files created with consistent structure
- [ ] settings.json references valid file paths
- [ ] Each agent has ≥3 unique capabilities
- [ ] Each agent references ≥2 exemplar files

---

### P-4: Update settings.json Agent Registry
**Priority**: HIGH
**Effort**: 1 hour
**Deliverable**: Updated `.claude/settings.json`

**Changes Required**:
```json
{
  "agents": [
    {
      "name": "cleanup-specialist",
      "description": "Code cleanup and dead code removal",
      "prompt": "Read .github/agents/cleanup-specialist.agent.md first. Then execute cleanup task."
    },
    // ... all 11 agents with valid file references
  ]
}
```

**Verification**:
- [ ] All 11 agents have corresponding `.agent.md` files
- [ ] No inline prompts exceed 200 characters (they delegate to files)
- [ ] Agent descriptions are unique and descriptive

---

### P-5: Implement Synchronization CI Check
**Priority**: MEDIUM
**Effort**: 2 hours
**Deliverable**: `.github/workflows/standards-sync.yml`

```yaml
name: Standards Synchronization Check
on:
  pull_request:
    paths:
      - 'tools/standards/STANDARDS.yaml'
      - 'CLAUDE.md'
      - '.github/copilot-instructions.md'
      - '.github/agents/*.agent.md'

jobs:
  verify-sync:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install dotnet-script
        run: dotnet tool install -g dotnet-script

      - name: Regenerate from STANDARDS.yaml
        run: |
          cd tools/standards
          dotnet script StandardsGen.csx

      - name: Verify No Drift
        run: |
          git diff --exit-code CLAUDE.md || \
            (echo "ERROR: CLAUDE.md out of sync with STANDARDS.yaml" && exit 1)
          git diff --exit-code .github/copilot-instructions.md || \
            (echo "ERROR: copilot-instructions.md out of sync" && exit 1)
```

**Verification**:
- [ ] Workflow runs on relevant file changes
- [ ] Fails if generated files differ from committed
- [ ] Clear error messages identify which file drifted

---

### P-6: Document Synchronization Rules in CONTRIBUTING.md
**Priority**: LOW
**Effort**: 1 hour
**Deliverable**: New `CONTRIBUTING.md` or section in README

**Content**:
```markdown
## Protocol Synchronization Rules

### The Golden Rule
**NEVER edit CLAUDE.md or copilot-instructions.md directly.**

Edit `tools/standards/STANDARDS.yaml` and run:
```bash
cd tools/standards
dotnet script StandardsGen.csx
```

### What Gets Synchronized
- CLAUDE.md ← Full expansion of STANDARDS.yaml
- copilot-instructions.md ← Condensed rules only
- .github/agents/*.agent.md ← [CRITICAL RULES] section only

### Adding a New Rule
1. Add to `tools/standards/STANDARDS.yaml` under appropriate category
2. Run generator
3. Commit all changed files together
4. CI will verify synchronization
```

**Verification**:
- [ ] Clear instructions for rule modification
- [ ] Links to generator tool
- [ ] CI verification mentioned

---

### P-7: Deprecate Inline Agent Prompts
**Priority**: MEDIUM
**Effort**: 1 hour
**Deliverable**: Updated `.claude/settings.json`

**Before**:
```json
{
  "prompt": "You are an advanced C# specialist focusing on dense, algebraic..."
}
```

**After**:
```json
{
  "prompt": "IMPORTANT: Read .github/agents/{name}.agent.md before proceeding."
}
```

**Verification**:
- [ ] All inline prompts are delegation stubs
- [ ] No prompt exceeds 100 characters
- [ ] File paths are correct and validated

---

### P-8: Validate Agent File Frontmatter Schema
**Priority**: LOW
**Effort**: 2 hours
**Deliverable**: `tools/standards/agent-schema.json`

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["name", "description", "capabilities", "domains"],
  "properties": {
    "name": { "type": "string", "pattern": "^[a-z-]+$" },
    "tier": { "type": "integer", "minimum": 1, "maximum": 3 },
    "description": { "type": "string", "maxLength": 100 },
    "capabilities": {
      "type": "array",
      "items": { "type": "string" },
      "minItems": 2
    },
    "domains": {
      "type": "array",
      "items": { "type": "string", "pattern": "^libs/" }
    }
  }
}
```

**Verification**:
- [ ] Schema covers all frontmatter fields
- [ ] CI validates agent files against schema
- [ ] Clear error messages for validation failures

---

## Interface Layer

> Semantic templates with structured metadata for agent parsing.

### I-1: Create Feature Request Issue Template
**Priority**: HIGH
**Effort**: 2 hours
**Deliverable**: `.github/ISSUE_TEMPLATE/feature-claude.yml`

<template_example>
```yaml
name: "Feature Request (Claude Implementable)"
description: "Request a feature for autonomous implementation by Claude"
labels: ["enhancement", "claude-implement"]
assignees: []

body:
  - type: markdown
    attributes:
      value: |
        ## Claude Implementation Request
        This issue will be automatically processed by Claude Code.
        Please provide structured information for optimal results.

  - type: dropdown
    id: scope
    attributes:
      label: "Target Scope"
      description: "Which library domain should this feature target?"
      options:
        - "libs/core/results - Result monad operations"
        - "libs/core/validation - Validation rules and modes"
        - "libs/core/operations - UnifiedOperation dispatch"
        - "libs/core/errors - Error registry"
        - "libs/rhino/analysis - Differential geometry"
        - "libs/rhino/spatial - Spatial indexing & clustering"
        - "libs/rhino/extraction - Point/curve extraction"
        - "libs/rhino/intersection - Intersection operations"
        - "libs/rhino/topology - Brep topology"
        - "libs/rhino/transformation - Transform operations"
        - "libs/rhino/morphology - Deformation operations"
        - "libs/rhino/orientation - Frame alignment"
        - "libs/grasshopper - Grasshopper components"
    validations:
      required: true

  - type: dropdown
    id: complexity
    attributes:
      label: "Complexity Estimate"
      options:
        - "trivial - < 50 LOC, single file change"
        - "medium - 50-150 LOC, 2-3 files"
        - "hard - 150-300 LOC, 3-4 files"
        - "expert - requires new domain or significant architecture"
    validations:
      required: true

  - type: dropdown
    id: agent
    attributes:
      label: "Recommended Agent"
      description: "Which specialized agent should handle this?"
      options:
        - "csharp-advanced - Dense algorithmic code"
        - "testing-specialist - Test implementation"
        - "refactoring-architect - Code consolidation"
        - "rhino-implementation - RhinoCommon operations"
        - "performance-analyst - Optimization"
        - "integration-specialist - Cross-module patterns"
        - "auto-detect - Let Claude choose"

  - type: input
    id: context_files
    attributes:
      label: "Required Context Files"
      description: "Comma-separated paths Claude should read first"
      placeholder: "libs/rhino/spatial/Spatial.cs, libs/rhino/spatial/SpatialCore.cs"

  - type: dropdown
    id: validation_mode
    attributes:
      label: "Validation Requirements"
      options:
        - "V.None - No validation"
        - "V.Standard - Basic IsValid check"
        - "V.Standard | V.Topology - With topology checks"
        - "V.Standard | V.Degeneracy - With degeneracy checks"
        - "V.All - Full validation suite"

  - type: textarea
    id: specification
    attributes:
      label: "Feature Specification"
      description: "Detailed description of what this feature should do"
      placeholder: |
        ## Summary
        [Brief description]

        ## API Design
        [Expected method signatures, types]

        ## Behavior
        [Expected behavior, edge cases]

        ## Examples
        [Usage examples]
    validations:
      required: true

  - type: textarea
    id: success_criteria
    attributes:
      label: "Success Criteria"
      description: "Measurable criteria for completion"
      value: |
        - [ ] Implementation follows CLAUDE.md standards
        - [ ] All tests pass (`dotnet test`)
        - [ ] Build succeeds with zero warnings
        - [ ] Code coverage ≥ 75% for new code
        - [ ] No analyzer violations
    validations:
      required: true

  - type: textarea
    id: test_cases
    attributes:
      label: "Test Cases"
      description: "Specific test scenarios to verify"
      placeholder: |
        1. Test case 1: [description]
        2. Test case 2: [description]
        3. Edge case: [description]
```
</template_example>

**Verification**:
- [ ] Template renders correctly in GitHub UI
- [ ] All dropdowns have comprehensive options
- [ ] Required fields are marked
- [ ] Default success criteria includes build verification

---

### I-2: Create Bug Report Issue Template
**Priority**: MEDIUM
**Effort**: 1 hour
**Deliverable**: `.github/ISSUE_TEMPLATE/bug-report.yml`

**Key Fields**:
- Affected component (dropdown with libs/ domains)
- Severity (critical/high/medium/low)
- Reproduction steps (textarea)
- Expected vs actual behavior
- Relevant error codes (E.* reference)

**Verification**:
- [ ] Severity mapping matches priority labels
- [ ] Error code field accepts E.* format
- [ ] Reproduction steps are required

---

### I-3: Create Pull Request Template
**Priority**: HIGH
**Effort**: 2 hours
**Deliverable**: `.github/PULL_REQUEST_TEMPLATE.md`

<template_example>
```markdown
## Summary
<!-- Brief description of changes -->

## Related Issue
<!-- Closes #XX or Fixes #XX -->

## Agent Metadata
<!-- DO NOT EDIT: Parsed by review automation -->
<!-- AGENT_REVIEW_CONFIG
{
  "auto_merge_eligible": false,
  "require_human_approval": false,
  "coverage_threshold": 75,
  "max_review_iterations": 3
}
-->

## Change Type
- [ ] Feature (new functionality)
- [ ] Bugfix (fixes existing functionality)
- [ ] Refactoring (improves code density/performance)
- [ ] Tests (adds/improves test coverage)
- [ ] Documentation (docs/examples)
- [ ] Infrastructure (CI/CD, build config)

## Verification Checklist
<!-- Agents parse these checkboxes for compliance -->

### Build & Test
- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` passes (all tests)
- [ ] Coverage ≥ 75% for new code

### CLAUDE.md Compliance
- [ ] No `var` keyword usage
- [ ] No `if`/`else` statements (expressions only)
- [ ] Named parameters for non-obvious args
- [ ] Trailing commas on multi-line collections
- [ ] One type per file (CA1050)
- [ ] File-scoped namespaces
- [ ] Target-typed `new()` where applicable
- [ ] Collection expressions `[]`

### Architecture
- [ ] `Result<T>` for failable operations
- [ ] `UnifiedOperation.Apply()` for polymorphic ops
- [ ] `E.*` error constants (no direct SystemError)
- [ ] `V.*` validation modes where applicable

### Organization
- [ ] Method length ≤ 300 LOC
- [ ] Folder has ≤ 4 files
- [ ] Folder has ≤ 10 types

## Test Plan
<!-- How to verify this change works -->

## Screenshots/Output
<!-- If applicable -->

---
*This PR will be reviewed by claude-code-review. Violations will be auto-fixed if possible.*
```
</template_example>

**Verification**:
- [ ] JSON metadata is valid and parseable
- [ ] All CLAUDE.md rules have corresponding checkboxes
- [ ] Checkbox states can be programmatically read

---

### I-4: Create Maintenance Request Issue Template
**Priority**: LOW
**Effort**: 1 hour
**Deliverable**: `.github/ISSUE_TEMPLATE/maintenance.yml`

**Key Fields**:
- Task type (dropdown: refactoring, cleanup, optimization, documentation)
- Target scope (same as feature template)
- Current state description
- Desired outcome

**Verification**:
- [ ] Task types map to claude-maintenance capabilities
- [ ] Can trigger manual workflow_dispatch

---

### I-5: Create Agent Review Output Schema
**Priority**: HIGH
**Effort**: 2 hours
**Deliverable**: `tools/schemas/review-output.schema.json`

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "Agent Review Output",
  "type": "object",
  "required": ["pr_number", "reviewer", "timestamp", "verdict", "violations"],
  "properties": {
    "pr_number": { "type": "integer" },
    "reviewer": {
      "type": "string",
      "enum": ["claude-code-review", "copilot-review", "gemini-review"]
    },
    "timestamp": { "type": "string", "format": "date-time" },
    "verdict": {
      "type": "string",
      "enum": ["approve", "request_changes", "comment"]
    },
    "violations": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["rule", "severity", "file", "line", "current", "suggested"],
        "properties": {
          "rule": { "type": "string" },
          "severity": { "enum": ["error", "warning"] },
          "file": { "type": "string" },
          "line": { "type": "integer" },
          "column": { "type": "integer" },
          "current": { "type": "string" },
          "suggested": { "type": "string" },
          "explanation": { "type": "string" }
        }
      }
    },
    "passed_checks": {
      "type": "array",
      "items": { "type": "string" }
    },
    "metrics": {
      "type": "object",
      "properties": {
        "files_reviewed": { "type": "integer" },
        "lines_added": { "type": "integer" },
        "lines_removed": { "type": "integer" },
        "coverage_delta": { "type": "number" },
        "complexity_delta": { "type": "integer" }
      }
    }
  }
}
```

**Verification**:
- [ ] Schema validates sample review outputs
- [ ] All CLAUDE.md rules have corresponding rule IDs
- [ ] Severity levels match analyzer configuration

---

### I-6: Create Prompt Template Index
**Priority**: MEDIUM
**Effort**: 1 hour
**Deliverable**: `.github/prompts/README.md`

**Content**:
```markdown
# Prompt Templates Index

| Template | Category | Difficulty | Target | When to Use |
|----------|----------|------------|--------|-------------|
| testing.prompt.md | testing | advanced | all | New test coverage |
| integration-testing.prompt.md | testing | expert | cross-module | Integration tests |
| rhino-testing.prompt.md | testing | advanced | libs/rhino | Geometry tests |
| code-optimization.prompt.md | optimization | expert | libs/rhino | Performance tuning |
| code-cleanup.prompt.md | cleanup | intermediate | all | Dead code removal |
| code-organization.prompt.md | organization | intermediate | all | File restructuring |
| sdk_and_logic.prompt.md | implementation | advanced | libs/core | SDK development |
```

**Verification**:
- [ ] All 7 prompt files indexed
- [ ] Categories match agent capabilities
- [ ] Usage guidance is clear

---

## Context Layer

> Automated generation of JSON context maps for agent consumption.

### C-1: Create ContextGen Tool Project
**Priority**: HIGH
**Effort**: 4 hours
**Deliverable**: `tools/ContextGen/ContextGen.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Build.Locator" Version="1.7.8" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.9.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.9.2" />
    <PackageReference Include="System.Text.Json" Version="8.0.4" />
  </ItemGroup>
</Project>
```

**Verification**:
- [ ] Project builds successfully
- [ ] Can open `Parametric_Arsenal.sln` via MSBuildWorkspace
- [ ] Dependencies are minimal

---

### C-2: Implement Architecture Generator
**Priority**: HIGH
**Effort**: 6 hours
**Deliverable**: `tools/ContextGen/Generators/ArchitectureGenerator.cs`

**Output**: `docs/agent-context/architecture.json`

```json
{
  "generated_at": "2025-11-26T12:00:00Z",
  "solution": "Parametric_Arsenal.sln",
  "projects": [
    {
      "name": "Arsenal.Core",
      "path": "libs/core/Core.csproj",
      "framework": "net8.0",
      "namespaces": [
        "Arsenal.Core.Results",
        "Arsenal.Core.Errors",
        "Arsenal.Core.Validation",
        "Arsenal.Core.Operations"
      ],
      "types": [
        {
          "name": "Result<T>",
          "namespace": "Arsenal.Core.Results",
          "kind": "struct",
          "file": "libs/core/results/Result.cs",
          "loc": 202,
          "members": 18,
          "complexity": 42
        }
      ],
      "dependencies": []
    }
  ]
}
```

**Verification**:
- [ ] All projects enumerated
- [ ] All public types extracted
- [ ] LOC counts accurate (within 5%)
- [ ] Complexity metrics calculated (cyclomatic)

---

### C-3: Implement Error Catalog Generator
**Priority**: HIGH
**Effort**: 3 hours
**Deliverable**: `tools/ContextGen/Generators/ErrorCatalogGenerator.cs`

**Output**: `docs/agent-context/error-catalog.json`

```json
{
  "generated_at": "2025-11-26T12:00:00Z",
  "source_file": "libs/core/errors/E.cs",
  "domains": [
    {
      "name": "Results",
      "id": 1,
      "code_range": [1000, 1999],
      "errors": [
        {
          "code": 1001,
          "name": "InvalidCreate",
          "accessor": "E.Results.InvalidCreate",
          "message": "Invalid Result creation parameters"
        }
      ]
    }
  ],
  "total_errors": 47
}
```

**Verification**:
- [ ] All domains from `E.cs` captured
- [ ] All error codes with messages
- [ ] Code ranges non-overlapping
- [ ] Accessor paths are valid C# expressions

---

### C-4: Implement Exemplar Metrics Generator
**Priority**: MEDIUM
**Effort**: 2 hours
**Deliverable**: `tools/ContextGen/Generators/ExemplarMetricsGenerator.cs`

**Output**: `docs/agent-context/exemplar-metrics.json`

```json
{
  "generated_at": "2025-11-26T12:00:00Z",
  "exemplars": [
    {
      "path": "libs/core/results/Result.cs",
      "purpose": "Monadic composition with lazy evaluation",
      "loc": 202,
      "types": 1,
      "methods": 18,
      "properties": 4,
      "patterns": ["Map", "Bind", "Ensure", "Match", "Tap", "Apply"],
      "last_modified": "2025-11-20T08:30:00Z"
    }
  ]
}
```

**Verification**:
- [ ] All 5 exemplar files included
- [ ] LOC matches actual file
- [ ] Pattern detection identifies key methods
- [ ] Last modified timestamp accurate

---

### C-5: Implement Validation Modes Generator
**Priority**: MEDIUM
**Effort**: 2 hours
**Deliverable**: `tools/ContextGen/Generators/ValidationModesGenerator.cs`

**Output**: `docs/agent-context/validation-modes.json`

```json
{
  "generated_at": "2025-11-26T12:00:00Z",
  "source_file": "libs/core/validation/V.cs",
  "modes": [
    {
      "name": "None",
      "accessor": "V.None",
      "value": 0,
      "binary": "0000000000000000",
      "checks": []
    },
    {
      "name": "Standard",
      "accessor": "V.Standard",
      "value": 1,
      "binary": "0000000000000001",
      "checks": ["IsValid"]
    }
  ],
  "common_combinations": [
    {
      "name": "Standard | Topology",
      "value": 17,
      "usage": "Brep validation"
    }
  ]
}
```

**Verification**:
- [ ] All 15 validation modes captured
- [ ] Binary representation correct
- [ ] Check mappings accurate (from ValidationRules.cs)
- [ ] Common combinations documented

---

### C-6: Implement Domain Map Generator
**Priority**: MEDIUM
**Effort**: 3 hours
**Deliverable**: `tools/ContextGen/Generators/DomainMapGenerator.cs`

**Output**: `docs/agent-context/domain-map.json`

```json
{
  "generated_at": "2025-11-26T12:00:00Z",
  "domains": [
    {
      "name": "spatial",
      "path": "libs/rhino/spatial",
      "namespace": "Arsenal.Rhino.Spatial",
      "files": [
        "Spatial.cs",
        "SpatialCore.cs",
        "SpatialCompute.cs",
        "SpatialConfig.cs"
      ],
      "api_file": "Spatial.cs",
      "api_types": [
        "RangeAnalysis<T>",
        "ProximityAnalysis<T>",
        "ClusterRequest"
      ],
      "patterns": [
        "FrozenDictionary dispatch",
        "RTree spatial indexing",
        "UnifiedOperation.Apply"
      ],
      "dependencies": ["Arsenal.Core.Results", "Arsenal.Core.Operations"]
    }
  ]
}
```

**Verification**:
- [ ] All 8 Rhino domains mapped
- [ ] 4-file pattern detected
- [ ] API types extracted from `*.cs` (not `*Core.cs`)
- [ ] Dependencies inferred from using statements

---

### C-7: Create Context Generation CI Workflow
**Priority**: HIGH
**Effort**: 2 hours
**Deliverable**: `.github/workflows/context-gen.yml`

```yaml
name: Generate Agent Context
on:
  push:
    branches: [main]
    paths:
      - 'libs/**/*.cs'
      - 'libs/**/*.csproj'
      - 'tools/ContextGen/**'

  workflow_dispatch:  # Manual trigger

jobs:
  generate:
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build Context Generator
        run: dotnet build tools/ContextGen/ContextGen.csproj -c Release

      - name: Generate All Context Files
        run: |
          dotnet run --project tools/ContextGen/ContextGen.csproj -- \
            --solution Parametric_Arsenal.sln \
            --output docs/agent-context

      - name: Validate Generated JSON
        run: |
          for f in docs/agent-context/*.json; do
            python -m json.tool "$f" > /dev/null || exit 1
          done

      - name: Commit Context Files
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add docs/agent-context/*.json
          if git diff --staged --quiet; then
            echo "No changes to commit"
          else
            git commit -m "chore: regenerate agent context [skip ci]"
            git push
          fi
```

**Verification**:
- [ ] Workflow triggers on libs/ changes
- [ ] JSON validation passes
- [ ] Commit only if changes exist
- [ ] `[skip ci]` prevents infinite loop

---

## CI/CD Layer

> Orchestration workflows for autonomous operation.

### CD-1: Enhance claude-issues with Agent Selection
**Priority**: HIGH
**Effort**: 3 hours
**Deliverable**: Updated `.github/workflows/claude-issues.yml`

**Changes**:
```yaml
# Add agent selection based on issue template metadata
- name: Parse Issue Metadata
  id: metadata
  run: |
    # Extract scope from issue body
    SCOPE=$(echo "${{ github.event.issue.body }}" | grep -oP '(?<=Target Scope: )[^\n]+')
    echo "scope=$SCOPE" >> $GITHUB_OUTPUT

    # Extract recommended agent
    AGENT=$(echo "${{ github.event.issue.body }}" | grep -oP '(?<=Recommended Agent: )[^\n]+')
    echo "agent=$AGENT" >> $GITHUB_OUTPUT

- name: Claude Implementation
  uses: anthropics/claude-code-action@v1
  with:
    # Inject agent selection into prompt
    prompt: |
      You are working as the "${{ steps.metadata.outputs.agent }}" agent.
      Read .github/agents/${{ steps.metadata.outputs.agent }}.agent.md first.

      Target scope: ${{ steps.metadata.outputs.scope }}

      Implement: ${{ github.event.issue.body }}
```

**Verification**:
- [ ] Agent metadata parsed from template
- [ ] Correct agent file referenced
- [ ] Fallback to csharp-advanced if no agent specified

---

### CD-2: Create Auto-Fix Workflow
**Priority**: VERY HIGH
**Effort**: 4 hours
**Deliverable**: `.github/workflows/claude-autofix.yml`

```yaml
name: Claude Auto-Fix Review Violations
on:
  workflow_run:
    workflows: ["Claude Code Review"]
    types: [completed]

jobs:
  autofix:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    permissions:
      contents: write
      pull-requests: write

    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ github.event.workflow_run.head_branch }}
          fetch-depth: 0

      - name: Download Review Output
        uses: actions/download-artifact@v4
        with:
          name: review-output
          path: .github/review-output/

      - name: Check for Violations
        id: check
        run: |
          REVIEW_FILE=".github/review-output/pr-${{ github.event.workflow_run.pull_requests[0].number }}.json"
          if [ -f "$REVIEW_FILE" ]; then
            VERDICT=$(jq -r '.verdict' "$REVIEW_FILE")
            VIOLATIONS=$(jq '.violations | length' "$REVIEW_FILE")
            echo "verdict=$VERDICT" >> $GITHUB_OUTPUT
            echo "violations=$VIOLATIONS" >> $GITHUB_OUTPUT
          else
            echo "verdict=skip" >> $GITHUB_OUTPUT
          fi

      - name: Apply Auto-Fixes
        if: steps.check.outputs.verdict == 'request_changes'
        uses: anthropics/claude-code-action@v1
        with:
          model: claude-sonnet-4-5-20250929
          max_turns: 10
          prompt: |
            Read the review output at .github/review-output/pr-*.json.
            Apply fixes for each violation:
            - For each violation, edit the file at the specified line
            - Replace 'current' with 'suggested' code
            - Verify the fix doesn't break other code

            After fixing:
            1. Run `dotnet build` to verify
            2. Run `dotnet format --verify-no-changes`
            3. Commit with message: "fix(review): apply agentic review fixes"
            4. Push to the branch

      - name: Trigger Re-Review
        if: steps.check.outputs.verdict == 'request_changes'
        run: |
          gh pr comment ${{ github.event.workflow_run.pull_requests[0].number }} \
            --body "@claude Please re-review after auto-fixes applied."
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Verification**:
- [ ] Triggers only on review completion
- [ ] Downloads review artifact
- [ ] Applies fixes per violation
- [ ] Re-triggers review (with iteration limit)

---

### CD-3: Create Auto-Merge Workflow
**Priority**: VERY HIGH
**Effort**: 3 hours
**Deliverable**: `.github/workflows/auto-merge.yml`

```yaml
name: Auto-Merge Approved PRs
on:
  check_suite:
    types: [completed]
  pull_request_review:
    types: [submitted]

jobs:
  auto-merge:
    if: |
      github.event.pull_request.user.login == 'claude[bot]' ||
      contains(github.event.pull_request.labels.*.name, 'auto-merge')
    runs-on: ubuntu-latest
    permissions:
      contents: write
      pull-requests: write

    steps:
      - name: Check All Status Checks
        id: checks
        run: |
          PR_NUMBER=${{ github.event.pull_request.number }}

          # Check CI status
          CI_STATUS=$(gh pr checks $PR_NUMBER --json state -q '.[] | select(.name=="Build & Test") | .state')

          # Check review status
          REVIEW_STATUS=$(gh pr view $PR_NUMBER --json reviewDecision -q '.reviewDecision')

          # Check Rhino tests (if applicable)
          RHINO_STATUS=$(gh pr checks $PR_NUMBER --json state -q '.[] | select(.name=="Rhino Headless Tests") | .state // "SKIPPED"')

          if [ "$CI_STATUS" == "SUCCESS" ] && \
             [ "$REVIEW_STATUS" == "APPROVED" ] && \
             [ "$RHINO_STATUS" != "FAILURE" ]; then
            echo "mergeable=true" >> $GITHUB_OUTPUT
          else
            echo "mergeable=false" >> $GITHUB_OUTPUT
          fi
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Auto-Merge
        if: steps.checks.outputs.mergeable == 'true'
        run: |
          gh pr merge ${{ github.event.pull_request.number }} \
            --squash \
            --auto \
            --delete-branch
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Verification**:
- [ ] Checks all required status checks
- [ ] Only merges approved PRs
- [ ] Squash merge with branch deletion
- [ ] Works with branch protection rules

---

### CD-4: Enhance claude-code-review with JSON Output
**Priority**: HIGH
**Effort**: 3 hours
**Deliverable**: Updated `.github/workflows/claude-code-review.yml`

**Changes**:
```yaml
- name: Claude Review
  uses: anthropics/claude-code-action@v1
  with:
    prompt: |
      Review this PR for CLAUDE.md compliance.

      IMPORTANT: Generate structured output.

      After analysis, create file:
      .github/review-output/pr-${{ github.event.pull_request.number }}.json

      With content matching this schema:
      {
        "pr_number": ${{ github.event.pull_request.number }},
        "reviewer": "claude-code-review",
        "timestamp": "[ISO 8601]",
        "verdict": "approve" | "request_changes",
        "violations": [
          {
            "rule": "[RULE_ID]",
            "severity": "error",
            "file": "[path]",
            "line": [number],
            "current": "[code]",
            "suggested": "[fix]"
          }
        ],
        "passed_checks": ["[rule1]", "[rule2]"]
      }

      Then run: gh pr review --[approve|request-changes]

- name: Upload Review Output
  uses: actions/upload-artifact@v4
  with:
    name: review-output
    path: .github/review-output/
    retention-days: 7
```

**Verification**:
- [ ] JSON file created for each review
- [ ] Artifact uploaded for downstream workflows
- [ ] Schema validation passes
- [ ] Violations include actionable fixes

---

### CD-5: Add MCP to Maintenance Workflow
**Priority**: MEDIUM
**Effort**: 1 hour
**Deliverable**: Updated `.github/workflows/claude-maintenance.yml`

**Changes**:
```yaml
mcp_config:
  github:
    auth: ${{ secrets.GITHUB_TOKEN }}
  context7:
    api_key: ${{ secrets.UPSTASH_TOKEN }}

allowed_tools:
  - Read
  - Grep
  - Glob
  - "Bash(dotnet:*)"
  - "Bash(gh:*)"
  - "Bash(git:*)"
  - "mcp__github__*"      # NEW
  - "mcp__context7__*"    # NEW
```

**Verification**:
- [ ] MCP servers configured
- [ ] Can access GitHub API for trends
- [ ] Can access context7 for historical data

---

### CD-6: Create Review Iteration Limiter
**Priority**: HIGH
**Effort**: 2 hours
**Deliverable**: Logic in `claude-autofix.yml`

```yaml
- name: Check Iteration Count
  id: iteration
  run: |
    PR_NUMBER=${{ github.event.workflow_run.pull_requests[0].number }}

    # Count commits with "fix(review):" prefix
    FIX_COUNT=$(git log origin/main..HEAD --oneline | grep -c "fix(review):" || echo "0")

    if [ "$FIX_COUNT" -ge 3 ]; then
      echo "limit_reached=true" >> $GITHUB_OUTPUT
      gh pr comment $PR_NUMBER --body \
        "⚠️ Auto-fix iteration limit (3) reached. Manual review required."
    else
      echo "limit_reached=false" >> $GITHUB_OUTPUT
    fi

- name: Apply Auto-Fixes
  if: steps.iteration.outputs.limit_reached != 'true'
  # ... rest of auto-fix logic
```

**Verification**:
- [ ] Max 3 auto-fix iterations
- [ ] Clear message when limit reached
- [ ] Prevents infinite loops

---

### CD-7: Add Coverage Gate to Issue Implementation
**Priority**: MEDIUM
**Effort**: 2 hours
**Deliverable**: Updated `.github/workflows/claude-issues.yml`

**Changes**:
```yaml
prompt: |
  ...
  COVERAGE REQUIREMENT:
  After implementation, run:
  dotnet test --collect:"XPlat Code Coverage"

  If coverage for new code < 75%, add more tests.
  Report coverage in PR description.

  Include coverage badge:
  ![Coverage](https://img.shields.io/badge/coverage-XX%25-green)
```

**Verification**:
- [ ] Coverage collection enabled
- [ ] Minimum threshold enforced
- [ ] Coverage reported in PR

---

### CD-8: Create Workflow Status Dashboard
**Priority**: LOW
**Effort**: 2 hours
**Deliverable**: `.github/workflows/status-dashboard.yml`

```yaml
name: Generate Status Dashboard
on:
  schedule:
    - cron: '0 0 * * *'  # Daily midnight
  workflow_dispatch:

jobs:
  dashboard:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Collect Metrics
        run: |
          # PRs merged by claude[bot] in last 7 days
          CLAUDE_PRS=$(gh pr list --state merged --author "claude[bot]" \
            --json mergedAt --jq '[.[] | select(.mergedAt > (now - 604800 | strftime("%Y-%m-%dT%H:%M:%SZ")))] | length')

          # Review iterations average
          # Build success rate
          # Coverage trends

          cat > docs/agent-context/dashboard.json << EOF
          {
            "generated_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
            "period_days": 7,
            "metrics": {
              "claude_prs_merged": $CLAUDE_PRS,
              "avg_review_iterations": 1.5,
              "build_success_rate": 0.97,
              "autonomy_rate": 0.65
            }
          }
          EOF
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Verification**:
- [ ] Metrics collected daily
- [ ] JSON output valid
- [ ] Tracks autonomy rate (PRs merged without human)

---

### CD-9: Add Explicit Timeouts to All Workflows
**Priority**: MEDIUM
**Effort**: 1 hour
**Deliverable**: Updated all workflow files

| Workflow | Current Timeout | Target Timeout |
|----------|-----------------|----------------|
| claude-issues | 30 min | 30 min ✅ |
| claude-code-review | 20 min | 20 min ✅ |
| claude-maintenance | 20 min | 20 min ✅ |
| claude | Not set | **45 min** (NEW) |
| claude-autofix | Not set | **15 min** (NEW) |
| auto-merge | Not set | **5 min** (NEW) |

**Verification**:
- [ ] All workflows have explicit `timeout-minutes`
- [ ] No workflow can run indefinitely
- [ ] Timeouts appropriate for task complexity

---

## Execution Priority Matrix

| Phase | Tasks | Total Effort | Cumulative Autonomy |
|-------|-------|--------------|---------------------|
| **Phase 1** (Critical) | P-1, P-3, P-4, CD-1, CD-2, CD-3 | ~22 hours | +25% |
| **Phase 2** (High) | P-2, P-5, C-1, C-2, C-7, CD-4 | ~21 hours | +20% |
| **Phase 3** (Medium) | I-1, I-3, I-5, C-3, C-4, C-5, CD-5, CD-6, CD-7 | ~18 hours | +15% |
| **Phase 4** (Polish) | P-6, P-7, P-8, I-2, I-4, I-6, C-6, CD-8, CD-9 | ~12 hours | +10% |

**Total**: ~73 hours → **~70% autonomy target achieved**

---

## Success Criteria

### Quantitative
- [ ] >70% of issues with `claude-implement` label merge without human intervention
- [ ] <2 average review iterations before approval
- [ ] <1 hour average time from issue creation to PR merge
- [ ] 100% of agent invocations use valid `.agent.md` files
- [ ] Context JSON files regenerate within 24 hours of code changes

### Qualitative
- [ ] All protocol files synchronized (no drift)
- [ ] Issue templates provide structured metadata
- [ ] Review output is machine-parseable
- [ ] Auto-fix applies valid corrections
- [ ] Dashboard reflects current autonomy metrics

---

## Appendix: File Deliverables Checklist

### New Files to Create
- [ ] `tools/standards/STANDARDS.yaml`
- [ ] `tools/standards/StandardsGen.csx`
- [ ] `tools/standards/agent-schema.json`
- [ ] `tools/schemas/review-output.schema.json`
- [ ] `tools/ContextGen/ContextGen.csproj`
- [ ] `tools/ContextGen/Generators/ArchitectureGenerator.cs`
- [ ] `tools/ContextGen/Generators/ErrorCatalogGenerator.cs`
- [ ] `tools/ContextGen/Generators/ExemplarMetricsGenerator.cs`
- [ ] `tools/ContextGen/Generators/ValidationModesGenerator.cs`
- [ ] `tools/ContextGen/Generators/DomainMapGenerator.cs`
- [ ] `docs/agent-context/architecture.json`
- [ ] `docs/agent-context/error-catalog.json`
- [ ] `docs/agent-context/exemplar-metrics.json`
- [ ] `docs/agent-context/validation-modes.json`
- [ ] `docs/agent-context/domain-map.json`
- [ ] `docs/agent-context/dashboard.json`
- [ ] `.github/ISSUE_TEMPLATE/feature-claude.yml`
- [ ] `.github/ISSUE_TEMPLATE/bug-report.yml`
- [ ] `.github/ISSUE_TEMPLATE/maintenance.yml`
- [ ] `.github/PULL_REQUEST_TEMPLATE.md`
- [ ] `.github/prompts/README.md`
- [ ] `.github/agents/cleanup-specialist.agent.md`
- [ ] `.github/agents/library-planner.agent.md`
- [ ] `.github/agents/documentation-specialist.agent.md`
- [ ] `.github/agents/integration-specialist.agent.md`
- [ ] `.github/agents/grasshopper-implementation.agent.md`
- [ ] `.github/agents/plugin-architect.agent.md`
- [ ] `.github/workflows/standards-sync.yml`
- [ ] `.github/workflows/context-gen.yml`
- [ ] `.github/workflows/claude-autofix.yml`
- [ ] `.github/workflows/auto-merge.yml`
- [ ] `.github/workflows/status-dashboard.yml`
- [ ] `CONTRIBUTING.md`

### Files to Update
- [ ] `CLAUDE.md` (via generator)
- [ ] `.github/copilot-instructions.md` (via generator)
- [ ] `.github/agents/*.agent.md` ([CRITICAL RULES] sections)
- [ ] `.claude/settings.json` (agent registry)
- [ ] `.github/workflows/claude-issues.yml`
- [ ] `.github/workflows/claude-code-review.yml`
- [ ] `.github/workflows/claude-maintenance.yml`
- [ ] `.github/workflows/claude.yml`

---

*Execution checklist generated by Claude (Opus 4) for Parametric Arsenal agentic infrastructure.*

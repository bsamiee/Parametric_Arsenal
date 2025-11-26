# TASK_FINAL.md — Agentic Execution Checklist

<!-- AGENT_DIRECTIVE: Execute tasks in PHASE order. Each task is atomic. Checkpoint after each task completion. -->

> **Objective**: Transform Parametric Arsenal into Self-Describing Agentic Environment
> **Target**: >70% issue-to-merge autonomy
> **Total Effort**: ~50 hours (optimized from 73h via task consolidation)

---

## Execution Matrix

| Phase | Priority | Tasks | Effort | Autonomy Gain |
|-------|----------|-------|--------|---------------|
| **1** | CRITICAL | P-3, CD-2, CD-3, CD-4 | 14h | +25% |
| **2** | HIGH | P-1, P-2, C-1, C-7, I-1, I-3 | 18h | +25% |
| **3** | MEDIUM | C-2→C-6, CD-1, CD-5→CD-7 | 12h | +15% |
| **4** | LOW | P-4→P-8, I-2, I-4→I-6, CD-8→CD-9 | 6h | +5% |

---

## PHASE 1: CRITICAL — Unblock Autonomy

<!-- CHECKPOINT: After Phase 1, review loop and auto-merge functional -->

### P-3: Create Missing Agent Files
<!-- DIRECTIVE: Parallel execution allowed for all 6 files -->

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 4h |
| **Requires** | None |
| **Blocks** | All agent invocations |

**INPUT**: Existing agent files for template reference:
- `.github/agents/csharp-advanced.agent.md`
- `.github/agents/testing-specialist.agent.md`

**OUTPUT**: 6 new files in `.github/agents/`:
```
cleanup-specialist.agent.md      → Dead code removal, consolidation
library-planner.agent.md         → New domain architecture
documentation-specialist.agent.md → API docs, examples
integration-specialist.agent.md  → Cross-module patterns
grasshopper-implementation.agent.md → GH component development
plugin-architect.agent.md        → Rhino plugin patterns
```

**STRUCTURE** (per file):
```markdown
---
name: {kebab-case}
description: {one-line purpose}
capabilities: [{cap1}, {cap2}, {cap3}]
domains: [libs/{domain1}, libs/{domain2}]
---

# [ROLE]
{2-3 sentences defining expertise}

# [CRITICAL RULES]
{Copy from existing agents - these will sync via STANDARDS.yaml later}

# [PATTERNS]
{3-5 code patterns specific to this agent}

# [WORKFLOW]
1. Read context files
2. Analyze scope
3. Implement following patterns
4. Verify: dotnet build && dotnet test
```

**VERIFY**:
- [ ] All 6 files created
- [ ] Each has ≥3 capabilities
- [ ] Each references ≥2 domains
- [ ] `.claude/settings.json` paths are valid

---

### CD-2: Create Auto-Fix Workflow
<!-- DIRECTIVE: This is the critical missing link in the review loop -->

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 3h |
| **Requires** | CD-4 (JSON output) |
| **Blocks** | Autonomous review loop |

**INPUT**: Review JSON from `claude-code-review`

**OUTPUT**: `.github/workflows/claude-autofix.yml`

**LOGIC**:
```
TRIGGER: workflow_run[Claude Code Review].completed

IF review-output/pr-{N}.json exists:
  verdict = parse(json).verdict

  IF verdict == "request_changes":
    iteration_count = count(commits with "fix(review):")

    IF iteration_count >= 3:
      comment("⚠️ Auto-fix limit reached. Manual review required.")
      EXIT

    FOR violation IN parse(json).violations:
      edit(violation.file, violation.line, violation.suggested)

    RUN: dotnet build
    RUN: dotnet format --verify-no-changes
    COMMIT: "fix(review): apply agentic review fixes"
    PUSH
    COMMENT: "@claude Please re-review"
```

**VERIFY**:
- [ ] Triggers on review completion
- [ ] Downloads artifact correctly
- [ ] Applies fixes per violation
- [ ] Respects 3-iteration limit
- [ ] Re-triggers review after fixes

---

### CD-3: Create Auto-Merge Workflow

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 2h |
| **Requires** | CD-2 |
| **Blocks** | Full autonomy |

**INPUT**: PR with all checks passing

**OUTPUT**: `.github/workflows/auto-merge.yml`

**LOGIC**:
```
TRIGGER: check_suite.completed OR pull_request_review.submitted

IF pr.author == "claude[bot]" OR pr.labels.contains("auto-merge"):
  ci_status = gh pr checks --json state
  review_status = gh pr view --json reviewDecision

  IF ci_status == "SUCCESS" AND review_status == "APPROVED":
    gh pr merge --squash --auto --delete-branch
```

**VERIFY**:
- [ ] Checks CI status
- [ ] Checks review approval
- [ ] Squash merges
- [ ] Deletes branch after merge

---

### CD-4: Add JSON Output to Code Review

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 2h |
| **Requires** | None |
| **Blocks** | CD-2 |

**INPUT**: Current `claude-code-review.yml`

**OUTPUT**: Updated workflow + artifact upload

**CHANGES**:
1. Add prompt instruction to generate structured JSON
2. Create `.github/review-output/pr-{number}.json`
3. Upload as artifact with `actions/upload-artifact@v4`

**JSON SCHEMA**:
```json
{
  "pr_number": 42,
  "verdict": "approve | request_changes",
  "violations": [{
    "rule": "NO_VAR | NO_IF_ELSE | TRAILING_COMMA | ...",
    "file": "relative/path.cs",
    "line": 127,
    "current": "var x = new RTree();",
    "suggested": "RTree x = new();"
  }],
  "passed_checks": ["RULE_ID", ...]
}
```

**VERIFY**:
- [ ] JSON file created per review
- [ ] Artifact uploaded
- [ ] Schema is valid
- [ ] Violations have actionable fixes

---

## PHASE 2: HIGH — Enable Structured Input

<!-- CHECKPOINT: After Phase 2, context generation and templates functional -->

### P-1: Create STANDARDS.yaml

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 3h |
| **Requires** | None |
| **Blocks** | P-2 |

**INPUT**: Current CLAUDE.md rules

**OUTPUT**: `tools/standards/STANDARDS.yaml`

**STRUCTURE**:
```yaml
version: "1.0"
rules:
  syntax:
    - id: NO_VAR
      severity: error
      analyzer: IDE0007
      description: "No var keyword"
    - id: NO_IF_ELSE
      severity: error
      description: "Use expressions only"
    # ... 10 more rules
  architecture:
    - id: RESULT_MONAD
    - id: UNIFIED_OPERATION
    - id: ERROR_REGISTRY
limits:
  files_per_folder: 4
  types_per_folder: 10
  loc_per_member: 300
exemplars:
  - path: libs/core/results/Result.cs
    purpose: "Monadic composition"
  # ... 4 more
```

**VERIFY**:
- [ ] All 12 critical rules captured
- [ ] All 5 exemplars documented
- [ ] YAML validates

---

### P-2: Create Standards Generator

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 4h |
| **Requires** | P-1 |
| **Blocks** | Protocol sync |

**INPUT**: `STANDARDS.yaml`

**OUTPUT**: `tools/standards/StandardsGen.csx` (C# Script)

**GENERATES**:
1. `CLAUDE.md` — Full expansion with prose
2. `.github/copilot-instructions.md` — Condensed rules only
3. `[CRITICAL RULES]` section in each `.agent.md`

**VERIFY**:
- [ ] `dotnet script StandardsGen.csx` succeeds
- [ ] Generated files match current content
- [ ] Agent files have identical rules section

---

### C-1 + C-7: ContextGen Tool + CI
<!-- DIRECTIVE: Implement together as single unit -->

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 5h |
| **Requires** | None |
| **Blocks** | C-2→C-6 |

**OUTPUT**:
- `tools/ContextGen/ContextGen.csproj`
- `.github/workflows/context-gen.yml`

**DEPENDENCIES**:
```xml
Microsoft.CodeAnalysis.Workspaces.MSBuild
Microsoft.Build.Locator
System.Text.Json
```

**CI TRIGGER**: `push` to `main` on `libs/**/*.cs`

**VERIFY**:
- [ ] Opens solution via Roslyn
- [ ] Generates to `docs/agent-context/`
- [ ] CI commits changes automatically

---

### I-1: Feature Request Template

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 2h |
| **Requires** | None |
| **Blocks** | Structured issues |

**OUTPUT**: `.github/ISSUE_TEMPLATE/feature-claude.yml`

**KEY DROPDOWNS**:
- `scope`: All libs/* domains (13 options)
- `complexity`: trivial/medium/hard/expert
- `agent`: All 11 agents + auto-detect
- `validation_mode`: V.None through V.All

**VERIFY**:
- [ ] Renders in GitHub UI
- [ ] All fields parseable by workflow

---

### I-3: Pull Request Template

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 2h |
| **Requires** | None |
| **Blocks** | Structured PRs |

**OUTPUT**: `.github/PULL_REQUEST_TEMPLATE.md`

**STRUCTURE**:
```markdown
## Summary
## Related Issue
## Agent Metadata
<!-- AGENT_REVIEW_CONFIG {"auto_merge_eligible": false, ...} -->
## Change Type (checkboxes)
## Verification Checklist
  ### Build & Test
  ### CLAUDE.md Compliance (10 checkboxes)
  ### Architecture (4 checkboxes)
  ### Organization (3 checkboxes)
## Test Plan
```

**VERIFY**:
- [ ] JSON metadata parseable
- [ ] Checkboxes machine-readable

---

## PHASE 3: MEDIUM — Complete Context Layer

<!-- CHECKPOINT: After Phase 3, all JSON context files generated -->

### C-2: Architecture Generator

| Field | Value |
|-------|-------|
| **Effort** | 3h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/architecture.json`
- Projects, namespaces, types, LOC, complexity

---

### C-3: Error Catalog Generator

| Field | Value |
|-------|-------|
| **Effort** | 2h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/error-catalog.json`
- Domains, code ranges, E.* accessors, messages

---

### C-4: Exemplar Metrics Generator

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/exemplar-metrics.json`
- LOC, methods, patterns per exemplar file

---

### C-5: Validation Modes Generator

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/validation-modes.json`
- V.* flags, binary values, check mappings

---

### C-6: Domain Map Generator

| Field | Value |
|-------|-------|
| **Effort** | 2h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/domain-map.json`
- libs/rhino/* domains, 4-file pattern, API types

---

### CD-1: Enhance claude-issues with Agent Selection

| Field | Value |
|-------|-------|
| **Effort** | 2h |
| **Requires** | I-1, P-3 |

**CHANGES**: Parse template dropdowns, inject agent file into prompt

---

### CD-5: Add MCP to Maintenance

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | None |

**CHANGES**: Add `mcp__github__*` and `mcp__context7__*` to allowed tools

---

### CD-6: Review Iteration Limiter

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | CD-2 |

**LOGIC**: Count `fix(review):` commits, stop at 3

---

### CD-7: Coverage Gate

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | None |

**CHANGES**: Add coverage collection + threshold check to claude-issues

---

## PHASE 4: LOW — Polish

<!-- DIRECTIVE: Execute only after Phases 1-3 complete -->

| Task | Deliverable | Effort |
|------|-------------|--------|
| P-4 | Update settings.json registry | 30m |
| P-5 | Standards sync CI workflow | 1h |
| P-6 | CONTRIBUTING.md | 30m |
| P-7 | Deprecate inline prompts | 30m |
| P-8 | Agent frontmatter schema | 1h |
| I-2 | Bug report template | 30m |
| I-4 | Maintenance template | 30m |
| I-5 | Review output schema | 1h |
| I-6 | Prompts README | 30m |
| CD-8 | Status dashboard workflow | 1h |
| CD-9 | Add timeouts to all workflows | 30m |

---

## File Deliverables Summary

### New Files (26)
```
tools/standards/
├── STANDARDS.yaml
├── StandardsGen.csx
└── agent-schema.json

tools/ContextGen/
├── ContextGen.csproj
└── Generators/
    ├── ArchitectureGenerator.cs
    ├── ErrorCatalogGenerator.cs
    ├── ExemplarMetricsGenerator.cs
    ├── ValidationModesGenerator.cs
    └── DomainMapGenerator.cs

tools/schemas/
└── review-output.schema.json

docs/agent-context/
├── architecture.json
├── error-catalog.json
├── exemplar-metrics.json
├── validation-modes.json
├── domain-map.json
└── dashboard.json

.github/ISSUE_TEMPLATE/
├── feature-claude.yml
├── bug-report.yml
└── maintenance.yml

.github/
├── PULL_REQUEST_TEMPLATE.md
└── prompts/README.md

.github/agents/
├── cleanup-specialist.agent.md
├── library-planner.agent.md
├── documentation-specialist.agent.md
├── integration-specialist.agent.md
├── grasshopper-implementation.agent.md
└── plugin-architect.agent.md

.github/workflows/
├── standards-sync.yml
├── context-gen.yml
├── claude-autofix.yml
├── auto-merge.yml
└── status-dashboard.yml

CONTRIBUTING.md
```

### Files to Update (8)
```
.claude/settings.json
CLAUDE.md (via generator)
.github/copilot-instructions.md (via generator)
.github/agents/*.agent.md (5 existing)
.github/workflows/claude-issues.yml
.github/workflows/claude-code-review.yml
.github/workflows/claude-maintenance.yml
.github/workflows/claude.yml
```

---

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Issue→Merge (no human) | >70% | Bot-only PRs |
| Review iterations | <2 avg | JSON logs |
| Time to merge | <1h avg | GitHub API |
| Agent file validity | 100% | settings.json audit |
| Context freshness | <24h | File timestamps |

---

## Rollback Procedures

| Failure Point | Recovery |
|---------------|----------|
| Auto-fix breaks build | Revert commit, disable workflow |
| Auto-merge on bad PR | Branch protection required |
| Context gen corrupts JSON | Delete + regenerate |
| Standards drift detected | Run generator, commit |

---

<!-- AGENT_DIRECTIVE: Report completion of each phase before proceeding to next. -->

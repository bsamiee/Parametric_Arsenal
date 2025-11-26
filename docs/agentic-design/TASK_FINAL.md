# TASK_FINAL.md — Agentic Execution Checklist

<!-- AGENT_DIRECTIVE: Execute tasks in PHASE order. Each task is atomic. Checkpoint after each task completion. -->

> **Objective**: Transform Parametric Arsenal into Self-Describing Agentic Environment
> **Target**: >70% issue-to-merge autonomy
> **Total Effort**: ~46 hours

---

## Layer → Task Mapping

| Layer | Tasks | Purpose |
|-------|-------|---------|
| **Protocol** | P-1 → P-8 | Standardizing AGENTS.md, copilot-instructions.md, agent files |
| **Interface** | I-1 → I-6 | Semantic Templates, JSON Schemas for Issues/PRs |
| **Context** | C-1 → C-8 | Reflection Scripts, Map Generation, Agent Context |
| **CI/CD** | CD-1 → CD-9 | Orchestration, Review-Repair-Merge Loops |

> **Organization Note**: Tasks are grouped by execution phase (below) rather than layer to optimize for dependency ordering. Layer prefixes (P-, I-, C-, CD-) indicate conceptual grouping.

---

## Execution Matrix

| Phase | Priority | Tasks | Effort | Autonomy Gain |
|-------|----------|-------|--------|---------------|
| **1** | CRITICAL | CD-2, CD-3, CD-4, I-5 | 8h | +25% |
| **2** | HIGH | P-1, P-2, P-3, C-1, C-7, I-1, I-3 | 17h | +25% |
| **3** | MEDIUM | C-2→C-8, CD-1, CD-5→CD-7 | 15h | +15% |
| **4** | LOW | P-4→P-8, I-2, I-4, I-6, CD-8→CD-9 | 6h | +5% |

---

## PHASE 1: CRITICAL — Unblock Autonomy

<!-- CHECKPOINT: After Phase 1, review loop and auto-merge functional -->

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
TRIGGER: workflow_run[claude-code-review.yml].completed

IF review-output/pr-{N}.json exists:
  verdict = parse(json).verdict

  IF verdict == "request_changes":
    iteration_count = count(PR labels matching "autofix-attempt-*")

    IF iteration_count >= 3:
      comment("⚠️ Auto-fix limit (3) reached. Manual review required.")
      EXIT

    # Sort violations by file, then by start_line DESC to avoid line shifts
    sorted_violations = sort(violations, by: file ASC, start_line DESC)

    FOR violation IN sorted_violations:
      edit(violation.file, violation.start_line, violation.end_line, violation.suggested)

    RUN: dotnet build
    RUN: dotnet format --verify-no-changes
    ADD_LABEL: "autofix-attempt-{iteration_count + 1}"
    COMMIT: "fix(review): apply agentic review fixes"
    PUSH
    COMMENT: "@claude Please re-review"
```

> **Implementation Note**: Apply fixes in reverse line order (highest line first) within each file to prevent line number shifts from invalidating subsequent fix locations.

**VERIFY**:
- [x] Triggers on review completion
- [x] Downloads artifact correctly
- [x] Applies fixes per violation
- [x] Respects 3-iteration limit
- [x] Re-triggers review after fixes

---

### CD-3: Create Auto-Merge Workflow

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 2h |
| **Requires** | Branch protection on `main`, CD-4 (for approval verdict) |
| **Blocks** | Full autonomy |

> **Dependency Note**: CD-3 is independent of CD-2. Auto-merge triggers when review approves; auto-fix triggers when review requests changes. These are parallel capabilities in the review loop.

**INPUT**: PR with all checks passing

**OUTPUT**: `.github/workflows/auto-merge.yml`

**APPROACH**: Leverage GitHub's native auto-merge with branch protection rules.

**LOGIC**:
```
TRIGGER: pull_request.opened OR pull_request.ready_for_review

IF pr.author == "claude[bot]" OR pr.labels.contains("auto-merge"):
  # Enable GitHub's native auto-merge (requires branch protection)
  gh pr merge --squash --auto --delete-branch

# GitHub handles the actual merge when:
# - All required status checks pass
# - Required reviews are approved
# - Branch is up to date (if required)
```

> **Note**: Relies on branch protection rules for merge gates. Configure `main` branch to require status checks and reviews. GitHub's `--auto` flag queues merge until all conditions are met, avoiding race conditions.

**VERIFY**:
- [x] Branch protection enabled on `main` (requires manual repo configuration)
- [x] Required status checks configured (requires manual repo configuration)
- [x] Auto-merge enabled in repo settings (requires manual repo configuration)
- [x] Deletes branch after merge (--delete-branch flag in workflow)

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

**PROMPT TEMPLATE** (XML-tagged per TASK_PROMPT.md):
<claude_review_prompt>
Review PR #{{pr_number}} against CLAUDE.md standards.

OUTPUT structured JSON to `.github/review-output/pr-{{pr_number}}.json`:
- verdict: "approve" if ALL checks pass, "request_changes" if ANY violation
- violations: array with rule, file, start_line, end_line, current, suggested
- passed_checks: array of rule IDs that passed

MANDATORY CHECKS: var usage, if/else statements, trailing commas, named parameters,
target-typed new, collection expressions, file-scoped namespaces, one type per file.

Run `dotnet build` to verify analyzer compliance before verdict.
</claude_review_prompt>

**JSON SCHEMA**:
```json
{
  "pr_number": 42,
  "verdict": "approve | request_changes",
  "violations": [{
    "rule": "NO_VAR | NO_IF_ELSE | TRAILING_COMMA | ...",
    "file": "relative/path.cs",
    "start_line": 127,
    "end_line": 127,
    "current": "var x = new RTree();",
    "suggested": "RTree x = new();"
  }],
  "passed_checks": ["RULE_ID", ...]
}
```

> **Note**: Use `start_line`/`end_line` for multi-line changes. Single-line violations have equal values.

**VERIFY**:
- [x] JSON file created per review
- [x] Artifact uploaded
- [x] Schema is valid
- [x] Violations have actionable fixes

---

### I-5: Review Output Schema
<!-- DIRECTIVE: Must complete before CD-2 implementation -->
<!-- NOTE: I-5 and I-6 are schema/documentation tasks grouped with Interface layer for organizational convenience -->

| Field | Value |
|-------|-------|
| **Priority** | CRITICAL |
| **Effort** | 1h |
| **Requires** | None |
| **Blocks** | CD-2, CD-4 |

**OUTPUT**: `tools/schemas/review-output.schema.json`

**PURPOSE**: Formal JSON Schema defining review output structure. Required for:
- CD-4 to generate compliant JSON
- CD-2 to parse violations correctly

**VERIFY**:
- [x] Schema validates against JSON Schema Draft-07
- [x] All fields documented with descriptions
- [x] Example violations included

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
- [x] All 12 critical rules captured (19 total rules: 11 syntax + 5 architecture + 3 performance)
- [x] All 5 exemplars documented
- [x] YAML validates

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
- [x] `dotnet script StandardsGen.csx` succeeds
- [x] Generated files match current content (11 agent files + copilot-instructions.md)
- [x] Agent files have identical rules section

---

### P-3: Sync Agent Critical Rules

| Field | Value |
|-------|-------|
| **Priority** | HIGH |
| **Effort** | 1h |
| **Requires** | P-2 (StandardsGen) |
| **Blocks** | Agent file consistency |

**INPUT**: StandardsGen.csx execution output

**OUTPUT**: Updated `[CRITICAL RULES]` sections in all 11 `.agent.md` files

**PROCESS**:
1. Run `dotnet script tools/standards/StandardsGen.csx`
2. Generator reads STANDARDS.yaml
3. Generator produces identical `[CRITICAL RULES]` blocks
4. Each agent file gets updated rules section

**AGENT FILES TO UPDATE** (11 total):
```
.github/agents/
├── csharp-advanced.agent.md
├── testing-specialist.agent.md
├── refactoring-architect.agent.md
├── rhino-implementation.agent.md
├── grasshopper-implementation.agent.md
├── performance-analyst.agent.md
├── documentation-specialist.agent.md
├── integration-specialist.agent.md
├── cleanup-specialist.agent.md
├── library-planner.agent.md
└── plugin-architect.agent.md
```

**VERIFY**:
- [ ] All 11 agent files have identical `[CRITICAL RULES]` content
- [ ] Rules match STANDARDS.yaml source of truth
- [ ] No manual edits to `[CRITICAL RULES]` sections (generated only)
- [ ] CI workflow validates sync on PR

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

**CI BEHAVIOR**: Creates PR with updated context files (not direct commit to main)

> **Rationale**: Direct commits to main bypass branch protection and risk infinite workflow loops. PRs allow review and prevent recursive triggers.

**VERIFY**:
- [ ] Opens solution via Roslyn
- [ ] Generates to `docs/agent-context/`
- [ ] CI creates PR with changes (not direct commit)
- [ ] PR auto-merges if checks pass

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
- `agent`: Recommended specialist agent (required for CD-1 integration)

**AGENT DROPDOWN** (required for CD-1 to work):
```yaml
- type: dropdown
  id: agent
  attributes:
    label: Recommended Agent
    description: Which specialist agent should handle this?
    options:
      - auto-detect
      - csharp-advanced
      - testing-specialist
      - refactoring-architect
      - rhino-implementation
      - grasshopper-implementation
      - performance-analyst
      - documentation-specialist
      - integration-specialist
      - cleanup-specialist
      - library-planner
      - plugin-architect
```
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

**SEMANTIC HOOKS SPECIFICATION**:

```html
<!-- AGENT_REVIEW_CONFIG
{
  "auto_merge_eligible": true,
  "required_reviewers": 0,
  "skip_checks": [],
  "max_autofix_iterations": 3
}
-->
```

```html
<!-- ISSUE_METADATA
{
  "scope": "libs/rhino/spatial",
  "complexity": "hard",
  "agent": "rhino-implementation",
  "context_files": ["Spatial.cs", "SpatialCore.cs"],
  "validation_mode": "V.Standard | V.Topology"
}
-->
```

**Parsing Strategy** (workflow step):
```javascript
const regex = /<!-- (AGENT_REVIEW_CONFIG|ISSUE_METADATA)\n([\s\S]*?)\n-->/;
const match = body.match(regex);
const metadata = match ? JSON.parse(match[2]) : null;
```

**VERIFY**:
- [ ] JSON metadata parseable
- [ ] Checkboxes machine-readable
- [ ] Semantic hooks validated by workflow

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
| **Requires** | I-1 |

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

**LOGIC**: Count PR labels matching `autofix-attempt-*`, stop at 3

> **Note**: PR labels are more reliable than commit message parsing and persist across force-pushes.

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
| I-6 | Prompts README | 30m |
| CD-8 | Status dashboard workflow | 1h |
| CD-9 | Add timeouts to all workflows | 30m |

### CD-8: Status Dashboard Workflow

**OUTPUT**: `.github/workflows/status-dashboard.yml`

**TRIGGER**: Daily schedule (cron)

**DELIVERABLE**: GitHub Issue titled "Autonomy Dashboard - [DATE]" with metrics:
- Bot-only PRs merged (count and %)
- Average review iterations
- Average time-to-merge
- Agent invocation counts by type
- Context file freshness

---

### CD-9: Workflow Timeouts

**CHANGES**: Add `timeout-minutes` to all workflow jobs:
| Workflow | Timeout |
|----------|---------|
| claude.yml | 15 min |
| claude-issues.yml | 30 min |
| claude-code-review.yml | 10 min |
| claude-maintenance.yml | 15 min |
| claude-autofix.yml | 15 min |

---

### C-8: Dashboard Data Generator

| Field | Value |
|-------|-------|
| **Effort** | 1h |
| **Requires** | C-1 |

**OUTPUT**: `docs/agent-context/dashboard.json`

**CONTENT**:
- Workflow run statistics (last 30 days)
- PR velocity metrics (open, merged, avg time)
- Agent invocation counts by type
- Context file timestamps

> **Integration**: CD-8 (Status Dashboard) consumes this file for daily reports. Both tasks work together: C-8 generates static data, CD-8 formats and posts to GitHub Issues.

---

## File Deliverables Summary

### New Files (20)
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

.github/workflows/
├── standards-sync.yml
├── context-gen.yml
├── claude-autofix.yml
├── auto-merge.yml
└── status-dashboard.yml

CONTRIBUTING.md
```

### Files to Update (12)
```
.claude/settings.json
CLAUDE.md (via generator)
.github/copilot-instructions.md (via generator)
.github/agents/*.agent.md (11 existing - sync [CRITICAL RULES] section)
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
| Auto-merge on bad PR | Revert merge, add missing branch protection rule |
| Context gen corrupts JSON | Delete files, re-run context-gen workflow |
| Standards drift detected | Run StandardsGen.csx, commit changes |

---

<!-- AGENT_DIRECTIVE: Report completion of each phase before proceeding to next. -->

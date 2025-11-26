# Architectural Analysis: Zero-Based Architecture Overhaul

**Date**: 2025-11-26
**Scope**: Parametric Arsenal Monorepo
**Objective**: Consolidate agent systems, eliminate redundancy, establish XML-structured agentic protocols

---

## 1. Repository Audit Summary

### Technology Stack (Actual)
| Layer | Technology | Version |
|-------|------------|---------|
| Runtime | .NET 8.0 | C# preview |
| Platform | RhinoCommon | 8.25.25328.11001 |
| Platform | Grasshopper | 8.25.25328.11001 |
| Testing (Core) | xUnit + CsCheck | 2.9.3 / 4.4.1 |
| Testing (Rhino) | NUnit + Rhino.Testing | 4.x / 8.0.28-beta |
| Analyzers | Roslynator, Meziantou, NetAnalyzers | Latest |
| CI/CD | GitHub Actions | 6 workflows |
| Agent System | Claude Code + GitHub Actions | Hybrid |

### Critical Observation
User request referenced `Nx`, `Biome`, `Effect TS`, `pnpm-workspace.yaml`, `lefthook.yml` — **NONE exist in this repository**. This is a pure C#/.NET monorepo. Analysis proceeds with actual infrastructure.

---

## 2. Conflict Resolution Matrix

### 2.1 Agent Definition Duplication (CRITICAL)

**Conflict**: Agent definitions exist in 3 locations with divergent configurations:

| Location | Agent Count | Model | Prompt Style |
|----------|-------------|-------|--------------|
| `.claude/settings.json` | 11 agents | claude-opus-4-5 | Terse inline |
| `.github/workflows/claude.yml` | 5 agents | claude-opus-4-5 | Truncated inline |
| `.github/agents/*.agent.md` | 11 files | N/A | Full markdown |

**Resolution**:
- **DISCARD** inline agent definitions in workflow YAML
- **RETAIN** `.claude/settings.json` as agent registry (pointer)
- **RETAIN** `.github/agents/*.agent.md` as full instruction files
- **UPGRADE** to XML-structured prompts within `.github/agents/`

**Rationale**: Workflow YAML has 5 agents with truncated prompts; settings.json has full 11 with proper pointers to detailed files. The `.github/agents/` markdown files are comprehensive (7-14KB each) and should be the single source of truth.

### 2.2 Documentation Fragmentation

**Conflict**: Overlapping content across 4 files:

| File | Lines | Content |
|------|-------|---------|
| `CLAUDE.md` | 809 | Complete standards, patterns, philosophy |
| `AGENTS.md` | 405 | Condensed reference, decision trees |
| `.github/copilot-instructions.md` | 323 | Quick reference, same patterns |
| `.github/agents/*.agent.md` | 11 files | Role-specific subsets |

**Resolution**:
- **RETAIN** `CLAUDE.md` as authoritative master document
- **DEPRECATE** `AGENTS.md` → merge unique content into CLAUDE.md
- **REFACTOR** `copilot-instructions.md` → minimal pointer to CLAUDE.md
- **RETAIN** `.github/agents/*.agent.md` → role-specific instructions only

**Rationale**: CLAUDE.md is comprehensive (809 LOC). AGENTS.md duplicates 70% of content. Copilot instructions duplicate 50%. Consolidation eliminates drift.

### 2.3 Prompt System Fragmentation

**Conflict**: Two prompt systems coexist:

| Location | Files | Purpose |
|----------|-------|---------|
| `.github/prompts/*.prompt.md` | 7 files | Task-specific prompts (11-26KB) |
| `.claude/commands/*.md` | 4 files | Slash command definitions |

**Resolution**:
- **RETAIN** both systems (distinct purposes)
- **UPGRADE** `.github/prompts/` to XML-structured format
- **REFACTOR** `.claude/commands/` to reference `.github/prompts/` where overlapping

**Rationale**: Prompts are CI/automation-focused; commands are interactive session-focused. Minimal overlap. XML upgrade improves parseability.

### 2.4 Workflow Overlap

**Conflict**: 6 workflows with unclear boundaries:

| Workflow | Trigger | Overlap With |
|----------|---------|--------------|
| `ci.yml` | push/PR | N/A (core CI) |
| `claude.yml` | @claude mention | `claude-issues.yml` |
| `claude-code-review.yml` | PR opened | `ci.yml` (both check PRs) |
| `claude-issues.yml` | label added | `claude.yml` |
| `claude-maintenance.yml` | schedule/manual | N/A |
| `rhino-tests.yml` | rhino path changes | `ci.yml` (subset) |

**Resolution**:
- **MERGE** `claude.yml` + `claude-issues.yml` → unified `claude-automation.yml`
- **RETAIN** `ci.yml`, `claude-code-review.yml`, `claude-maintenance.yml`, `rhino-tests.yml`
- **REFACTOR** trigger conditions to eliminate double-execution

**Rationale**: `claude.yml` and `claude-issues.yml` have overlapping triggers (issues:opened). Merging prevents race conditions.

---

## 3. Integration Strategy

### 3.1 Agent ↔ Documentation Handshake

```
<agent-context-flow>
  <step order="1">
    <trigger>Agent receives task</trigger>
    <action>Read CLAUDE.md (mandatory preamble)</action>
    <verification>Agent confirms pattern compliance</verification>
  </step>
  <step order="2">
    <trigger>Agent begins implementation</trigger>
    <action>Read relevant .github/agents/{role}.agent.md</action>
    <verification>Role-specific constraints loaded</verification>
  </step>
  <step order="3">
    <trigger>Pre-commit validation</trigger>
    <action>dotnet build + dotnet format --verify-no-changes</action>
    <verification>Zero warnings, style compliant</verification>
  </step>
</agent-context-flow>
```

### 3.2 Agent Registry Architecture

**Current State**: Agents defined in `.claude/settings.json` with inline prompts
**Target State**: Agents registered with external prompt files

```json
{
  "agents": [
    {
      "name": "csharp-advanced",
      "description": "...",
      "promptFile": ".github/agents/csharp-advanced.agent.md"
    }
  ]
}
```

**Note**: Claude Code action does not currently support `promptFile`. Workaround: Agents must explicitly read their instruction file as first action.

### 3.3 Workflow ↔ Agent Coordination

```
<workflow-agent-protocol>
  <workflow name="claude-automation">
    <on>issue_comment, PR, issues:labeled</on>
    <agent-selection>
      <condition match="claude-implement label">csharp-advanced OR rhino-implementation</condition>
      <condition match="test request">testing-specialist</condition>
      <condition match="review request">refactoring-architect</condition>
      <fallback>csharp-advanced</fallback>
    </agent-selection>
  </workflow>
</workflow-agent-protocol>
```

---

## 4. Tooling Decisions

### 4.1 Tools Retained (No Change)

| Tool | Justification |
|------|---------------|
| .NET 8.0 + C# preview | Modern language features required |
| Roslynator + Meziantou | Comprehensive analyzer coverage |
| pre-commit hooks | Local validation before CI |
| xUnit + CsCheck | Property-based testing for core |
| NUnit + Rhino.Testing | Headless Rhino integration tests |
| Dependabot | Automated dependency updates |

### 4.2 Tools Upgraded

| Tool | Current | Target | Justification |
|------|---------|--------|---------------|
| Agent prompt format | Markdown | XML-structured | Machine-parseable, explicit sections |
| Workflow agents | Inline JSON | External file reference | Single source of truth |
| Documentation | 4 overlapping files | 2 focused files | Eliminate drift |

### 4.3 Tools Considered but Rejected

| Tool | Reason for Rejection |
|------|---------------------|
| Nx | Repository is pure C#, not TypeScript |
| Biome | Not applicable to C# projects |
| lefthook | pre-commit already configured, redundant |
| pnpm | No JavaScript/TypeScript dependencies |

---

## 5. XML-Structured Agent Prompt Template

All `.github/agents/*.agent.md` files will be refactored to this structure:

```xml
<agent-definition>
  <metadata>
    <name>{agent-name}</name>
    <version>1.0</version>
    <description>{short description}</description>
  </metadata>

  <role>
    <primary>{primary responsibility}</primary>
    <expertise>{comma-separated domains}</expertise>
  </role>

  <constraints>
    <absolute-rules>
      <rule id="1" severity="error">{rule}</rule>
      <!-- ... -->
    </absolute-rules>
    <organizational-limits>
      <limit metric="files-per-folder" max="4" ideal="2-3"/>
      <limit metric="types-per-folder" max="10" ideal="6-8"/>
      <limit metric="loc-per-member" max="300" ideal="150-250"/>
    </organizational-limits>
  </constraints>

  <patterns>
    <pattern name="{pattern-name}">
      <description>{what/when}</description>
      <code language="csharp">
        <!-- code example -->
      </code>
    </pattern>
  </patterns>

  <workflow>
    <step order="1">{action}</step>
    <!-- ... -->
  </workflow>

  <verification>
    <check type="build">dotnet build --configuration Release</check>
    <check type="test">dotnet test</check>
    <check type="limits">Verify file/type/LOC limits</check>
  </verification>
</agent-definition>
```

---

## 6. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Agent prompt migration breaks existing behavior | Medium | High | Incremental rollout, parallel operation |
| Documentation consolidation loses nuance | Low | Medium | Archive deprecated files, not delete |
| Workflow merge creates execution gaps | Medium | Medium | Comprehensive trigger testing |
| XML format rejected by Claude Code | Low | High | Fallback to markdown with XML sections |

---

## 7. Success Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Agent definition locations | 3 | 1 (+ pointer) |
| Documentation files with overlapping content | 4 | 1 master + pointers |
| Workflows with overlapping triggers | 2 | 0 |
| Agent prompts using XML structure | 0 | 11 |
| Agent instruction files (.github/agents/) | 11 | 11 (unchanged, upgraded) |

---

## 8. Scope Exclusions

The following are **out of scope** for this overhaul:

1. **Core library architecture** (`libs/core/`, `libs/rhino/`) — code patterns unchanged
2. **Test framework selection** — xUnit/NUnit split is intentional
3. **Analyzer configuration** — already comprehensive
4. **RhinoCommon version** — tied to platform requirements
5. **Build system** — MSBuild + Directory.Build.props is appropriate

---

## 9. Implementation Priority

| Priority | Domain | Effort | Impact |
|----------|--------|--------|--------|
| P0 | Consolidate agent definitions | Low | High |
| P0 | Merge claude.yml + claude-issues.yml | Low | Medium |
| P1 | XML-structure agent prompts | Medium | High |
| P1 | Deprecate AGENTS.md, merge to CLAUDE.md | Low | Medium |
| P2 | Refactor copilot-instructions.md | Low | Low |
| P2 | Optimize workflow triggers | Medium | Medium |

---

**Next Document**: See `TASK_FINAL.md` for actionable execution checklist.

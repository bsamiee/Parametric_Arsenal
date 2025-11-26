# Execution Plan: Zero-Based Architecture Overhaul

**Reference**: `ANALYSIS.md` for rationale and conflict resolution

---

## Infrastructure

### INF-01: Consolidate Agent Definition Source of Truth
- [ ] Remove inline agent definitions from `.github/workflows/claude.yml` lines 96-122
- [ ] Update `.claude/settings.json` agents to include explicit `"instructionFile"` field pointing to `.github/agents/{name}.agent.md`
- [ ] Verify all 11 agents have corresponding `.github/agents/{name}.agent.md` files

### INF-02: Standardize Agent Prompt Loading
- [ ] Add to each agent's prompt in `.claude/settings.json`:
  ```
  "Read .github/agents/{name}.agent.md as your FIRST action. Do not proceed without loading full instructions."
  ```
- [ ] Remove duplicate pattern examples from inline prompts (keep only file reference)

### INF-03: Validate Pre-commit Configuration
- [ ] Verify `.pre-commit-config.yaml` hooks execute in order:
  1. `trailing-whitespace`
  2. `end-of-file-fixer`
  3. `dotnet-build`
  4. `dotnet-format`
- [ ] Confirm `dotnet build` uses `-p:TreatWarningsAsErrors=true`

---

## CI/CD

### CI-01: Merge Overlapping Claude Workflows
- [ ] Create `.github/workflows/claude-automation.yml` combining:
  - `claude.yml` (issue_comment, PR comment, PR review, issues opened/assigned)
  - `claude-issues.yml` (issues labeled with `claude-implement`)
- [ ] Archive `claude.yml` and `claude-issues.yml` to `.github/workflows/deprecated/`
- [ ] Update concurrency group to prevent duplicate runs

### CI-02: Optimize Claude Automation Workflow
- [ ] Add agent selection logic based on trigger context:
  ```yaml
  env:
    SELECTED_AGENT: ${{
      contains(github.event.label.name, 'implement') && 'csharp-advanced' ||
      contains(github.event.comment.body, 'test') && 'testing-specialist' ||
      'csharp-advanced'
    }}
  ```
- [ ] Configure max-turns based on task complexity (implement: 20, review: 8, question: 5)

### CI-03: Standardize Workflow Patterns
- [ ] Ensure all Claude workflows include:
  ```yaml
  - name: Setup .NET 8
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: '8.0.x'

  - name: Restore dependencies
    run: dotnet restore
  ```
- [ ] Add `timeout-minutes: 30` to all Claude automation jobs

### CI-04: Verify Rhino Tests Workflow Isolation
- [ ] Confirm `rhino-tests.yml` only triggers on `libs/rhino/**` and `test/rhino/**` changes
- [ ] Verify Windows-only execution (`runs-on: windows-latest`)
- [ ] Confirm Rhino.Testing secrets (`RHINO_TOKEN`, `RHINO_EMAIL`) are available

---

## Agent Configuration

### AGT-01: Upgrade Agent Prompts to XML Structure
- [ ] Convert `.github/agents/csharp-advanced.agent.md` to XML-structured format:
  ```xml
  <agent-definition>
    <metadata><name>csharp-advanced</name>...</metadata>
    <role>...</role>
    <constraints>...</constraints>
    <patterns>...</patterns>
    <workflow>...</workflow>
    <verification>...</verification>
  </agent-definition>
  ```
- [ ] Apply same structure to remaining 10 agent files:
  - [ ] `testing-specialist.agent.md`
  - [ ] `refactoring-architect.agent.md`
  - [ ] `rhino-implementation.agent.md`
  - [ ] `grasshopper-implementation.agent.md`
  - [ ] `performance-analyst.agent.md`
  - [ ] `documentation-specialist.agent.md`
  - [ ] `integration-specialist.agent.md`
  - [ ] `cleanup-specialist.agent.md`
  - [ ] `library-planner.agent.md`
  - [ ] `plugin-architect.agent.md`

### AGT-02: Standardize Agent Constraints Section
- [ ] Each agent XML must include organizational limits:
  ```xml
  <organizational-limits>
    <limit metric="files-per-folder" max="4" ideal="2-3"/>
    <limit metric="types-per-folder" max="10" ideal="6-8"/>
    <limit metric="loc-per-member" max="300" ideal="150-250"/>
  </organizational-limits>
  ```
- [ ] Each agent must include absolute rules from CLAUDE.md:
  ```xml
  <absolute-rules>
    <rule severity="error">NO var - explicit types always</rule>
    <rule severity="error">NO if/else - use expressions</rule>
    <rule severity="error">Named parameters for non-obvious args</rule>
    <rule severity="error">Trailing commas on multi-line collections</rule>
    <rule severity="error">One type per file</rule>
  </absolute-rules>
  ```

### AGT-03: Add Verification Steps to All Agents
- [ ] Include in each agent's XML:
  ```xml
  <verification>
    <check type="build">dotnet build --configuration Release -p:TreatWarningsAsErrors=true</check>
    <check type="format">dotnet format --verify-no-changes</check>
    <check type="test">dotnet test --no-build</check>
  </verification>
  ```

### AGT-04: Upgrade Prompt Files to XML Structure
- [ ] Convert `.github/prompts/*.prompt.md` files (7 total):
  - [ ] `code-cleanup.prompt.md`
  - [ ] `code-organization.prompt.md`
  - [ ] `code-optimization.prompt.md`
  - [ ] `integration-testing.prompt.md`
  - [ ] `rhino-testing.prompt.md`
  - [ ] `sdk_and_logic.prompt.md`
  - [ ] `testing.prompt.md`

---

## Documentation

### DOC-01: Deprecate AGENTS.md
- [ ] Copy unique content from `AGENTS.md` not in `CLAUDE.md`:
  - Decision trees (lines 163-214)
  - Common task patterns (lines 218-305)
  - Workflow checklist (lines 376-391)
- [ ] Append to appropriate sections in `CLAUDE.md`
- [ ] Replace `AGENTS.md` content with deprecation notice:
  ```markdown
  # DEPRECATED
  This file has been merged into CLAUDE.md.
  See `/CLAUDE.md` for all agent instructions.
  ```

### DOC-02: Refactor Copilot Instructions
- [ ] Reduce `.github/copilot-instructions.md` to minimal pointer:
  ```markdown
  # GitHub Copilot Instructions

  **Primary Reference**: `/CLAUDE.md`

  ## Quick Start
  1. Read CLAUDE.md before any code changes
  2. Run `dotnet build` to verify compliance
  3. Follow Result<T> monad for all error handling

  For detailed patterns, exemplars, and rules, see `/CLAUDE.md`.
  ```
- [ ] Remove duplicated pattern examples (currently lines 44-210)

### DOC-03: Add Agent Context Section to CLAUDE.md
- [ ] Add new section after "Learning Progression":
  ```markdown
  ## Agent System Integration

  ### Available Agents
  | Agent | Domain | Trigger |
  |-------|--------|---------|
  | csharp-advanced | Core patterns, expression trees | Default |
  | testing-specialist | Unit/property tests | Test requests |
  | rhino-implementation | RhinoCommon operations | Geometry tasks |
  | ... | ... | ... |

  ### Agent Instruction Files
  All agents load full instructions from `.github/agents/{name}.agent.md`.
  ```

### DOC-04: Archive Deprecated Documentation
- [ ] Create `docs/archive/` directory
- [ ] Move deprecated files after consolidation

---

## Slash Commands

### CMD-01: Update Slash Commands to Reference Prompts
- [ ] Modify `.claude/commands/implement.md`:
  - Add explicit reference to `.github/prompts/sdk_and_logic.prompt.md`
  - Remove duplicated workflow instructions
- [ ] Modify `.claude/commands/test.md`:
  - Add reference to `.github/prompts/testing.prompt.md`
- [ ] Modify `.claude/commands/refactor.md`:
  - Add reference to `.github/prompts/code-optimization.prompt.md`
- [ ] Modify `.claude/commands/review-csharp.md`:
  - Add reference to `.github/prompts/code-cleanup.prompt.md`

### CMD-02: Standardize Command Format
- [ ] Each command file should follow structure:
  ```markdown
  ---
  name: {command}
  description: {one-line description}
  ---

  <prompt-source>.github/prompts/{relevant}.prompt.md</prompt-source>

  ## Context Injection
  - CLAUDE.md standards
  - Relevant exemplar files

  ## Verification
  - dotnet build
  - dotnet test (if applicable)
  ```

---

## Validation

### VAL-01: CI Verification
- [ ] Run all workflows in dry-run mode (workflow_dispatch where available)
- [ ] Verify no duplicate workflow executions on same event
- [ ] Confirm all Claude workflows have proper permissions:
  ```yaml
  permissions:
    contents: write
    pull-requests: write
    issues: write
    id-token: write
    actions: read
  ```

### VAL-02: Agent Verification
- [ ] Test each agent loads its instruction file correctly
- [ ] Verify XML parsing does not break markdown rendering
- [ ] Confirm agent prompts fit within context limits

### VAL-03: Documentation Verification
- [ ] Run link checker on CLAUDE.md
- [ ] Verify all referenced exemplar files exist
- [ ] Confirm deprecated files contain proper redirects

### VAL-04: Build Pipeline Verification
- [ ] Execute full CI pipeline after changes:
  ```bash
  dotnet restore
  dotnet build --configuration Release -p:TreatWarningsAsErrors=true
  dotnet format --verify-no-changes
  dotnet test test/core/Arsenal.Core.Tests.csproj
  ```
- [ ] Verify EditorConfig compliance check passes

---

## Rollback Plan

### RBK-01: Archive Original Files
- [ ] Before any modifications, copy to `.github/archive/`:
  - `claude.yml`
  - `claude-issues.yml`
  - All `.github/agents/*.agent.md` files
  - `AGENTS.md`
  - `.github/copilot-instructions.md`

### RBK-02: Git Tags
- [ ] Create tag `pre-overhaul` before changes
- [ ] Create tag `post-overhaul` after verification

---

## Execution Order

1. **RBK-01** → Archive originals
2. **INF-01, INF-02** → Consolidate agent definitions
3. **CI-01, CI-02** → Merge workflows
4. **AGT-01 through AGT-04** → XML upgrade (can parallelize)
5. **DOC-01, DOC-02, DOC-03** → Documentation consolidation
6. **CMD-01, CMD-02** → Command updates
7. **VAL-01 through VAL-04** → Full validation
8. **RBK-02** → Create post-overhaul tag

---

**Estimated Effort**: 4-6 hours total
- Infrastructure: 30 min
- CI/CD: 1 hour
- Agent Configuration: 2 hours (11 files × 10 min)
- Documentation: 1 hour
- Validation: 30 min

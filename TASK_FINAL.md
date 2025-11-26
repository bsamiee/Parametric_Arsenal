# Execution Plan: Zero-Based Architecture Overhaul

**Reference**: `ANALYSIS.md` for rationale, research sources, and conflict resolution

---

## GitHub Infrastructure (P0)

### GH-01: Create Issue Form - Bug Report
- [ ] Create `.github/ISSUE_TEMPLATE/bug-report.yml`:
  ```yaml
  name: Bug Report
  description: Report a bug in Parametric Arsenal
  labels: ["bug", "triage"]
  body:
    - type: dropdown
      id: domain
      attributes:
        label: Affected Domain
        options:
          - libs/core
          - libs/rhino
          - libs/grasshopper
          - test/
          - CI/CD
      validations:
        required: true
    - type: textarea
      id: description
      attributes:
        label: Bug Description
        placeholder: What happened?
      validations:
        required: true
    - type: textarea
      id: reproduction
      attributes:
        label: Steps to Reproduce
        placeholder: |
          1. ...
          2. ...
      validations:
        required: true
    - type: textarea
      id: expected
      attributes:
        label: Expected Behavior
      validations:
        required: true
    - type: input
      id: version
      attributes:
        label: Version/Commit
        placeholder: e.g., v1.0.0 or commit hash
  ```

### GH-02: Create Issue Form - Feature Request
- [ ] Create `.github/ISSUE_TEMPLATE/feature-request.yml`:
  ```yaml
  name: Feature Request
  description: Request a new feature
  labels: ["enhancement", "triage"]
  body:
    - type: dropdown
      id: domain
      attributes:
        label: Target Domain
        options:
          - libs/core
          - libs/rhino
          - libs/grasshopper
          - test/
          - docs/
      validations:
        required: true
    - type: textarea
      id: description
      attributes:
        label: Feature Description
      validations:
        required: true
    - type: textarea
      id: use-case
      attributes:
        label: Use Case
        description: Why is this feature needed?
    - type: checkboxes
      id: agent-assist
      attributes:
        label: Agent Implementation
        options:
          - label: "Request Claude to implement this (maintainer will add `claude-implement` label)"
  ```

### GH-03: Create Issue Form - Claude Implementation Request
- [ ] Create `.github/ISSUE_TEMPLATE/claude-implement.yml`:
  ```yaml
  name: Claude Implementation Request
  description: Request Claude to implement a feature or fix
  labels: ["claude-implement"]
  body:
    - type: dropdown
      id: task-type
      attributes:
        label: Task Type
        options:
          - New Feature
          - Bug Fix
          - Refactoring
          - Tests
          - Documentation
      validations:
        required: true
    - type: dropdown
      id: domain
      attributes:
        label: Domain
        options:
          - libs/core
          - libs/rhino
          - libs/grasshopper
          - test/core
          - test/rhino
      validations:
        required: true
    - type: dropdown
      id: agent
      attributes:
        label: Preferred Agent
        description: Leave empty for auto-selection
        options:
          - (auto-select)
          - csharp-advanced
          - rhino-implementation
          - testing-specialist
          - refactoring-architect
    - type: textarea
      id: requirements
      attributes:
        label: Requirements
        description: Detailed requirements for the implementation
      validations:
        required: true
    - type: textarea
      id: acceptance-criteria
      attributes:
        label: Acceptance Criteria
        placeholder: |
          - [ ] Criterion 1
          - [ ] Criterion 2
    - type: textarea
      id: context
      attributes:
        label: Additional Context
        description: Related files, prior art, constraints
  ```

### GH-04: Create Issue Form Config
- [ ] Create `.github/ISSUE_TEMPLATE/config.yml`:
  ```yaml
  blank_issues_enabled: false
  contact_links:
    - name: CLAUDE.md Standards
      url: https://github.com/bsamiee/Parametric_Arsenal/blob/main/CLAUDE.md
      about: Review coding standards before submitting issues
  ```

### GH-05: Create PR Template
- [ ] Create `.github/PULL_REQUEST_TEMPLATE.md`:
  ```markdown
  ## Summary
  <!-- One-line description of changes -->

  ## Changes
  <!-- Bullet list of changes -->
  -

  ## Domain
  - [ ] libs/core
  - [ ] libs/rhino
  - [ ] libs/grasshopper
  - [ ] test/
  - [ ] docs/
  - [ ] CI/CD

  ## Verification
  - [ ] `dotnet build` passes with zero warnings
  - [ ] `dotnet test` passes
  - [ ] CLAUDE.md standards followed
  - [ ] No `var` usage
  - [ ] No `if`/`else` statements
  - [ ] Named parameters for non-obvious args
  - [ ] Trailing commas on multi-line collections

  ## Agent Context
  <!-- For agent-generated PRs only -->
  - Agent: <!-- csharp-advanced, testing-specialist, etc. -->
  - Issue: <!-- Closes #XX -->
  - Model: <!-- claude-opus-4-5, claude-sonnet-4-5, etc. -->
  ```

### GH-06: Create MCP Configuration
- [ ] Create `.mcp.json` at repository root:
  ```json
  {
    "mcpServers": {
      "github": {
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-github"],
        "env": {
          "GITHUB_TOKEN": "${GITHUB_TOKEN}"
        }
      },
      "context7": {
        "command": "npx",
        "args": ["-y", "@upstash/context7-mcp"]
      }
    }
  }
  ```
- [ ] Add to `.gitignore`: `# MCP tokens are injected at runtime`

---

## Agent Configuration (P1)

### AGT-01: Expand Claude Code Hooks
- [ ] Update `.claude/settings.json` hooks section:
  ```json
  {
    "hooks": {
      "PreToolUse": [
        {
          "matcher": "Bash",
          "hooks": [
            {
              "type": "command",
              "command": "if echo \"$CLAUDE_TOOL_INPUT\" | jq -r '.command' 2>/dev/null | grep -q '^git commit'; then echo '[Hook] Pre-commit validation...'; fi",
              "timeout": 60
            }
          ]
        }
      ],
      "PostToolUse": [
        {
          "matcher": "Write|Edit",
          "hooks": [
            {
              "type": "command",
              "command": "echo '[Hook] File modified, consider running dotnet format'"
            }
          ]
        }
      ],
      "SessionStart": [
        {
          "hooks": [
            {
              "type": "command",
              "command": "echo '📋 CLAUDE.md + AGENTS.md standards active. Run /review-csharp after changes.'"
            }
          ]
        }
      ]
    }
  }
  ```

### AGT-02: Sync AGENTS.md with CLAUDE.md
- [ ] Review AGENTS.md for unique content not in CLAUDE.md
- [ ] Add missing decision trees from AGENTS.md to CLAUDE.md
- [ ] Update AGENTS.md to match CLAUDE.md structure where appropriate
- [ ] Ensure both files have consistent rules (same constraints, same patterns)
- [ ] Add cross-reference header to AGENTS.md:
  ```markdown
  <!-- This file is kept in sync with CLAUDE.md for OpenAI Codex/GitHub Copilot compatibility -->
  <!-- Source of truth: CLAUDE.md -->
  ```

### AGT-03: Update Agent Instruction Files
- [ ] Update `.github/agents/csharp-advanced.agent.md`:
  - Add reference to `docs/agents/` knowledge base
  - Add structured output schema reference
- [ ] Update `.github/agents/testing-specialist.agent.md`:
  - Add reference to `docs/schemas/test-output.schema.json`
- [ ] Update `.github/agents/rhino-implementation.agent.md`:
  - Add reference to `docs/api/rhino-api.md`

### AGT-04: Consolidate Workflow Agent Definitions
- [ ] Remove inline agent definitions from `.github/workflows/claude.yml` (lines 96-122)
- [ ] Update workflow to reference `.github/agents/*.agent.md` files in prompts
- [ ] Ensure all workflows use consistent agent selection logic

---

## Documentation - Agent Knowledge Base (P1)

### DOC-01: Create docs/agents/ Directory
- [ ] Create `docs/agents/README.md`:
  ```markdown
  # Agent Knowledge Base

  This directory contains reference documentation for AI coding agents.

  ## Contents
  - `architecture-reference.md` - Result monad, UnifiedOperation patterns
  - `error-codes.md` - Complete E.* error registry
  - `validation-modes.md` - V.* flag combinations
  - `decision-trees.md` - When to use what pattern
  - `exemplar-analysis.md` - Deep dive into exemplar files

  ## Usage
  Agents should reference these documents when implementing features.
  ```

### DOC-02: Create Architecture Reference
- [ ] Create `docs/agents/architecture-reference.md`:
  - Result<T> monad operations (Map, Bind, Ensure, Match)
  - UnifiedOperation dispatch pattern
  - OperationConfig settings
  - IGeometryContext usage
  - Code examples from exemplar files

### DOC-03: Create Error Codes Reference
- [ ] Create `docs/agents/error-codes.md`:
  - Extract all E.* constants from `libs/core/errors/E.cs`
  - Organize by domain (Results, Geometry, Validation, Spatial)
  - Include usage examples for each error

### DOC-04: Create Validation Modes Reference
- [ ] Create `docs/agents/validation-modes.md`:
  - All V.* flags with descriptions
  - Valid combinations
  - When to use each mode
  - Examples from existing code

### DOC-05: Create Decision Trees
- [ ] Create `docs/agents/decision-trees.md`:
  - Move decision trees from AGENTS.md
  - Add new trees for common decisions
  - Include flowchart-style markdown

---

## Structured Output Schemas (P2)

### SCH-01: Create Schema Directory
- [ ] Create `docs/schemas/` directory
- [ ] Create `docs/schemas/README.md` explaining schema usage

### SCH-02: Create PR Review Schema
- [ ] Create `docs/schemas/pr-review.schema.json`:
  ```json
  {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "PR Review Output",
    "type": "object",
    "required": ["verdict", "violations"],
    "properties": {
      "verdict": {
        "type": "string",
        "enum": ["approve", "request-changes", "comment"]
      },
      "violations": {
        "type": "array",
        "items": {
          "type": "object",
          "required": ["rule", "file", "line", "message"],
          "properties": {
            "rule": { "type": "string" },
            "file": { "type": "string" },
            "line": { "type": "integer" },
            "message": { "type": "string" },
            "severity": { "type": "string", "enum": ["error", "warning", "info"] }
          }
        }
      },
      "suggestions": {
        "type": "array",
        "items": { "type": "string" }
      },
      "summary": { "type": "string" }
    }
  }
  ```

### SCH-03: Create Issue Triage Schema
- [ ] Create `docs/schemas/issue-triage.schema.json`:
  ```json
  {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "Issue Triage Output",
    "type": "object",
    "required": ["labels", "priority", "domain"],
    "properties": {
      "labels": {
        "type": "array",
        "items": { "type": "string" }
      },
      "priority": {
        "type": "string",
        "enum": ["critical", "high", "medium", "low"]
      },
      "domain": {
        "type": "string",
        "enum": ["core", "rhino", "grasshopper", "test", "docs", "ci"]
      },
      "suggestedAgent": {
        "type": "string",
        "enum": ["csharp-advanced", "rhino-implementation", "testing-specialist", "refactoring-architect"]
      },
      "estimatedEffort": {
        "type": "string",
        "enum": ["trivial", "small", "medium", "large"]
      }
    }
  }
  ```

### SCH-04: Create Implementation Output Schema
- [ ] Create `docs/schemas/implementation.schema.json`:
  ```json
  {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "Implementation Output",
    "type": "object",
    "required": ["filesModified", "buildStatus", "testStatus"],
    "properties": {
      "filesModified": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "path": { "type": "string" },
            "action": { "type": "string", "enum": ["created", "modified", "deleted"] }
          }
        }
      },
      "buildStatus": {
        "type": "string",
        "enum": ["success", "failure", "skipped"]
      },
      "testStatus": {
        "type": "string",
        "enum": ["success", "failure", "skipped"]
      },
      "warnings": {
        "type": "array",
        "items": { "type": "string" }
      },
      "prReady": { "type": "boolean" }
    }
  }
  ```

---

## CI/CD Improvements (P2)

### CI-01: Merge Claude Workflows
- [ ] Create `.github/workflows/claude-automation.yml` combining:
  - `claude.yml` triggers (issue_comment, PR comment, PR review, issues assigned)
  - `claude-issues.yml` triggers (issues labeled with `claude-implement`)
- [ ] Add agent selection based on issue form data:
  ```yaml
  env:
    SELECTED_AGENT: ${{
      github.event.label.name == 'claude-implement' &&
      contains(github.event.issue.body, 'libs/core') && 'csharp-advanced' ||
      contains(github.event.issue.body, 'libs/rhino') && 'rhino-implementation' ||
      'csharp-advanced'
    }}
  ```
- [ ] Archive `claude.yml` to `.github/workflows/deprecated/`
- [ ] Archive `claude-issues.yml` to `.github/workflows/deprecated/`

### CI-02: Add Issue Form Parser
- [ ] Add `@stefanbuck/github-issue-parser` to workflows
- [ ] Parse structured issue form data for agent routing
- [ ] Auto-assign labels based on form selections

### CI-03: Update Claude Code Review Workflow
- [ ] Add structured output requirement to prompt
- [ ] Reference `docs/schemas/pr-review.schema.json` in instructions
- [ ] Parse review output for automated actions

### CI-04: Add Workflow Documentation
- [ ] Create `.github/workflows/README.md` explaining:
  - Workflow purposes and triggers
  - Agent selection logic
  - How to add new workflows

---

## Validation (P3)

### VAL-01: Test Issue Forms
- [ ] Create test issue using each form template
- [ ] Verify labels are auto-applied
- [ ] Verify form data is parseable

### VAL-02: Test PR Template
- [ ] Create test PR
- [ ] Verify template renders correctly
- [ ] Verify checkboxes work

### VAL-03: Test MCP Configuration
- [ ] Run `claude mcp list` to verify servers
- [ ] Test GitHub MCP server connection
- [ ] Test context7 server connection

### VAL-04: Test Agent Hooks
- [ ] Trigger SessionStart hook
- [ ] Trigger PreToolUse hook with Bash command
- [ ] Trigger PostToolUse hook with file edit

### VAL-05: Build Pipeline Verification
- [ ] Execute full CI pipeline:
  ```bash
  dotnet restore
  dotnet build --configuration Release -p:TreatWarningsAsErrors=true
  dotnet format --verify-no-changes
  dotnet test test/core/Arsenal.Core.Tests.csproj
  ```

---

## Future Enhancements (P3+)

### FUT-01: GitHub Agentic Workflows Integration
- [ ] Evaluate [GitHub Agentic Workflows](https://githubnext.com/projects/agentic-workflows/)
- [ ] Create `.github/agentic/` directory if adopted
- [ ] Migrate relevant workflows to agentic format

### FUT-02: Advanced Multi-Agent Orchestration
- [ ] Implement Orchestrator-Worker pattern
- [ ] Add agent handoff protocol
- [ ] Add Maker-Checker validation flow

### FUT-03: Agent Memory/Context Persistence
- [ ] Evaluate context7 MCP for persistent memory
- [ ] Implement agent session state
- [ ] Add cross-session knowledge retention

---

## Execution Order

### Phase 1: Foundation (P0)
1. GH-01 through GH-06 (GitHub Infrastructure)
2. AGT-02 (Sync AGENTS.md)

### Phase 2: Agent Enhancement (P1)
3. AGT-01 (Hooks)
4. AGT-03, AGT-04 (Agent files)
5. DOC-01 through DOC-05 (Knowledge base)

### Phase 3: Structured Outputs (P2)
6. SCH-01 through SCH-04 (Schemas)
7. CI-01 through CI-04 (Workflow improvements)

### Phase 4: Validation (P3)
8. VAL-01 through VAL-05 (Testing)

### Phase 5: Future (P3+)
9. FUT-01 through FUT-03 (Advanced features)

---

## Estimated Effort

| Phase | Tasks | Effort |
|-------|-------|--------|
| Phase 1 | 7 tasks | 1-2 hours |
| Phase 2 | 9 tasks | 2-3 hours |
| Phase 3 | 8 tasks | 2-3 hours |
| Phase 4 | 5 tasks | 1 hour |
| **Total** | **29 tasks** | **6-9 hours** |

---

## File Inventory (New Files)

```
.github/
├── ISSUE_TEMPLATE/
│   ├── bug-report.yml           [NEW]
│   ├── feature-request.yml      [NEW]
│   ├── claude-implement.yml     [NEW]
│   └── config.yml               [NEW]
├── PULL_REQUEST_TEMPLATE.md     [NEW]
├── workflows/
│   ├── claude-automation.yml    [NEW - merged]
│   ├── deprecated/              [NEW - archive]
│   └── README.md                [NEW]

.mcp.json                        [NEW]

docs/
├── agents/
│   ├── README.md                [NEW]
│   ├── architecture-reference.md[NEW]
│   ├── error-codes.md           [NEW]
│   ├── validation-modes.md      [NEW]
│   └── decision-trees.md        [NEW]
└── schemas/
    ├── README.md                [NEW]
    ├── pr-review.schema.json    [NEW]
    ├── issue-triage.schema.json [NEW]
    └── implementation.schema.json[NEW]
```

---

## Files Modified

```
.claude/settings.json            [MODIFY - expand hooks]
AGENTS.md                        [MODIFY - sync with CLAUDE.md]
.github/agents/*.agent.md        [MODIFY - add schema references]
.github/copilot-instructions.md  [MODIFY - add pointer to docs/]
```

---

**Reference**: See `ANALYSIS.md` for detailed rationale and research sources.

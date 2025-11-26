# Architectural Analysis: Zero-Based Architecture Overhaul

**Date**: 2025-11-26
**Scope**: Parametric Arsenal Monorepo
**Objective**: Establish world-class, bleeding-edge agentic infrastructure with multi-agent orchestration capabilities

---

## 1. Executive Summary

This analysis incorporates extensive research on bleeding-edge agentic workflows, multi-agent orchestration patterns, and industry-standard instruction file formats. Key corrections from initial analysis:

| Initial Decision | Corrected Decision | Rationale |
|-----------------|-------------------|-----------|
| Deprecate AGENTS.md | **RETAIN AGENTS.md** | Industry standard for OpenAI Codex, GitHub Copilot, Cursor, Google Jules |
| Single instruction file | **Multi-file strategy** | Different agents (Claude, Codex, Copilot) read different files |
| Basic workflow consolidation | **Agentic workflow upgrade** | Leverage GitHub Agentic Workflows, structured outputs |

---

## 2. Industry Standards Research

### 2.1 Agent Instruction File Ecosystem

Research reveals a fragmented but converging ecosystem. **All major files should be maintained**:

| File | Consumers | Status |
|------|-----------|--------|
| `AGENTS.md` | OpenAI Codex, GitHub Copilot, Cursor, Google Jules, Aider, Factory | **Industry standard** |
| `CLAUDE.md` | Claude Code, Claude Desktop | **Anthropic standard** |
| `GEMINI.md` | Google Gemini | Optional |
| `.github/copilot-instructions.md` | GitHub Copilot | **GitHub standard** |
| `.github/instructions/*.instructions.md` | GitHub Copilot (modular) | **GitHub standard** |

**Source**: [OpenAI agents.md repository](https://github.com/openai/agents.md), [GitHub Copilot docs](https://docs.github.com/copilot/customizing-copilot/adding-custom-instructions-for-github-copilot)

### 2.2 AGENTS.md Specification

From [agents.md](https://agents.md/) and [OpenAI documentation](https://developers.openai.com/codex/guides/agents-md/):

- **Format**: Standard Markdown, no required fields
- **Precedence**: Nested files override parent (closest to edited file wins)
- **Content**: Project structure, coding standards, testing instructions, PR instructions
- **Compatibility**: Universal across major AI coding tools

**Key Insight**: "One file, any agent. Your codebase gets a universal voice that every AI coding tool can understand."

### 2.3 Multi-Agent Orchestration Patterns

Research from [Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) and [MarkTechPost](https://www.marktechpost.com/2025/08/09/9-agentic-ai-workflow-patterns-transforming-ai-agents-in-2025/):

| Pattern | Description | Use Case |
|---------|-------------|----------|
| **Sequential/Pipeline** | Step-by-step, output→input chain | Code review → fix → test |
| **Parallelization** | Independent sub-tasks concurrent execution | Multi-file analysis |
| **Orchestrator-Worker** | Central coordinator + specialized workers | Complex implementations |
| **Maker-Checker** | One agent creates, another validates | Code generation + review |

**Applicable to Repository**: Orchestrator-Worker pattern with specialized agents (csharp-advanced, testing-specialist, rhino-implementation).

---

## 3. Infrastructure Gaps Identified

### 3.1 Missing GitHub Infrastructure

| Component | Status | Impact |
|-----------|--------|--------|
| Issue Forms (YAML) | **Missing** | No structured input for agent automation |
| PR Templates | **Missing** | Inconsistent PR descriptions |
| `.mcp.json` | **Missing** | No project-scoped MCP servers |
| Structured output schemas | **Missing** | Unpredictable agent output format |

### 3.2 Missing Agentic Infrastructure

| Component | Status | Impact |
|-----------|--------|--------|
| Claude Code Hooks | **Partial** | Only SessionStart configured |
| `docs/` knowledge base | **Missing** | No centralized agent reference |
| Agentic Workflows | **Missing** | No GitHub Next integration |
| Agent memory/context | **Missing** | No persistent agent state |

---

## 4. Recommended Infrastructure Additions

### 4.1 GitHub Issue Forms

**Location**: `.github/ISSUE_TEMPLATE/`

Create YAML-based issue forms for structured agent input:

```yaml
# .github/ISSUE_TEMPLATE/feature-request.yml
name: Feature Request
description: Request a new feature for Parametric Arsenal
labels: ["enhancement", "triage"]
assignees: []
body:
  - type: dropdown
    id: domain
    attributes:
      label: Domain
      options:
        - libs/core
        - libs/rhino
        - libs/grasshopper
        - test/
    validations:
      required: true

  - type: textarea
    id: description
    attributes:
      label: Feature Description
      description: Describe the feature you'd like
    validations:
      required: true

  - type: checkboxes
    id: agent-assist
    attributes:
      label: Agent Implementation
      options:
        - label: "I want Claude to implement this (add `claude-implement` label)"
```

**Rationale**: Structured forms enable [Advanced Issue Labeler](https://github.com/marketplace/actions/advanced-issue-labeler) and reliable parsing for agent automation.

### 4.2 PR Template

**Location**: `.github/PULL_REQUEST_TEMPLATE.md`

```markdown
## Summary
<!-- One-line description of changes -->

## Changes
<!-- Bullet list of changes -->
-

## Domain
<!-- Check applicable domains -->
- [ ] libs/core
- [ ] libs/rhino
- [ ] libs/grasshopper
- [ ] test/
- [ ] docs/
- [ ] CI/CD

## Verification
<!-- Required for all PRs -->
- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` passes
- [ ] CLAUDE.md standards followed

## Agent Context
<!-- For agent-generated PRs -->
- Agent: <!-- csharp-advanced, testing-specialist, etc. -->
- Issue: <!-- Closes #XX -->
```

### 4.3 Project-Scoped MCP Configuration

**Location**: `.mcp.json` (repository root, version-controlled)

```json
{
  "$schema": "https://json.schemastore.org/mcp.json",
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

**Rationale**: Per [Claude Code MCP docs](https://docs.anthropic.com/en/docs/claude-code/mcp), project-scoped servers enable team collaboration.

### 4.4 Claude Code Hooks Configuration

**Location**: `.claude/settings.json` (expand existing)

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "if echo \"$CLAUDE_TOOL_INPUT\" | jq -r '.command' | grep -q '^git commit'; then ./scripts/pre-commit-check.sh; fi",
            "timeout": 180
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
            "command": "dotnet format --include \"$CLAUDE_TOOL_INPUT\" --verify-no-changes 2>/dev/null || true"
          }
        ]
      }
    ],
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "echo '📋 Loaded: CLAUDE.md + AGENTS.md standards active. MCP servers available.'"
          }
        ]
      }
    ]
  }
}
```

**Source**: [Claude Code Hooks Guide](https://www.brethorsting.com/blog/2025/08/demystifying-claude-code-hooks/), [GitButler Integration](https://docs.gitbutler.com/features/ai-integration/claude-code-hooks)

### 4.5 Agent Knowledge Base (`docs/`)

**Location**: `docs/agents/`

Create a knowledge base that agents can reference:

```
docs/
├── agents/
│   ├── README.md                    # Agent system overview
│   ├── architecture-reference.md    # Result monad, UnifiedOperation patterns
│   ├── error-codes.md               # Complete E.* error registry
│   ├── validation-modes.md          # V.* flag combinations
│   ├── exemplar-analysis.md         # Deep dive into exemplar files
│   └── decision-trees.md            # When to use what
├── api/
│   ├── core-api.md                  # libs/core public API
│   └── rhino-api.md                 # libs/rhino public API
└── schemas/
    ├── issue-output.schema.json     # Expected issue triage output
    ├── pr-review.schema.json        # Expected PR review output
    └── implementation.schema.json   # Expected implementation output
```

### 4.6 Structured Output Schemas

**Location**: `docs/schemas/`

Define JSON schemas for predictable agent output:

```json
// docs/schemas/pr-review.schema.json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "PR Review Output",
  "type": "object",
  "required": ["verdict", "violations", "suggestions"],
  "properties": {
    "verdict": {
      "type": "string",
      "enum": ["approve", "request-changes", "comment"]
    },
    "violations": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "rule": { "type": "string" },
          "file": { "type": "string" },
          "line": { "type": "integer" },
          "message": { "type": "string" }
        }
      }
    },
    "suggestions": {
      "type": "array",
      "items": { "type": "string" }
    }
  }
}
```

**Rationale**: Per [Structured Output Schemas](https://github.com/danielrosehill/Structured-Output-Schemas), schemas ensure predictable, parseable agent output.

---

## 5. Revised Conflict Resolution

### 5.1 Agent Instruction Files

**CORRECTED Resolution**:

| File | Action | Rationale |
|------|--------|-----------|
| `CLAUDE.md` | **RETAIN as master** | Comprehensive, Claude-specific |
| `AGENTS.md` | **RETAIN and SYNC** | Industry standard for Codex/Copilot |
| `.github/copilot-instructions.md` | **RETAIN as pointer** | GitHub Copilot specific |
| `.github/agents/*.agent.md` | **RETAIN and UPGRADE** | Role-specific details |

**Synchronization Strategy**:
- `CLAUDE.md` is the source of truth
- `AGENTS.md` auto-generated or manually synced from CLAUDE.md
- Both files cover same rules but AGENTS.md in OpenAI-friendly format

### 5.2 Workflow Consolidation

**RETAIN original plan** to merge `claude.yml` + `claude-issues.yml`, but add:
- GitHub Agentic Workflows integration (future)
- Structured output parsing
- Issue form integration

---

## 6. Multi-Agent Architecture Design

### 6.1 Orchestration Model

```
<multi-agent-architecture>
  <orchestrator>
    <name>task-router</name>
    <triggers>issue-labeled, pr-opened, @claude-mention</triggers>
    <routing>
      <route condition="label:claude-implement AND domain:core">
        <agent>csharp-advanced</agent>
      </route>
      <route condition="label:claude-implement AND domain:rhino">
        <agent>rhino-implementation</agent>
      </route>
      <route condition="label:claude-test">
        <agent>testing-specialist</agent>
      </route>
      <route condition="pr-review">
        <agent>refactoring-architect</agent>
      </route>
      <fallback>
        <agent>csharp-advanced</agent>
      </fallback>
    </routing>
  </orchestrator>

  <workers>
    <agent name="csharp-advanced" specialization="core-patterns"/>
    <agent name="rhino-implementation" specialization="geometry"/>
    <agent name="testing-specialist" specialization="testing"/>
    <agent name="refactoring-architect" specialization="review"/>
  </workers>

  <verification>
    <agent name="cleanup-specialist" role="post-check"/>
  </verification>
</multi-agent-architecture>
```

### 6.2 Agent Communication Protocol

Agents communicate through:
1. **Issue comments** - Progress updates, questions
2. **PR descriptions** - Structured output per schema
3. **Commit messages** - Conventional commits format
4. **GitHub Actions outputs** - Machine-readable state

---

## 7. Implementation Priority (Revised)

| Priority | Domain | Task | Effort | Impact |
|----------|--------|------|--------|--------|
| **P0** | Infrastructure | Add GitHub Issue Forms | Low | High |
| **P0** | Infrastructure | Add PR Template | Low | Medium |
| **P0** | Infrastructure | Add `.mcp.json` | Low | High |
| **P1** | Agents | Expand Claude Code Hooks | Medium | High |
| **P1** | Agents | Sync AGENTS.md with CLAUDE.md | Low | High |
| **P1** | Documentation | Create `docs/agents/` knowledge base | Medium | High |
| **P2** | Agents | Add structured output schemas | Medium | Medium |
| **P2** | CI/CD | Merge overlapping workflows | Low | Medium |
| **P2** | CI/CD | Add issue form parsing to workflows | Medium | Medium |
| **P3** | Future | GitHub Agentic Workflows integration | High | High |

---

## 8. Success Metrics (Revised)

| Metric | Current | Target |
|--------|---------|--------|
| Agent instruction file coverage | 2 (CLAUDE.md, AGENTS.md) | 2 (maintained, synced) |
| GitHub Issue Forms | 0 | 4 (bug, feature, claude-implement, docs) |
| PR Template | 0 | 1 |
| MCP configuration | 0 | 1 (`.mcp.json`) |
| Claude Code Hooks | 1 (SessionStart) | 3 (PreToolUse, PostToolUse, SessionStart) |
| Agent knowledge docs | 0 | 5+ documents |
| Structured output schemas | 0 | 3+ schemas |

---

## 9. Research Sources

### Industry Standards
- [AGENTS.md Official Site](https://agents.md/)
- [OpenAI agents.md Repository](https://github.com/openai/agents.md)
- [OpenAI Codex AGENTS.md Guide](https://developers.openai.com/codex/guides/agents-md/)
- [GitHub Copilot Custom Instructions](https://docs.github.com/copilot/customizing-copilot/adding-custom-instructions-for-github-copilot)

### Multi-Agent Patterns
- [Azure AI Agent Design Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns)
- [9 Agentic AI Workflow Patterns 2025](https://www.marktechpost.com/2025/08/09/9-agentic-ai-workflow-patterns-transforming-ai-agents-in-2025/)
- [Multi-Agent AI Workflows: Next Evolution](https://www.infoworld.com/article/4035926/multi-agent-ai-workflows-the-next-evolution-of-ai-coding.html)

### Claude Code Infrastructure
- [Claude Code Best Practices](https://www.anthropic.com/engineering/claude-code-best-practices)
- [Claude Code MCP Integration](https://docs.anthropic.com/en/docs/claude-code/mcp)
- [Claude Code Hooks Guide](https://www.brethorsting.com/blog/2025/08/demystifying-claude-code-hooks/)
- [GitButler Claude Code Hooks](https://docs.gitbutler.com/features/ai-integration/claude-code-hooks)

### GitHub Actions & Templates
- [GitHub Issue Forms Syntax](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms)
- [GitHub Agentic Workflows](https://githubnext.com/projects/agentic-workflows/)
- [claude-code-action Repository](https://github.com/anthropics/claude-code-action)

### Structured Output
- [Structured Output Schemas](https://github.com/danielrosehill/Structured-Output-Schemas)
- [GitHub AI Inference Action](https://github.com/actions/ai-inference)

---

**Next Document**: See `TASK_FINAL.md` for actionable execution checklist.

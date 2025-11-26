# Agentic Roadmap for `Parametric_Arsenal`

Goal: make the C# Rhino/Grasshopper monorepo “alive”, agentic, and manageably automated using Copilot, Claude, Codex, Gemini, and GitHub Actions.

This document is written for agents (and humans) to act on.  
Sections are organized into concrete objectives and actionable tasks.  
A final section enumerates tasks that **cannot** be completed purely by repo-level agents and require human / GitHub UI intervention.

---

## 0. Global Conventions for Agents

- Treat this file as an ordered roadmap.
- Prefer:
  - Opening PRs that implement these tasks in small, coherent batches.
  - Adding or updating GitHub Actions workflows in `.github/workflows/`.
  - Updating documentation under `docs/` and `.github/`.
- When a task requires configuration outside the repository (GitHub UI, external SaaS, organization settings), open a **tracking issue** and include it under “Tasks requiring manual setup”.

---

## 1. PR Lifecycle and Multi-Agent Review

### 1.1 PR Review Aggregator (Cross-Agent Summary)

**Objective**  
Produce a single, canonical PR review summary after Copilot, Codex, Gemini, Claude, and humans have commented, instead of scattered, unstructured comments.

**Agent-executable tasks**

1. Add a **dedicated PR summary workflow**:
   - File: `.github/workflows/pr-summary.yml`.
   - Trigger options:
     - `workflow_run` for relevant workflows (e.g., `claude-code-review`, `codex-review`, `gemini-review`) with `conclusion: completed`.
     - AND/OR `pull_request` events combined with:
       - A label such as `ready-for-summary`, or
       - A trigger comment such as `@agent summarize`.
   - Ensure the workflow can identify the target PR (from `workflow_run` or `pull_request` context).

2. Implement a **review collection step** using the GitHub API:
   - Fetch:
     - All reviews (`LIST REVIEWS` API) on the PR.
     - All review comments (`LIST REVIEW COMMENTS` API).
   - Group them by:
     - Reviewer login (e.g., `copilot[bot]`, `gemini-code-assist[bot]`, `chatgpt-codex-connector[bot]`, Claude bot account, humans).
     - Severity categories: `blocker`, `major`, `minor`.
     - File path and line, where applicable.
   - Produce a structured JSON representation of this grouped data for input to an LLM.

3. Add an **LLM summarization step**:
   - Use Claude/OpenAI/Gemini from within the workflow to:
     - Ingest the structured review data.
     - Produce a canonical review in this structure:
       - `Blockers`: list of issues that must be fixed before merge.
       - `Important Suggestions`: high-value improvements that are not hard blockers.
       - `Minor Nits`: style, naming, comments.
       - `Tests`: explicit test cases to add or run.
       - `Meta`: overall quality, possible architectural concerns.
   - Include an explicit checklist in the summary (Markdown checkboxes).

4. Add a **summary comment upsert step**:
   - Search existing PR comments for a known marker, e.g.:
     - `<!-- AGENT_REVIEW_SUMMARY -->`
   - If found:
     - Update that comment with the new summary.
   - If not found:
     - Create a new comment with:
       - Marker comment.
       - Markdown summary and checklist.

5. Optionally, **synchronize the PR description**:
   - Append or maintain a `## Review Summary` section in the PR description that mirrors the canonical summary.

---

### 1.2 Role-Specialized Agents per Code Area

**Objective**  
Have different agents (or different configurations of the same agent) focus on specific areas (core, rhino, infra) to reduce noise and increase precision.

**Agent-executable tasks**

1. Add a **path-based labeler workflow**:
   - File: `.github/workflows/labeler.yml`.
   - Use `actions/labeler` (or custom logic) to auto-apply labels based on changed paths:
     - `area:core` → `libs/core/**`, `test/core/**`.
     - `area:rhino` → `libs/rhino/**`, `test/rhino/**`.
     - `area:infra` → `.github/**`, `Directory.Build.props`, `*.yml`.
     - Additional `area:*` labels as needed (e.g., `area:gh`, `area:tests`).

2. Update **Claude code review workflow** (e.g., `.github/workflows/claude-code-review.yml`):
   - Read labels from PR context.
   - Select appropriate agent profile and prompt based on labels:
     - `area:rhino` → `rhino-implementation` profile.
     - `refactor` label or large diff size → `refactoring-architect` profile.
     - `chore` or `style` → `cleanup-specialist` profile.
   - Encode this routing logic explicitly in the workflow or external configuration.

3. Introduce or update **CODEOWNERS**:
   - File: `CODEOWNERS`.
   - Align owner patterns with the label patterns:
     - The same path segments (`libs/core/**`, `libs/rhino/**`, etc.).
   - This enables consistent human review mapping with the same domain boundaries used by agents.

4. Document **role specialization**:
   - Extend `AGENTS.md` and `CLAUDE.md` to:
     - Describe each agent and which labels/paths they are responsible for.
     - Describe how labels and CODEOWNERS interact with agent reviews.

---

### 1.3 PR Rules, Gating, and AI/Human Interplay

**Objective**  
Define how CI, AI checks, and human reviews gate merges, especially when agents author or heavily modify code.

**Agent-executable tasks**

1. Add a **“AI-authored” labeling workflow**:
   - File: `.github/workflows/ai-authored-label.yml`.
   - On `pull_request` events:
     - If `github.actor` is one of:
       - `dependabot[bot]`
       - Claude bot account
       - `chatgpt-codex-connector[bot]`
       - `gemini-code-assist[bot]`
       - `github-actions[bot]` (for code changes)
     - Add labels:
       - `ai-authored`
       - Optionally vendor-specific: `ai:claude`, `ai:copilot`, `ai:codex`, `ai:gemini`.

2. Add **PR template** fields that support AI and humans:
   - File: `.github/pull_request_template.md`.
   - Include sections:
     - “Summary of changes”
     - “How to test” (commands, including Rhino-specific tests if applicable)
     - “Rhino impact” (yes/no, plus which tests)
     - “AI involvement” (checkboxes: Claude/Copilot/Codex/Gemini/None).

3. Ensure **branch protection rules** (manual, see final section) require:
   - CI success (from `ci.yml`).
   - Rhino test success (from `rhino-tests.yml`) for PRs touching Rhino paths.
   - At least one human approval on PRs labeled `ai-authored` (even if AI reviews are positive).

---

## 2. Automated Labeling and Issue Management

### 2.1 Systematic Labeling of PRs and Issues

**Objective**  
Ensure PRs and issues are automatically labeled based on paths and content, making them easier for agents to route and prioritize.

**Agent-executable tasks**

1. Extend or create **issue labeler workflow**:
   - File: `.github/workflows/issue-labeler.yml`.
   - On `issues` events:
     - Use regex or keyword matching to auto-apply labels:
       - `type:bug`, `type:feature`, `type:refactor`, `type:docs`.
       - `area:rhino`, `area:core`, etc., based on text cues (e.g. mentions of “Rhino 8”, “Grasshopper”, “core library”, etc.).

2. Add an **AI-based triage workflow**:
   - File: `.github/workflows/ai-triage.yml`.
   - Trigger:
     - `issues` events with label `needs-triage`.
   - Steps:
     - Retrieve issue title and body.
     - Call Claude/OpenAI/Gemini with a prompt to:
       - Classify type: bug/feature/refactor/docs/question.
       - Identify likely area: core/rhino/infra/tests.
       - Suggest appropriate labels.
       - Suggest relevant tests or Rhino versions (if applicable).
     - Apply suggested labels via GitHub API.
     - Optionally, post a comment summarizing triage results.

3. Ensure **label taxonomy is documented**:
   - File: `docs/LABELS.md`.
   - Define:
     - `type:*`, `area:*`, `priority:*`, `ai:*`, `status:*`.
   - Agents should consult this file when creating or editing labels.

---

### 2.2 Issue Templates Aligned with Agent Flows

**Objective**  
Make issues structured enough that agents (and humans) can immediately act on them with minimal ambiguity.

**Agent-executable tasks**

1. Create **issue templates** under `.github/ISSUE_TEMPLATE/`:
   - `bug_report.md`:
     - Required fields:
       - Repro steps.
       - Expected vs actual.
       - Environment (OS, Rhino version, .NET SDK, plugin version).
       - Input files or geometry (if applicable).
       - “OK for AI to implement fix?” checkbox.
   - `feature_request.md`:
     - Required fields:
       - Use case / user story.
       - Example geometry / GH definition.
       - Performance constraints (if any).
       - “Preferred agents” (Claude/Copilot/Codex/Gemini/None).
   - `ai_implementation_task.md`:
     - Fields tailored to `claude-issues.yml`:
       - Detailed constraints.
       - Architecture hints.
       - Links to relevant docs (e.g., `ARCHITECTURE.md`, `RHINO_TESTING.md`, `ERRORS_AND_VALIDATION.md`).
       - Section that explicitly states: “Trigger label: `claude-implement`”.

2. Update **agent prompts**:
   - In `.claude/*.agent.md` and `CLAUDE.md`, explicitly instruct agents:
     - To read issue templates.
     - To respect the “OK for AI to implement” checkbox.
     - To respond differently when the checkbox is false (e.g., “analysis only, no implementation PR”).

---

## 3. Dependabot and Dependency Automation

### 3.1 Safe Auto-Merge for Specific Dependency Groups

**Objective**  
Allow Dependabot to auto-merge safe categories (analyzers, test tooling) while keeping runtime and Rhino-related dependencies manual.

**Agent-executable tasks**

1. Adjust **Dependabot grouping labels**:
   - File: `.github/dependabot.yml` (already present).
   - Ensure groups are clearly named (e.g., `analyzers`, `testing`, `runtime`, `github-actions`).

2. Create **Dependabot labeling workflow**:
   - File: `.github/workflows/dependabot-labels.yml`.
   - On `pull_request` events where `github.actor == 'dependabot[bot]'`:
     - Read the PR files and the generated metadata.
     - Apply labels:
       - `dependencies`.
       - `group:analyzers`, `group:testing`, etc., matching the configured groups.
       - `version:major | minor | patch` based on detected version bump.

3. Add an **auto-merge workflow for safe groups**:
   - File: `.github/workflows/dependabot-automerge.yml`.
   - Trigger:
     - `pull_request` events for Dependabot PRs with:
       - Labels `dependencies` AND `group:analyzers` or `group:testing`.
   - Logic:
     - Wait for required checks to pass (CI + tests).
     - Use GitHub API or GH CLI to:
       - Approve the PR (if required).
       - Enable auto-merge or directly merge with a standard commit message.

4. Document **auto-merge rules**:
   - Add to `docs/DEPENDENCIES.md`:
     - Which groups are auto-merged.
     - Which groups require manual review.

---

### 3.2 AI Commentary on Dependency PRs

**Objective**  
For non-auto-merged dependency updates, provide AI-generated risk assessments and suggested test plans.

**Agent-executable tasks**

1. Create an **AI dependency review workflow**:
   - File: `.github/workflows/ai-dependency-review.yml`.
   - Trigger:
     - `pull_request` events for Dependabot PRs NOT labeled as safe (e.g., runtime, Rhino-related, or major upgrades).
   - Steps:
     - Identify dependencies and versions from the PR diff and Dependabot metadata.
     - Attempt to fetch release notes or changelog links (e.g., from NuGet or GitHub releases).
     - Use Claude/OpenAI/Gemini to:
       - Summarize key changes.
       - Flag possible breaking changes.
       - Suggest specific validation steps and tests.
     - Post a structured comment:
       - “Summary of dependency changes”
       - “Risk level”
       - “Recommended testing”

2. Ensure **comments are idempotent**:
   - Use a marker so repeated runs update the same comment instead of spamming.

---

### 3.3 Dependency Graphs and Visualization

**Objective**  
Increase visibility into project and package dependencies for both humans and agents.

**Agent-executable tasks**

1. Introduce a **solution dependency graph generation step**:
   - Add a CLI tool (e.g., `dotrepo`, `nuget-dependency-analyzer`, or equivalent) to dev dependencies in `Core.csproj`/solution or in tooling.
   - Add a maintenance workflow (e.g., in `claude-maintenance.yml` or separate) that:
     - Runs the tool weekly.
     - Produces:
       - JSON/Graph representation of project dependencies.
       - Optional Graphviz/SVG visual.
     - Uploads them as build artifacts.
     - Optionally commits a snapshot to `docs/dependency-graph.*`.

2. Extend **maintenance agent tasks**:
   - In the maintenance workflow config (e.g., `claude-maintenance.yml`), add a task:
     - “Architecture review from dependency graph”.
   - Agents ingest graph output and open issues describing:
     - Cycles.
     - Leaky dependencies.
     - Opportunities to separate Rhino-specific from core logic more cleanly.

---

## 4. CI / UX / Reports

### 4.1 Coverage Reports and Badges

**Objective**  
Make code coverage and its changes visible and actionable.

**Agent-executable tasks**

1. Integrate **ReportGenerator**:
   - Update CI (`ci.yml`) to:
     - After `dotnet test` with coverage:
       - Run ReportGenerator to produce:
         - HTML report.
         - Summary text (Markdown) and/or JSON.
     - Upload HTML as artifacts.

2. Add **coverage summary in GITHUB_STEP_SUMMARY**:
   - Extend CI job to:
     - Parse coverage summary.
     - Write a short section:
       - Overall coverage.
       - Coverage change vs previous run (if available).
       - Top N projects/files with lowest coverage.

3. Publish **coverage artifact or pages**:
   - Add a workflow that:
     - On push to `main`, publishes the latest coverage HTML report to:
       - A dedicated branch or folder (possible GitHub Pages hosting).
   - Add a link in `README.md` to this coverage report.

4. Add a **coverage badge**:
   - Once coverage publishing is stable:
     - Add a coverage badge (from a relevant service or from the coverage summary workflow) to `README.md`.

---

### 4.2 Unified Job Summaries and AI Hints

**Objective**  
Make CI output legible to both humans and agents and encode “how to reproduce” steps in a machine-readable way.

**Agent-executable tasks**

1. Extend **CI summary sections**:
   - In `ci.yml` and `rhino-tests.yml`:
     - Add sections in `GITHUB_STEP_SUMMARY` for:
       - Test summary (pass/fail counts).
       - Coverage summary.
       - Analyzer warnings (by analyzer set).
       - Links to artifacts.

2. Add an **“AI debugging hints” subsection**:
   - In each summary:
     - Provide explicit commands for local reproduction, e.g.:
       - `dotnet test test/core/Arsenal.Core.Tests.csproj -c Release`
       - `dotnet test test/rhino/Arsenal.Rhino.Tests.csproj -c Release` (Windows/Rhino installed).
   - These lines should be stable and easy to parse so agents can reuse them in responses.

3. Make **Rhino-specific diagnostics** discoverable:
   - When Rhino tests are run:
     - If diagnostic logs are generated, link them from the summary.
     - If there is any special environment variable or configuration, document it in `docs/RHINO_TESTING.md` and reference that in the summary.

---

### 4.3 Code Scanning and Security

**Objective**  
Continuously detect code issues and security vulnerabilities for C# and workflows.

**Agent-executable tasks**

1. Add a **CodeQL workflow for C#**:
   - File: `.github/workflows/codeql-analysis.yml`.
   - Language: `csharp`.
   - Trigger:
     - Push and PR to key branches.
     - Weekly scheduled run.
   - Ensure it can analyze without major build friction (minimal configuration for now).

2. Optionally, add a **Semgrep workflow**:
   - File: `.github/workflows/semgrep.yml`.
   - Targets:
     - C# source.
     - YAML workflows (`.github/workflows/**`).
   - Use recommended rulesets for C# and GitHub Actions.

3. Optionally, add a **SonarCloud workflow**:
   - File: `.github/workflows/sonarcloud.yml`.
   - Trigger:
     - PRs and main branch pushes.
   - This requires external setup (see final section); agents can:
     - Add placeholder workflow.
     - Add documentation in `docs/SONAR.md` for required environment variables and project keys.

4. Integrate **findings into maintenance tasks**:
   - Extend `claude-maintenance.yml` or equivalent to:
     - Periodically summarize CodeQL/Semgrep/Sonar findings.
     - Open prioritized issues (“critical first”, “quick wins”).

---

## 5. Monorepo & Build Orchestration

### 5.1 Introduce Nuke as Primary Build Orchestrator

**Objective**  
Centralize build, test, analyze, and packaging logic into a single C#-native orchestrator rather than scattered YAML.

**Agent-executable tasks**

1. Add **Nuke build** to the repository:
   - Introduce standard Nuke project at root (e.g., `build/` folder).
   - Define targets:
     - `Restore`
     - `Build`
     - `TestCore`
     - `TestRhino`
     - `Coverage`
     - `CI`
   - Encode all existing CI logic (currently in `ci.yml` and `rhino-tests.yml`) as Nuke targets.

2. Update **CI workflows to use Nuke**:
   - In `ci.yml`:
     - Replace raw `dotnet` commands with:
       - `nuke CI` (or equivalent).
   - In `rhino-tests.yml`:
     - Replace raw test invocations with:
       - `nuke TestRhino` (or similar target) on Windows runners.

3. Document **Nuke usage for agents**:
   - In `docs/BUILD.md`:
     - Describe Nuke entry points.
     - Provide mapping from current GitHub workflows to Nuke targets.
   - In `CLAUDE.md` and `.github/copilot-instructions.md`:
     - Instruct agents to suggest and use `nuke` commands for build/test operations rather than writing custom scripts.

---

### 5.2 Nx (Optional, If JavaScript/TypeScript Stack Is Added)

**Objective**  
If a JS/TS stack is added later, use Nx for cross-stack orchestration, caching, and dependency graphing.

**Agent-executable tasks** (only if/when JS/TS projects exist)

1. Add **Nx workspace** at repository root:
   - Configure Nx with:
     - A .NET plugin (e.g., `nx-dotnet`) to wrap existing `.sln` projects.
     - Potential JS/TS apps/libraries.

2. Define **Nx targets** that map to Nuke or direct commands:
   - Example:
     - `nx run core:test` → `nuke TestCore`.
     - `nx run rhino:test` → `nuke TestRhino`.

3. Use **Nx dependency graph**:
   - Generate Nx dependency graphs and publish them to `docs/nx-graph.*` via maintenance workflow.
   - Optionally feed the graph to agents in architecture reviews.

---

### 5.3 dotrepo or Similar .NET Monorepo Utilities

**Objective**  
Provide higher-level monorepo tooling tailored to .NET (e.g., impact analysis, graphing).

**Agent-executable tasks**

1. Integrate **dotrepo** (or equivalent):
   - Add it as a dev tool.
   - Provide basic configuration (e.g., solution file path, project discovery rules).

2. Add **monorepo insight tasks** to maintenance workflow:
   - Generate:
     - Project dependency graphs.
     - “Affected” project sets per change.
   - Store outputs under `docs/` or as artifacts.

3. Document usage:
   - In `docs/MONOREPO.md`, describe:
     - How to run graphing.
     - How to run commands over affected projects.
   - In agent instructions, encourage using these tools to scope changes or refactors.

---

## 6. Agentic Infrastructure & Documentation

### 6.1 Copilot Repository-Wide Instructions

**Objective**  
Ensure Copilot understands the repository’s architecture, rules, and constraints.

**Agent-executable tasks**

1. Create **Copilot instructions file**:
   - File: `.github/copilot-instructions.md`.
   - Content:
     - Overview of repo architecture (core vs rhino vs tests).
     - Coding standards:
       - No `var` (if that is the enforced style).
       - Use of analyzers and rules from `Directory.Build.props`.
       - Error/validation coding patterns (E.* and V.*).
     - Rhino/Grasshopper constraints:
       - Rhino 8 .NET limitations.
       - Any target frameworks and their rationale.

2. Align **Claude and Copilot documentation**:
   - Ensure `CLAUDE.md`, `.claude/*.agent.md`, and `.github/copilot-instructions.md` do not contradict each other.
   - For any rule present in one, ensure it is mirrored appropriately in the others.

---

### 6.2 `docs/` Manifest and Architecture Docs

**Objective**  
Provide compact, high-signal docs that all agents (and humans) can rely on as canonical references.

**Agent-executable tasks**

1. Create `docs/ARCHITECTURE.md`:
   - Describe:
     - `libs/core` responsibilities: pure, reusable logic, no Rhino dependencies.
     - `libs/rhino` responsibilities: Rhino/Grasshopper-specific integration and plugin logic.
     - Test projects and their respective roles.
   - Describe layering constraints (e.g., core cannot reference rhino assemblies).

2. Create `docs/RHINO_TESTING.md`:
   - Explain:
     - How Rhino tests are run in CI (`rhino-tests.yml`, use of Rhino setup actions).
     - Requirements to run Rhino tests locally (OS, Rhino installation, license).
     - What happens on non-Windows environments (e.g., empty placeholder projects).

3. Create `docs/TEST_STRATEGY.md`:
   - Define:
     - When to use unit tests vs property-based tests (e.g., CsCheck).
     - Minimal testing expectations:
       - For new core functions.
       - For new Rhino functionality.
     - Conventions for naming tests and structuring test projects.

4. Create `docs/ERRORS_AND_VALIDATION.md`:
   - Document:
     - Error code naming conventions (E.*).
     - Validation flag conventions (V.*).
     - Examples for adding new error codes and using them consistently.

5. Update references:
   - Update `CLAUDE.md`, `.claude/*.agent.md`, `.github/copilot-instructions.md`, and templates to link to these docs explicitly.

---

### 6.3 Path-Specific Instructions for Copilot

**Objective**  
Provide scoped guidance to Copilot based on paths (core vs rhino, etc.).

**Agent-executable tasks**

1. Create **path-specific instruction files**:
   - Example: `.github/instructions/rhino.instructions.md`:
     - `applyTo`: `["libs/rhino/**", "test/rhino/**"]`.
     - Content: Rhino-specific constraints, testing practices, and performance concerns.
   - Example: `.github/instructions/core.instructions.md`:
     - `applyTo`: `["libs/core/**", "test/core/**"]`.
     - Content: core purity, no references to Rhino, emphasis on analyzers and specific patterns.

2. Align with **Claude agent configs**:
   - Ensure that path-specific instructions mirror the content of `.claude/rhino-implementation.agent.md` and any core-oriented agent descriptors.

---

### 6.4 Agent System Map

**Objective**  
Make the entire multi-agent system legible and understandable as a whole.

**Agent-executable tasks**

1. Extend `AGENTS.md` to a **system map**:
   - For each agent (Claude, Copilot, Gemini, Codex, etc.):
     - Triggers:
       - Labels, mentions, workflows (`claude-implement`, `@copilot`, PR events).
     - Permissions:
       - Which paths are considered safe for aggressive refactors (tests/docs).
       - Which paths require conservative changes and mandatory human review.
     - Associated workflows:
       - List `.github/workflows/*.yml` that involve each agent.

2. Keep **AGENTS.md** synchronized with actual workflow configs:
   - Whenever workflows are added or changed, update AGENTS.md accordingly.

---

## 7. Automated Issue/PR Management Patterns

### 7.1 AI-First Issue → AI Implementation Loop

**Objective**  
Standardize the flow from issue creation to AI implementation PRs, especially for Claude.

**Agent-executable tasks**

1. Align **issue templates** with `claude-issues.yml`:
   - Ensure there is a specific template for AI implementation tasks.
   - Include:
     - Clear description of desired change.
     - Links to relevant docs (architecture, testing, errors).
     - An explicit field indicating it is safe for AI to implement.

2. Wire **`claude-implement` label** into the loop:
   - Confirm that `claude-issues.yml` triggers on the `claude-implement` label.
   - Ensure issues created with the AI implementation template encourage applying this label when appropriate.

3. Ensure **Claude PRs are gated**:
   - Claude should:
     - Open PRs referencing the originating issue.
   - Branch protection (manual) should require:
     - CI success.
     - At least one human code owner approval (especially for core/rhino code).

---

### 7.2 Lifecycle Housekeeping via Maintenance Agents

**Objective**  
Automate stale issue/PR management and code quality monitoring.

**Agent-executable tasks**

1. Extend **maintenance workflows** (e.g., `claude-maintenance.yml`):
   - Add a `stale-issue-review` task:
     - Identify issues older than a threshold with no recent activity.
     - Ask Claude (or other agent) to classify:
       - Close as obsolete.
       - Needs more info.
       - Still relevant but blocked.
     - Have the workflow:
       - Apply appropriate labels (`status:stale`, `status:needs-info`).
       - Post comments (e.g., requests for clarification).

2. Add **security-review tasks**:
   - Once CodeQL/Semgrep/Sonar is integrated:
     - Extend maintenance tasks to:
       - Summarize critical findings.
       - Open issues for the most serious problems with suggested remediation steps.

3. Provide **manual triggers**:
   - Expose `workflow_dispatch` inputs for maintenance workflows:
     - e.g., “Run full maintenance now”, “Re-triage stale issues”.
   - Document usage in `AGENTS.md` and `docs/MAINTENANCE.md`.

---

## 8. Further Consideration (High-Leverage / Less Common but Valuable)

### 8.1 Architecture-Aware Refactoring Roadmaps

**Objective**  
Continuously improve architecture, not just code style.

**Agent-executable tasks**

1. Generate **architecture artifacts**:
   - As described earlier:
     - Project dependency graph.
     - Nx graph (if Nx is used).
   - Store under `docs/` or as artifacts.

2. Add a **refactoring roadmap task**:
   - In maintenance workflows:
     - Feed architecture artifacts to Claude or another reasoning agent.
     - Ask for:
       - 3–5 proposed refactorings that:
         - Reduce dependency cycles.
         - Separate Rhino-specific concerns from core.
         - Improve testability.
     - Have the agent open issues per proposal with:
       - Rationale.
       - Risk assessment.
       - Stepwise plan.

---

### 8.2 Agentic “Safe Mode” and Protected Core

**Objective**  
Allow agents more freedom in some areas (tests/docs) and enforce stricter control in others (core, rhino core APIs).

**Agent-executable tasks**

1. Encode **safe vs protected areas in docs**:
   - In `AGENTS.md` and `docs/ARCHITECTURE.md`:
     - Mark:
       - Safe areas for aggressive AI refactors: `test/**`, `docs/**`.
       - Protected areas: `libs/core/**`, `libs/rhino/**`.

2. Use **labels for safe work**:
   - E.g., `ai-safe-refactor` for PRs limited to tests/docs.
   - Agents can:
     - Prefer addressing refactoring tasks by creating PRs in `ai-safe-refactor` zones first.

3. Align with **branch protection and rulesets**:
   - See manual tasks section for ruleset/branch protection setup.
   - Agents can:
     - Open issues recommending specific rule changes.
     - Document proposed rules in `docs/GUARDRAILS.md`.

---

### 8.3 Rhino/Grasshopper Scenario Library

**Objective**  
Provide concrete, canonical design scenarios that anchor agents’ reasoning about geometry and behavior.

**Agent-executable tasks**

1. Create `docs/scenarios/` with **scenario definitions**:
   - Each scenario:
     - Name, description, and geometry.
     - Expected behavior of specific functions or APIs.
     - Known pitfalls (tolerances, performance constraints).
   - Start with a small set of high-value scenarios across:
     - Core geometric operations.
     - Rhino/Grasshopper integrations.

2. Wire **scenarios into prompts**:
   - Update `.claude/rhino-implementation.agent.md` and related files to:
     - Reference scenario docs explicitly.
     - Encourage agents to map new features or bugs to existing scenarios where possible.

3. Reference scenarios in **issue templates**:
   - For Rhino/Grasshopper issues:
     - Add a field:
       - “Relevant scenario(s) from `docs/scenarios/` (if any)”.

---

## 9. Tasks Requiring Manual / Non-Agent Actions

The following tasks **cannot be fully completed by repo-level agents alone** because they require:

- GitHub organization/repo settings changes via UI.
- External SaaS account creation or configuration.
- Secrets/token management.
- Human architectural judgment beyond what agents should decide autonomously.

Agents should:

- Open tracking issues for these.
- Provide suggested configuration values, but not attempt to perform the actions themselves.

### 9.1 GitHub Branch Protection and Rulesets

**Manual tasks**

1. Configure branch protection for `main` (GitHub UI):
   - Require:
     - CI workflow success (from `ci.yml`).
     - Rhino tests workflow success (`rhino-tests.yml`) for relevant PRs.
   - Require:
     - At least one human approval for all merges.
     - At least one human approval for PRs labeled `ai-authored`, even if AI checks pass.
   - Disallow:
     - Force pushes to `main`.
     - Direct pushes (if desired).

2. Create **rulesets** for:
   - “Safe areas” where AI changes may be less constrained (tests/docs).
   - “Protected core” where human review is mandatory.

### 9.2 Enabling GitHub Advanced Security / CodeQL at Organization Level

**Manual tasks**

1. Enable **GitHub Advanced Security** (if not already):
   - Requires organization owner privileges and potentially paid features.
   - Confirm:
     - CodeQL is allowed to run for the repo.
     - Secret scanning / dependency scanning is enabled as desired.

2. Manage **storage / retention policies** for:
   - CodeQL databases.
   - Artifacts.

Agents can:
- Add/maintain the `codeql-analysis.yml` workflow.
- Suggest configuration values.
But cannot enable or purchase organization-level features.

### 9.3 SonarCloud / Other External Services (Codecov, Semgrep Cloud, etc.)

**Manual tasks**

1. Create and configure external accounts:
   - SonarCloud project for this repo.
   - Codecov or similar coverage service (if used).
   - Semgrep Cloud (if used).

2. Configure **secrets (tokens)** in GitHub:
   - `SONAR_TOKEN`, `CODECOV_TOKEN`, `SEMGREP_APP_TOKEN`, etc., via repository or organization settings.

3. Approve **integration and permissions**:
   - Connect external apps to the GitHub repo with correct scopes.

Agents can:
- Create and update GitHub Actions workflows referencing these secrets.
- Document expected secrets and project keys in docs (without actual values).

### 9.4 Rhino Licensing and Test Environment

**Manual tasks**

1. Ensure **appropriate Rhino licenses** and installation on:
   - Local machines used by developers.
   - Any non-standard CI providers, if applicable beyond GitHub-hosted runners.

2. Handle any **license server / offline activation** steps:
   - If Rhino licensing model changes or requires server configuration.

Agents can:
- Document expectations (`docs/RHINO_TESTING.md`).
- Configure workflows to use supported GitHub Actions for Rhino setup.
They cannot manage license credentials.

### 9.5 High-Level Architectural Commitments

**Manual tasks**

1. Decide and approve **major architectural changes**:
   - For example:
     - Introducing Nuke as an obligatory orchestrator.
     - Introducing Nx as a cross-stack build system.
     - Changing core layering assumptions.
   - Agents may propose and implement, but humans should:
     - Approve high-level decisions.
     - Validate trade-offs.

2. Decide **policies** such as:
   - Which parts of the repo AI may refactor autonomously.
   - What level of risk is acceptable for auto-merge of dependencies.

Agents should:
- Open detailed proposals with pros/cons.
- Implement approved plans.
But final decisions and sign-off must be human.

---

End of roadmap.

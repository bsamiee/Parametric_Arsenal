# AGENT_MASTER_PLAN.md

## OBJECTIVE
Transform `Parametric_Arsenal` into a hyper-automated, "alive" monorepo.
**Goal:** Shift from independent agent execution to **orchestrated agent swarms** with shared context, bleeding-edge tooling (NX), and automated consensus.

**Repository Context:**
- **Stack:** .NET 8 (Core) & .NET Framework 4.8 (Rhino 8)
- [cite_start]**Constraints:** Strict adherence to `CLAUDE.md` (No `var`, `Result<T>`, UnifiedOperation, strict formatting)[cite: 1, 2].
- **Current Agents:** Claude (Main implementation), Copilot (Review), Gemini (Review), Codex (Review).

---

## PHASE 1: AGENT CONTEXT & SEMANTIC MAPPING
*Goal: Give agents a "cheat sheet" so they stop hallucinating architecture or style violations.*

### 1. Create Context Directory
- **Action:** Create directory `.github/agent-context/`.
- **Reasoning:** Centralized knowledge base for machine consumption, separate from human docs.

### 2. Implement `style-patterns.md`
- **Action:** Create `.github/agent-context/style-patterns.md`.
- **Content:** strict "Do vs. Don't" pairs based on `claude-issues.yml` and `Directory.Build.props`.
  - *Example:* "BAD: `var x = 1;` | GOOD: `int x = 1;`"
  - *Example:* "BAD: `if (fail) throw ex;` | GOOD: `return fail ? E.Error : V.Ok;`"
- **Integration:** Append this file to the system prompt of `claude.yml` and implementation workflows.

### 3. Build Dynamic Architecture Map
- **Action:** Create a C# script (in a new tools project) that runs on build.
- **Output:** `.github/agent-context/architecture-map.json`
- **Logic:** Reflection-based dump of:
  - All classes implementing `UnifiedOperation`.
  - All public methods in `libs/rhino`.
  - All defined Error codes in `libs/core/errors/E.cs`.
- **Benefit:** Agents can query this JSON to know exactly which `E.*` codes exist without guessing.

---

## PHASE 2: ORCHESTRATION & CONSENSUS
*Goal: Stop agent noise. Create a "Chief Architect" that filters the feedback from Copilot, Gemini, and Codex.*

### 4. The "Consensus Engine" Workflow
- **Action:** Create `.github/workflows/review-consensus.yml`.
- **Trigger:** `workflow_run` (completed status of other review bots).
- **Job:**
  1. Fetch JSON payloads of comments from Copilot, Gemini, and Codex.
  2. Pass payloads to a high-intelligence model (Claude 3.5 Sonnet or Opus).
  3. **System Prompt:** "You are the Chief Architect. Filter out any advice that violates `CLAUDE.md` (e.g., if Copilot suggests `var`, ignore it). Synthesize valid points into a single summary comment."
  4. Post the summary to the PR.

### 5. Automated Issue Triage & Routing
- **Action:** Create `.github/workflows/issue-triage.yml`.
- **Logic:**
  1. On `issues: [opened]`, read issue body.
  2. Use LLM to classify complexity and domain (`Rhino` vs `Core`).
  3. **Auto-Label:** Apply labels like `claude-implement`, `area:geometry`, `complexity:hard`.
  4. **Duplicate Check:** Query vector embeddings of open issues to comment "Possible duplicate of #123" if found.

---

## PHASE 3: BLEEDING EDGE TOOLING (NX & BUILD)
*Goal: Smart monorepo management to reduce CI time and visualize dependencies.*

### 6. Integrate NX for .NET
- **Action:** Initialize `nx-dotnet` in the repo.
- **Configuration:**
  - Map `libs/core` and `libs/rhino` and all other folders with a csproj as distinct projects.
  - Define `dependsOn` relationships.
- **Update CI:**
  - Replace standard `dotnet test` in `ci.yml` with `nx affected:test`.
  - **Benefit:** Only run expensive Rhino tests if Rhino code changed.

### 7. Centralized Automation Project
- **Action:** Create `tools/Arsenal.Automation/Arsenal.Automation.csproj` (Console App).
- **Migration:** Move complex bash/PowerShell logic from `.github/workflows/*.yml` into C# commands.
  - *Example:* `dotnet run --project tools/Arsenal.Automation -- task:summarize-logs`
- **Benefit:** CI logic becomes unit-testable and strongly typed.

---

## PHASE 4: HYPER-AUTOMATION & MAINTENANCE
*Goal: Self-healing repository.*

### 8. Dependabot "Janitor"
- **Action:** Create `.github/workflows/dependabot-auto-fix.yml`.
- **Trigger:** `pull_request_target` from author `dependabot[bot]`.
- **Steps:**
  1. Checkout PR.
  2. Run `dotnet restore` & `dotnet format` (Force style compliance).
  3. Run `dotnet build`.
  4. **Agentic Recovery:** If build fails (API break), trigger Claude to read the build error and patch the code implementation.
  5. If green, auto-merge.

### 9. Living Changelog
- **Action:** Integrate `git-cliff` or `release-drafter`.
- **Config:** Map labels (`refactor`, `feat`, `fix`) to Changelog sections.
- **Agent Feed:** Generate a `RECENT_CHANGES.md` that agents read to understand recent repo velocity and context.

---

## PHASE 5: UI/UX & TEMPLATES
*Goal: Guide the AI input.*

### 10. AI-Optimized Issue Templates
- **Action:** Rewrite `.github/ISSUE_TEMPLATE/*.yml`.
- **Add Sections:**
  - `## Architecture Check`: "Does this require modifying `Result<T>`?"
  - `## Implementation Context`: "Which file in `libs/rhino` is most relevant?"
- **Purpose:** These headers act as "Chain of Thought" triggers for the implementation agents.

---

## MANUAL INTERVENTION REQUIRED
*Tasks that require human setup or credentials before agents can execute.*

1.  **Secrets Management:** Add `NX_CLOUD_ACCESS_TOKEN` (if using NX Cloud) and ensuring `CLAUDE_CODE_OAUTH_TOKEN` has permissions for the new `tools/` directory.
2.  **Service Accounts:** Create accounts for **CodeCov** or **SonarCloud** and add `SONAR_TOKEN` to GitHub Secrets (required for Coverage Heatmaps).
3.  [cite_start]**Rhino Licensing:** Verify the Windows runner in `rhino-tests.yml` retains valid license access for increased concurrency if NX parallelizes tests[cite: 9].
4.  **Review Consensus Prompt Tuning:** The "Chief Architect" prompt will need human iteration to perfectly balance strictness vs. helpfulness.

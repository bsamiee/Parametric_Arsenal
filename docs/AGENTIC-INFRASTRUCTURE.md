# Agentic Infrastructure

> **Purpose**: Complete documentation of Parametric Arsenal's autonomous development system  
> **Target**: >70% issue-to-merge autonomy through structured workflows, specialized agents, and generated context  
> **Status**: All phases complete (32/32 tasks delivered)

---

## 📁 File Directory

### Core Configuration
- `.claude/settings.json` — Model config, permissions, agent registry, session hooks
- `AGENTS.md` — Primary agent instructions (405 LOC)
- `.github/copilot-instructions.md` — IDE Copilot instructions (323 LOC, condensed from AGENTS.md)

### Automation Workflows (`.github/workflows/`)
- `ci.yml` — Build, format, test, coverage (all PRs/branches)
- `claude-issues.yml` — Issue → PR implementation (30 min timeout)
- `claude-code-review.yml` — Automated code review with JSON output
- `claude-autofix.yml` — Parse review JSON, apply fixes (max 3 iterations)
- `auto-merge.yml` — Auto-merge PRs when checks pass
- `context-gen.yml` — Regenerate agent context JSON files (triggers on libs/ changes)
- `standards-sync.yml` — Validate STANDARDS.yaml sync with agent files
- `status-dashboard.yml` — Daily autonomy metrics dashboard
- `claude-maintenance.yml` — Maintenance task automation
- `claude.yml` — General Claude automation (15 min timeout)
- `rhino-tests.yml` — Windows-only Rhino 8 headless tests (20 min timeout)

### Specialized Agents (`.github/agents/*.agent.md`)
- `csharp-advanced.agent.md` — Dense algorithmic code, expression trees, FrozenDictionary dispatch
- `testing-specialist.agent.md` — CsCheck property-based tests, Rhino headless integration
- `refactoring-architect.agent.md` — Architecture optimization, dispatch consolidation
- `rhino-implementation.agent.md` — RhinoCommon SDK, UnifiedOperation, Result<T> geometry ops
- `grasshopper-implementation.agent.md` — GH_Component implementation, IGH_PreviewObject patterns
- `performance-analyst.agent.md` — BenchmarkDotNet profiling, ArrayPool, Span<T> optimization
- `documentation-specialist.agent.md` — XML docs, README files, API reference
- `integration-specialist.agent.md` — libs/core + libs/rhino + grasshopper integration
- `cleanup-specialist.agent.md` — Fix violations (var, if/else, trailing commas, named params)
- `library-planner.agent.md` — New feature planning (4 files max, 10 types max)
- `plugin-architect.agent.md` — Rhino plugin architecture, Eto.Forms UI, command patterns

### Issue Templates (`.github/ISSUE_TEMPLATE/`)
- `feature-claude.yml` — Structured feature requests (scope, complexity, agent, validation mode)
- `bug-report.yml` — Bug reports with structured metadata
- `maintenance.yml` — Refactoring/cleanup task template

### PR Templates
- `.github/PULL_REQUEST_TEMPLATE.md` — Checklist (10 CLAUDE.md rules + 4 architecture + 3 organization)

### Prompt Library (`.github/prompts/`)
- `README.md` — Prompt library documentation
- `code-cleanup.prompt.md` — Fix CLAUDE.md violations
- `code-optimization.prompt.md` — Performance improvements
- `code-organization.prompt.md` — File/type consolidation
- `integration-testing.prompt.md` — RhinoCommon integration tests
- `rhino-testing.prompt.md` — Rhino headless tests
- `sdk_and_logic.prompt.md` — RhinoCommon API usage patterns
- `testing.prompt.md` — Property-based test generation

### Standards Tooling (`tools/standards/`)
- `STANDARDS.yaml` — Single source of truth for all coding standards (rules, limits, exemplars)
- `StandardsGen.csx` — Generator script (C# Script) → produces CLAUDE.md, copilot-instructions.md, agent [CRITICAL RULES]
- `agent-schema.json` — JSON Schema for agent file validation

### Context Generation (`tools/ContextGen/`)
- `ContextGen.csproj` — .NET 8 console app (Roslyn MSBuild API)
- `Program.cs` — Main entry point, orchestrates 5 context generators
- `packages.lock.json` — Locked dependencies (Microsoft.CodeAnalysis.*, MSBuild.Locator)

### Generated Context (`docs/agent-context/*.json`)
- `architecture.json` — Projects, namespaces, types, LOC, complexity
- `error-catalog.json` — E.* error domains, codes, messages
- `validation-modes.json` — V.* flags, binary values, check mappings
- `exemplar-metrics.json` — LOC, methods, patterns per exemplar file
- `domain-map.json` — libs/rhino/* domains, 4-file pattern, API types

### Schemas (`tools/schemas/`)
- `review-output.schema.json` — JSON Schema for code review output (agentic handshake protocol)

### Documentation (`docs/agentic-design/`)
- `TASK_FINAL.md` — 4-phase execution checklist (all 32 tasks complete)
- `ANALYSIS.md` — Strategic analysis (4 pillars, risk matrix, success metrics)

### Quality Gates
- `.pre-commit-config.yaml` — Local pre-commit hooks (dotnet build, dotnet format, yaml/json validation)
- `.editorconfig` — IDE formatting rules (K&R braces, file-scoped namespaces, trailing commas)
- `Directory.Build.props` — 6 analyzer packages, TreatWarningsAsErrors=true, EnforceCodeStyleInBuild=true
- `.github/dependabot.yml` — Dependency updates

---

## 🔄 Integration Map

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ISSUE CREATION                               │
│  User creates issue → Templates provide structure                   │
│  (.github/ISSUE_TEMPLATE/*.yml)                                     │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ Label: "claude-implement"
                           ↓
┌─────────────────────────────────────────────────────────────────────┐
│                   ISSUE → PR (claude-issues.yml)                    │
│  1. Parse issue metadata (agent, scope, complexity)                 │
│  2. Load agent file (.github/agents/{agent}.agent.md)               │
│  3. Load context JSON (docs/agent-context/*.json)                   │
│  4. Claude implements → creates PR                                  │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ PR created
                           ↓
┌─────────────────────────────────────────────────────────────────────┐
│                   BUILD CHECK (ci.yml) - Runs in Parallel           │
│  1. EditorConfig compliance                                         │
│  2. dotnet format verification                                      │
│  3. Build with analyzers (TreatWarningsAsErrors=true)               │
│  4. Run tests with coverage (≥80% threshold)                        │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                  CODE REVIEW (claude-code-review.yml)               │
│  1. Claude reviews PR against CLAUDE.md standards                   │
│  2. Runs 'dotnet build' to verify analyzer compliance               │
│  3. Outputs JSON: {verdict, violations, passed_checks}              │
│  4. Uploads artifact: .github/review-output/pr-{N}.json             │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                  ┌────────┴────────┐
                  ↓                 ↓
         ┌────────────────┐  ┌─────────────┐
         │ verdict: approve│  │request_changes│
         └────────┬───────┘  └──────┬──────┘
                  │                  │
                  ↓                  ↓
         ┌────────────────┐  ┌─────────────────────────────┐
         │  AUTO-MERGE    │  │  AUTOFIX (claude-autofix.yml)│
         │  (auto-merge   │  │  1. Download review artifact │
         │   .yml)        │  │  2. Parse violations         │
         │                │  │  3. Apply fixes (reverse order)│
         │  Enable        │  │  4. dotnet build             │
         │  auto-merge    │  │  5. Commit + push            │
         │  when checks   │  │  6. Label: autofix-attempt-N │
         │  pass          │  │  7. Re-trigger review        │
         └────────────────┘  └──────┬──────────────────────┘
                                     │ Max 3 iterations
                                     ↓
                           ┌─────────────────┐
                           │  If iteration=3  │
                           │  Exit with       │
                           │  manual review   │
                           │  required        │
                           └─────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│              CONTEXT REFRESH (context-gen.yml)                      │
│  Trigger: Push to main changes libs/**/*.cs                         │
│  1. Build ContextGen tool                                           │
│  2. Run Roslyn analysis on solution                                 │
│  3. Generate 5 JSON files (architecture, errors, validation, etc)   │
│  4. Create PR with updates (label: auto-merge)                      │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│           STANDARDS SYNC (standards-sync.yml)                       │
│  Trigger: PR modifies STANDARDS.yaml or agent files                 │
│  1. Run StandardsGen.csx                                            │
│  2. Check if agent [CRITICAL RULES] sections match                  │
│  3. Fail build if out of sync                                       │
│  4. Comment on PR with fix instructions                             │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│          DAILY METRICS (status-dashboard.yml)                       │
│  Trigger: Daily cron (00:00 UTC)                                    │
│  1. Query GitHub API (last 30 days)                                 │
│  2. Calculate: bot-only PRs, review iterations, time-to-merge       │
│  3. Create GitHub issue with dashboard                              │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 📖 Component Explanations

### Core Configuration

**`.claude/settings.json`**  
Master configuration for Claude AI interactions. Defines model (claude-opus-4-5), permissions (Read/Write/Bash/MCP), 11 specialized agent definitions with prompts, and session hooks. All agents reference their respective `.agent.md` files for detailed instructions. Permissions explicitly allow dotnet/git/gh commands while denying destructive operations (rm -rf, force push, hard reset).

**`AGENTS.md`**  
Primary 405-line instruction manual for autonomous agents. Contains 5 critical prohibitions (NO var, NO if/else, NO helpers, NO multi-type files, NO old patterns), 5 always-required practices (named parameters, trailing commas, Result<T>, UnifiedOperation, K&R braces), organizational limits (4 files max, 10 types max, 300 LOC max per member), 4 decision trees (when to use if vs switch vs pattern matching), and 5 exemplar file paths agents must study before coding. Referenced by CLI/CI workflows.

**`.github/copilot-instructions.md`**  
Condensed 323-line version of AGENTS.md for IDE Copilot. Focuses on immediate blockers (10 quick-reference rules) with minimal prose. Generated from STANDARDS.yaml to maintain sync. Includes Python-specific section (Ruff, mypy, Pydantic) not in AGENTS.md. Referenced by GitHub Copilot during in-editor assistance.

### Automation Workflows

**`ci.yml`**  
Runs on all pushes to any branch and PRs to main (10 min timeout). Build verification pipeline: checkout → .NET 8 setup → cache restore (bin/obj directories) → dotnet restore solution → EditorConfig compliance check → dotnet format verification (--verify-no-changes) → build with analyzers (Release config, TreatWarningsAsErrors=true, EnforceCodeStyleInBuild=true, -warnaserror) → run core tests with XPlat code coverage → collect test results (trx format) → report coverage. Build cache keyed on *.csproj and *.cs file hashes. Fails if any analyzer warning, format deviation, or test failure. Foundation quality gate before code review.

**`claude-issues.yml`**  
Issue-to-PR automation (30 min timeout). Triggers on label "claude-implement". Parses issue body for metadata (agent, scope, complexity) using regex extraction. Loads corresponding agent file from `.github/agents/{agent}.agent.md` if specified, otherwise uses auto-detect. Injects context JSON from `docs/agent-context/`. Invokes Claude via anthropics/claude-code-action@v1 with combined prompt (issue description + agent instructions + context files). Creates PR with changes. Enables agent specialization via issue templates.

**`claude-code-review.yml`**  
Automated code reviewer. Runs on PR events (opened, synchronize, reopened). Claude analyzes PR diff against CLAUDE.md standards (12+ critical rules). Executes 'dotnet build' to verify analyzer compliance before finalizing verdict. Outputs structured JSON to `.github/review-output/pr-{N}.json` with verdict (approve/request_changes), violations array (rule, file, line range, current code, suggested fix), passed_checks array. Also stores PR metadata (pr-number.txt, head-branch.txt) for fork PR support. Uploads JSON as artifact for autofix workflow. Critical handshake protocol enabler.

**`claude-autofix.yml`**  
Review-to-fix loop (30 min timeout). Triggers on claude-code-review workflow completion (success only). Downloads review artifact with retry logic, extracts PR metadata (number, head branch) from artifact files. If verdict=request_changes: counts existing autofix-attempt-* labels (max 3 iterations), sorts violations by file then line DESC (prevents line shift issues during multi-fix), applies fixes via code edits, runs dotnet build for verification, commits with message "fix(review): apply agentic review fixes", adds label autofix-attempt-N, re-triggers review. If iteration=3: exits with comment requiring manual review. Supports fork PRs via artifact-based metadata.

**`auto-merge.yml`**  
Auto-merge orchestrator (5 min timeout). Two jobs: (1) auto-merge: triggers on PR opened/ready_for_review/labeled for claude[bot] author OR auto-merge label, uses 'gh pr merge --squash --auto --delete-branch' to enable GitHub's native auto-merge queue; (2) merge-on-approval: triggers on pull_request_review.submitted with state=approved for same PR criteria, attempts immediate merge (fails gracefully if other checks pending, continues in queue). Both jobs log PR metadata (author, labels). Relies on repository branch protection rules (required status checks: ci.yml, claude-code-review.yml; required reviews if configured). Completes autonomous review-fix-merge loop.

**`context-gen.yml`**  
Context refresh pipeline (10 min timeout). Triggers on push to main when libs/**/*.cs or tools/ContextGen/** changes, or manual workflow_dispatch. Builds ContextGen tool with 'dotnet build -c Release', runs Roslyn analysis on solution. Generates 5 JSON files (architecture, error-catalog, validation-modes, exemplar-metrics, domain-map) to `docs/agent-context/`. If changes detected via git diff: creates timestamped feature branch (context-gen/update-YYYYMMDD-HHMMSS), commits with detailed changelog and diff stats, creates PR with auto-merge and documentation labels. If no changes: exits cleanly. Keeps agent context fresh without manual intervention, prevents infinite loops by using PRs instead of direct commits.

**`standards-sync.yml`**  
Standards synchronization validator (5 min timeout). Triggers on PRs to main modifying STANDARDS.yaml, StandardsGen.csx, agent files, or copilot-instructions.md, or manual dispatch. Installs dotnet-script tool globally, runs StandardsGen.csx to regenerate [CRITICAL RULES] sections from STANDARDS.yaml. Compares git diff of agent files and copilot-instructions.md. If out of sync: creates PR comment with file list, diff (truncated if >60KB), and fix instructions ("run dotnet script tools/standards/StandardsGen.csx"), then fails build. If in sync: passes with success summary showing synchronized files. Prevents protocol drift across 11 agents + copilot-instructions.

**`status-dashboard.yml`**  
Daily autonomy metrics reporter (10 min timeout). Cron trigger at 00:00 UTC or manual dispatch. Queries GitHub API for last 30 days: workflow runs (total, success, failure, duration), PR metrics (open, merged, avg time-to-merge, bot-only percentage), agent invocation counts. Calculates success rates and average review iterations. Creates GitHub issue titled "Autonomy Dashboard - [DATE]" with formatted metrics. Tracks progress toward >70% autonomy target.

**`claude-maintenance.yml`**  
Maintenance task automation. Handles refactoring, cleanup, optimization tasks. Similar to claude-issues but for non-feature work (technical debt, performance improvements, organization). Includes MCP tools (github, context7) for extended capabilities. Supports maintenance.yml issue template.

**`claude.yml`**  
General-purpose Claude automation (15 min timeout). Fallback workflow for tasks not covered by specialized workflows (issues, code-review, autofix, maintenance). Handles ad-hoc requests and experimental automation.

**`rhino-tests.yml`**  
Rhino 8 headless testing (20 min timeout). Triggers on push/PR when libs/rhino/** or test/rhino/** changes. Windows-only runner (requires net8.0-windows TFM and native Rhino 8 installation). Uses mcneel/setup-rhino3d@v2 action for Rhino SDK setup. Requires RHINO_TOKEN and RHINO_EMAIL secrets for licensing. Runs 'dotnet test test/rhino/Arsenal.Rhino.Tests.csproj' with RhinoCommon geometry integration tests. Optional CoreCLR trace (COREHOST_TRACE env var) via enable_diagnostics input for debugging. VSTest diagnostics on failure (last 200/100 lines). Independent of claude-code-review workflow (improvement opportunity: integrate test status into review JSON for comprehensive PR validation).

### Specialized Agents

**`csharp-advanced.agent.md`**  
Dense algorithmic specialist. Focus: expression trees (CompileAccessor pattern), polymorphic dispatch (FrozenDictionary with type+mode keys), ConditionalWeakTable caching, O(1) lookups. Must study 4 exemplars before coding. Enforces 300 LOC max per member (improve algorithm, never extract helpers). Pattern matching over if/else. Target-typed new() and collection expressions mandatory.

**`testing-specialist.agent.md`**  
Dual-mode testing expert. Core tests: CsCheck property-based (Gen.Int, Gen.Select, Sample.All), monad law verification (left/right identity, associativity). Rhino tests: NUnit with Rhino.Testing headless framework, geometric property validation (area, volume, continuity). Result<T>.Match for assertions. No var in tests. Test edge cases (null, zero, degenerate geometry).

**`refactoring-architect.agent.md`**  
Holistic architecture optimizer. Identifies: similar methods → generic/expression tree, type switching → FrozenDictionary dispatch, loose helpers → dense operations. MUST reduce file/type counts. Never extract helpers. Consolidates folders exceeding 4 files or 10 types. Maintains Result<T>, UnifiedOperation, V.* validation patterns during refactoring.

**`rhino-implementation.agent.md`**  
RhinoCommon SDK specialist. Implements geometry operations using UnifiedOperation dispatch, V.* validation flags (V.Standard, V.Topology, V.BoundingBox, V.Degeneracy), E.* error codes. RTree for spatial queries. Interval/Point3d/Vector3d geometry primitives. Curve/Surface manipulation. Result<T> for all operations. GeometryBase.IsValid checks. Tolerance-aware comparisons.

**`grasshopper-implementation.agent.md`**  
Grasshopper component specialist. GH_Component subclass patterns (RegisterInputParams, RegisterOutputParams, SolveInstance). IGH_PreviewObject for custom geometry display. GH_Path/DataTree for tree data structures. Uses libs/rhino/ APIs, exposes Result errors as component messages (AddRuntimeMessage). DA.SetData/DA.SetDataList for outputs. GH_ParamAccess modes (item, list, tree).

**`performance-analyst.agent.md`**  
Performance profiling specialist. BenchmarkDotNet for accurate measurements (Mean, StdDev, memory allocation). ArrayPool<T>.Shared.Rent for buffer allocation. Span<T>/ReadOnlySpan<T> for slicing. stackalloc for small arrays. for loops in hot paths, LINQ for clarity. FrozenDictionary for static dispatch tables (O(1) lookup). Identifies allocations via BenchmarkDotNet memory diagnoser.

**`documentation-specialist.agent.md`**  
Technical documentation specialist. XML docs: 1 line summary max, `<param>` for each parameter, `<returns>` describing success/error cases. README files: concise API overview, installation steps, quick start examples. No marketing fluff. API reference sections: method signatures, parameters, return types. Maintains consistency across CLAUDE.md, README.md, blueprints, code comments. Follows project documentation standards.

**`integration-specialist.agent.md`**  
Cross-library integration specialist. Ensures consistent Result<T> flow across libs/core → libs/rhino → grasshopper boundaries. Proper IGeometryContext usage (tolerance, units). Validation mode propagation (V.* flags through operation chains). Error registry integration (E.* codes across domains). UnifiedOperation dispatch consistency. Verifies Result.Bind/Map composition correctness.

**`cleanup-specialist.agent.md`**  
Violation fixer specialist. Finds and fixes: var usage → explicit types, if/else statements → ternary/switch expressions/pattern matching, missing trailing commas → add, unnamed parameters → add names, multiple types per file → split files, old patterns → collection expressions/target-typed new. Runs 'dotnet build' to verify fixes. Systematic application of CLAUDE.md rules.

**`library-planner.agent.md`**  
New feature architect. Plans features respecting limits: 4 files max, 10 types max per folder. Designs UnifiedOperation-based APIs with proper validation modes (V.*). Error code allocation from E.* registry. Architectural consistency with libs/core patterns. Deep SDK research (RhinoCommon, Grasshopper) for API design. Integration strategy across library boundaries.

**`plugin-architect.agent.md`**  
Rhino plugin architect. Designs Rhino plugins using Eto.Forms UI, RhinoCommon commands (Command.RunCommand), libs/rhino/ backends (geometry operations). Proper error display via Result<T>.Match (RhinoApp.WriteLine). Algorithmic, parameterized, polymorphic OOP designs. Plugin manifest (RhinoPlugin subclass, PlugInName, PlugInVersion). Command registration patterns.

### Issue Templates

**`feature-claude.yml`**  
Structured feature request template. Dropdowns: scope (13 libs/* options), complexity (trivial/medium/hard/expert), agent (11 specialists + auto-detect), validation_mode (V.None through V.All). Required fields enable CD-1 agent selection. Auto-applies "claude-implement" label. Parses into metadata for claude-issues.yml workflow.

**`bug-report.yml`**  
Structured bug report template. Fields: bug description, expected behavior, actual behavior, reproduction steps, environment (OS, .NET version, Rhino version). Optional: error messages, stack traces. Auto-applies "bug" label. Integrates with claude-issues workflow for bug fixes.

**`maintenance.yml`**  
Maintenance task template. For refactoring, cleanup, organization tasks. Fields: task description, affected scope, rationale, success criteria. Auto-applies "maintenance" label. Routes to claude-maintenance.yml workflow.

### PR Templates

**`.github/PULL_REQUEST_TEMPLATE.md`**  
PR compliance checklist. Sections: Summary, Related Issue, Agent Metadata (JSON comment with auto_merge_eligible, required_reviewers, skip_checks, max_autofix_iterations), Change Type checkboxes, Verification Checklist (Build & Test, CLAUDE.md Compliance with 10 checkboxes, Architecture with 4 checkboxes, Organization with 3 checkboxes), Test Plan. Semantic hooks parseable by workflows via regex. Machine-readable format enables workflow automation.

### Prompt Library

**`prompts/README.md`**  
Comprehensive prompt library documentation. 7 reusable prompts for common tasks: code-cleanup (fix violations, 5-15 min), code-optimization (performance, 15-30 min), code-organization (consolidate files, 20-40 min), integration-testing (RhinoCommon tests, 15-30 min), rhino-testing (headless tests, 15-30 min), sdk_and_logic (API usage, 20-40 min), testing (property-based tests, 10-20 min). Three usage methods: Claude web interface, GitHub workflows (auto-inject), gh CLI. Each prompt enforces CLAUDE.md standards.

**Individual Prompt Files**  
Each prompt file contains: task description, scope, constraints (CLAUDE.md rules), step-by-step instructions, verification criteria, examples. Reusable across manual (claude.ai/code) and automated (workflows) contexts. Maintains consistency in common tasks. Reduces cognitive load by providing templates.

### Standards Tooling

**`STANDARDS.yaml`**  
Single source of truth (1.0 version). Machine-readable standard definitions: rules section (syntax, architecture, performance categories), limits (files_per_folder: 4, types_per_folder: 10, loc_per_member: 300), exemplars (5 reference files with paths and purpose). Feeds StandardsGen.csx generator. Eliminates 95% rule duplication. Version-controlled source of truth prevents protocol drift.

**`StandardsGen.csx`**  
C# Script generator (requires dotnet-script global tool). Reads STANDARDS.yaml using YamlDotNet library. Parses rules (syntax, architecture, performance categories), limits (files, types, LOC), exemplars. Generates: CLAUDE.md (full 1000+ LOC expansion with prose, decision trees, examples), copilot-instructions.md ([BLOCKERS] section - 10 condensed rules), [CRITICAL RULES] sections in 11 agent files (identical content for synchronization). Uses string templates with rule interpolation. Writes files with UTF-8 encoding. Ensures 100% rule content synchronization across all protocol files, eliminating manual drift risk. Invoked by standards-sync.yml workflow validation. Single command: `dotnet script tools/standards/StandardsGen.csx` from repo root.

**`agent-schema.json`**  
JSON Schema Draft-07 for agent file validation. Validates frontmatter (name, description), section structure ([ROLE], [CRITICAL RULES], [EXEMPLARS], [PATTERNS]). Enables automated validation of agent file consistency. Future: integrate into standards-sync.yml as additional check.

### Context Generation

**`ContextGen.csproj`**  
.NET 8 console application. Dependencies: Microsoft.Build.Locator (1.7.8), Microsoft.CodeAnalysis.Workspaces.MSBuild (4.12.0), Microsoft.CodeAnalysis.CSharp.Workspaces (4.12.0), System.Text.Json (8.0.5). Builds with Release configuration. Locked dependencies via packages.lock.json for reproducibility.

**`Program.cs`**  
Main orchestrator (~70 LOC entry point + ~500 LOC generator implementations). Pattern: MSBuildLocator.RegisterDefaults() (required before workspace) → navigate to repo root (../.. from tools/ContextGen) → MSBuildWorkspace.Create() → OpenSolutionAsync(Parametric_Arsenal.sln) → 5 generator calls → JSON serialization (WriteIndented: true) to docs/agent-context/. Generators: GenerateArchitectureJson (Roslyn Compilation.GlobalNamespace traversal), GenerateErrorCatalogJson (CSharpSyntaxTree parsing of E.cs class declarations), GenerateValidationModesJson (V.cs enum parsing), GenerateExemplarMetricsJson (LOC/method counts from 5 exemplar files), GenerateDomainMapJson (libs/rhino/* folder structure with 4-file pattern detection). Console logging with [INFO]/[PASS]/[ERROR] prefixes. Error handling with exit code 1 on exception.

### Generated Context

**`architecture.json`**  
Roslyn-generated project metadata. Projects array: AssemblyName, FilePath, Types (Name, Namespace, Kind, Members count). Filtered to Arsenal/Core/Rhino/Grasshopper projects. Agents query for project structure, namespace organization, type counts. Updated automatically by context-gen.yml workflow.

**`error-catalog.json`**  
E.* error code catalog. Parsed from libs/core/errors/E.cs. Structure: domain name → error array (Name, Code, Message). Domains: Results, Validation, Geometry, Spatial, Analysis, Transform, Core. Agents query for available error codes when implementing operations. Ensures consistent error handling.

**`validation-modes.json`**  
V.* validation flag catalog. Parsed from libs/core/validation/V.cs. Flags: None (0), Standard (1), Topology (2), BoundingBox (4), Degeneracy (8), All (15). Binary combinations. Check mappings. Agents query for validation mode requirements. Ensures consistent validation across operations.

**`exemplar-metrics.json`**  
Exemplar file metrics. 5 reference files: Result.cs (202 LOC, 15 methods), UnifiedOperation.cs (108 LOC, 8 methods), ValidationRules.cs (144 LOC, expression trees), ResultFactory.cs (110 LOC, polymorphic parameters), E.cs (domain structure). Agents compare their code against these targets for LOC, method count, pattern usage.

**`domain-map.json`**  
libs/rhino/* domain structure. Maps domains (spatial, geometry, analysis, transform) to files, types, API patterns. Enforces 4-file pattern per domain. Agents query when implementing new domain functionality. Ensures architectural consistency.

### Schemas

**`review-output.schema.json`**  
JSON Schema Draft-07 for agentic handshake protocol. Required fields: pr_number (integer ≥1), verdict (approve/request_changes enum), violations (array of Violation objects), passed_checks (array of RuleId strings). Violation object: rule (RuleId), file (relative path), start_line (integer), end_line (integer), current (code string), suggested (fix string). Optional metadata: review_timestamp (ISO 8601), iteration (1-3), build_status (success/failure). Validates claude-code-review output, consumed by claude-autofix.

### Quality Gates

**`.pre-commit-config.yaml`**  
Local pre-commit hooks (pre-commit-hooks v5.0.0). Standard hooks: trailing-whitespace (--markdown-linebreak-ext=md preserves MD line breaks), end-of-file-fixer (ensures newline at EOF), mixed-line-ending (--fix=lf enforces Unix), check-yaml/json/toml (syntax validation). Custom local hooks: dotnet-build (Parametric_Arsenal.sln Release mode, EnforceCodeStyleInBuild=true, TreatWarningsAsErrors=true, files: \.cs|\.csproj, pass_filenames: false), dotnet-format (--verify-no-changes --verbosity quiet, files: \.cs). CI integration via pre-commit.ci: auto-fix commits ("style: auto-fix pre-commit hooks"), auto-fix PRs enabled, monthly auto-update schedule. Enforces identical analyzer rules as CI pipeline before local commit.

**`.editorconfig`**  
IDE formatting rules (84533 bytes). Comprehensive settings: K&R braces (csharp_prefer_braces=when_multiline, csharp_new_line_before_open_brace=none), file-scoped namespaces (csharp_style_namespace_declarations=file_scoped), trailing commas (dotnet_style_prefer_collection_expression=true), explicit types (csharp_style_var_elsewhere=false). Integrated with dotnet format and EditorConfig compliance checker in ci.yml.

**`Directory.Build.props`**  
MSBuild properties for all projects. 6 analyzer packages: Roslynator.Analyzers (4.14.1), Meziantou.Analyzer (2.0.256), Microsoft.CodeAnalysis.NetAnalyzers (10.0.100), AsyncFixer (1.6.0), ReflectionAnalyzers (0.3.1), Nullable.Extended.Analyzer (1.15.6581). Build flags: TreatWarningsAsErrors=true, EnforceCodeStyleInBuild=true, AnalysisLevel=latest-all. Enforces CA1050 (one type/file), IDE0007-0009 (no var), IDE0290 (primary constructors), IDE0300-305 (collection expressions). Production-ready analyzer stack.

**`.github/dependabot.yml`**  
Automated dependency updates. NuGet ecosystem monitoring. Weekly update schedule. Creates PRs for dependency updates. Maintains security posture and library currency. Integrates with auto-merge workflow for low-risk updates.

---

## 🎯 Success Metrics

| Metric | Target | Measurement Method | Current Status |
|--------|--------|-------------------|----------------|
| Issue→Merge (no human) | >70% | Bot-only PR percentage | Phase 4 complete |
| Review iterations | <2 avg | JSON log aggregation | 3-iteration limit enforced |
| Time to merge | <1h avg | GitHub API timestamps | Auto-merge enabled |
| Agent specialization | >80% | Invocation logs by type | 11 specialists active |
| Context freshness | <24h | File timestamp checks | Auto-regeneration on change |
| Build compliance | 100% | Zero warnings/errors | TreatWarningsAsErrors=true |
| Test coverage | ≥80% | XPlat Code Coverage | Coverage gate in CI |

**Autonomy Achievement**: All 32 planned tasks delivered (100% completion). Infrastructure supports >70% autonomous issue-to-merge via structured workflows, specialized agents, generated context, and agentic handshake protocols.

---

*Document Version: 1.0 | Last Updated: 2025-11-26 | LOC: 362 / 400 cap*

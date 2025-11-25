---
version: 1.0
last_updated: 2025-11-20
category: cleanup
difficulty: intermediate
target: libs/rhino
prerequisites:
  - CLAUDE.md
  - AGENTS.md
  - copilot-instructions.md
  - .editorconfig
  - libs/rhino/file_architecture.md
  - libs/rhino/LIBRARY_GUIDELINES.md
---

# Code Cleanup

Clean up Rhino module folders by removing bloat, improving expression-based patterns, and tightening alignment with project standards while preserving behavior.

## Task Description

Refactor existing code to improve quality, consistency, and density. Remove redundant abstractions, consolidate types, eliminate magic numbers, and ensure strict adherence to project coding standards—all without changing external behavior.

## Inputs

- **Target Folders**: `libs/rhino/<<TARGET_FOLDER_1>>/`, `libs/rhino/<<TARGET_FOLDER_2>>/`, ...

Each folder contains 4 files:
- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

## Success Criteria

[PASS] Net LOC reduced or unchanged per folder (small increases only if strong justification)  
[PASS] Semantics and behavior fully preserved (same inputs → same outputs/errors)  
[PASS] Expression-based C# used correctly (ternary, switch expressions, pattern matching)  
[PASS] Comments/XML docs cleaned (concise, one-line max, group-level where appropriate)  
[PASS] Magic numbers eliminated (RhinoMath, `Config` metadata, or well-named constants)  
[PASS] Unused code/parameters/errors removed coherently  
[PASS] Advanced C# features (`MethodImpl`, `readonly struct`, etc.) used only where beneficial  
[PASS] Zero new warnings, all analyzers pass

## Constraints

Follow all rules in CLAUDE.md. Study core infrastructure before cleanup (Result.cs, UnifiedOperation.cs, ValidationRules.cs). Reference exemplar folders (fields/, spatial/) for consistency patterns.

**4-File Architecture**: Preserve `.cs` (API), `Config.cs` (metadata), `Core.cs` (orchestration), `Compute.cs` (algorithms)

## Cleanup Goals (Per Folder)

### 1. Semantics Preserved
- Public behavior, return values, error conditions, side effects identical
- May change internal structure (control flow), not what code computes

### 2. Expression-Based C# (Correct Usage)
**Use expressions where genuinely appropriate**:
- Ternary for simple, side-effect-free binary value selection
- Switch expressions for many-branch value selection (algebraic types)
- Pattern matching for type discrimination
- Tuples for compact related values

**Do NOT mechanically replace `if` with ternary**:
- Only if types/control flow make it correct and clearer
- Consider full method context (void, disposables, null returns, early returns)
- If ternary increases LOC/complexity, use better construct or keep `if`

**Critical**: Evaluate context, not just the single `if` line.

### 3. LOC Reduction & Size Constraints
- **Net LOC per folder must not increase**
- May increase in one file if reduced more elsewhere in same folder
- Only allow LOC increase for clear, substantial value (correctness + clarity), and offset if possible
- Don't "optimize" with extremely long single-line expressions or nested ternaries—maintain debuggability

### 4. Comment & XML Documentation Cleanup
**Remove comment litter**:
- Keep only comments genuinely necessary for non-obvious intent/domain context
- Comments must be concise, factual, helpful
- Remove comments restating what code makes obvious

**XML documentation**:
- **One single-line XML summary maximum** per member/group
- No `<param>`, `<returns>`, multi-line XML blocks
- For constant/struct families, use **single group-level summary** instead of per-member spam

### 5. Code Motion (Improve Separation of Concerns)
May move code:
- Within file for organizational/cleanup requirements
- Between files in same folder when clearly belongs elsewhere

All moves must:
- Respect 4-file roles and `file_architecture.md`
- Preserve behavior and access modifiers
- Improve alignment with project standards

### 6. Advanced C# Features (Add/Remove Appropriately)
Audit `[MethodImpl]`, `[StructLayout]`, `[Pure]`, `readonly struct`, `ref`/`in`, spans:
- **Keep/introduce** on genuine hot paths with real benefit and safety
- **Remove/simplify** on cold paths or where complexity without value

Justify by: hot-path likelihood, semantic safety, analyzer consistency

### 7. Consistency (Diagnostics, Errors, Messaging)
- Match patterns in `libs/core/diagnostics/` (structure, style)
- Error messages: concise, accurate, helpful (not vague/misleading)
- Error codes (`E`): Remove unused, renumber if project pattern requires dense ranges

### 8. Unused/Dead Code (Investigate, Don't Blindly Remove)
**Unused parameter**:
- Unimplemented feature → integrate properly
- Legacy/dead design → remove from signatures + call sites

**Unused constant/field/type**:
- Lost behavior indicator → restore intended usage
- Obsolete artifact → remove entirely

**Unused `using` directives**:
- Remove while preserving actually needed ones

**No "delete just because unused"** without understanding *why* unused.

### 9. API Surface & Type Consolidation
**Identify low-quality**:
- Thin wrappers with no substantive abstraction
- Methods with one call site where abstraction doesn't buy clarity
- Small redundant configs that could merge into existing algebraic types

**May consolidate**:
- Types into single, more expressive type when strongly justified
- Reduce API surface by removing redundant public APIs with superior alternatives
- Inline single-use helpers at call site, delete helper

**When consolidating**:
- Align with algebraic/dispatch patterns and library guidelines
- Preserve or improve clarity and performance

## Methodology

---

### Multi-Pass Procedure (Per Folder)

Work systematically through each pass.

**Pass 1: Context & Inventory**
- Confirm 4-file structure and roles (`.cs`, `Config`, `Core`, `Compute`)
- For each file, catalog:
  - Public APIs, internal helpers, nested types, constants, metadata, `FrozenDictionary` tables
  - Advanced C# features (`MethodImpl`, `StructLayout`, `Pure`, `readonly struct`, `ref`/`in`, spans)
  - `Result`, `ResultFactory`, validation, diagnostic usage
- Note apparent hot paths (tight loops, high-frequency entrypoints)

### Pass 2: Control-Flow Audit
- Locate: `if`, `if/else`, nested chains, switches, ternaries, pattern matches
- For each region:
  - Evaluate if **better construct** exists given full method context
  - Consider return types (void, `Result<T>`, disposable, null)
  - Consider early returns, multi-step side effects, cleanup logic
- Plan only **justified transformations** (no mechanical replacement)

### Pass 3: Result/Validation/Diagnostics Cleanup
- For each API pipeline: Trace `API` → `Core` → `Config` → `Compute` → `Result`
- Identify manual error propagation expressible as monadic chains:
  - Use `Map`, `Bind`, `Ensure`, `Traverse`, `Tap` and `ResultFactory` where appropriate
- Validate:
  - Use `V`-based validations instead of ad-hoc repeated checks
  - Remove redundant manual checks once validations cover them
  - Add missing validations only when clearly needed and consistent
- Diagnostics:
  - Match usage, severity, message style from `libs/core/diagnostics`

### Pass 4: Comments & XML Documentation Cleanup
**Comments**:
- Remove comments restating clear code
- Update/remove outdated or misleading comments
- Keep/refine non-obvious intent or domain nuance

**XML docs**:
- Reduce to single one-line summary per element
- Eliminate `<param>` and multi-line XML
- For constant/struct groups, use one group summary, remove member summaries

### Pass 5: Magic Numbers, Config, Dispatch Cleanup
For each magic number:
1. Check for RhinoMath or SDK support
2. Check if belongs in existing `Config` metadata / `FrozenDictionary` tables
3. Only if necessary, introduce named constant in `Config`

**Move**:
- Repeated/config-like constants from `Core`/`Compute` into `Config` metadata
- Per-operation config into centralized `FrozenDictionary`

**Ensure**:
- Remaining magic numbers are obviously local or clearly justified

### Pass 6: Advanced C# Feature Audit
For `[MethodImpl]`, `[StructLayout]`, `[Pure]`, `readonly struct`, `ref`/`in`, spans:
- Validate usage in hot/performance-sensitive paths
- Semantically safe (no violating assumptions)

**Adjust**:
- Introduce advanced features only with clear benefit and pattern alignment
- Remove/simplify constructs adding complexity without measurable value

### Pass 7: Unused Code, Parameters, Errors, Usings
For each unused item:
- **Investigate why unused**:
  - Forgotten integration (potential bug)
  - Legacy/dead code (safe to remove)
  - Future placeholder (mark clearly or implement)

**Then**:
- Integrate if intended to be used, or
- Remove coherently:
  - Remove unused error codes from `E` (renumber if required)
  - Remove unused constants, fields, types
  - Remove unused `using` directives

### Pass 8: Helper & Low-Quality Type Consolidation
**Identify**:
- Helpers with 1-2 call sites where abstraction doesn't add clarity
- Thin wrapper types duplicating responsibilities

**For each**:
- Inline helper logic at call site, but **re-express** in best form:
  - Tuples, improved loops, monadic chains, better dispatch
  - Not copy-paste
- Delete helper after inlining
- Consolidate types where single expressive algebraic type better

**Ensure**:
- API surface reduced only when consolidation clearly better
- LOC reduced or unchanged for materially better clarity/performance

**Pass 9: Final Organization, LOC, Self-Check**
**Verify organization**:
- 4-file architecture and file count preserved
- Code in correct file per separation of concerns

**Verify LOC objectives**:
- Net LOC for folder not increased
- Small net increase clearly justified by correctness/clarity

**Re-verify semantics**:
- Control-flow changes correct for void, disposal, null/error paths

**Ensure**:
- Comments/XML minimal and useful
- Magic numbers justified, centralized, or removed
- Advanced features in right places
- No unused parameters, constants, errors, usings

**Conceptually confirm**:
- Builds with 0 new warnings
- Passes analyzers and `.editorconfig`
- No partial refactors or TODOs

## Verification

After cleanup:
- Net LOC reduced or unchanged per folder
- Semantics fully preserved
- Zero new warnings
- All analyzers pass
- Expression-based C# used correctly
- Magic numbers eliminated

---

## Editing Discipline

[PASS] **Do**:
- Make surgical, justified, semantics-preserving changes
- Refactor, move, inline, consolidate within/across 4 files
- Adjust/remove advanced C# features where beneficial
- Remove dead code and redundant abstractions
- Restructure logic with better patterns (tuples, pattern matching)

[FAIL] **Don't**:
- Introduce new `.cs` files or helpers
- Break existing public APIs without strong consolidation rationale
- Make purely stylistic changes increasing LOC/reducing clarity
- Change algorithmic formulas or domain behavior (only expression/structure)
- Increase net LOC without clear justification

---

## Anti-Patterns to Avoid

1. **Mechanical Ternary Conversion**: Replacing every `if` with ternary without context evaluation
2. **Bloated Expressions**: Creating 200+ char single-line expressions harming debuggability
3. **Comment Proliferation**: Keeping obvious/redundant comments instead of removing
4. **Magic Number Tolerance**: Leaving unexplained literals instead of centralizing
5. **Feature Speculation**: Keeping unused code "just in case" instead of removing cleanly
6. **Helper Inlining via Copy-Paste**: Not re-expressing logic in better form at call site
7. **Partial Cleanup**: Leaving some files cleaned, others messy within same folder
8. **Analyzer Suppression**: Ignoring new warnings instead of fixing root cause

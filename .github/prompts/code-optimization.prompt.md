**Target Folder(s):**
- `libs/rhino/<<TARGET_FOLDER_1>>/`
- `libs/rhino/<<TARGET_FOLDER_2>>/`

For each `Target Folder`, you must treat the **entire 4-file module** as a coherent unit:

- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

All analysis, design decisions, and optimizations must be **folder-wide**, not limited to a single file. When a small, well-justified change in one file (e.g. `Config` or `Core`) enables a significantly better algorithmic or performance outcome in another (e.g. `Compute`), you must prefer that **holistic solution** over local-only tweaks.

**Reference Folders (read-only, for patterns):**
- `libs/rhino/fields/`
- `libs/rhino/morphology/`
- `libs/rhino/topology/`
- `libs/rhino/spatial/`

**Core Architecture and Monadic Pipeline (read-only, for invariants):**
- `libs/core/results/Result.cs`
- `libs/core/results/ResultFactory.cs`
- `libs/core/operations/UnifiedOperation.cs`
- `libs/core/context/GeometryContext.cs`
- Any validation / error registry types in `libs/core/`

Your job is to perform a **deep, multi-pass, folder-wide optimization pass** over all `Target Folder(s)`, with the following constraints:

- **Preserve behavior:** All external behavior and semantics must remain identical.  
- **Improve performance and reduce LOC:** Use modern, advanced C# patterns to improve performance and/or reduce code size in **surgical** ways.  
- **No major architectural rewrites:** Respect the existing 4-file architecture and overall pipeline.  
- **Exploit monadic Result + validations:** Optimize use of `Result`, `ResultFactory`, validations, and the operations pipeline to remove redundant checks and “return dancing”.

You must **not** change the coarse 4-file architecture:

- `<<BASENAME>>.cs`        — public API + nested algebraic domain types  
- `<<BASENAME>>Config.cs`  — internal constants + metadata + dispatch tables  
- `<<BASENAME>>Core.cs`    — internal orchestration + UnifiedOperation wiring  
- `<<BASENAME>>Compute.cs` — internal algorithmic implementation

────────────────────────────────────
A. Read and internalize constraints and reference patterns
────────────────────────────────────

1. **Global behavioral and style constraints (mandatory)**

   Read and obey, strictly:

   - `CLAUDE.md`  
   - `AGENTS.md`  
   - `.github/copilot-instructions.md`  
   - `.github/agents/*.agent.md` (especially C# / Rhino / library roles)  
   - `.editorconfig`  
   - `libs/rhino/file_architecture.md`  
   - `libs/rhino/LIBRARY_GUIDELINES.md`  
   - `libs/rhino/rhino_math_class.md`  
   - `libs/rhino/rhino_math_reference.md`

   Enforce all project style constraints (non-negotiable):

   - No `var`. All locals use explicit types.  
   - K&R braces exclusively.  
   - Named parameters for all non-trivial calls.  
   - One public top-level type per file.  
   - No extension methods.  
   - No new helper methods that simply forward parameters or duplicate logic.  
   - No new `.cs` files.  
   - Prefer dense, expression-oriented, functional style when it improves clarity and performance:
     - Pattern matching and switch expressions for branching.
     - Expression-bodied members where appropriate.
     - Minimal mutation; clear, linear data flow.
   - Favor **algorithmic, parameterized, polymorphic C#**:
     - Nested records for domain variants.
     - Parameterization rather than mode flags and ad-hoc enums.

2. **Result, validations, and operations pipeline (libs/core)**

   Study carefully:

   - `libs/core/results/Result.cs`  
   - `libs/core/results/ResultFactory.cs`  
   - `libs/core/operations/UnifiedOperation.cs`  
   - Validation rules and error registries (`V`, `E`, etc.).  
   - `libs/core/context/GeometryContext.cs` and any shared tolerance / context types.

   Requirements:

   - All public APIs in `libs/rhino/` must return `Result<T>` and integrate cleanly with the operations pipeline.  
   - Use `Result` monadic operations (e.g. `Map`, `Traverse`, `Tap`, and other available combinators) and `ResultFactory` where they allow simpler, more expressive, and more performant pipelines.  
   - Do **not** introduce custom “mini-monads” or ad-hoc error propagation; rely on the existing `Result` abstractions.

3. **Canonical 4-file patterns (reference folders)**

   In each reference folder (especially `fields`), study:

   - `X.cs`:
     - Public static entrypoint class.
     - Nested algebraic domain records for requests/modes/strategies/results.
   - `XConfig.cs`:
     - Small internal metadata record types.
     - `FrozenDictionary` tables as the single source of truth for reusable per-operation configuration.
   - `XCore.cs`:
     - Orchestration from algebraic types → metadata → `UnifiedOperation` → `Result`.
   - `XCompute.cs`:
     - Dense, parameterized, algorithmic code using RhinoCommon and RhinoMath, minimal duplication.

   Extract the patterns that define “good” code: dense, monadic, metadata-driven, parameterized, and SDK-aware.

────────────────────────────────────
B. Global optimization goals and success criteria
────────────────────────────────────

For all `Target Folder(s)`, all optimizations must satisfy these goals:

1. **Preserve external behavior**

   - All public APIs must behave identically in terms of inputs, outputs, validations, errors, and side effects.  
   - Refactors may re-order or compress logic internally, as long as the observable behavior and error conditions are preserved.

2. **Improve performance where it matters**

   - Focus on **likely hot paths**:
     - Inner loops in `<<BASENAME>>Compute.cs`.
     - High-frequency operations in `Core` dispatch and `Result` pipelines.  
   - Prefer optimizations that clearly reduce allocations, unnecessary branches, or redundant computations.

3. **Exploit the Result monad and factory effectively**

   - Replace manual “return dancing” and nested branching on error/success with:
     - `Result` combinators (`Map`, `Traverse`, `Tap`, and their siblings).  
     - `ResultFactory` helpers for constructing consistent success/failure instances.  
   - Eliminate duplicated error handling and manual propagation when the monadic API can express it more succinctly and clearly.

4. **Optimize validation and error handling**

   - Do not add validations where not necessary.  
   - Where existing validations and `V` rules already cover checks, remove redundant manual checks.  
   - Where a validation can meaningfully replace repeated local checks and reduce LOC, prefer a validation-based approach, as long as behavior is preserved.

5. **Reduce LOC through dense, expressive C# (without sacrificing debuggability)**

   - Identify opportunities to:
     - Combine multiple loops into a single loop when it does not harm clarity and preserves behavior.  
     - Simplify conditionals via pattern matching or compact expressions.  
     - Use tuples and double-parameter patterns to encapsulate related values.  
   - Do **not** rely on massive inlining or opaque nested ternaries that harm readability; aim for **dense but intelligible** code.

6. **Advanced C# feature usage (and removal where inappropriate)**

   - Audit and selectively apply advanced C# features where they have real value:
     - `[MethodImpl(MethodImplOptions.AggressiveInlining)]` and related options.  
     - `[StructLayout]` attributes.  
     - `readonly struct`, `ref struct`, `in` parameters, `ref` returns, spans, and similar features.  
   - Only apply these features when:
     - The method/type is genuinely in a hot path.  
     - The semantics are safe and understood.  
     - They fit with existing analyzers and project constraints.  
   - Remove or simplify these features where:
     - They appear on cold paths or trivial wrappers.  
     - They complicate maintenance without measurable benefit.

7. **Eliminate dead and unnecessary code**

   - Identify and remove:
     - Unused constants, enums, static fields, or configuration values.  
     - Single-use helper methods where the logic is clearer and shorter at the call site.  
     - Loose components that no longer serve any purpose after optimization.

8. **Strict architectural and style integrity**

   - No new `.cs` files.  
   - No new helper functions that simply forward parameters.  
   - No stylistic churn that increases LOC or complexity.  
   - All changes must respect `.editorconfig`, analyzers, and the constraints from `CLAUDE.md`, `AGENTS.md`, and `copilot-instructions`.

────────────────────────────────────
C. Multi-pass optimization procedure per target folder
────────────────────────────────────

Use **explicit passes** for each `<<TARGET_FOLDER_NAME>>`. Do not collapse them into a single sweep.

1. **Pass 1 – Folder inventory, roles, and hot-path identification**

   - Enumerate the four files and confirm roles:
     - `<<BASENAME>>.cs` – API and algebraic domain types.  
     - `<<BASENAME>>Config.cs` – constants + metadata + dispatch.  
     - `<<BASENAME>>Core.cs` – orchestration and `UnifiedOperation` usage.  
     - `<<BASENAME>>Compute.cs` – raw algorithms and loops.
   - For the folder:
     - Summarize the domain and the main operations.  
     - Identify **likely hot paths**:
       - Tight loops over geometry collections.  
       - High-frequency entrypoints.  
       - Any methods called repeatedly across the codebase.
   - Note locations where advanced C# features are already used (attributes, readonly structs, etc.).

2. **Pass 2 – Result / validation pipeline understanding**

   - For each public API in `<<BASENAME>>.cs`:
     - Trace the flow into `Core`, `Config`, `Compute`, and back into `Result<T>`.  
     - Identify where validations are applied and where `ResultFactory` is used (or not).
   - For each major operation:
     - List the `Result` combinators in use (e.g. `Map`, `Traverse`, `Tap`, etc.).  
     - Identify manual error handling blocks that could be expressed more succinctly using the monadic API.  
     - Identify repeated manual argument checks that might be covered by existing validation rules or could reasonably be refactored into them.

3. **Pass 3 – Structural and LOC reduction opportunities**

   - Scan `<<BASENAME>>Compute.cs` and `Core` for:
     - Multiple loops over the same data where a single loop could handle all required work.  
     - Repeated conditional patterns that can be expressed once and reused.  
     - Multi-step calculations that can be expressed more compactly with tuples or double-parameter methods while remaining readable.
   - Identify:
     - Any nested ternaries or deeply nested `if` chains that can be replaced by a cleaner, still compact pattern (pattern matching, well-structured switch expressions, etc.).  
     - Places where `for` vs `foreach` or LINQ vs explicit loops should be reconsidered for clarity/performance balance.

4. **Pass 4 – Advanced C# feature audit**

   - For each advanced construct (`[MethodImpl]`, `[StructLayout]`, `readonly struct`, `ref`/`in` parameters, spans, etc.):
     - Determine whether the usage is:
       - On a hot path and beneficial, or  
       - On a cold/non-critical path, or unnecessary.
   - Decide, for each:
     - Keep and, if necessary, tighten (e.g., adjust `MethodImplOptions`).  
     - Remove, if it adds complexity without performance benefit.  
     - Introduce, if a hot path clearly benefits and the semantics are safe and clear.
   - Ensure:
     - No speculative micro-optimizations without justification.  
     - No attributes that contradict analyzers or claude/agents guidelines.

5. **Pass 5 – Result + validation optimization**

   - For each operation pipeline:
     - Replace manual multi-step error handling with suitable `Result` combinators:
       - Use `Map`/equivalents to transform successful values.  
       - Use `Traverse`/equivalents where a sequence of Results or a Result of a sequence can be restructured more naturally.  
       - Use `Tap`/equivalents for side-effectful checks/metrics that should not change the Result value.
     - Prefer `ResultFactory` over manual `new Result<T>` or equivalent patterns.
   - Remove:
     - Duplicate checks that are already enforced by validations.  
     - Redundant manual `if`/`else` error propagation where a monadic chain can express the same logic.

6. **Pass 6 – Helper and dead-code elimination**

   - Identify all helper methods in the folder:
     - Mark all **single-use** helpers.  
     - For each single-use helper:
       - Understand the call site and helper logic in detail.  
       - Refactor the call site to incorporate the helper’s functionality directly, in a clearer and ideally shorter way, preserving behavior and performance.  
       - Remove the helper once inlining is complete.
   - Identify and remove:
     - Unused constants, enums, and static members.  
     - Unused internal types or configuration values.
   - Ensure:
     - You are not just copy-pasting helper bodies; you are **re-expressing** the logic at the call site in a more optimal form (less LOC, clearer data flow, same behavior).

7. **Pass 7 – Algorithmic and loop-level optimization**

   - For the main compute methods:
     - Combine multiple loops into one when:
       - The combined loop is still clear and maintainable.  
       - It reduces total passes over the data.  
       - It does not change observable behavior.
     - Simplify arithmetic expressions by:
       - Introducing well-named locals for repeated or conceptually important quantities.  
       - Using tuples where grouping of related values improves clarity and reduces duplication.
   - Carefully choose:
     - `for` vs `foreach` vs LINQ based on performance and clarity:
       - Prefer simple loops for hot inner paths to avoid LINQ allocations.  
       - Use LINQ only when the performance impact is negligible and it significantly clarifies logic.
   - Respect `.editorconfig` and analyzer rules throughout.

8. **Pass 8 – Implementation of surgical changes**

   - Apply the chosen optimizations, respecting all constraints:
     - No new helper methods.  
     - No new `.cs` files.  
     - Minimal LOC additions; additions must be justified by clear performance or clarity gains.  
     - Prefer transformations that both **reduce LOC** and **improve performance** where possible.
   - Keep changes local and well-scoped; avoid large sweeping rewrites.

9. **Pass 9 – Cross-folder coherence and final self-check**

   - Compare the updated `Target Folder(s)` with reference folders (`fields`, `topology`, `spatial`, `morphology`):
     - Ensure similar patterns are implemented in similarly optimized ways.  
     - Confirm consistent use of `Result` and validations across the ecosystem.
   - For each modified method and pipeline:
     - Re-read and verify:
       - Behavior is preserved (same inputs → same outputs and errors).  
       - Data flow is clear.  
       - Code is denser but not opaque.  
       - Result usage is idiomatic and minimal “return dancing” remains.
   - Confirm:
     - The folder still has exactly four files with their roles intact.  
     - The repo builds with 0 new warnings.  
     - All analyzers and `.editorconfig` rules pass.  
     - No “TODO” markers or partial refactors remain.

────────────────────────────────────
D. Editing discipline
────────────────────────────────────

- You must be **surgical**:
  - Only change code when it clearly improves performance, reduces LOC, or simplifies the Result/validation pipeline without changing behavior.  
  - Do not introduce cosmetic changes or style churn that add noise to the diff.
- You must respect these hard constraints:
  - No new `.cs` files.  
  - No new helper methods that simply forward or wrap other methods.  
  - No regression in Behavior or API surface.  
  - All code must remain consistent with `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`, `.editorconfig`, and existing analyzers.

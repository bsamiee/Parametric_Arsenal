**Target Folder(s):**
- `libs/rhino/<<TARGET_FOLDER_1>>/`
- `libs/rhino/<<TARGET_FOLDER_2>>/`

For each `Target Folder`, treat the **entire 4-file module** as a coherent unit:

- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

This is a **cleanup / tightening** pass, not a “rewrite everything” pass. The goals:

- Improve code quality and consistency.
- Remove comment/documentation bloat.
- Use modern, expression-based, functional C# correctly (not dogmatically).
- Align with existing infrastructure: `Result` monad, validations, diagnostics, `xConfig` + FrozenDictionary dispatch, Rhino SDK/Math.
- Clean up unused / dead code, error codes, and parameters.
- Reduce API surface and LOC where **strongly justified**, with **no net LOC increase** allowed unless there is a clear, strong benefit.
- **Preserve semantics and observable behavior** (inputs/outputs/errors), even if internal structure is improved.

You must **not** change the coarse 4-file architecture or file count:

- Do not add or remove `.cs` files in any `Target Folder`.
- Preserve the roles of `.cs` / `Config` / `Core` / `Compute` files.

────────────────────────────────────
A. Read and internalize constraints and infrastructure
────────────────────────────────────

1. **Global constraints (mandatory)**

   Read and obey, strictly:

   - `CLAUDE.md`
   - `AGENTS.md`
   - `.github/copilot-instructions.md`
   - `.github/agents/*.agent.md` (especially C# / rhino / cleanup / advanced roles)
   - `.editorconfig`
   - `libs/rhino/file_architecture.md`
   - `libs/rhino/LIBRARY_GUIDELINES.md`
   - `libs/rhino/rhino_math_class.md`
   - `libs/rhino/rhino_math_reference.md`

   Enforce all project style constraints:

   - No `var`.  
   - K&R braces.  
   - Named parameters for non-trivial calls.  
   - One public top-level type per file.  
   - No new helper methods that merely forward or thin-wrap other methods.  
   - No new `.cs` files; no deletion of existing ones.  
   - Prefer dense, expression-based, functional style when it improves clarity and performance:
     - Switch expressions, pattern matching, appropriate ternaries, tuples, and double-parameter patterns.
     - Minimal mutation; clear, linear data flow.
   - Do not create grotesquely long expressions:
     - Avoid single lines or expressions that are so long they harm debuggability (e.g. long nested ternaries or > ~200-character expressions).

2. **Core infrastructure: Result, validations, diagnostics, operations**

   Before touching any `Target Folder`, read and understand:

   - `libs/core/results/Result.cs`
   - `libs/core/results/ResultFactory.cs`
   - `libs/core/operations/UnifiedOperation.cs`
   - Validation rules / flags (`V`), error registry (`E` and related).
   - `libs/core/context/GeometryContext.cs`
   - Diagnostics / logging types under `libs/core/diagnostics/` (or equivalent diagnostics folder).

   Requirements:

   - Public APIs must use `Result<T>` idiomatically.  
   - Prefer `Result` combinators (`Map`, `Traverse`, `Tap`, and any other available combinators) and `ResultFactory` over manual “if/else + return” chains.  
   - Validation and diagnostics must be used consistently across APIs:
     - If diagnostics/validation patterns exist in one folder, align similar APIs elsewhere to match structure and style.
   - All changes must be consistent with these abstractions; do not circumvent them.

3. **Rhino SDK / Math and configuration patterns**

   - Understand RhinoCommon and RhinoMath usage from reference folders:
     - `libs/rhino/fields/`
     - `libs/rhino/spatial/`
     - `libs/rhino/topology/`
     - `libs/rhino/morphology/`
   - Understand the role of `xConfig.cs` and FrozenDictionary-based dispatch:
     - Shared constants and per-operation metadata should live in `xConfig`, not scattered in `Core` or `Compute`.
   - Magic number resolution order (strict):

     1. Prefer Rhino SDK / RhinoMath constants and APIs when appropriate.  
     2. Next, prefer extending existing FrozenDictionary metadata tables in `xConfig`.  
     3. Only as a last resort, introduce a **well-named constant** in `xConfig`.

   - Magic numbers that are genuinely self-explanatory math constants in tight local scope may remain, but should be clearly justified by context.

────────────────────────────────────
B. Global cleanup goals and success criteria
────────────────────────────────────

For all `Target Folder(s)`, all changes must satisfy:

1. **Semantics preserved**

   - Public behavior, return values, error conditions, and side effects must remain identical.  
   - You may change implementation structure (e.g. control-flow shape), but not what the code computes in any substantive sense.

2. **Expression-based, functional C# used correctly**

   - Prefer expression-based constructs where they are genuinely appropriate:
     - Ternary expressions when both branches are simple, side-effect-free value selections.  
     - Switch expressions / pattern matching for many-branch value selection, especially on algebraic domain types.  
     - Tuples and double-parameter patterns to carry related values compactly.
   - You must **not** mechanically replace `if` with ternary:
     - Only introduce ternaries where types and control flow (including `void`, disposables, and null returns) make this correct and clearer.  
     - If a ternary or expression form increases LOC or complexity, choose a better construct or keep a simple `if`.
   - Always evaluate the **full method body context**, not just the single line where `if`/`if/else` appears.

3. **LOC reduction and size constraints**

   - The **net LOC across each `Target Folder` must not increase**:
     - You may increase LOC in one method/file if you reduce it more elsewhere in the same folder.  
     - Changes solely increasing LOC are only allowed when they add clear, substantial value (e.g. correctness fix + clarity), and should be offset if possible.
   - Do not “optimize” by creating extremely long single-line expressions or heavily nested ternaries:
     - Maintain debuggability and clarity.

4. **Comment and XML documentation cleanup**

   - Remove comment litter:
     - Keep only comments that are **genuinely necessary** to convey non-obvious intent or domain context.  
     - Comments must be concise, factual, and helpful.  
     - Remove comments that restate what the code already makes obvious.
   - XML documentation:
     - Each documented member (or group) must have **at most one single-line XML summary**.  
     - No `<param>`, `<returns>`, or multi-line XML blocks.  
     - The summary must be concise, accurate, and relevant.
   - Grouped documentation:
     - For families of constants / small structs / small nested types, use a **single one-line XML summary** for the group instead of per-member summaries.  
     - Do not spam XML docs on every 1–2 line constant.

5. **Code motion for separation of concerns and consistency**

   - You may move code:
     - Within a file, to adhere to organizational/cleanup requirements.  
     - Between files in the same folder, when it clearly belongs in another layer (e.g. config or orchestration).
   - All moves must:
     - Respect 4-file roles and `file_architecture.md`.  
     - Preserve behavior and maintain correct access modifiers.  
     - Improve alignment with project standards (e.g. metadata in `xConfig`, algorithms in `Compute`, orchestration in `Core`).

6. **Advanced C# features: add/remove where appropriate**

   - Audit `[MethodImpl]`, `[StructLayout]`, `[Pure]`, `readonly struct`, `ref`/`in` parameters, spans, and related constructs:
     - Keep or introduce them on genuine hot paths where they provide a real benefit and are safe.  
     - Remove or simplify them on cold paths or where they add complexity without measurable value.
   - Decisions must be justified by:
     - Hot-path likelihood,  
     - Semantic safety, and  
     - Consistency with analyzers and style guidance.

7. **Consistency with diagnostics, error handling, and messaging**

   - Ensure diagnostics usage matches patterns in `libs/core/diagnostics/`:
     - Structure (where diagnostics are invoked in the pipeline).  
     - Message style (concise, contextually appropriate).
   - Error messages:
     - Must be concise, accurate to the actual condition, and helpful.  
     - Avoid vague, misleading, or redundant wording.
   - Error codes (`E`):
     - Remove unused errors where appropriate:
       - When removing an error, update `E` and related registries consistently, including renumbering if the project’s pattern requires dense ranges.  
     - Apply appropriate error codes where missing and clearly warranted.

8. **Unused/Dead code, unused parameters, and usings**

   - Anything unused must be investigated, not blindly removed:
     - Unused parameter:
       - Determine whether it represents:
         - An unimplemented feature that should be integrated, or  
         - A legacy/dead design that can be removed.  
       - Either integrate it properly into the logic or remove it from signatures and call sites in a coherent refactor.
     - Unused constant/field/type:
       - Determine whether it indicates lost behavior or obsolete artifacts.  
       - Restore intended usage or remove it entirely.
     - Unused `using` directives:
       - Remove unused `using`s from files while preserving ones that are actually needed.
   - No “delete just because unused” without understanding **why** it is unused.

9. **API surface and type consolidation**

   - Identify low-quality types/members:
     - Thin wrappers around other types with no substantive abstraction.  
     - Methods with one call site where the abstraction is not buying clarity.  
     - Small, redundant configurations that could be merged into existing algebraic types / metadata records.
   - You may:
     - Consolidate types into a single, more expressive type when strongly justified.  
     - Reduce API surface by:
       - Removing redundant public APIs that have an equivalent or superior alternative in the project.  
       - Inlining single-use helpers at their call site and then deleting the helper.
   - When consolidating:
     - Ensure the new design aligns with algebraic / dispatch patterns and library guidelines.  
     - Preserve or improve clarity and performance.

────────────────────────────────────
C. Multi-pass cleanup procedure per target folder
────────────────────────────────────

For each `<<TARGET_FOLDER_NAME>>` in `Target Folder(s)`, follow these passes. Do not collapse them into a single sweep.

1. **Pass 1 – Context and inventory**

   - Confirm the 4-file structure and roles (`.cs`, `Config`, `Core`, `Compute`).  
   - For each file:
     - Catalog public APIs, internal helpers, nested types, constants, metadata, and FrozenDictionary tables.  
     - Identify existing advanced C# features (`MethodImpl`, `StructLayout`, `Pure`, `readonly struct`, `ref`/`in`, spans).  
     - Identify usage of `Result`, `ResultFactory`, validations, and diagnostics.
   - Note any apparent hot paths (tight loops, high-frequency entrypoints) for later optimization decisions.

2. **Pass 2 – If / ternary / control-flow audit**

   - Locate:
     - `if`, `if/else`, nested `if` chains.  
     - Switch statements, nested ternaries, and pattern matches.
   - For each candidate region:
     - Evaluate whether a **better construct** exists given the full method context:
       - Ternary expressions for simple, side-effect-free value choices.  
       - Switch expressions or pattern matching for many-branch value selection.  
       - Keeping a straightforward `if` when expression-based forms would be contorted or less clear.
     - Consider:
       - Return types (`void`, `Result<T>`, disposable resources, null returns).  
       - Early returns, multi-step side effects, and clean-up logic.
   - Plan only **justified** transformations:
     - No mechanical “replace all `if` with ternary”.  
     - Only change when it reduces LOC and/or clarifies data flow without harming semantics or debuggability.

3. **Pass 3 – Result/validation/diagnostics cleanup**

   - For each API pipeline:
     - Trace: `API` → `Core` → `Config` → `Compute` → `Result`.  
     - Identify manual error-propagation / nested branching that can be expressed as monadic chains:
       - Use `Map`, `Traverse`, `Tap`, and related combinators, and `ResultFactory` helpers where appropriate.
   - Validate:
     - Use `V`-based validations instead of ad-hoc repeated checks where possible.  
     - Remove redundant manual checks once validations cover them.  
     - Add missing validations only when clearly needed and consistent with existing patterns.
   - Diagnostics:
     - Ensure diagnostics usage, severity, and message style match patterns in `libs/core/diagnostics`.

4. **Pass 4 – Comments and XML documentation cleanup**

   - Comments:
     - Remove comments that merely restate clear code.  
     - Update or remove comments that are outdated or misleading.  
     - Keep and refine comments that encapsulate non-obvious intent or domain-specific nuance.
   - XML docs:
     - Reduce each documented element to a single, one-line summary.  
     - Eliminate `<param>` and multi-line XML documentation.  
     - For groups of similar constants/structs, use one group-level summary and remove redundant member-level summaries.

5. **Pass 5 – Magic numbers, config, and dispatch cleanup**

   - For each magic number:
     - Check for direct RhinoMath or SDK support.  
     - Next, check whether it belongs in existing `xConfig` metadata / FrozenDictionary tables.  
     - Only if necessary, introduce a named constant in `xConfig`.
   - Move:
     - Repeated or configuration-like constants from `Core`/`Compute` into `xConfig` and metadata tables.  
     - Per-operation config into centralized FrozenDictionary metadata where this improves clarity and reuse.
   - Ensure:
     - Remaining magic numbers are obviously local (e.g. loop bounds) or clearly justified.

6. **Pass 6 – Advanced C# feature audit and adjustment**

   - For `[MethodImpl]`, `[StructLayout]`, `[Pure]`, `readonly struct`, `ref`/`in` parameters, spans, and similar features:
     - Validate they are used in:
       - Hot or performance-sensitive paths.  
       - Semantically safe ways (no violating assumptions).
   - Adjust:
     - Introduce advanced features only where they have clear benefit and align with existing patterns.  
     - Remove or simplify advanced constructs that add complexity without measurable value.

7. **Pass 7 – Unused code, parameters, error codes, and usings**

   - For each unused parameter/field/type/constant/error:
     - Investigate why it is unused:
       - Forgotten integration (potential bug).  
       - Legacy/dead code (safe to remove).  
       - Future placeholder (should be clearly marked or implemented).
   - Then:
     - Integrate the unused item into the logic using existing infrastructure if it was intended to be used, or  
     - Remove it coherently:
       - Remove unused error codes from `E` and related registries (renumber if required by project patterns).  
       - Remove unused constants, fields, and types.  
       - Remove unused `using` directives per file.
   - Ensure all such changes are coherent and do not break semantics.

8. **Pass 8 – Helper and low-quality type consolidation**

   - Identify:
     - Helper methods with 1 (or at most 2) call sites where the abstraction does not add clarity.  
     - Thin wrapper types that duplicate responsibilities.
   - For each:
     - Inline the helper logic at the call site, but:
       - Re-express it in the best possible form: tuples, double parameters, improved loops, monadic chains, or better dispatch, rather than copy-paste.  
       - Delete the helper after inlining.
     - Consolidate types where a single, more expressive algebraic type can replace multiple thin types.
   - Ensure:
     - API surface is reduced only when consolidation is clearly better and uses no legacy constraints (no migration worries).  
     - LOC is reduced or, at worst, unchanged for materially better clarity/performance.

9. **Pass 9 – Final organization, LOC, and self-check**

   - Verify organization:
     - 4-file architecture and file count preserved.  
     - Code is placed in the correct file according to separation of concerns.
   - Verify LOC objectives:
     - Net LOC for the folder is not increased; if there is a small net increase, it must be clearly justified by correctness or significant clarity improvements.
   - Re-verify semantics:
     - Control-flow changes (if/else → ternary/switch/etc.) are correct, especially for:
       - `void` methods.  
       - Disposal/resource management.  
       - Null/error paths.
   - Ensure:
     - Comments and XML docs are minimal and useful.  
     - Magic numbers are either justified, centralized, or removed.  
     - Advanced features are in the right places.  
     - No unused parameters, constants, error codes, or usings remain.
   - Conceptually confirm:
     - The code builds with 0 new warnings, passes analyzers and `.editorconfig` checks, and there are no partial refactors or leftover TODOs.

────────────────────────────────────
D. Editing discipline
────────────────────────────────────

- All changes must be **surgical, justified, semantics-preserving, and infrastructure-aware**.  
- You may:
  - Refactor, move, inline, and consolidate code within and across the 4 files of a folder.  
  - Adjust or remove advanced C# features where beneficial.  
  - Remove dead code and redundant abstractions.  
  - Restructure logic to use better design patterns, tuples, and double-parameter patterns.
- You must not:
  - Introduce new `.cs` files or helper methods.  
  - Break existing public APIs without strong, explicit consolidation rationale.  
  - Make purely stylistic changes that increase LOC or reduce clarity.  
  - Change algorithmic formulas or domain behavior; only their expression and structure.

The final state must be: **denser, clearer, more idiomatic, fully aligned with the project’s monadic / algebraic / dispatch architecture, free of comment/documentation bloat and dead code, with no unjustified net LOC increase.**

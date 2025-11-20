**Target Folder(s):**
- `libs/rhino/<<TARGET_FOLDER_1>>/`
- `libs/rhino/<<TARGET_FOLDER_2>>/`

For each `Target Folder`, you must treat the **entire 4-file module** as a coherent unit:

- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

All analysis, design decisions, and refactors must be **folder-wide**, not limited to a single file. When a small, well-justified change in one file (e.g. `Config` or `Core`) allows a significantly better algorithmic or architectural solution in another (e.g. `Compute`), you must prefer that **holistic solution** over local “hamfisted” fixes.

**Reference Folders (read-only, for patterns):**
- `libs/rhino/fields/`
- `libs/rhino/morphology/`
- `libs/rhino/topology/`
- `libs/rhino/spatial/`

**Core Architecture (read-only, for wiring and invariants):**
- `libs/core/`
- `libs/rhino/rhino_math_class.md`
- `libs/rhino/rhino_math_reference.md`
- `libs/rhino/file_architecture.md`
- `libs/rhino/LIBRARY_GUIDELINES.md`

Your job is to perform a **deep, multi-pass, folder-wide logic + SDK + dispatch audit and surgical refactor** for all `Target Folder(s)`, focusing on:

- Mathematical / algorithmic correctness of all logic, especially in `<<BASENAME>>Compute.cs`
- Full and consistent RhinoCommon / RhinoMath / Rhino collections usage
- Strategic, unified constants and `FrozenDictionary`-based dispatch in `<<BASENAME>>Config.cs`
- Correct `UnifiedOperation` wiring and validation in `<<BASENAME>>Core.cs`
- Clean algebraic API and nested domain types in `<<BASENAME>>.cs`
- Cross-folder coherence with the canonical patterns in the reference folders

You must **not** change the coarse 4-file architecture:

- `<<BASENAME>>.cs`        — public API + nested algebraic domain types  
- `<<BASENAME>>Config.cs`  — internal constants + metadata + dispatch tables  
- `<<BASENAME>>Core.cs`    — internal orchestration + UnifiedOperation wiring  
- `<<BASENAME>>Compute.cs` — internal algorithmic implementation

Only make changes that are **fully justified** by logic correctness, SDK/math correctness, dispatch/config discipline, or alignment with established patterns.

────────────────────────────────────
A. Read and internalize constraints and reference patterns
────────────────────────────────────

1. **Global behavioral and style constraints (mandatory)**

   Read and obey, strictly:

   - `CLAUDE.md`  
   - `AGENTS.md`  
   - `.github/copilot-instructions.md`  
   - `.github/agents/*.agent.md` (all relevant roles)  
   - `.editorconfig`  
   - `libs/rhino/file_architecture.md`  
   - `libs/rhino/LIBRARY_GUIDELINES.md`  
   - `libs/rhino/rhino_math_class.md`  
   - `libs/rhino/rhino_math_reference.md`

   Enforce all project style constraints (non-negotiable):

   - No `var`. All local variables use explicit types.  
   - K&R braces exclusively.  
   - Named parameters for all non-trivial method calls.  
   - One public top-level type per file.  
   - No extension methods.  
   - No “helper” methods that simply forward parameters.  
   - No proliferation of nearly-duplicate methods; prefer dense, parameterized logic.  
   - Prefer **expression-oriented, functional style** when it improves clarity:
     - Pattern matching and switch expressions for branching.
     - Expression-bodied members where appropriate.
     - Minimal mutation; favor immutable locals and clear data flow.
   - Favor **algebraic, parameterized, polymorphic C#**:
     - Nested records encoding closed sets of domain variants.
     - Parameterization over mode flags and ad-hoc enums.
     - Making impossible domain states unrepresentable via type design.

2. **Core runtime / wiring patterns (libs/core)**

   Study carefully:

   - `Result` and `ResultFactory`  
   - `UnifiedOperation` / `OperationConfig`  
   - `ValidationRules` and `V` validation flags  
   - `E` error registry and domain ranges  
   - Any core algebraic types used for shared configuration or context

   Requirements:

   - All public APIs in `libs/rhino/` must return `Result<T>`.  
   - All non-trivial operations must go through `UnifiedOperation.Apply` with `OperationConfig` using proper `V` flags and `E` error codes.  
   - No ad-hoc validation paths or bypasses.

3. **Canonical 4-file patterns (reference folders)**

   In each reference folder (especially `fields`), study:

   - `X.cs`:
     - Public static entrypoint class.
     - Nested algebraic domain records (requests, modes, strategies, results).
   - `XConfig.cs`:
     - Small internal metadata record types.
     - One or a few `FrozenDictionary` tables as the **single source of truth** for operation metadata (validation modes, operation names, shared thresholds, etc.).
   - `XCore.cs`:
     - Orchestration layer that:
       - Pattern matches on algebraic types.
       - Looks up metadata from `XConfig`.
       - Constructs `OperationConfig` and calls `UnifiedOperation.Apply`.
   - `XCompute.cs`:
     - Dense, algorithmic, parameter-driven code using RhinoCommon and RhinoMath.
     - No `Result<T>`, no `UnifiedOperation`, no `V`/`E` knowledge.

   Extract from these folders the concrete patterns that define “good” code in this repo:  
   algebraic, metadata-driven, parameterized, SDK-aware, dense, and non-redundant.

────────────────────────────────────
B. Global goals and success criteria
────────────────────────────────────

For all `Target Folder(s)`, all changes must satisfy these goals:

1. **No incorrect logic**

   - Algorithms, formulas, and branching across the entire folder—especially in `<<BASENAME>>Compute.cs`—must be mathematically and geometrically correct, consistent with the documented domain, and free of both obvious and subtle math mistakes.

2. **No partial / half-implemented formulas or algorithms**

   - Identify and eliminate half-baked, stub-like, or inconsistent algorithmic implementations anywhere in the folder.  
   - Either:
     - Complete the algorithm properly, or  
     - Remove / redesign it so that every public operation has a fully realized, coherent implementation.

3. **Full, correct usage of RhinoCommon / RhinoMath / Rhino collections**

   - Prefer Rhino types, collections, and math utilities over generic .NET ones where appropriate.  
   - Use RhinoMath and the mapped constants from `rhino_math_class.md` / `rhino_math_reference.md` instead of ad-hoc numeric literals.  
   - Be aware that RhinoMath exposes values like `SqrtTwo`, `TwoPi`, etc., instead of a generic `Pi`; use the project’s established mappings consistently.  
   - Ensure consistent units, angle conventions, and tolerances with the rest of the project and reference folders.

4. **Strategic, unified constants and dispatch**

   - Before introducing any new constants or types, check existing unified dispatch tables and config patterns in `<<BASENAME>>Config.cs`.  
   - Prefer augmenting existing metadata tables and config records over adding “loose” constants, local enums, or scattered thresholds.  
   - New reusable constants must live in `<<BASENAME>>Config.cs` and be wired into `FrozenDictionary`-based metadata dispatch, not scattered in `Core` or `Compute`.

5. **Parameter / variable-driven logic over constant spam**

   - Design algorithms so that they **reuse named parameters, intermediate variables, and earlier outputs**, rather than duplicating numeric expressions or inlining the same arithmetic.  
   - When an intermediate quantity is conceptually important (radius, step size, normalized vector, tolerance derived from geometry, etc.), compute it **once**, assign to a clearly named local, and reuse that variable downstream.  
   - Avoid fragile magic-number logic; express relationships symbolically in terms of inputs and previously derived values wherever possible.  
   - Favor algebraic and polymorphic designs over branching tangles that hard-code modes or strategies as bare constants.

6. **Folder-level architectural integrity**

   - Preserve the 4-file architecture in all `Target Folder(s)`:
     - `<<BASENAME>>.cs` – public API + nested algebraic domain types.
     - `<<BASENAME>>Config.cs` – internal constants + metadata + `FrozenDictionary` dispatch tables.
     - `<<BASENAME>>Core.cs` – internal orchestration + `UnifiedOperation` wiring.
     - `<<BASENAME>>Compute.cs` – internal algorithmic implementation.
   - Do not add or remove `.cs` files in the folder.  
   - Maintain a clean separation of responsibilities across the four files while allowing **small, justified cross-file adjustments** to achieve better logic and architecture.

7. **Cross-folder coherence**

   - For similar concepts (e.g., distance fields, sampling strategies, tolerance derivation, orientation logic), ensure that `Target Folder(s)` are coherent with the canonical reference folders.  
   - Do not invent folder-local patterns that contradict better-established designs unless there is a clear, documented reason and benefit.

────────────────────────────────────
C. Multi-pass procedure per target folder (full-folder)
────────────────────────────────────

You must use **multiple explicit thinking passes** for each `<<TARGET_FOLDER_NAME>>` and treat the **entire 4-file module** as the unit of work. Do not collapse these passes into a single hybrid sweep.

For each `<<TARGET_FOLDER_NAME>>` in `Target Folder(s)`, follow this exact sequence:

1. **Pass 1 – Folder inventory and roles**

   - Enumerate all files in the folder and confirm that the 4-file architecture is intact:
     - `<<BASENAME>>.cs`
     - `<<BASENAME>>Config.cs`
     - `<<BASENAME>>Core.cs`
     - `<<BASENAME>>Compute.cs`
   - For the folder as a whole:
     - Summarize the conceptual domain (what this module is about).  
     - Describe how each file participates in that domain (API, config, orchestration, compute).  
     - Identify any obvious inconsistencies between these roles and the reference-folder patterns.

2. **Pass 2 – Algorithm + formula understanding (no edits yet)**

   For the entire folder, with emphasis on `<<BASENAME>>Compute.cs` but relating back to `Core` and `Config`:

   - For each `<<BASENAME>>Compute` method:
     - Read the method **line by line twice**.  
     - Write, for yourself, a brief and precise explanation of:
       - What the method is supposed to compute in mathematical / geometric terms.  
       - What intermediate quantities it derives and why they matter.  
     - Explicitly note:
       - All literal numeric constants.  
       - Assumptions about angle units, orientation, tolerances, iteration counts, convergence criteria.  
       - All RhinoCommon / RhinoMath calls and how they relate to the domain goal.
   - For `<<BASENAME>>Core` and `<<BASENAME>>Config`:
     - Map which public operations in `<<BASENAME>>.cs` route to which compute methods, via which metadata entries and validations.  
     - Identify how domain variants (requests/modes/strategies) are encoded in nested types, metadata, and dispatch.

   Do **not** edit code in this pass; this is pure understanding.

3. **Pass 3 – Logic and completeness audit (folder-wide)**

   Still without editing code:

   - For each compute method and its surrounding orchestration:
     - Are all branches exhaustive for the documented domain?  
     - Are edge cases (degenerate geometry, zero-length vectors, coincident points, empty collections, invalid inputs) handled appropriately and consistently with reference folders?  
     - Are all intermediate checks logically coherent (correct inequality directions, unit assumptions, robust comparisons)?
   - Identify, across the folder:
     - Obvious math errors or suspicious formulas.  
     - “Half-done” algorithms (TODO markers, unreachable branches, unused parameters, incomplete loops, missing convergence checks) in any of the four files.  
     - Duplicated logic that should instead reuse previously computed intermediate variables or be expressed through more algebraic, polymorphic structures.
   - Produce a consolidated internal issue list per folder, grouping issues by:
     - Pure logic/math problems.  
     - Incomplete or inconsistent algorithms.  
     - Cross-file mismatches (API/Config/Core/Compute not aligned).

4. **Pass 4 – Formula invariants and domain expectations**

   For each major operation (especially those involving geometry, fields, orientation, topology, or analysis):

   - Restate key formulas **symbolically**, focusing on:
     - Units and dimensions.  
     - Behavior as parameters approach limiting values (0, ∞, very large/small geometry).  
     - Symmetry, monotonicity, and boundedness where applicable.
   - Cross-check these properties against:
     - The reference folders’ behavior.  
     - Any domain expectations implicit in library guidelines or core types (e.g., ranges ensured by `E` and `V` flags).
   - Mark any algorithm that violates expected invariants (e.g., non-symmetric where symmetry is expected, divergence where convergence is expected, unbounded outputs where boundedness is required).

5. **Pass 5 – RhinoCommon / RhinoMath alignment**

   For each `<<BASENAME>>Compute` method and relevant parts of `Core`/`Config`:

   - Cross-check all math against RhinoMath and your Rhino math reference docs.  
   - Replace ad-hoc math with RhinoMath equivalents where available and appropriate.  
   - Ensure collections and geometry types use RhinoCommon idioms (e.g., `Point3dList`, `Mesh`, `Curve`, `Brep` utilities) when these provide clearer or more robust implementations than raw .NET equivalents.  
   - Confirm angle and unit consistency with established patterns in the reference folders.
   - Ensure consistent use of tolerances:
     - Prefer deriving tolerances from shared configuration or GeometryContext-style mechanism, not ad-hoc literals.

6. **Pass 6 – Constants, metadata, and unified dispatch normalization**

   For each `<<BASENAME>>`:

   - In `<<BASENAME>>Config.cs`:
     - Identify all constants, enums, and metadata record types.  
     - Locate any duplication of operation names, validation flags, thresholds, or magic numbers across `Config`, `Core`, and `Compute`.  
     - Normalize these into **small internal metadata record types plus one or a few `FrozenDictionary` tables**, following the canonical `Fields` pattern:
       - Metadata should carry:
         - `V` flags for validation.  
         - Operation name string (for diagnostics/logging).  
         - Any per-operation shared constants (buffer sizes, thresholds, iteration limits, codes, modes).
   - In `<<BASENAME>>Core.cs`:
     - Ensure all `UnifiedOperation` calls take `ValidationMode`, `OperationName`, and reusable thresholds from metadata in `<<BASENAME>>Config.cs`, not from inline literals.  
     - Remove micro-dispatch tables embedded in `Core`; those mappings belong in metadata in `Config`.
   - In `<<BASENAME>>Compute.cs`:
     - Retain only **truly local** numeric constants that are purely algorithmic and not part of shared operation configuration.  
     - Prefer local variables derived from inputs or metadata over repeated literals.

   The goal is: **one coherent metadata + dispatch layer per folder**, no loose constants or duplicated metadata.

7. **Pass 7 – Parameter-driven, algebraic refactor (full-folder)**

   For each folder, and particularly for `<<BASENAME>>Compute.cs` and how it is called from `Core` and `<<BASENAME>>.cs`:

   - Introduce clearly named local variables for any conceptually meaningful intermediate value that is:
     - Used more than once, or  
     - Important for understanding the algorithm (lengths, angles, tolerances, normalized factors, scaling parameters, field falloff rates, etc.).
   - Rewrite formulas to derive values from parameters and earlier locals instead of:
     - Repeating numeric constants, or  
     - Recomputing the same expression multiple times.  
   - Where appropriate, restructure logic to:
     - Use nested algebraic records (in `<<BASENAME>>.cs`) to encode domain variants.  
     - Use metadata lookups (in `Config`) rather than big `switch` statements on primitive IDs.  
     - Use polymorphic / pattern-matching dispatch (in `Core`) rather than flag-heavy or mode-code-heavy branching.
   - Ensure downstream steps reuse earlier outputs wherever appropriate, instead of recomputing or using hardcoded offsets.
   - Maintain extremely dense and non-redundant code:
     - No gratuitous helpers.  
     - No cosmetic reshuffling without logical benefit.  
     - Prefer polymorphic / algebraic parameterization over branching cascades.

   When a **minor, well-contained change** to `<<BASENAME>>.cs`, `Config`, or `Core` enables a materially cleaner algorithm or dispatch pattern in `Compute`, you must take that full-folder approach rather than leaving a poor structure in place.

8. **Pass 8 – Cross-folder coherence**

   - Compare the final structure and behavior of the `Target Folder` against the reference folders:
     - Are similar operations encoded in broadly similar ways (algebraic types, metadata shape, dispatch style)?  
     - Are tolerances, iteration patterns, and error handling consistent with established practices?  
   - Where reasonable, align the `Target Folder`’s patterns with the most mature reference (often `fields`), unless there is a clear, documented domain-specific reason to differ.

9. **Pass 9 – Final integration and self-check**

   - Confirm, per folder:
     - The folder still has exactly the four files with their intended roles.  
     - All public APIs remain in `<<BASENAME>>.cs` and return `Result<T>`.  
     - All non-trivial operations go through `UnifiedOperation.Apply` using metadata from `Config`.  
     - There are no new loose constants, stray enums, or per-operation magic numbers outside `Config`, unless they are truly local and clearly justified.
   - For each modified `<<BASENAME>>Compute` method and its wiring:
     - Re-read once more and verify:
       - Logical correctness of branches and formulas.  
       - Consistent, deliberate use of RhinoMath / RhinoCommon.  
       - Parameter-driven structure (no unnecessary hard-coding or duplication).  
       - Adherence to `.editorconfig` rules and project style (no `var`, K&R braces, named parameters, expression-based and algebraic where appropriate).
   - Ensure:
     - The repo builds with 0 new warnings under existing analyzers.  
     - All changes are tightly scoped, fully justified, and improve correctness, robustness, or architectural clarity.  
     - No `TODO` comments or partial refactors remain; each change is complete at the appropriate level of the chain of logic.

────────────────────────────────────
D. Editing discipline
────────────────────────────────────

- You must be **surgical**:
  - Only change code when logic, completeness, SDK usage, or dispatch/constant discipline requires it.  
  - Do not perform large stylistic rewrites that do not improve correctness, robustness, or architectural alignment.
- When you change a method or type:
  - Maintain or improve performance where reasonable.  
  - Preserve the overall algebraic model and 4-file architecture.  
  - If an API shape must change, it must be justified by domain clarity and remain consistent with patterns in the reference folders.
- After all edits:
  - The code must obey the constraints from `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`, and `.editorconfig`.  
  - The changes must make the folder’s logic, math, and architecture strictly better aligned with the project’s standards and goals.

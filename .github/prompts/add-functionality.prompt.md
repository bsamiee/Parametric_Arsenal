Your task is to **add or upgrade functionality** in exactly one Rhino module folder:

- Target Folder:`libs/rhino/<<TARGET_FOLDER_NAME>>/`
- Target Basename: `<<TARGET_BASENAME>>`
- Guided by a free-text description: `CAPABILITY_GOAL` (e.g. “add robust mesh-normal-based orientation helpers for façade shading design”).

You must:

- Preserve and extend the existing **4-file architecture** and monadic / algebraic style.
- Use **RhinoCommon, RhinoMath, and Rhino collections** correctly and idiomatically.
- Reuse and extend existing infrastructure:
  - `Result<T>`, `ResultFactory`
  - `V` validation flags
  - `E` error registry
  - `UnifiedOperation` / `OperationConfig`
  - `FrozenDictionary`-based configs
- Make changes that are **surgical, dense, performant, composable**, and **non-duplicative**.

All sections below are **mandatory**. You may abort only in the specific case described in §3.3.

---

## Placeholders

- `<<TARGET_FOLDER_NAME>>` – the folder under `libs/rhino/` whose functionality you will work on (e.g. `fields`, `spatial`, `orientation`).
- `<<TARGET_BASENAME>>` – the basename used by that folder’s four canonical files (e.g. `fields`, `spatial`, `orientation`).
- `CAPABILITY_GOAL` – a short natural-language description of the desired new or upgraded capability.

---

## 0. Global, Non-Negotiable Constraints

Before any reasoning or code:

### 0.1 Project rules to read and obey

You must read and strictly obey (paths are relative to repo root):

- `CLAUDE.md`
- `AGENTS.md`
- `.github/copilot-instructions.md`
- `.github/agents/*.agent.md` (for your role)
- `.editorconfig`
- `libs/rhino/file_architecture.md`
- `libs/rhino/LIBRARY_GUIDELINES.md`
- `libs/rhino/rhino_math_class.md`
- `libs/rhino/rhino_math_reference.md`

### 0.2 Style and structure constraints

These rules apply everywhere:

- No `var`. All locals use explicit types.
- K&R braces only.
- Named parameters for all non-trivial method calls.
- One public top-level type per file.
- No extension methods.
- No “helper” methods whose body merely forwards parameters.
- No proliferation of nearly-duplicate methods; prefer **dense, parameterized, polymorphic logic**.

### 0.3 Canonical 4-file Rhino folder architecture

For `libs/rhino/<<TARGET_FOLDER_NAME>>/` you must preserve exactly this structure and role split:

- `<<TARGET_BASENAME>>.cs`  
  Public static API + nested algebraic domain types only.

- `<<TARGET_BASENAME>>Config.cs`  
  Internal constants + metadata + `FrozenDictionary`-based dispatch.  
  Examples of canonical patterns from existing folders:
  - A single or small number of `FrozenDictionary` tables mapping:
    - Geometry `Type → metadata record`, or
    - `(Type InputType, string OperationType) → metadata record`.
  - Metadata record(s) bundling:
    - `V ValidationMode`
    - `string OperationName`
    - Shared constants (buffer sizes, thresholds, iteration limits, algorithm flags, delegates).

- `<<TARGET_BASENAME>>Core.cs`  
  Internal orchestration / dispatch only:
  - Pattern matches over algebraic request types.
  - Looks up metadata in `Config`.
  - Builds `OperationConfig` and calls `UnifiedOperation.Apply`.
  - Wraps raw compute results into `Result<T>` and domain records.

- `<<TARGET_BASENAME>>Compute.cs`  
  Internal algorithmic implementation only:
  - Dense, SDK-focused methods.
  - No `Result<T>`, no `UnifiedOperation`, no direct `V`/`E` usage.

You must **not** add or remove `.cs` files in the folder, and must **not** introduce any additional public top-level types.

### 0.4 Core runtime and validation infrastructure

You must use the existing monadic and validation infrastructure:

- All public APIs in `libs/rhino/` must return `Result<T>`.
- All non-trivial operations must go through `UnifiedOperation.Apply` (via existing core orchestration patterns), with `OperationConfig` carrying:
  - Geometry / execution context
  - `V` validation flags
  - `E` error codes
  - Any other core metadata (diagnostics, performance flags, etc.)

- Use `V` as a **flag struct** (bitwise combinations for validation modes), reusing existing flags wherever possible.
- Use `E` as the central error registry:
  - Prefer existing codes.
  - Only add new codes where absolutely necessary.
  - Any new `E` code must be actually used in implemented logic.

### 0.5 Monadic / functional discipline

- Use the `Result` API and `ResultFactory` for composition:
  - `Create`, `Map`, `Bind`, `Ensure`, `OnError`, `Traverse`, `Accumulate`, etc.
- Keep side effects localized and wrapped by `Result<T>`.
- Do not create any parallel validation or error pipeline.

### 0.6 Anti-hallucination rule

- You must not invent SDK types, methods, overloads, or project-local types.  
- For any RhinoCommon or repo-local type/method:
  - Confirm existence and signature in the codebase or SDK docs before using it.
- If you cannot confidently locate or confirm an API, you must not use it.

---

## 1. Repository and SDK Research (No Code Changes)

You must complete §1 before editing any code.

### 1.1 Internal architecture patterns

1. Re-read the project-level documents in §0.1.
2. Study at least **two mature Rhino folders** end-to-end (including their `Config`, `Core`, `Compute`), such as:

   - `libs/rhino/fields/`
   - `libs/rhino/spatial/`
   - Optionally one of: `libs/rhino/morphology/`, `libs/rhino/topology/`, `libs/rhino/orientation/`

3. Extract for yourself a concrete pattern checklist:

   - Public static class with nested algebraic domain types.
   - Metadata record types + `FrozenDictionary` tables in `Config` as the **single source of truth** for:
     - Validation flags
     - Operation names
     - Shared constants and algorithm parameters
   - `Core`:
     - Pattern-matching on algebraic request records.
     - Metadata lookup.
     - `UnifiedOperation.Apply` as the central pipeline.
   - `Compute`:
     - Dense, SDK-oriented algorithms.
     - No monadic or validation logic.

### 1.2 Rhino SDK / RhinoMath / collections research

For the concrete `CAPABILITY_GOAL`:

1. Use RhinoCommon docs, developer guides, and vetted discussions to identify:

   - Relevant geometry types (`Mesh`, `Brep`, `Curve`, `Point3dList`, `PointCloud`, `RTree`, etc.).
   - RhinoMath constants and functions appropriate to the domain, consistent with the project’s `rhino_math_*` mapping.
   - Standard patterns for:
     - Units and tolerances.
     - Angle conventions and orientation.
     - Use of Rhino-native collections vs generic .NET collections.

2. For yourself, summarize which SDK calls and patterns are canonical for this capability, and which should be avoided.

**You must not edit code during §1.**

---

## 2. Target Folder Reconnaissance (No Code Changes)

For `libs/rhino/<<TARGET_FOLDER_NAME>>/`:

### 2.1 Verify structural invariants

- Confirm the folder contains exactly:
  - `<<TARGET_BASENAME>>.cs`
  - `<<TARGET_BASENAME>>Config.cs`
  - `<<TARGET_BASENAME>>Core.cs`
  - `<<TARGET_BASENAME>>Compute.cs`

### 2.2 Build a capability map

1. In `<<TARGET_BASENAME>>.cs`:
   - List all public `Result<T>` entrypoints.
   - For each, write (for yourself) a 1–2 sentence domain description.

2. For each public entrypoint:
   - Map:
     - Which nested request/strategy/result records it uses.
     - Which metadata entries in `Config` it relies on.
     - Which compute methods in `Compute` it ultimately invokes.

### 2.3 Identify adjacent semantics in other Rhino folders

- Note responsibilities of other folders like:
  - `fields`, `spatial`, `topology`, `morphology`, `orientation`, etc.
- Ensure you understand which concepts belong in `<<TARGET_FOLDER_NAME>>` and which do not.

### 2.4 Capture invariants

- Collect implicit invariants and assumptions:
  - Planarity, closedness, orientation, tolerance usage, dimensional restrictions, etc.

**You must not edit code during §2.**

---

## 3. Capability Discovery, Evaluation, and Selection (Planning Only)

Using `CAPABILITY_GOAL` + the capability map + SDK research:

### 3.1 Generate internal candidate capabilities

Internally propose **2–5 candidate capabilities** of the following kinds:

- New, generally useful operation(s) that naturally belong to `<<TARGET_FOLDER_NAME>>`.
- Substantial upgrades to existing operations:
  - More robust.
  - More complete in algorithmic terms.
  - Additional modes or strategies modeled algebraically.
- New modes/strategies for existing APIs that can be expressed cleanly within existing patterns.

### 3.2 Strict evaluation criteria

For each candidate, evaluate:

1. **Value**  
   - Realistic utility for Rhino workflows, not hyper-niche corner cases.

2. **Scope fit**  
   - Concept truly belongs in `<<TARGET_FOLDER_NAME>>`, not another folder.

3. **Architectural fit**  
   - Implementable as:
     - One or more nested algebraic domain records in `<<TARGET_BASENAME>>.cs`.
     - One or a few metadata entries in `<<TARGET_BASENAME>>Config.cs`.
     - A **small** number of dense methods in `<<TARGET_BASENAME>>Compute.cs` with no method spam.

### 3.3 Selection rule and abort condition

- Select **1–3 capabilities** that pass all three criteria.
- If **no candidate** passes:
  - Stop without editing code.
  - Emit a concise explanation that no sufficiently valuable, well-scoped, architecturally clean capability could be identified under current constraints.

---

## 4. Cross-File Change Plan (Still No Code Changes)

For each selected capability, design a **complete, cross-file plan** before editing code anywhere.

### 4.1 Algebraic API design – `<<TARGET_BASENAME>>.cs`

For each capability:

1. Decide which nested records are required or must be extended:

   - Request types (e.g. `Request`, `Mode`, `Strategy` hierarchies).
   - Mode/strategy records as sealed types.
   - Result records where tuples are not expressive enough.

2. Encode invariants in the type system:

   - Avoid “one struct with a `Mode` discriminator and many nullable fields”.
   - Prefer separate sealed records per conceptual variant.
   - Make impossible states unrepresentable.

3. Define public static entrypoints:

   - Accept nested algebraic request/strategy types.
   - Return `Result<T>` where `T` is a domain result record or Rhino type wrapped in a domain record.
   - Contain **no heavy algorithmic logic** (only basic argument shaping and delegation to `Core`).

### 4.2 Metadata and dispatch design – `<<TARGET_BASENAME>>Config.cs`

1. Decide whether to:
   - Extend an existing metadata record type, or
   - Introduce a small, new metadata record type (only if justified).

2. Decide on key structure for `FrozenDictionary` tables:

   - Geometry `Type → metadata` (fields-style), or
   - `(Type InputType, string OperationType) → metadata` (spatial-style), or
   - A similarly coherent scheme.

3. Determine exactly what each metadata record must carry:

   - `V ValidationMode`
   - `string OperationName`
   - Shared constants (buffer sizes, thresholds, iteration limits, algorithm flags, delegates, etc.)

4. Plan how you will eliminate duplicated literals and configuration from `Core`/`Compute` by centralizing them in metadata.

### 4.3 Orchestration design – `<<TARGET_BASENAME>>Core.cs`

For each capability:

1. Specify pattern-matching over algebraic request records.
2. Specify metadata lookup from `Config`.
3. Specify a single `UnifiedOperation.Apply` call:
   - Input and output types.
   - Delegate into `Compute`.
   - `OperationConfig` built exclusively from metadata and call-site parameters (no magic numbers).

### 4.4 Compute design – `<<TARGET_BASENAME>>Compute.cs`

For each capability:

1. Choose exact RhinoCommon types for inputs and outputs.
2. Choose SDK algorithms and RhinoMath functions (based on §1.2).
3. Identify **conceptually important intermediate values**:
   - Normals, distances, angles, tolerances, radii, field magnitudes, etc.
4. Decide which intermediates become named locals (computed once and reused).

### 4.5 Validation and error design

For each capability:

1. Decide which `V` validation flags are required; reuse existing flags wherever possible.
2. Decide whether new `E` error codes are genuinely necessary:
   - If yes, define their semantics and planned trigger points.
   - If no, commit to reusing existing codes.

**Do not edit code until the entire plan is coherent across all four files.**

---

## 5. Implementation Passes (Code Changes)

You must now implement the plan in structured passes, treating the 4-file folder as a single unit of work.

### 5.1 Public API and domain records – `<<TARGET_BASENAME>>.cs`

1. Add or refine nested domain records (requests, modes, strategies, results) according to the plan.
2. Add or adjust public static entrypoints:

   - Construct the appropriate request/strategy records.
   - Delegate immediately into `Core`.
   - Avoid algorithmic loops or heavy logic.

3. Remove obsolete, non-algebraic signatures that the new API supersedes (if the plan requires it), keeping the public surface minimal and coherent.

### 5.2 Metadata and `FrozenDictionary` dispatch – `<<TARGET_BASENAME>>Config.cs`

1. Implement or extend metadata record types for the new capabilities.
2. Implement or refine unified dispatch tables:

   - Add entries for new capabilities to existing tables, or create a small new table only if clearly justified.
   - Ensure each distinct operation or mode has exactly one metadata entry.

3. Remove magic numbers and duplicated configuration from `Core`/`Compute`, moving them into metadata where they represent shared operational configuration.

### 5.3 Orchestration and `UnifiedOperation` wiring – `<<TARGET_BASENAME>>Core.cs`

1. Implement pattern matching over algebraic request records to:

   - Look up metadata entries from `Config`.
   - Build `OperationConfig` from metadata + call-site parameters.
   - Call `UnifiedOperation.Apply` exactly once per operation, with the appropriate compute delegate.

2. Ensure there are **no**:

   - Inline `OperationName` literals.
   - Inline `V` combinations.
   - “Micro-dispatch tables” in `Core` duplicating what belongs in `Config`.

3. Use mature patterns from existing folders as a guide (e.g. range/proximity routing in spatial, distance field routing in fields).

### 5.4 Compute algorithms – `<<TARGET_BASENAME>>Compute.cs`

1. Implement algorithms as **dense, parameterized methods**, similar in spirit to the strongest existing modules:

   - Use RhinoCommon geometry types and collections appropriately.
   - Use RhinoMath and the project’s math mappings instead of raw numerics when available.

2. Apply these guidelines:

   - Compute important intermediate values once; assign to clearly named locals and reuse them.
   - Prefer expression-bodied members and switch expressions where they improve clarity and density (without harming readability).
   - Avoid duplication: if the same computation appears multiple times, factor it into a single expression or variable, not a helper method that only forwards parameters.

3. Maintain strict separation of concerns:

   - `Compute` must not know about `Result<T>`, `UnifiedOperation`, `V`, or `E`.
   - `Compute` returns raw values; `Core` wraps them into monadic results and domain records.

---

## 6. Validation and Error Discipline Pass

After implementing code, perform a dedicated pass focused only on validation and error semantics.

### 6.1 Enumerate validations and errors per operation

For each new or modified operation:

1. List (for yourself):

   - Which `V` validation flags are used and where.
   - Which `E` error codes can be produced.

### 6.2 New error codes sanity check

If you added new `E` codes:

1. Confirm each is actually reachable in the implemented logic.
2. Confirm each is semantically distinct and not trivially replaceable by an existing code.
3. If any new error remains unused:
   - Either adjust the logic so it is used as intended, or
   - Remove the error and revert to existing codes.

### 6.3 Avoid redundant validation

- Prefer centralizing validation via `UnifiedOperation` + `V` flags.
- Keep local guards only when:
  - They are necessary for algorithmic safety or numerical robustness, or
  - They materially improve performance without duplicating core validations.

---

## 7. Cross-Folder Coherence and Core Integration

1. Compare the modified `<<TARGET_FOLDER_NAME>>` against mature reference folders:

   - Are algebraic domain models, metadata shapes, and dispatch patterns aligned with the strongest existing modules?
   - Are tolerance and unit conventions consistent?

2. Ensure correct integration with `libs/core/`:

   - All public APIs return `Result<T>`.
   - All non-trivial operations flow through `UnifiedOperation.Apply`.
   - Any new validations or error codes integrate cleanly with existing `V` and `E` infrastructure (correct ranges, semantics, and usage).
   - No parallel or ad-hoc monadic/validation system exists.

3. Verify clear responsibility boundaries:

   - No analysis logic leaking into morphology.
   - No spatial indexing logic leaking into unrelated folders.
   - No duplication of functionality that already belongs in another module.

---

## 8. Final Folder-Wide Quality Pass

Perform a final, holistic pass over `libs/rhino/<<TARGET_FOLDER_NAME>>/`.

### 8.1 Structural invariants

- Folder contains exactly:
  - `<<TARGET_BASENAME>>.cs`
  - `<<TARGET_BASENAME>>Config.cs`
  - `<<TARGET_BASENAME>>Core.cs`
  - `<<TARGET_BASENAME>>Compute.cs`
- `<<TARGET_BASENAME>>.cs` is the only file with public API and public nested domain types.

### 8.2 Style and analyzer compliance

- No `var`.
- K&R braces everywhere.
- Named parameters for non-trivial calls.
- No extension methods.
- No trivial helper methods that simply forward parameters.
- Repository builds cleanly with **zero new warnings** under existing analyzers.

### 8.3 Algorithmic and architectural integrity

- New capabilities are implemented as **few, dense, parameter-driven operations**, not a proliferation of near-duplicates.
- Metadata and dispatch are unified and centralized in `Config`; no stray constants or operation names remain in `Core` or `Compute`.
- All algorithms are fully implemented:
  - No TODOs.
  - No stub branches.
  - No half-baked modes that violate domain invariants or Rhino expectations.
- Existing behaviour is preserved or strictly improved:
  - Robustness.
  - Expressiveness.
  - Diagnostics and error clarity.

### 8.4 Error and validation sanity check

- All new `E` codes (if any) are used and justified.
- All new or modified uses of `V` flags are appropriate and non-redundant.

If any check in §8 fails, you must **revise code within this folder** until all criteria are satisfied, without introducing regressions in earlier sections.

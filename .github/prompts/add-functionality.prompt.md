# Add Functionality Agent

**Role**: Expert C# developer adding surgical, high-value functionality to Rhino computational geometry modules.

**Mission**: Add or upgrade exactly one capability in `libs/rhino/<<TARGET_FOLDER_NAME>>/` following strict architectural patterns.

## Inputs

- **Target Folder**: `libs/rhino/<<TARGET_FOLDER_NAME>>/`
- **Basename**: `<<TARGET_BASENAME>>`  
- **Goal**: `CAPABILITY_GOAL` (e.g., "add robust mesh-normal-based orientation helpers for façade shading design")

## Success Criteria

✅ New capability adds genuine value for Rhino workflows  
✅ Implementation uses 4-file architecture (`.cs`, `Config.cs`, `Core.cs`, `Compute.cs`)  
✅ Code is dense (no helpers), algebraic (nested records), performant (FrozenDictionary dispatch)  
✅ Integrates seamlessly with `Result<T>`, `V` flags, `E` registry, `UnifiedOperation`  
✅ Zero new warnings, all analyzers pass, builds cleanly  
✅ No duplicate logic or magic numbers—everything flows from metadata

## Non-Negotiable Constraints

**Before any code**, read and strictly obey:
- `/CLAUDE.md` - Absolute coding standards and exemplars
- `/AGENTS.md` - Agent-specific patterns
- `/.github/copilot-instructions.md` - Quick reference
- `/libs/rhino/file_architecture.md` - 4-file architecture roles
- `/libs/rhino/LIBRARY_GUIDELINES.md` - Domain patterns
- `/libs/rhino/rhino_math_class.md` - RhinoMath usage
- `/libs/rhino/rhino_math_reference.md` - SDK reference

**Style (zero tolerance)**:
- No `var` - explicit types always
- No `if`/`else` **statements** - use ternary (binary), switch expression (multiple), pattern matching (type discrimination). **Note**: `if` without `else` for early return/throw is acceptable.
- K&R braces - opening brace on same line
- Named parameters - for all non-obvious calls
- Trailing commas - in all multi-line collections
- One type per file - never multiple top-level types (CA1050)
- No extension methods, no helpers that forward parameters

**4-File Architecture (mandatory)**:
- `<<BASENAME>>.cs` - Public API + nested algebraic domain types only
- `<<BASENAME>>Config.cs` - Internal constants + metadata + `FrozenDictionary` dispatch
- `<<BASENAME>>Core.cs` - Orchestration via `UnifiedOperation.Apply`
- `<<BASENAME>>Compute.cs` - Dense SDK algorithms, no `Result<T>`/`V`/`E`

**Core Infrastructure (mandatory)**:
- All public APIs return `Result<T>`
- All operations use `UnifiedOperation.Apply` with `OperationConfig`
- Validation via `V` flags (bitwise combinations)
- Errors via `E` registry (prefer existing codes)
- No parallel validation or error pipelines

**Anti-Hallucination Rule**:
- Never invent SDK types, methods, or overloads
- Confirm existence in codebase or docs before using
- If uncertain, do not use it

---

## Phase 1: Research (No Code Changes)

**Goal**: Understand patterns, SDK, and existing architecture before proposing changes.

### 1.1 Study Internal Patterns

1. **Mandatory reading**: Re-read all documents in "Non-Negotiable Constraints" section
2. **Study exemplar folders end-to-end** (pick 2):
   - `libs/rhino/fields/` - FrozenDictionary dispatch, algebraic requests
   - `libs/rhino/spatial/` - Metadata-driven operations
   - `libs/rhino/morphology/`, `libs/rhino/topology/`, `libs/rhino/orientation/`

3. **Extract pattern checklist**:
   - Public static class with nested sealed records (requests, modes, strategies, results)
   - Metadata records + `FrozenDictionary` tables in `Config` as single source of truth
   - `Core`: Pattern match → metadata lookup → `UnifiedOperation.Apply`
   - `Compute`: Dense SDK algorithms, no awareness of `Result`/`V`/`E`

### 1.2 SDK Research

For `CAPABILITY_GOAL`:
1. Identify relevant RhinoCommon types (`Mesh`, `Brep`, `Curve`, `RTree`, etc.)
2. Map RhinoMath constants/functions appropriate to domain
3. Understand units, tolerances, angle conventions, orientation
4. Note canonical patterns vs patterns to avoid

**Deliverable**: Internal summary of SDK calls and patterns needed for this capability.

---

## Phase 2: Target Folder Reconnaissance (No Code Changes)

**Goal**: Map existing capabilities and identify extension points.

### 2.1 Verify Structure

Confirm folder contains exactly:
- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

### 2.2 Build Capability Map

1. **In** `<<BASENAME>>.cs`:
   - List all public `Result<T>` entrypoints
   - Describe domain purpose for each (1-2 sentences)

2. **Trace each entrypoint**:
   - Which nested records it uses (requests/strategies/results)
   - Which `Config` metadata entries it relies on
   - Which `Compute` methods it ultimately invokes

3. **Identify adjacent semantics**:
   - Note responsibilities of other folders (`fields`, `spatial`, `topology`, etc.)
   - Ensure clear boundaries—what belongs in `<<TARGET_FOLDER_NAME>>` vs elsewhere

4. **Capture invariants**:
   - Implicit assumptions: planarity, closedness, orientation, tolerance, dimensions

---

## Phase 3: Capability Discovery & Selection

**Goal**: Propose 2-5 candidates, evaluate strictly, select 1-3 best.

### 3.1 Generate Candidates

Propose 2-5 capabilities of these types:
- New operation naturally belonging to `<<TARGET_FOLDER_NAME>>`
- Substantial upgrade (robustness, completeness, new algebraic modes)
- New modes/strategies cleanly expressible within existing patterns

### 3.2 Evaluation Criteria (All Must Pass)

For each candidate, assess:

**1. Value**  
- Realistic utility for Rhino workflows (not hyper-niche)
- Fills genuine gap in current capabilities

**2. Scope Fit**  
- Truly belongs in `<<TARGET_FOLDER_NAME>>`, not another folder
- Aligns with existing domain model

**3. Architectural Fit**  
- Implementable as nested algebraic records in `.cs`
- One or few metadata entries in `Config.cs`
- Small number of dense methods in `Compute.cs` (no method spam)

### 3.3 Selection Rule & Abort Condition

- **Select 1-3 capabilities** that pass all three criteria
- **If zero candidates pass**: Stop without editing code. Explain why no sufficiently valuable, well-scoped, architecturally clean capability exists under current constraints.

---

## Phase 4: Cross-File Design (No Code Changes)

**Goal**: Complete architectural plan before writing any code.

### 4.1 Algebraic API Design (`<<BASENAME>>.cs`)

For each selected capability:

1. **Decide nested records**:
   - Request types, mode/strategy hierarchies
   - Sealed records per conceptual variant (no "one struct with nullable fields")
   - Make impossible states unrepresentable

2. **Define public static entrypoints**:
   - Accept nested algebraic types
   - Return `Result<T>` with domain result records
   - No heavy logic—basic shaping and delegation to `Core`

### 4.2 Metadata & Dispatch Design (`<<BASENAME>>Config.cs`)

1. **Metadata record types**:
   - Extend existing or introduce new (only if justified)
   - Must carry: `V ValidationMode`, `string OperationName`, shared constants

2. **FrozenDictionary key structure**:
   - `Type → metadata` (fields-style)
   - `(Type, string) → metadata` (spatial-style)
   - Or coherent alternative

3. **Centralize configuration**:
   - Eliminate duplicated literals from `Core`/`Compute`
   - Buffer sizes, thresholds, iteration limits, algorithm flags

### 4.3 Orchestration Design (`<<BASENAME>>Core.cs`)

For each capability:

1. Pattern match algebraic request records
2. Lookup metadata from `Config`
3. Call `UnifiedOperation.Apply` with:
   - Delegate into `Compute`
   - `OperationConfig` built from metadata (no magic numbers)

### 4.4 Compute Design (`<<BASENAME>>Compute.cs`)

1. **Choose exact RhinoCommon types** for inputs/outputs
2. **Select SDK algorithms** and RhinoMath functions
3. **Identify important intermediates**:
   - Normals, distances, angles, tolerances, radii, magnitudes
   - Compute once, name clearly, reuse

### 4.5 Validation & Error Design

1. **Validation**: Reuse existing `V` flags wherever possible; add new flags only if necessary
2. **Errors**: Reuse existing `E` codes; add new codes only if genuinely necessary with clear semantics

**Checkpoint**: Entire plan must be coherent across all 4 files before proceeding.

---

## Phase 5: Implementation

**Goal**: Implement plan in structured passes treating 4-file folder as single unit.

### 5.1 Public API & Domain Records (`<<BASENAME>>.cs`)

1. Add/refine nested domain records (requests, modes, strategies, results)
2. Add/adjust public static entrypoints:
   - Construct request/strategy records
   - Delegate to `Core`
   - No algorithmic loops or heavy logic
3. Remove obsolete non-algebraic signatures if plan requires (keep surface minimal)

### 5.2 Metadata & Dispatch (`<<BASENAME>>Config.cs`)

1. Implement/extend metadata record types
2. Implement/refine `FrozenDictionary` dispatch tables:
   - Add entries for new capabilities
   - Ensure each operation has exactly one metadata entry
3. Move magic numbers and config from `Core`/`Compute` into metadata

### 5.3 Orchestration (`<<BASENAME>>Core.cs`)

1. Pattern match algebraic request records to:
   - Lookup metadata from `Config`
   - Build `OperationConfig` from metadata + call-site parameters
   - Call `UnifiedOperation.Apply` exactly once per operation

2. Eliminate:
   - Inline `OperationName` literals
   - Inline `V` combinations
   - Micro-dispatch tables (belong in `Config`)

3. Follow mature folder patterns (e.g., fields' distance routing, spatial's proximity routing)

### 5.4 Compute Algorithms (`<<BASENAME>>Compute.cs`)

1. **Dense, parameterized methods**:
   - Use RhinoCommon geometry types appropriately
   - Use RhinoMath instead of raw numerics
   - Compute important intermediates once, assign to named locals, reuse

2. **Guidelines**:
   - Expression-bodied members and switch expressions where they improve clarity
   - Avoid duplication—factor into expressions/variables, not helpers
   - Prefer `for` loops in hot paths, LINQ for clarity elsewhere

3. **Strict separation**:
   - `Compute` never knows about `Result<T>`, `UnifiedOperation`, `V`, `E`
   - `Core` wraps raw values into monadic results and domain records

---

## Phase 6: Validation & Error Discipline

**Goal**: Dedicated pass ensuring validation and error semantics are correct.

### 6.1 Enumerate Per Operation

For each new/modified operation:
- Which `V` validation flags are used and where
- Which `E` error codes can be produced

### 6.2 New Error Codes Sanity Check

If new `E` codes added:
1. Confirm each is reachable in implemented logic
2. Confirm each is semantically distinct from existing codes
3. If unused: either use as intended or remove and revert to existing codes

### 6.3 Avoid Redundant Validation

- Centralize via `UnifiedOperation` + `V` flags
- Keep local guards only when:
  - Necessary for algorithmic safety/numerical robustness
  - Materially improve performance without duplicating core validations

---

## Phase 7: Cross-Folder Coherence

**Goal**: Ensure integration with core and alignment with mature folders.

1. **Compare with reference folders**:
   - Algebraic models, metadata shapes, dispatch patterns aligned?
   - Tolerance and unit conventions consistent?

2. **Verify core integration**:
   - All public APIs return `Result<T>`
   - All operations flow through `UnifiedOperation.Apply`
   - Validations/errors integrate cleanly with `V` and `E`
   - No parallel or ad-hoc systems

3. **Verify responsibility boundaries**:
   - No analysis in morphology
   - No spatial indexing in unrelated folders
   - No duplication of existing functionality

---

## Phase 8: Final Quality Pass

**Goal**: Holistic verification of folder-wide quality.

### 8.1 Structural Invariants

- Folder contains exactly 4 files: `.cs`, `Config.cs`, `Core.cs`, `Compute.cs`
- Only `.cs` has public API and public nested types

### 8.2 Style & Analyzer Compliance

- No `var`, K&R braces, named parameters
- No extension methods, no trivial helpers
- **Zero new warnings** under existing analyzers

### 8.3 Algorithmic & Architectural Integrity

- New capabilities are **few, dense, parameter-driven** (not proliferation)
- Metadata/dispatch unified in `Config` (no stray constants in `Core`/`Compute`)
- All algorithms fully implemented:
  - No TODOs, no stub branches
  - No half-baked modes violating invariants

### 8.4 Error & Validation Sanity

- All new `E` codes (if any) are used and justified
- All `V` flag usage is appropriate and non-redundant

**If any check fails**: Revise within folder until all criteria satisfied.

---

## Editing Discipline

✅ **Do**:
- Study exemplar folders before writing code
- Reuse existing infrastructure aggressively
- Write dense, algebraic, expression-oriented code
- Make surgical changes preserving existing behavior
- Test incrementally as you go

❌ **Don't**:
- Add new `.cs` files or change file count
- Create helper methods that forward parameters
- Introduce magic numbers outside `Config`
- Bypass `UnifiedOperation` or create parallel pipelines
- Change unrelated code or fix unrelated bugs

---

## Anti-Patterns to Avoid

1. **Feature Creep**: Adding multiple loosely related capabilities—focus on 1-3 coherent additions
2. **Method Spam**: Creating many similar methods instead of parameterized dispatch
3. **Config Sprawl**: Loose constants instead of unified metadata tables
4. **Validation Duplication**: Manual checks instead of leveraging `V` flags
5. **SDK Reinvention**: Implementing geometry operations already in RhinoCommon
6. **Magic Numbers**: Unexplained literals instead of named metadata
7. **Partial Implementation**: Leaving TODOs or stub branches
8. **Breaking Boundaries**: Adding functionality that belongs in another folder

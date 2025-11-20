# SDK & Logic Audit Agent

**Role**: Expert C# developer auditing and refining mathematical correctness, SDK usage, and architectural patterns in Rhino computational geometry modules.

**Mission**: Perform deep, surgical audit and refactor of `libs/rhino/<<TARGET_FOLDER_N>>/` focusing on algorithmic correctness, complete RhinoCommon/RhinoMath usage, unified dispatch, and cross-folder coherence.

## Inputs

- **Target Folders**: `libs/rhino/<<TARGET_FOLDER_1>>/`, `libs/rhino/<<TARGET_FOLDER_2>>/`, ...

Each folder contains 4 files:
- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

## Success Criteria

✅ All algorithms mathematically/geometrically correct (no logic errors, no half-implemented formulas)  
✅ Full RhinoCommon/RhinoMath/Rhino collections usage (no SDK reinvention)  
✅ Unified constants and FrozenDictionary dispatch in `Config` (no loose magic numbers)  
✅ Correct `UnifiedOperation` wiring and validation in `Core`  
✅ Clean algebraic API with nested domain types in `.cs`  
✅ Cross-folder coherence with canonical patterns from reference folders  
✅ Parameter-driven logic (no constant spam, reuse computed intermediates)  
✅ Zero new warnings, all analyzers pass

## Non-Negotiable Constraints

**Before any code**, read and strictly obey:
- `/CLAUDE.md` - Standards, exemplars, patterns
- `/AGENTS.md` - Agent patterns
- `/.github/copilot-instructions.md` - Quick reference
- `/.editorconfig` - Style enforcement
- `/libs/rhino/file_architecture.md` - 4-file architecture
- `/libs/rhino/LIBRARY_GUIDELINES.md` - Domain patterns
- `/libs/rhino/rhino_math_class.md` - RhinoMath usage (SqrtTwo, TwoPi, etc.)
- `/libs/rhino/rhino_math_reference.md` - SDK reference

**Core Infrastructure (understand thoroughly)**:
- `libs/core/results/Result.cs` - Monad patterns
- `libs/core/results/ResultFactory.cs` - Creation helpers
- `libs/core/operations/UnifiedOperation.cs` - Dispatch engine
- `libs/core/validation/ValidationRules.cs` - Expression tree compilation
- `libs/core/errors/E.cs` - Error registry and domain ranges

**Reference Folders (study for patterns)**:
- `libs/rhino/fields/` - Exemplar FrozenDictionary dispatch, algebraic design
- `libs/rhino/spatial/` - Metadata-driven operations
- `libs/rhino/morphology/`, `libs/rhino/topology/`

**Style (zero tolerance)**:
- No `var` - explicit types always
- No `if`/`else` **statements** - ternary (binary), switch expression (multiple), pattern matching (type). **Note**: `if` without `else` for early return/throw is acceptable.
- K&R braces - opening on same line
- Named parameters - non-obvious calls
- Trailing commas - multi-line collections
- One type per file (CA1050)
- No extension methods, no helpers forwarding parameters
- Dense, parameterized logic (no method proliferation)

**4-File Architecture (preserve)**:
- `.cs` - Public API + nested algebraic domain types
- `Config.cs` - Constants + metadata + `FrozenDictionary` dispatch (single source of truth)
- `Core.cs` - Orchestration: pattern match → metadata lookup → `UnifiedOperation.Apply`
- `Compute.cs` - Dense SDK algorithms, no `Result<T>`/`V`/`E` knowledge

---

## Audit Goals (Per Folder)

### 1. No Incorrect Logic
Algorithms, formulas, branching across folder—especially in `Compute.cs`—must be:
- Mathematically and geometrically correct
- Consistent with documented domain
- Free of obvious and subtle math mistakes

### 2. No Partial/Half-Implemented Algorithms
Eliminate:
- Half-baked, stub-like, or inconsistent implementations
- TODO markers, unreachable branches, unused parameters, incomplete loops

**Either**: Complete algorithm properly **or** remove/redesign so every public operation has fully realized, coherent implementation.

### 3. Full RhinoCommon/RhinoMath/Rhino Collections Usage
- Prefer Rhino types/collections/math over generic .NET where appropriate
- Use RhinoMath constants (`SqrtTwo`, `TwoPi`, etc.) instead of ad-hoc numeric literals
- Follow project's established mappings from `rhino_math_class.md` / `rhino_math_reference.md`
- Ensure consistent units, angle conventions, tolerances with reference folders

### 4. Strategic, Unified Constants & Dispatch
- Check existing unified dispatch tables and config patterns in `Config.cs` before introducing new constants/types
- Prefer augmenting existing metadata tables over adding "loose" constants, local enums, scattered thresholds
- New reusable constants → `Config.cs`, wired into `FrozenDictionary` metadata dispatch

### 5. Parameter/Variable-Driven Logic (Not Constant Spam)
Design algorithms to:
- **Reuse named parameters, intermediate variables, earlier outputs** instead of duplicating numeric expressions
- When intermediate is conceptually important (radius, step size, normalized vector, tolerance from geometry), compute **once**, assign to clearly named local, reuse downstream
- Avoid fragile magic-number logic—express relationships symbolically in terms of inputs and derived values
- Favor algebraic and polymorphic designs over branching tangles hard-coding modes/strategies

### 6. Folder-Level Architectural Integrity
Preserve 4-file architecture in all folders:
- No adding/removing `.cs` files
- Maintain clean separation of responsibilities
- Allow **small, justified cross-file adjustments** for better logic/architecture

### 7. Cross-Folder Coherence
For similar concepts (distance fields, sampling strategies, tolerance derivation, orientation):
- Ensure coherence with canonical reference folders
- Don't invent folder-local patterns contradicting better-established designs unless clear, documented reason

---

## Multi-Pass Procedure (Per Folder)

**Use multiple explicit thinking passes** treating entire 4-file module as unit of work. Do not collapse passes.

### Pass 1: Folder Inventory & Roles
- Enumerate all files, confirm 4-file architecture intact
- For folder as whole:
  - Summarize conceptual domain (what module is about)
  - Describe how each file participates (API, config, orchestration, compute)
  - Identify inconsistencies with reference-folder patterns

### Pass 2: Algorithm & Formula Understanding (No Edits)
**For entire folder, emphasis on `Compute.cs` relating back to `Core` and `Config`:**

For each `Compute` method:
- **Read line by line twice**
- Write (internally) brief, precise explanation:
  - What method computes (mathematical/geometric terms)
  - What intermediate quantities derived and why they matter
- Explicitly note:
  - All literal numeric constants
  - Assumptions: angle units, orientation, tolerances, iteration counts, convergence
  - All RhinoCommon/RhinoMath calls and relation to domain goal

For `Core` and `Config`:
- Map which public operations in `.cs` route to which compute methods via which metadata/validations
- Identify how domain variants (requests/modes/strategies) encoded in nested types, metadata, dispatch

**No edits in this pass** - pure understanding.

### Pass 3: Logic & Completeness Audit (Folder-Wide, No Edits)
For each compute method and surrounding orchestration:
- Are all branches exhaustive for documented domain?
- Edge cases handled (degenerate geometry, zero-length vectors, coincident points, empty collections, invalid inputs)?
- All intermediate checks logically coherent (correct inequality directions, unit assumptions, robust comparisons)?

**Identify across folder**:
- Obvious math errors or suspicious formulas
- "Half-done" algorithms (TODO markers, unreachable branches, unused parameters, incomplete loops, missing convergence)
- Duplicated logic that should reuse computed intermediates or express through algebraic/polymorphic structures

**Produce consolidated issue list** per folder, grouping:
- Pure logic/math problems
- Incomplete or inconsistent algorithms
- Cross-file mismatches (API/Config/Core/Compute not aligned)

### Pass 4: Formula Invariants & Domain Expectations
For each major operation (geometry, fields, orientation, topology, analysis):

**Restate key formulas symbolically**:
- Units and dimensions
- Behavior as parameters approach limits (0, ∞, very large/small geometry)
- Symmetry, monotonicity, boundedness where applicable

**Cross-check against**:
- Reference folders' behavior
- Domain expectations implicit in library guidelines or core types (ranges ensured by `E` and `V`)

**Mark violations**: Non-symmetric where symmetry expected, divergence where convergence expected, unbounded where boundedness required.

### Pass 5: RhinoCommon/RhinoMath Alignment
For each `Compute` method and relevant `Core`/`Config` parts:

- Cross-check all math against RhinoMath and Rhino math reference docs
- Replace ad-hoc math with RhinoMath equivalents where available and appropriate
- Ensure collections/geometry types use RhinoCommon idioms (`Point3dList`, `Mesh`, `Curve`, `Brep` utilities)
- Confirm angle and unit consistency with reference folder patterns
- Ensure consistent tolerance usage:
  - Prefer deriving from shared configuration or GeometryContext-style mechanism
  - Not ad-hoc literals

### Pass 6: Constants, Metadata, Unified Dispatch Normalization
For each `<<BASENAME>>`:

**In `Config.cs`**:
- Identify all constants, enums, metadata record types
- Locate duplication of operation names, validation flags, thresholds, magic numbers across `Config`, `Core`, `Compute`
- **Normalize into small internal metadata record types + FrozenDictionary tables** following canonical Fields pattern:
  - Metadata carries: `V` flags, operation name, per-operation shared constants (buffer sizes, thresholds, iteration limits, codes, modes)

**In `Core.cs`**:
- Ensure all `UnifiedOperation` calls take `ValidationMode`, `OperationName`, reusable thresholds from metadata in `Config.cs` (not inline literals)
- Remove micro-dispatch tables embedded in `Core` (mappings belong in metadata in `Config`)

**In `Compute.cs`**:
- Retain only **truly local** numeric constants purely algorithmic and not part of shared operation config
- Prefer local variables derived from inputs or metadata over repeated literals

**Goal**: **One coherent metadata + dispatch layer per folder**, no loose constants or duplicated metadata.

### Pass 7: Parameter-Driven, Algebraic Refactor (Full-Folder)
For each folder, particularly `Compute.cs` and how called from `Core` and `.cs`:

- Introduce clearly named local variables for conceptually meaningful intermediates:
  - Used more than once **or**
  - Important for understanding (lengths, angles, tolerances, normalized factors, scaling parameters, field falloff rates)

- Rewrite formulas to derive from parameters and earlier locals instead of:
  - Repeating numeric constants
  - Recomputing same expression multiple times

- Where appropriate, restructure logic to:
  - Use nested algebraic records (in `.cs`) to encode domain variants
  - Use metadata lookups (in `Config`) rather than big switch statements on primitive IDs
  - Use polymorphic/pattern-matching dispatch (in `Core`) rather than flag-heavy or mode-code branching

- Ensure downstream steps reuse earlier outputs (not recomputing or using hardcoded offsets)

- Maintain extremely dense, non-redundant code:
  - No gratuitous helpers
  - No cosmetic reshuffling without logical benefit
  - Prefer polymorphic/algebraic parameterization over branching cascades

**When minor, well-contained change** to `.cs`, `Config`, or `Core` enables materially cleaner algorithm or dispatch in `Compute`, take full-folder approach.

### Pass 8: Cross-Folder Coherence
Compare final structure and behavior against reference folders:
- Similar operations encoded in broadly similar ways (algebraic types, metadata shape, dispatch style)?
- Tolerances, iteration patterns, error handling consistent with established practices?

Where reasonable, align with most mature reference (often `fields`), unless clear documented domain-specific reason to differ.

### Pass 9: Final Integration & Self-Check
**Confirm per folder**:
- Folder has exactly 4 files with intended roles
- All public APIs in `.cs` return `Result<T>`
- All non-trivial operations go through `UnifiedOperation.Apply` using metadata from `Config`
- No new loose constants, stray enums, per-operation magic numbers outside `Config` (unless truly local and justified)

**For each modified `Compute` method and wiring**:
- Re-read once more, verify:
  - Logical correctness of branches and formulas
  - Consistent, deliberate RhinoMath/RhinoCommon usage
  - Parameter-driven structure (no unnecessary hard-coding or duplication)
  - `.editorconfig` and project style adherence

**Ensure**:
- Repo builds with 0 new warnings under analyzers
- Changes tightly scoped, fully justified, improve correctness/robustness/architectural clarity
- No TODO comments or partial refactors

---

## Editing Discipline

✅ **Do**:
- Be surgical—change only when logic, completeness, SDK usage, or dispatch/constant discipline requires
- Maintain or improve performance where reasonable
- Preserve overall algebraic model and 4-file architecture
- If API shape must change, justify by domain clarity and remain consistent with reference patterns

❌ **Don't**:
- Perform large stylistic rewrites not improving correctness/robustness/architectural alignment
- Change unrelated code or fix unrelated bugs
- Add new `.cs` files
- Create helper methods forwarding parameters
- Break existing public APIs without strong justification

**After all edits**:
- Code must obey constraints from CLAUDE.md, AGENTS.md, `.editorconfig`
- Changes make folder's logic, math, architecture strictly better aligned with project standards

---

## Algorithmic Correctness Checklist

Use this for each compute method:

**Mathematics**:
- [ ] All arithmetic operations correct (no sign errors, order of operations)
- [ ] Division by zero handled or proven impossible
- [ ] Overflow/underflow considered for extreme inputs
- [ ] Floating-point comparison uses appropriate epsilon/tolerance
- [ ] Trigonometric functions use correct angle units (radians vs degrees)

**Geometry**:
- [ ] Vector operations correct (cross product handedness, normalization)
- [ ] Distance calculations account for geometry type (Euclidean, geodesic)
- [ ] Intersection logic handles all cases (parallel, coincident, skew)
- [ ] Bounding box usage correct (tight vs loose, world vs local)
- [ ] Tolerance-based comparisons use appropriate tolerance source

**Logic**:
- [ ] All branches reachable and correct
- [ ] Loop termination guaranteed (no infinite loops)
- [ ] Recursion has proper base case
- [ ] Early returns don't skip necessary cleanup
- [ ] Error conditions detected and propagated correctly

**Edge Cases**:
- [ ] Empty collections handled
- [ ] Null/invalid inputs handled
- [ ] Degenerate geometry (zero-length, coincident points) handled
- [ ] Extreme values (very large/small) handled
- [ ] Boundary conditions (first/last element, single element) correct

---

## Magic Number Resolution Priority (Strict)

When encountering numeric literal in code:

**Priority 1**: Check if RhinoMath or Rhino SDK provides constant
- `RhinoMath.SqrtTwo`, `RhinoMath.TwoPi`, `RhinoMath.UnsetValue`, etc.
- Rhino tolerance constants, unit conversion factors

**Priority 2**: Check if belongs in existing `Config` metadata / FrozenDictionary table
- Per-operation threshold, buffer size, iteration limit
- Validation tolerance, convergence criterion
- Algorithm mode/flag constant

**Priority 3**: Introduce well-named constant in `Config.cs` (last resort)
- Must be reusable across multiple operations
- Must have clear semantic meaning
- Must be wired into metadata dispatch

**Acceptable**: Remain as literal only if:
- Obviously local (loop bound derived from input length)
- Self-explanatory mathematical constant in tight local scope (e.g., `/ 2.0` for midpoint)
- Clearly justified by context

---

## Anti-Patterns to Avoid

1. **SDK Reinvention**: Implementing geometry operations already in RhinoCommon
2. **Magic Number Proliferation**: Unexplained literals instead of named metadata
3. **Validation Duplication**: Manual checks instead of leveraging `V` flags
4. **Config Sprawl**: Loose constants instead of unified metadata tables
5. **Half-Baked Algorithms**: Leaving TODOs or stub branches
6. **Formula Copy-Paste**: Recomputing same intermediate instead of variable reuse
7. **Monolithic Branching**: Giant switch on primitive mode instead of algebraic dispatch
8. **Tolerance Inconsistency**: Different ad-hoc tolerances across similar operations
9. **Unit Confusion**: Mixing radians/degrees without clear conversion
10. **Boundary Neglect**: Not handling empty collections, single elements, edge cases

---

## RhinoMath Quick Reference

**Constants** (use these, not hand-rolled):
- `RhinoMath.SqrtTwo`, `RhinoMath.SqrtThree`
- `RhinoMath.TwoPi`, `RhinoMath.Pi` (note: not just `Pi`, also `TwoPi`)
- `RhinoMath.UnsetValue`, `RhinoMath.UnsetIntIndex`
- `RhinoMath.ZeroTolerance`, `RhinoMath.SqrtEpsilon`

**Functions** (prefer over System.Math when equivalent):
- Angle/trig operations with appropriate units
- Clamping, interpolation utilities
- Comparison with tolerance

**Collections** (prefer Rhino-specific):
- `Point3dList` over `List<Point3d>`
- `RTree` for spatial indexing
- Rhino-native collections for geometry workflows

**Consult**: `/libs/rhino/rhino_math_class.md` and `/libs/rhino/rhino_math_reference.md` for complete mappings.

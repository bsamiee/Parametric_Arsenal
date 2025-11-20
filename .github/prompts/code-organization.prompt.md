**Target Folder(s):**
- `libs/rhino/<<TARGET_FOLDER_1>>/`
- `libs/rhino/<<TARGET_FOLDER_2>>/`

For each `Target Folder`, you must treat each of the four standard files as a coherent unit:

- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

The goal of this pass is **purely organizational**:

- Reorder existing members (fields, nested types, methods) to achieve a consistent, logical structure.
- Within each category, **shorter members (fewer vertical lines) must appear before longer members**.
- Keep **FrozenDictionary declarations at the top of the type** (when present).
- Consolidate XML documentation and comments **only where it does not change meaning**, to form cleaner groupings.
- **No functional behavior changes** are allowed.

You must **not**:

- Change method signatures, generic parameters, accessibility, attributes, or return types.
- Introduce new methods, new types, or new `.cs` files.
- Remove or add executable statements that alter behavior.
- Change namespaces or using directives.

You **may**:

- Reorder members inside a type.
- Move existing XML comments to sit above a group instead of per-member, when grouping makes sense.
- Delete/merge duplicate or trivially redundant XML comments when consolidating documentation for a group.

────────────────────────────────────
A. Read and internalize constraints and patterns
────────────────────────────────────

1. **Global constraints (mandatory)**

   Read and obey, strictly:

   - `CLAUDE.md`
   - `AGENTS.md`
   - `.github/copilot-instructions.md`
   - `.github/agents/*.agent.md` (all relevant roles)
   - `.editorconfig`
   - `libs/rhino/file_architecture.md`
   - `libs/rhino/LIBRARY_GUIDELINES.md`

   Enforce all project style constraints (non-negotiable):

   - No `var`.  
   - K&R braces.  
   - Named parameters.  
   - One public top-level type per file.  
   - No extension methods.  
   - No new helper methods.  
   - No functional code changes; this pass is **reordering and documentation grouping only**.

2. **Understand file roles**

   For each `<<BASENAME>>` in the target folder, confirm roles:

   - `<<BASENAME>>.cs`        — public API + nested algebraic domain types.
   - `<<BASENAME>>Config.cs`  — configuration: constants, metadata, `FrozenDictionary` tables.
   - `<<BASENAME>>Core.cs`    — orchestration, `UnifiedOperation` wiring, no public API.
   - `<<BASENAME>>Compute.cs` — dense algorithms and helper logic.

   You are not changing any of these roles; you are only **reordering and grouping members within each file**.

────────────────────────────────────
B. Category model and ordering rules
────────────────────────────────────

You must adopt a **consistent category model** for all files, then order members within each category by **increasing vertical size (fewest lines first)**.

Definitions:

- “Vertical size” means the number of lines from the member’s declaration to its closing brace/terminator (including nested blocks and attributes).
- “Smaller” means fewer lines; ties may be broken by name (lexicographical order) to ensure deterministic ordering.

All categories below refer to members inside the same top-level type (e.g. `internal static class TopologyCore`, `public static class Spatial`, etc.).

1. **Config files: `<<BASENAME>>Config.cs`**

   Inside the config type, use this category ordering:

   1. `FrozenDictionary` fields  
      - All `FrozenDictionary<...>` fields (e.g. dispatch tables, metadata tables) at the top of the type.
      - Order them by increasing vertical size of the initializer (shortest initializer first).

   2. Other static fields (non-FrozenDictionary)  
      - Static fields or properties that are not `FrozenDictionary`.
      - Order by size.

   3. Constants (`const` fields)  
      - Group related constants together (e.g. same domain/purpose).
      - Within each group, order by size, then by name.

   4. Enums and small nested types (e.g. internal enums, small records/structs)  
      - Order by size, then by name.

   No methods should be introduced here. If there are existing methods, treat them as a separate category between static fields and constants:

   - Methods (if present), ordered by size, then name.

2. **Non-config files: `<<BASENAME>>.cs`, `<<BASENAME>>Core.cs`, `<<BASENAME>>Compute.cs`**

   Inside each type, use this category ordering:

   1. `FrozenDictionary` fields  
      - Any `FrozenDictionary<...>` fields must appear at the very top of the type (after attributes).
      - Order by initializer size.

   2. Other static fields  
      - `static readonly` fields, caches, comparers, etc.
      - Order by size.

   3. Nested types (interfaces, enums, records, structs, marker types)  
      - Group by role if it is evident (e.g. marker interfaces, small value types, domain result records, strategy records).
      - Within each group, order nested types by size, then by name.

   4. Public API methods  
      - In `<<BASENAME>>.cs`, these are the public static entrypoints.
      - Order by size, then by name.
      - Keep related overloads adjacent.

   5. Internal methods  
      - In `Core` and `Compute` files, group internal methods by logical role if evident (e.g. main operations vs helpers).
      - Within each group, order by size, then by name.

   6. Private methods  
      - Private helpers inside the type.
      - Group by role when possible.
      - Order by size, then by name.

Within each category, you must **not** split methods that are clearly related and intended to be read together (e.g. a public method followed immediately by a private helper that exists only to support that method). When this happens, treat the “cluster” as a single unit for vertical size ordering.

────────────────────────────────────
C. Multi-pass organizational procedure per file
────────────────────────────────────

For each file in each `Target Folder`, follow these passes:

1. **Pass 1 – Member inventory and classification**

   - List all members inside the top-level type:
     - Static fields (FrozenDictionary vs other).
     - Nested types (interfaces, enums, records, structs).
     - Methods (public, internal, private).
   - For each member (or tightly coupled cluster), record:
     - Category according to Section B.
     - Vertical size (approximate line count from declaration to closing brace/terminator).
   - Identify any **obvious groups**:
     - Sequential constants of same domain.
     - Families of nested types (e.g. related records).
     - Public method + dedicated private helper pairs that should remain adjacent.

2. **Pass 2 – Documentation and grouping plan (no edits yet)**

   - For constants and small nested types:
     - Identify repeated XML comments that describe essentially the same group of things.
     - Determine where a **single group-level XML comment** can replace multiple duplicate member-level comments without losing meaning.
   - For methods:
     - Do not remove method-level documentation unless it is verbatim redundant with an immediately preceding group-level doc you introduce.
   - Plan groupings:
     - For each category, define the sequence of members ordered by vertical size (smallest to largest), while keeping groupings and coupled clusters intact.

3. **Pass 3 – Reordering**

   - Within the file, rearrange members according to:
     - Category ordering (Section B).
     - Size ordering within each category (Pass 2 plan).
   - When reordering:
     - Keep attributes attached to their members.
     - Keep compiler directives (if any) with their associated members.
     - Preserve blank line structure where it reinforces grouping, but allow minor adjustments to reflect new group boundaries (e.g., a blank line between categories).

4. **Pass 4 – Documentation consolidation (non-functional edits only)**

   - For constants and nested types:
     - Where multiple members share the same conceptual description, replace repeated XML docs with a single group-level XML doc on the first member in the group.
     - Do not remove unique or semantically distinct documentation.
   - For methods:
     - Leave method-level documentation as-is unless there is a clear, verbatim duplication you can consolidate safely.
   - Do **not**:
     - Introduce new documentation that asserts behavior.  
     - Change the meaning of existing comments. You may only delete duplicates or move them to a group-level position.

5. **Pass 5 – Final consistency and safety check**

   - Confirm for each file:
     - All code remains identical aside from:
       - Member ordering.
       - XML doc / comment consolidation.
       - Harmless whitespace/blank-line adjustments necessary to reflect new groupings.
     - No method/field/type signatures or attributes have changed.
     - No executable statements were added or removed.
   - Verify that:
     - `FrozenDictionary` fields are at the top of the type in any file that has them.
     - Within each category, smaller members appear before larger ones.
     - The file still compiles under the project’s rules and analyzers (conceptually; do not introduce known violations).

────────────────────────────────────
D. Editing discipline
────────────────────────────────────

- This pass is **organizational only**:
  - Do not change behavior.
  - Do not change any public surface (names, signatures, accessibility).
  - Do not introduce or remove methods, types, or `.cs` files.
- All changes must serve one of the following purposes:
  - Consistent category-based ordering across the project.
  - Smallest-to-largest ordering within categories to maximize visible variety of members when scanning.
  - Consolidation of duplicate XML docs/comments into clean group-level documentation.
- When in doubt:
  - Prefer **no change** over a change that might affect behavior or semantics.
  - Keep tightly coupled code (e.g. a public API method with its single supporting helper) adjacent as a cluster, ordered with respect to other clusters by cluster size.

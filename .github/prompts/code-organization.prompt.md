---
version: 1.0
last_updated: 2025-11-20
category: organization
difficulty: beginner
target: libs/rhino
prerequisites:
  - CLAUDE.md
  - AGENTS.md
  - copilot-instructions.md
  - .editorconfig
  - libs/rhino/file_architecture.md
---

# Code Organization

Reorder members within Rhino module folders to achieve consistent category-based organization with size-based ordering—purely organizational, no functional changes.

## Task Description

Enforce consistent, logical structure across module folders. Reorder members by category and size, consolidate duplicate documentation, adjust whitespace for grouping clarity. No signature, behavior, or logic changes allowed.

## Inputs

- **Target Folders**: `libs/rhino/<<TARGET_FOLDER_1>>/`, `libs/rhino/<<TARGET_FOLDER_2>>/`, ...

Each folder contains 4 files:
- `<<BASENAME>>.cs`
- `<<BASENAME>>Config.cs`
- `<<BASENAME>>Core.cs`
- `<<BASENAME>>Compute.cs`

## Success Criteria

[PASS] Members reordered by consistent category structure  
[PASS] Within categories, shorter members appear before longer (fewest lines first)  
[PASS] FrozenDictionary fields at top of types (when present)  
[PASS] XML documentation consolidated where duplicate/redundant  
[PASS] No functional behavior changes whatsoever  
[PASS] No signature, attribute, accessibility, namespace changes  
[PASS] Zero new warnings, all analyzers pass

## Constraints

Follow CLAUDE.md rules. This is a non-functional task—reordering and documentation consolidation only. No signature, behavior, or logic changes allowed.

**4-File Architecture**: Understand roles of `.cs` (API), `Config.cs` (metadata), `Core.cs` (orchestration), `Compute.cs` (algorithms)

## Organizational Principles

### Purpose
Create **predictable, scannable code** where:
- Developers quickly find members by category
- Shorter members (higher variety) appear first, increasing visible density
- Related members stay grouped (e.g., overloads, tightly coupled helpers)
- Documentation consolidated for groups reduces noise

### Category Model
Consistent categories across all files with vertical size ordering within each category.

**Vertical Size**: Number of lines from member declaration to closing brace/terminator (including attributes, nested blocks).

---

## Category Ordering Rules

### For Config Files (`<<BASENAME>>Config.cs`)

**Category Order**:
1. **FrozenDictionary fields** - At top, ordered by initializer size (shortest first)
2. **Other static fields** - Non-FrozenDictionary statics, ordered by size
3. **Constants** - Group related constants, order by size then name
4. **Enums and small nested types** - Ordered by size then name
5. **Methods** (if present) - Ordered by size then name

**Rationale**: Configuration at top (FrozenDictionary is primary dispatch mechanism), followed by supporting data, then helper logic if any.

### For Other Files (`.cs`, `Core.cs`, `Compute.cs`)

**Category Order**:
1. **FrozenDictionary fields** - At very top (after attributes), ordered by initializer size
2. **Other static fields** - Caches, comparers, etc., ordered by size
3. **Nested types** - Interfaces, enums, records, structs. Group by role if evident, order by size then name
4. **Public API methods** - In `.cs` only. Ordered by size then name. Keep related overloads adjacent.
5. **Internal methods** - In `Core`/`Compute`. Group by logical role if evident, order by size then name
6. **Private methods** - Private helpers. Group by role when possible, order by size then name

**Rationale**: Data structures first (especially dispatch tables), then type definitions, then operations (public → internal → private).

### Special Cases

**Tightly Coupled Clusters**: When public method followed immediately by dedicated private helper existing only to support that method, treat as single unit for ordering purposes. Measure cluster size by combined vertical lines.

**Overload Families**: Keep adjacent even if sizes differ. Order family by size of smallest member.

## Methodology

---

### Multi-Pass Procedure (Per File)

**Pass 1: Member Inventory & Classification**
- List all members inside top-level type:
  - Static fields (FrozenDictionary vs other)
  - Nested types (interfaces, enums, records, structs)
  - Methods (public, internal, private)
- For each member or tightly coupled cluster:
  - Assign category per rules above
  - Record vertical size (line count from declaration to closing brace)
- Identify **obvious groups**:
  - Sequential constants of same domain
  - Families of related nested types (e.g., request records)
  - Public method + dedicated private helper pairs (keep adjacent)

### Pass 2: Documentation & Grouping Plan (No Edits)
**For constants and small nested types**:
- Identify repeated XML comments describing essentially same group
- Determine where **single group-level XML comment** can replace multiple duplicate member-level comments without losing meaning

**For methods**:
- Do not remove method-level documentation unless verbatim redundant with immediately preceding group-level doc

**Plan groupings**:
- For each category, define sequence ordered by vertical size (smallest to largest)
- Keep groupings and coupled clusters intact

### Pass 3: Reordering
Within file, rearrange members per:
- Category ordering (section above)
- Size ordering within category (Pass 2 plan)

**When reordering**:
- Keep attributes attached to their members
- Keep compiler directives (if any) with associated members
- Preserve blank line structure reinforcing grouping
- Allow minor adjustments to reflect new group boundaries

### Pass 4: Documentation Consolidation (Non-Functional Only)
**For constants and nested types**:
- Where multiple members share same conceptual description, replace repeated XML docs with single group-level XML doc on first member
- Do not remove unique or semantically distinct documentation

**For methods**:
- Leave method-level documentation as-is unless clear verbatim duplication can be consolidated safely

**Do NOT**:
- Introduce new documentation asserting behavior
- Change meaning of existing comments—only delete duplicates or move to group-level

**Pass 5: Final Consistency & Safety Check**
**Confirm for each file**:
- All code identical aside from:
  - Member ordering
  - XML doc / comment consolidation
  - Harmless whitespace/blank-line adjustments for groupings
- No method/field/type signatures or attributes changed
- No executable statements added or removed

**Verify**:
- FrozenDictionary fields at top of type (if present)
- Within each category, smaller members before larger
- File still compiles under project rules and analyzers

## Verification

After organization:
- Members reordered by category and size
- FrozenDictionary fields at top
- XML docs consolidated where duplicate
- No functional changes
- Zero new warnings

---

## Editing Discipline

[PASS] **Do**:
- Reorder members within types to match category model
- Consolidate duplicate XML docs/comments into clean group-level documentation
- Adjust whitespace/blank lines to reinforce new groupings
- Keep tightly coupled code (API method + dedicated helper) adjacent as cluster

[FAIL] **Don't**:
- Change any signatures, attributes, accessibility, namespaces, using directives
- Add or remove executable statements
- Introduce new methods, types, or `.cs` files
- Change behavior in any way
- Remove unique/meaningful documentation
- Split tightly coupled method clusters just for size ordering

---

## Size Ordering Rationale

**Why shorter members first?**

1. **Visual Density**: Smaller members let developers see more variety when scanning
2. **Progressive Detail**: Overview before deep dive—constants/small types before complex methods
3. **Predictability**: Consistent rule across all files/folders
4. **Refactoring Signal**: If many large members cluster at bottom, signals potential for better algorithm

**Why not alphabetical?**

- Alphabetical ignores semantic importance and structural patterns
- Size ordering naturally groups similar complexity levels
- Combined with categories, provides both semantic (category) and complexity (size) organization

---

## Anti-Patterns to Avoid

1. **Breaking Clusters**: Separating tightly coupled method + helper just for size ordering
2. **Fragmented Overloads**: Scattering overload family members far apart
3. **Documentation Spam**: Keeping verbose redundant XML comments on every constant
4. **Blind Sorting**: Mechanically sorting without considering semantic groupings
5. **Inconsistent Application**: Organizing some files but not others in same folder
6. **Accidental Behavior Change**: Moving code between contexts where semantics differ (e.g., static field initialization order)
7. **Over-Engineering Grouping**: Creating too many micro-categories that fragment code

---

## Edge Cases & Considerations

### Static Field Initialization Order
**Critical**: C# static field initializers execute in declaration order. When reordering static fields:
- Verify no field depends on another declared later
- If dependency exists, respect initialization order over size ordering
- Document any intentional exception to size ordering

### Nested Type Dependencies
If nested type A references nested type B:
- Keep dependency order even if size ordering would swap them
- Semantic correctness always trumps size ordering

### Partial Classes
If file contains partial class (rare in this codebase):
- Organize each partial separately
- Maintain consistency across all partials for same type

### Preprocessor Directives
If `#if DEBUG` or similar directives present:
- Keep directive-controlled members with their controlling directive
- Organize within directive-controlled sections separately
- Don't split directive blocks for size ordering

---

## Visual Scanning Benefits (Example)

**Before Organization** (random order):
```csharp
// 150 LOC method
// 200 LOC method
// 50 LOC method
// 3 LOC constant
// 180 LOC method
// 5 LOC constant
```
→ Developer sees 2-3 large methods when opening file, must scroll to find constants

**After Organization** (size-ordered):
```csharp
// Category: Constants
// 3 LOC constant
// 5 LOC constant

// Category: Methods
// 50 LOC method
// 150 LOC method
// 180 LOC method
// 200 LOC method
```
→ Developer immediately sees constants, can quickly scan small methods, large methods grouped at bottom for deep work

---
name: refactor
description: Refactor code for density and architectural improvements
---

Refactor the specified code using the refactoring-architect agent approach.

## ANALYSIS PHASE

1. **Scan current state**:
   - Count files per folder (max 4)
   - Count types per folder (max 10)
   - Identify methods >300 LOC
   - Find if/else, var usage
   - Locate repeated patterns

2. **Identify opportunities**:
   - Multiple similar methods → One generic parameterized method
   - Repeated type switching → FrozenDictionary dispatch table
   - Loose helper methods → Consolidate into dense operations
   - Manual validation → Move to ValidationRules
   - Exception handling → Convert to Result<T>

## REFACTORING RULES

**MUST reduce complexity:**
- Fewer files, not more
- Fewer types, not more
- Shorter methods through better algorithms
- Never extract helpers - improve inline

**NEVER:**
- Extract helper methods
- Add abstraction layers without clear benefit
- Split dense algorithms into procedural steps
- Increase file/type counts

## VERIFICATION

Before completing:
- [ ] File count reduced or unchanged
- [ ] Type count reduced or unchanged
- [ ] All members ≤300 LOC
- [ ] No var, no if/else
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes

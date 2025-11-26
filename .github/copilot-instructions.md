# GitHub Copilot Instructions for Parametric Arsenal

**Context**: .NET 8.0 monorepo with C# libraries for Rhino 8/Grasshopper parametric design + Python scripting. Extremely strict code quality standards enforced by analyzers.

**Primary Reference**: For detailed patterns and examples, see `/CLAUDE.md`

---

## [BLOCKERS] IMMEDIATE BLOCKERS (Fix Before Proceeding)

These violations fail the build. Check for and fix immediately:

1. **var x = new RTree();** → Replace with explicit type
2. **if (x > 0) { return y; } else { return z; }** → Replace with ternary (binary), switch expression (multiple), or pattern matching (type discrimination)
3. **[item1, item2]** → Add `,` at end
4. **ResultFactory.Create(error)** → Add `parameter: value`
5. **Dictionary<string, int> dict = new Dictionary<string, int>();** → Use target-typed `new()`
6. **new List<int> { 1, 2, 3 }** → Use collection expressions `[]`
7. **namespace X { ... }** → Use file-scoped namespace declarations
8. **Folder has >4 files** → Consolidate into 2-3 files
9. **Folder has >10 types** → Consolidate into 6-8 types
10. **Member has >300 LOC** → Improve algorithm, don't extract helpers



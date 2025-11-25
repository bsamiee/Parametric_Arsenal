---
name: implement
description: Implement a feature using the appropriate specialist agent
---

Implement the requested feature following CLAUDE.md standards.

## WORKFLOW

1. **Analyze the request** - Determine which domain (core, rhino, grasshopper, test)
2. **Select appropriate agent** based on task:
   - New geometry operation → Use `rhino-implementation` agent
   - New core functionality → Use `csharp-advanced` agent
   - Tests needed → Use `testing-specialist` agent
   - Grasshopper component → Use `grasshopper-implementation` agent

3. **Before coding**:
   - Read CLAUDE.md completely
   - Study exemplar files in libs/core/
   - Check folder limits (4 files, 10 types max)

4. **Implementation requirements**:
   - NO var - explicit types only
   - NO if/else - use ternary, switch expressions, pattern matching
   - Named parameters for non-obvious args
   - Trailing commas on multi-line collections
   - Result<T> for all failable operations
   - UnifiedOperation for polymorphic dispatch
   - E.* errors from libs/core/errors/

5. **Verification**:
   - Run `dotnet build` - must pass with zero warnings
   - Run `dotnet test` - all tests must pass
   - Verify folder limits not exceeded

## OUTPUT

Provide:
1. Files created/modified with full paths
2. Summary of implementation approach
3. Build/test verification results

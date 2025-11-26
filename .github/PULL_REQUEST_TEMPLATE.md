## Summary
<!-- Provide a brief description of the changes in this PR -->

## Related Issue
<!-- Link to the issue this PR addresses -->
Closes #

## Agent Metadata
<!-- DO NOT EDIT: Auto-populated by Claude workflows -->
<!-- AGENT_REVIEW_CONFIG
{
  "auto_merge_eligible": false,
  "required_reviewers": 0,
  "skip_checks": [],
  "max_autofix_iterations": 3
}
-->

<!-- ISSUE_METADATA
{
  "scope": "",
  "complexity": "",
  "agent": "",
  "context_files": [],
  "validation_mode": ""
}
-->

## Change Type
<!-- Check all that apply -->
- [ ] Feature (new functionality)
- [ ] Bug fix (non-breaking change fixing an issue)
- [ ] Refactor (code restructuring without behavior change)
- [ ] Performance improvement
- [ ] Documentation update
- [ ] Test coverage improvement
- [ ] Breaking change (fix or feature causing existing functionality to break)

## Verification Checklist

### Build & Test
- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` passes (all tests)
- [ ] `dotnet format --verify-no-changes` passes
- [ ] EditorConfig compliance verified
- [ ] New functionality has unit tests
- [ ] Edge cases covered (null, empty, degenerate inputs)

### CLAUDE.md Compliance
<!-- Critical rules enforced by analyzers -->
- [ ] NO `var` - explicit types used throughout
- [ ] NO `if`/`else` statements - ternary, switch expressions, or pattern matching used
- [ ] Named parameters used for all non-obvious arguments
- [ ] Trailing commas on all multi-line collections/arrays
- [ ] K&R brace style (opening brace on same line)
- [ ] File-scoped namespaces (`namespace X;`)
- [ ] Target-typed `new()` used (not `new Type()`)
- [ ] Collection expressions `[]` used (not `new List<>()`)
- [ ] One type per file (CA1050)
- [ ] Primary constructors used where appropriate

### Architecture
- [ ] `Result<T>` used for all failable operations (no exceptions)
- [ ] `UnifiedOperation` used for polymorphic dispatch
- [ ] All errors use `E.*` error registry (libs/core/errors/E.cs)
- [ ] Validation uses `V.*` flags (libs/core/validation)

### Organization
- [ ] No folder exceeds 4 files
- [ ] No folder exceeds 10 types
- [ ] No member (method/property) exceeds 300 LOC

## Test Plan
<!-- Describe how this change was tested -->

### Unit Tests
<!-- List new or modified unit tests -->
- [ ] Test file: `test/.../...Tests.cs`
- [ ] Coverage: >90% for new code
- [ ] Property-based tests with CsCheck (if applicable)

### Integration Tests
<!-- For RhinoCommon/Grasshopper features -->
- [ ] Rhino.Testing framework used
- [ ] Real geometry tested (not mocks)
- [ ] Headless Rhino testing verified

### Manual Testing
<!-- Steps taken to manually verify the changes -->
1. 
2. 
3. 

## Performance Considerations
<!-- Optional: For performance-sensitive changes -->
- [ ] Benchmarks added with BenchmarkDotNet
- [ ] No performance regressions measured
- [ ] Memory allocations minimized
- [ ] Hot path optimizations applied (if applicable)

## Documentation
<!-- Check all that apply -->
- [ ] XML comments added for public APIs
- [ ] README.md updated (if applicable)
- [ ] BLUEPRINT.md created/updated (for new features)
- [ ] Examples added (if applicable)
- [ ] Migration guide provided (if breaking change)

## Breaking Changes
<!-- If this is a breaking change, describe the impact and migration path -->

**Impact**:
<!-- What existing code will break? -->

**Migration Path**:
<!-- How should users update their code? -->

## Additional Notes
<!-- Any additional context, screenshots, or information -->

---

## For Reviewers
<!-- Automatically populated by claude-code-review workflow -->

**Review Status**: Pending

**Automated Checks**:
- [ ] Build passed
- [ ] Tests passed
- [ ] Code review completed
- [ ] No analyzer warnings

**Auto-Fix Attempts**: 0/3

# Pre-commit Hook Examples

This document shows real examples of how pre-commit hooks work in practice.

## Example 1: Successful Commit

When everything passes:

```bash
$ git add MyFile.cs
$ git commit -m "feat: add new geometry operation"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Passed

[main a1b2c3d] feat: add new geometry operation
 1 file changed, 25 insertions(+)
```

✅ **Result**: Commit succeeds

## Example 2: Build Failure

When code has errors:

```bash
$ git add MyFile.cs
$ git commit -m "feat: add feature"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Failed
- hook id: dotnet-build-analyzers
- exit code: 1

/home/user/Parametric_Arsenal/libs/core/MyFile.cs(42,10): error CS1002: ; expected [/home/user/Parametric_Arsenal/libs/core/Core.csproj]
/home/user/Parametric_Arsenal/libs/core/MyFile.cs(42,11): error CS1519: Invalid token '=' in class, record, struct, or interface member declaration [/home/user/Parametric_Arsenal/libs/core/Core.csproj]

Build FAILED.
```

❌ **Result**: Commit blocked. Fix the errors and try again.

## Example 3: Formatting Issues

When code isn't formatted correctly:

```bash
$ git add MyFile.cs
$ git commit -m "feat: add feature"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Failed
- hook id: dotnet-format-verify
- exit code: 1

/home/user/Parametric_Arsenal/libs/core/MyFile.cs(15,1): warning WHITESPACE: Fix whitespace formatting. Replace 4 characters with '\t'. [/home/user/Parametric_Arsenal/libs/core/Core.csproj]
```

❌ **Result**: Commit blocked. Format the code:

```bash
$ dotnet format Parametric_Arsenal.sln
$ git add .
$ git commit -m "feat: add feature"
# Now it passes!
```

## Example 4: Analyzer Violations

When code violates analyzer rules:

```bash
$ git add MyFile.cs
$ git commit -m "feat: add feature"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Failed
- hook id: dotnet-build-analyzers
- exit code: 1

/home/user/Parametric_Arsenal/libs/core/MyFile.cs(10,16): error IDE0290: Use primary constructor [/home/user/Parametric_Arsenal/libs/core/Core.csproj]
/home/user/Parametric_Arsenal/libs/core/MyFile.cs(23,9): error MA0051: Method 'ProcessData' is too long (75 lines; maximum allowed: 60) [/home/user/Parametric_Arsenal/libs/core/Core.csproj]
```

❌ **Result**: Commit blocked. Fix the analyzer violations:
- Use primary constructors as required
- Refactor long methods (max 60 lines)

## Example 5: Python Linting (with ruff installed)

When Python code has linting issues:

```bash
$ git add my_script.py
$ git commit -m "feat: add script"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
Ruff lint Python files...................................................Failed
- hook id: ruff-check
- exit code: 1
- files were modified by this hook

my_script.py:10:1: F401 [*] `numpy` imported but unused
my_script.py:23:9: E501 Line too long (125 > 120 characters)
Found 2 errors (1 fixed, 1 remaining).
```

⚠️ **Result**: Some issues auto-fixed, others need manual fix. Stage the changes and try again:

```bash
$ git add my_script.py  # Add auto-fixed changes
# Fix remaining issues manually
$ git commit -m "feat: add script"
# Now it passes!
```

## Example 6: Large File Detection

When trying to commit a large file:

```bash
$ git add huge-model.3dm
$ git commit -m "feat: add model"

Check for merge conflicts................................................Passed
Check for large files....................................................Failed
- hook id: check-large-files
- exit code: 1

File huge-model.3dm is too large (2048000 bytes > 500KB)
```

❌ **Result**: Commit blocked. Large files should use Git LFS or be excluded.

Solution:
```bash
# Use Git LFS (already configured for .3dm files)
git lfs track "*.3dm"
git add .gitattributes
git add huge-model.3dm
git commit -m "feat: add model"
```

## Example 7: Merge Conflict Markers

When accidentally trying to commit merge conflicts:

```bash
$ git add MyFile.cs
$ git commit -m "fix: resolve conflict"

Check for merge conflicts................................................Failed
- hook id: check-merge-conflict
- exit code: 1

<<<<<<< HEAD
Merge conflict markers found
```

❌ **Result**: Commit blocked. Resolve the merge conflicts first:

```bash
# Edit MyFile.cs and resolve conflicts
$ git add MyFile.cs
$ git commit -m "fix: resolve conflict"
# Now it passes!
```

## Example 8: Bypassing Hooks (Emergency)

When you absolutely must commit without checks:

```bash
$ git commit --no-verify -m "fix: emergency hotfix"

[main a1b2c3d] fix: emergency hotfix
 1 file changed, 5 insertions(+)
```

✅ **Result**: Commit succeeds without running hooks

⚠️ **Warning**: Use `--no-verify` sparingly! It defeats the purpose of quality checks.

## Example 9: Running Hooks Manually

Check specific files before committing:

```bash
$ pre-commit run --files libs/core/MyFile.cs

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Passed
```

Check all files in repository:

```bash
$ pre-commit run --all-files

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Passed
Ruff lint Python files...................................................Passed
Ruff format Python files.................................................Passed
```

## Example 10: Mixed C# and Python Changes

When committing both C# and Python files:

```bash
$ git add libs/core/MyFile.cs rhino/plugins/my_script.py
$ git commit -m "feat: add feature with Python script"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Passed
Ruff lint Python files...................................................Passed
Ruff format Python files.................................................Passed

[main a1b2c3d] feat: add feature with Python script
 2 files changed, 75 insertions(+)
```

✅ **Result**: Both .NET and Python checks run automatically

## Tips for Success

1. **Commit early and often** - Smaller commits pass hooks faster
2. **Run `dotnet format` before committing** - Avoids format check failures
3. **Build locally first** - `dotnet build` before committing
4. **Use `pre-commit run` manually** - Check before committing
5. **Keep codebase clean** - Don't leave broken code in other files

## Common Questions

**Q: Why does my commit fail when I only changed one file?**  
A: Pre-commit runs a full solution build. If there are errors anywhere in the codebase, the commit is blocked.

**Q: Can I skip the .NET build check?**  
A: Not without modifying `.pre-commit-config.yaml`. The build check ensures code quality.

**Q: Why are Python checks skipped?**  
A: Python checks only run if `ruff` is installed. Install with `pip install ruff` or use the setup scripts.

**Q: How do I update the hooks?**  
A: Run `pre-commit autoupdate` to get the latest versions.

**Q: Can I customize the hooks?**  
A: Yes, edit `.pre-commit-config.yaml` but discuss with the team first.

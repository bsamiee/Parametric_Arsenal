# Pre-commit Quick Start Guide

## 5-Minute Setup

### 1. Install pre-commit

```bash
pip install pre-commit
```

### 2. Install hooks in your repository

```bash
cd /path/to/Parametric_Arsenal
pre-commit install
```

### 3. Done!

From now on, every `git commit` will automatically run:
- ✅ .NET build with all analyzers (Roslynator, Meziantou, IDisposableAnalyzers)
- ✅ .editorconfig enforcement
- ✅ `dotnet format` verification
- ✅ Python linting with ruff (if installed)
- ✅ File quality checks

## What Happens on Commit?

```bash
$ git commit -m "feat: add new feature"

Check for merge conflicts................................................Passed
Check for large files....................................................Passed
.NET Restore packages....................................................Passed
.NET Build with analyzers................................................Passed
.NET Format verification.................................................Passed
Ruff lint Python files...................................................Passed
Ruff format Python files.................................................Passed

[main a1b2c3d] feat: add new feature
 2 files changed, 50 insertions(+)
```

## If a Check Fails

The commit will be **blocked** and you'll see the error:

```bash
$ git commit -m "feat: add new feature"

.NET Build with analyzers................................................Failed
- hook id: dotnet-build-analyzers
- exit code: 1

/path/to/file.cs(42,10): error CS1002: ; expected
```

Fix the issue and try again:

```bash
# Fix the error
$ git add .
$ git commit -m "feat: add new feature"
# Now it passes!
```

## Bypass Hooks (Emergency Only)

If you absolutely must commit without running hooks:

```bash
git commit --no-verify -m "message"
```

⚠️ **Use sparingly** - this defeats the purpose of quality checks!

## Common Commands

| Command | Purpose |
|---------|---------|
| `pre-commit run` | Run hooks on staged files |
| `pre-commit run --all-files` | Run hooks on all files |
| `pre-commit autoupdate` | Update hooks to latest versions |
| `pre-commit uninstall` | Remove hooks |
| `git commit --no-verify` | Skip hooks for one commit |

## Understanding the Checks

### .NET Build with Analyzers

This hook:
- Runs `dotnet build` on the **entire solution**
- Enforces all `.editorconfig` rules
- Runs Roslynator, Meziantou, and IDisposableAnalyzers
- Treats warnings as errors

**Important**: Even if you only change one file, the entire solution is built. This ensures the codebase remains in a buildable state.

### .NET Format Verification

This hook:
- Runs `dotnet format --verify-no-changes`
- Checks that code follows `.editorconfig` formatting rules
- Does NOT automatically format your code
- Tells you which files need formatting

To format files:
```bash
dotnet format Parametric_Arsenal.sln
```

### Python Linting (ruff)

If `ruff` is installed:
- Automatically fixes linting issues
- Formats Python code
- Enforces rules from `pyproject.toml`

If `ruff` is not installed, these checks are skipped.

## Troubleshooting

### "Hook failed" but I didn't change those files

Pre-commit runs a **full solution build**. If there are existing errors elsewhere in the codebase, your commit will be blocked.

Options:
1. Fix the existing errors (recommended)
2. Use `--no-verify` to bypass (not recommended)
3. Ask the team to fix the broken files

### Hooks are slow

First run takes longer (establishes caches). Subsequent runs are much faster.

To speed up:
- Only commit related files together
- Use `git add <specific-files>` instead of `git add .`

### I want to run hooks manually

```bash
# On staged files
pre-commit run

# On all files
pre-commit run --all-files

# On specific files
pre-commit run --files src/MyFile.cs
```

### I want to skip Python checks

Python checks are automatically skipped if `ruff` is not installed.

### I want to skip .NET checks

You can't selectively skip checks without modifying `.pre-commit-config.yaml`. The .NET checks are essential for code quality.

## Best Practices

1. ✅ **Install hooks immediately** after cloning
2. ✅ **Run `pre-commit run --all-files`** before submitting PR
3. ✅ **Fix issues** rather than bypassing hooks
4. ✅ **Keep commits focused** - easier to pass checks
5. ❌ **Don't use `--no-verify`** unless absolutely necessary

## Integration with CI

Pre-commit hooks mirror the CI pipeline. If hooks pass locally, CI should pass too.

Local (pre-commit) → Remote (GitHub Actions CI) → Same checks!

## More Information

See [Pre-commit Hooks Guide](PRE-COMMIT.md) for detailed information.

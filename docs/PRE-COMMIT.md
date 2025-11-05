# Pre-commit Hooks Setup Guide

This document explains how to set up and use pre-commit hooks in the Parametric Arsenal repository. Pre-commit hooks automatically validate your code before it's committed, ensuring all code meets quality standards.

## What Gets Checked?

The pre-commit hooks enforce:

### .NET/C# Checks
- **Analyzers**: Roslynator, Meziantou, IDisposableAnalyzers (from `Directory.Build.props`)
- **Code Style**: All `.editorconfig` rules enforced during build
- **Formatting**: `dotnet format` verification
- **Build Validation**: Full Release build with warnings as errors

### Python Checks
- **Linting**: Ruff linter with auto-fix
- **Formatting**: Ruff formatter
- **Type Checking**: mypy and basedpyright validation

### General File Checks
- Trailing whitespace removal
- End-of-file newline enforcement
- YAML/JSON/TOML validation
- Large file prevention (>500KB)
- Merge conflict detection
- Consistent line endings (LF)

## Installation

### Automatic Setup (Recommended)

#### Linux/macOS
```bash
./scripts/setup-precommit.sh
```

#### Windows
```powershell
.\scripts\setup-precommit.ps1
```

### Manual Setup

1. **Install pre-commit**:
   ```bash
   pip install pre-commit
   ```

2. **Install hooks**:
   ```bash
   pre-commit install
   ```

3. **Test installation** (optional):
   ```bash
   pre-commit run --all-files
   ```

## Usage

### Automatic Mode (Default)

Once installed, hooks run automatically on `git commit`. If any check fails, the commit is blocked:

```bash
$ git commit -m "feat: add new feature"
# Pre-commit hooks run automatically
# If they pass, commit succeeds
# If they fail, commit is blocked with error messages
```

### Manual Execution

Run hooks on staged files:
```bash
pre-commit run
```

Run hooks on all files:
```bash
pre-commit run --all-files
```

Run a specific hook:
```bash
pre-commit run dotnet-build-analyzers
```

### Skipping Hooks (Use Sparingly)

To skip hooks for a single commit (not recommended):
```bash
git commit --no-verify -m "message"
```

## Hook Details

### .NET Hooks

#### 1. `dotnet-restore`
Restores NuGet packages for the solution.
- **Triggers on**: `.cs`, `.csproj`, `.sln`, `.props` files
- **Command**: `dotnet restore Parametric_Arsenal.sln`

#### 2. `dotnet-build-analyzers`
Builds the solution with all analyzers enabled and treats warnings as errors.
- **Triggers on**: `.cs`, `.csproj` files
- **Command**: `dotnet build --configuration Release -p:EnforceCodeStyleInBuild=true -p:TreatWarningsAsErrors=true`
- **Validates**:
  - Roslynator analyzer rules
  - Meziantou analyzer rules
  - IDisposableAnalyzers rules
  - All `.editorconfig` settings

#### 3. `dotnet-format-verify`
Verifies code formatting without making changes.
- **Triggers on**: `.cs` files
- **Command**: `dotnet format --verify-no-changes`
- **Validates**: Code formatting matches `.editorconfig` rules

### Python Hooks

#### 4. `ruff` (linting)
Runs Ruff linter with auto-fix enabled.
- **Triggers on**: `.py` files
- **Command**: `ruff --fix --exit-non-zero-on-fix`
- **Config**: Uses `pyproject.toml` configuration

#### 5. `ruff-format`
Formats Python code according to Ruff standards.
- **Triggers on**: `.py` files
- **Command**: `ruff format`
- **Config**: Uses `pyproject.toml` configuration

#### 6. `mypy`
Type checks Python code with mypy.
- **Triggers on**: `.py` files
- **Command**: `mypy --config-file=pyproject.toml`
- **Config**: Uses strict settings with Rhino interop exceptions

#### 7. `pyright` (basedpyright)
Type checks Python code with basedpyright.
- **Triggers on**: `.py` files
- **Command**: `pyright --project=pyproject.toml`
- **Config**: Uses strict mode with Rhino-specific overrides

## Troubleshooting

### Hook Installation Failed
```bash
# Reinstall hooks
pre-commit uninstall
pre-commit install
```

### Hook Running Too Slow
Pre-commit caches results. To clear cache:
```bash
pre-commit clean
```

### Update Hooks to Latest Versions
```bash
pre-commit autoupdate
```

### .NET Build Fails
Ensure all packages are restored:
```bash
dotnet restore Parametric_Arsenal.sln
dotnet build Parametric_Arsenal.sln
```

### Python Type Checking Fails
Ensure Python dependencies are installed:
```bash
# Using uv (recommended)
uv sync

# Or using pip
pip install -e .
```

## CI/CD Integration

Pre-commit hooks mirror the CI pipeline checks defined in `.github/workflows/ci.yml`. This means:

✅ **If pre-commit passes locally, CI should pass too**

The same checks run in both places:
- Local: Pre-commit hooks
- CI: GitHub Actions workflow

## Configuration Files

- **Pre-commit config**: `.pre-commit-config.yaml`
- **.NET analyzers**: `Directory.Build.props`
- **Code style**: `.editorconfig`
- **Python tools**: `pyproject.toml`

## Best Practices

1. **Install hooks immediately** after cloning the repository
2. **Run `pre-commit run --all-files`** periodically to catch issues early
3. **Don't skip hooks** unless absolutely necessary
4. **Fix issues** rather than working around them
5. **Update hooks monthly**: `pre-commit autoupdate`

## Performance Tips

Pre-commit hooks are designed to run quickly:

- **Incremental checks**: Only staged files are checked (unless using `--all-files`)
- **Parallel execution**: Hooks run in parallel when possible
- **Smart caching**: Results are cached between runs
- **.NET incremental build**: Only changed files are recompiled

Expected timing for typical commits:
- **Python files only**: ~2-5 seconds
- **C# files only**: ~5-15 seconds
- **Mixed changes**: ~10-20 seconds
- **First run**: ~30-60 seconds (establishes caches)

## Additional Resources

- [Pre-commit documentation](https://pre-commit.com/)
- [Ruff documentation](https://docs.astral.sh/ruff/)
- [.NET format documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- Repository-specific standards: `CLAUDE.md`

# Pre-commit Hooks FAQ

Common questions and answers about pre-commit hooks in Parametric Arsenal.

## Installation & Setup

### Q: Do I need to install pre-commit?

**A:** Yes. Pre-commit is a Python tool that must be installed:

```bash
pip install pre-commit
```

### Q: Is there a setup script?

**A:** Yes! Use the automated scripts:

```bash
# Linux/macOS
./scripts/setup-precommit.sh

# Windows
.\scripts\setup-precommit.ps1
```

### Q: Do I need to install hooks after cloning?

**A:** Yes. After cloning the repository:

```bash
pre-commit install
```

This creates the git hooks in `.git/hooks/`.

### Q: Can I use pre-commit with other git clients (GitKraken, SourceTree, etc.)?

**A:** Yes. Pre-commit hooks work with any git client because they're standard git hooks.

## How It Works

### Q: When do the hooks run?

**A:** Automatically on `git commit`. They run BEFORE the commit is created.

### Q: What files are checked?

**A:** Only **staged** files (files you've `git add`ed), unless you run `pre-commit run --all-files`.

### Q: Do hooks run on `git push`?

**A:** No. Hooks only run on `git commit`. However, CI runs the same checks on push.

### Q: Why does the full solution build when I only changed one file?

**A:** To ensure the entire codebase remains buildable. Changing one file could break another file that depends on it.

## Failures & Errors

### Q: My commit failed but I didn't change those files. Why?

**A:** Pre-commit runs a full solution build. If there are pre-existing errors elsewhere, your commit will be blocked.

**Solutions:**
1. Fix the pre-existing errors (recommended)
2. Ask team to fix broken files
3. Use `git commit --no-verify` (not recommended)

### Q: How do I fix .NET format failures?

**A:** Run the formatter:

```bash
dotnet format Parametric_Arsenal.sln
git add .
git commit -m "your message"
```

### Q: How do I fix analyzer errors?

**A:** Analyzer errors are real code quality issues. Fix them according to the error message:

- **IDE0290**: Use primary constructors
- **MA0051**: Method too long (max 60 lines)
- **RCS1032**: Add missing parentheses
- etc.

See `.editorconfig` for all rules.

### Q: Can I ignore analyzer warnings?

**A:** No. Pre-commit treats warnings as errors (`-p:TreatWarningsAsErrors=true`). This ensures high code quality.

### Q: What if I have a valid reason to suppress an analyzer rule?

**A:** Add a suppression to the specific line:

```csharp
#pragma warning disable MA0051
public void MyMethod() {
    // Long method with valid reason
}
#pragma warning restore MA0051
```

Or update `.editorconfig` if it's a project-wide decision.

## Performance

### Q: Why is the first commit so slow?

**A:** First run:
- Restores NuGet packages
- Builds solution
- Establishes caches

Subsequent commits are much faster (5-15 seconds).

### Q: How can I speed up commits?

**A:** 
1. Commit smaller changesets
2. Use incremental builds (don't `dotnet clean` frequently)
3. Keep solution buildable (avoid breaking changes)

### Q: Can I run checks in parallel?

**A:** Pre-commit already runs independent checks in parallel automatically.

## Skipping & Bypassing

### Q: How do I skip hooks for one commit?

**A:** Use the `--no-verify` flag:

```bash
git commit --no-verify -m "message"
```

### Q: When should I skip hooks?

**A:** Very rarely. Valid reasons:
- Emergency hotfix
- Reverting a broken commit
- Committing WIP on a feature branch (not recommended)

### Q: Can I disable hooks permanently?

**A:** Yes, but not recommended:

```bash
pre-commit uninstall
```

To re-enable:

```bash
pre-commit install
```

### Q: Can I disable specific hooks?

**A:** Edit `.pre-commit-config.yaml` and comment out the hook, but discuss with team first.

## Python-Specific

### Q: Why are Python checks skipped?

**A:** Python checks (ruff) only run if ruff is installed:

```bash
pip install ruff
# or
pip install -r requirements.txt  # if one exists
```

### Q: Do I need Python installed for C# commits?

**A:** Yes, pre-commit itself is a Python tool. But ruff (Python linting) is optional.

### Q: What Python version do I need?

**A:** Python 3.9+ (though pre-commit works with 3.8+)

## .NET-Specific

### Q: Do I need .NET SDK installed?

**A:** Yes. .NET 8 SDK is required. Download from https://dotnet.microsoft.com/

### Q: What if I'm on macOS/Linux?

**A:** .NET SDK works on all platforms. Pre-commit hooks work identically on Windows, macOS, and Linux.

### Q: Can I use different .NET versions?

**A:** This project requires .NET 8. Using a different version may cause issues.

## Git LFS

### Q: Do hooks check Git LFS files?

**A:** The large file check (>500KB) runs, but .3dm, .gh, and other binary files should use Git LFS (already configured).

### Q: What if I accidentally commit a large file?

**A:** The hook will block the commit. Use Git LFS:

```bash
git lfs track "*.3dm"
git add .gitattributes
git add your-file.3dm
git commit -m "message"
```

## CI/CD Integration

### Q: Are pre-commit checks the same as CI checks?

**A:** Yes! The same checks run locally (pre-commit) and in CI (GitHub Actions).

### Q: What if pre-commit passes but CI fails?

**A:** This shouldn't happen. If it does:
1. Check if CI has additional checks
2. Verify you ran hooks on all changed files
3. Report the discrepancy to the team

### Q: Can I skip CI checks?

**A:** No. CI checks are mandatory and cannot be skipped.

## Troubleshooting

### Q: Hooks aren't running at all

**A:** Check installation:

```bash
pre-commit --version  # Should show version
ls -la .git/hooks/    # Should see pre-commit file
```

Reinstall if needed:

```bash
pre-commit install
```

### Q: "command not found: pre-commit"

**A:** Pre-commit isn't installed or not in PATH:

```bash
pip install --user pre-commit
# or
pip install pre-commit
```

### Q: Hooks fail with "dotnet: command not found"

**A:** .NET SDK isn't installed or not in PATH. Install from https://dotnet.microsoft.com/

### Q: Hooks fail with network timeout errors

**A:** This shouldn't happen with the local hooks configuration. If it does, check your internet connection or firewall.

### Q: "fatal: cannot run .git/hooks/pre-commit: No such file or directory"

**A:** Hooks aren't installed. Run:

```bash
pre-commit install
```

### Q: Hooks run forever and never complete

**A:** Check for infinite loops or very large files. Use Ctrl+C to cancel and investigate.

## Advanced

### Q: Can I run hooks without committing?

**A:** Yes:

```bash
pre-commit run              # On staged files
pre-commit run --all-files  # On all files
```

### Q: Can I run a specific hook?

**A:** Yes:

```bash
pre-commit run dotnet-build-analyzers
pre-commit run ruff-check
```

### Q: Can I update hooks automatically?

**A:** Yes:

```bash
pre-commit autoupdate
```

This updates hook versions in `.pre-commit-config.yaml`.

### Q: Can I customize hooks for my workflow?

**A:** Edit `.pre-commit-config.yaml`, but coordinate with the team to avoid conflicts.

### Q: Where are hook configurations stored?

**A:** In `.pre-commit-config.yaml` at the repository root.

## Getting Help

### Q: Where can I find more documentation?

**A:** See:
- [Quick Start Guide](PRE-COMMIT-QUICKSTART.md) - 5-minute setup
- [Complete Guide](PRE-COMMIT.md) - Detailed documentation
- [Examples](PRE-COMMIT-EXAMPLES.md) - Real-world scenarios

### Q: Who do I ask if I have issues?

**A:** 
1. Check this FAQ
2. Check other documentation files
3. Ask in team chat/discussion
4. Check [pre-commit documentation](https://pre-commit.com/)

### Q: Can I contribute improvements to the hooks?

**A:** Yes! Submit a PR with changes to `.pre-commit-config.yaml` and documentation.

## Best Practices

### Q: Should I commit often or batch changes?

**A:** Commit often. Smaller commits:
- Pass checks faster
- Easier to debug if they fail
- Better git history

### Q: Should I run hooks manually before committing?

**A:** Optional but recommended for large changes:

```bash
pre-commit run --all-files
```

### Q: Should everyone on the team use pre-commit?

**A:** Yes! Consistency across the team ensures code quality.

### Q: What if someone commits without hooks?

**A:** CI will catch the issues. But it's better to catch them locally.

---

**Still have questions?** Check the [pre-commit documentation](https://pre-commit.com/) or ask the team!

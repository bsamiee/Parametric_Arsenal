# Repository Guidelines

## Project Structure & Module Organization
Core algebraic logic lives in `libs/core`; Rhino and Grasshopper adapters sit in `libs/rhino` and `libs/grasshopper`. Packaged plugins and definitions are versioned under `rhino/plugins` and `grasshopper/definitions`; large binaries stay in Git LFS. Shared fixtures live in `test/shared`; xUnit suites in `test/core`, Rhino NUnit suites in `test/rhino`. Build outputs land in `artifacts/`.

## Environment & Tooling
Install the .NET 8 SDK and Python 3.9+; `uv sync` pulls the locked toolchain from `pyproject.toml`. Run `pre-commit install` so restore/build/format hooks guard every commit. Rhino automation expects Rhino 8 plus `RHINO_EMAIL` and `RHINO_TOKEN` secrets (see `.github/workflows/rhino-tests.yml`).

## Design Principles & Code Density
Study `CLAUDE.md` before editing. Code stays algorithmic, parameterized, polymorphic, and strongly typed—compose existing types rather than spawning helpers. Rely on UnifiedOperation dispatch and the Result monad instead of ad-hoc branching. **Never** use `if/else`; apply tuple patterns, guarded `switch` expressions, or strategy dictionaries. Keep implementations compact (≤300 LOC), reuse `libs/` primitives for effects and validation, and chase zero allocations with spans, caching, and pooling.

## Build, Test, and Development Commands
```bash
dotnet restore Parametric_Arsenal.sln
dotnet build Parametric_Arsenal.sln --configuration Release --no-restore -p:EnforceCodeStyleInBuild=true -warnaserror
dotnet format Parametric_Arsenal.sln --verify-no-changes
dotnet test Parametric_Arsenal.sln --configuration Release --no-build
dotnet test test/rhino/Arsenal.Rhino.Tests.csproj --configuration Release --no-build --verbosity normal
```

## Coding Style & Naming Conventions
`.editorconfig` and `Directory.Build.props` enforce four-space indents, file-scoped namespaces, nullable references, explicit types (no `var`), target-typed `new`, named parameters, and trailing commas on multi-line constructs. Analyzer warnings fail builds, so default to pattern matching, tuple deconstruction, primary constructors, collection expressions, and exhaustive switch expressions. Python scripts must satisfy `ruff check --fix`, `ruff format`, `mypy`, and `basedpyright`; keep imports sorted and favor snake_case in `rhino/scripts`. Run `pre-commit run --all-files` before pushing.

## Testing Guidelines
Unit tests mirror runtime libraries—extend `Arsenal.Tests.Shared` instead of duplicating scaffolding. Name xUnit classes `*Tests.cs`, Rhino-focused NUnit fixtures `Rhino*Tests.cs`. Place golden data in `test/**/Resources` and track large fixtures with LFS. Local Rhino tests need `RhinoCommon.dll` on the probing path (`mcneel/setup-rhino3d` provides it in CI). Share coverage deltas for geometry or algorithmic changes.

## Commit & Pull Request Guidelines
Use Conventional Commits (`feat:`, `fix:`, `chore:`) with scopes aligned to folders (e.g., `feat(core): add loft evaluation`). Keep commits atomic, reference issues via `refs #123`, and document validation steps. Pull requests must describe intent, list executed commands, and attach Rhino/Grasshopper visuals when UX shifts. Block merges on failing checks or missing tests—the CI matrix mirrors the local commands above.

<!-- Badges -->

[![GitHub last commit](https://img.shields.io/github/last-commit/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub license](https://img.shields.io/github/license/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal/blob/main/LICENSE)
[![GitHub LFS enabled](https://img.shields.io/badge/Git%20LFS-enabled-brightgreen?style=flat-square)](https://git-lfs.github.com/)
[![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-yellow.svg?style=flat-square)](https://conventionalcommits.org)
[![GitHub repo size](https://img.shields.io/github/repo-size/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![Python](https://img.shields.io/badge/Python-3.13+-blue?style=flat-square&logo=python)](https://www.python.org/)

---

# Parametric Arsenal

PLACEHOLDER

## Development Setup

### Pre-commit Hooks

This repository uses pre-commit hooks to enforce code quality standards before commits. The hooks automatically check:

- **C#/.NET**: Analyzers (Roslynator, Meziantou, IDisposableAnalyzers), `.editorconfig` rules, and formatting
- **Python**: Ruff linting/formatting, mypy and basedpyright type checking
- **General**: File quality checks (trailing whitespace, line endings, etc.)

#### Quick Setup

**Linux/macOS:**
```bash
./scripts/setup-precommit.sh
```

**Windows:**
```powershell
.\scripts\setup-precommit.ps1
```

#### Manual Setup

```bash
pip install pre-commit
pre-commit install
```

For detailed information, see [Pre-commit Hooks Guide](docs/PRE-COMMIT.md).

## License

This project is licensed under the MIT License - see [LICENSE](LICENSE) for details.

---

<p align="center">
Built with ❤️ for the AEC community by <a href="https://github.com/bsamiee">Bardia Samiee</a>
</p>

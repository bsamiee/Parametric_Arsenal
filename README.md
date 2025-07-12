<!-- Badges -->

[![GitHub last commit](https://img.shields.io/github/last-commit/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub license](https://img.shields.io/github/license/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal/blob/main/LICENSE)
[![Python](https://img.shields.io/badge/Python-3.13+-blue?style=flat-square&logo=python)](https://www.python.org/)
[![Poetry](https://img.shields.io/badge/Poetry-managed-blue?style=flat-square&logo=poetry)](https://python-poetry.org/)
[![Code style: Ruff](https://img.shields.io/badge/code%20style-ruff-000000?style=flat-square)](https://github.com/astral-sh/ruff)
[![Type checked: MyPy](https://img.shields.io/badge/type%20checked-mypy-blue?style=flat-square)](http://mypy-lang.org/)
[![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-yellow.svg?style=flat-square)](https://conventionalcommits.org)
[![GitHub repo size](https://img.shields.io/github/repo-size/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub LFS enabled](https://img.shields.io/badge/Git%20LFS-enabled-brightgreen?style=flat-square)](https://git-lfs.github.com/)
[![pre-commit.ci status](https://results.pre-commit.ci/badge/github/bsamiee/Parametric_Arsenal/master.svg)](https://results.pre-commit.ci/latest/github/bsamiee/Parametric_Arsenal/master)

---

# 🏗️ Parametric Arsenal

A comprehensive monorepo for architectural automation, parametric design, and creative workflows. This project aims to bridge the gap between design software, providing unified tooling and libraries for architects, designers, and computational design specialists.

## 🎯 Vision

Parametric Arsenal is building towards a future where architectural and design workflows are:

-   **Interconnected**: Seamless data flow between Rhino, Grasshopper, AutoCAD, and Adobe Creative Suite
-   **Automated**: Repetitive tasks handled by smart scripts and plugins
-   **Reproducible**: Version-controlled design assets and parametric definitions
-   **Scalable**: From single scripts to enterprise-level design systems

## 🚀 What's Inside

### Core Libraries (`libs/`)

Our shared libraries provide the foundation for all projects:

-   **`mzn.types`** - A sophisticated type system with validation, ensuring data integrity across all tools
-   **`mzn.cache`** - High-performance async caching with multiple backend support
-   **`mzn.errors`** - Unified error handling and reporting
-   **`mzn.log`** - Structured logging for debugging complex workflows
-   **`mzn.metrics`** - Performance monitoring and optimization tools

### Projects (Coming Soon)

-   **Rhino/Grasshopper Plugins** - Custom components and automation tools
-   **AutoCAD Scripts** - Batch processing and drawing automation
-   **Adobe Actions** - Illustrator, Photoshop, and InDesign workflow enhancers
-   **CLI Tools** - Command-line utilities for design file processing

## 🏛️ Architecture

```
Parametric_Arsenal/
├── libs/mzn/                       # Core namespace packages
│   ├── types/                      # Type system with validation
│   ├── cache/                      # Async caching solution
│   ├── errors/                     # Error management
│   ├── log/                        # Structured logging
│   └── metrics/                    # Performance tracking
├── projects/                       # Individual projects using libs
│   ├── rhino_tools/                # (planned)
│   ├── grasshopper_components/     # (planned)
│   └── adobe_scripts/              # (planned)
├── tests/                          # Comprehensive test suites
└── typings/                        # Type stubs for external packages
```

## 🛠️ Technology Stack

-   **Language**: Python 3.13+ with cutting-edge type hints
-   **Type Safety**: MyPy & Pyright in strict mode
-   **Async**: Built on `anyio` for maximum performance
-   **Quality**: Ruff, pre-commit, and comprehensive CI/CD
-   **Architecture**: Monorepo managed with Poetry
-   **Large Files**: Git LFS for design assets

## 🚦 Getting Started

### Prerequisites

-   Python 3.13+
-   Poetry
-   Git LFS
-   (Optional) Rhino 8 for Rhino-specific tools

### Installation

```bash
# Clone the repository
git clone https://github.com/bsamiee/Parametric_Arsenal.git
cd Parametric_Arsenal

# Install dependencies
poetry install

# Set up git hooks
git config core.hooksPath .githooks

# Initialize Git LFS
git lfs install
```

### Development

```bash
# Run all quality checks
nox

# Run specific checks
poetry run mypy libs/
poetry run ruff check libs/
poetry run pytest

# Use the CLI
poetry run pa
```

## 📐 Design Philosophy

### Type-First Development

Every piece of data is validated at the edge using our sophisticated type system:

```python
from mzn.types.packages.general.aliases import FilePath
from mzn.types.packages.errors.types import ErrorCode

# Types carry their validation rules
rhino_file = FilePath("/path/to/model.3dm")  # Automatically validated
```

### Async Throughout

All I/O operations are asynchronous for maximum performance:

```python
from mzn.cache import Cache

cache = await Cache.memory("design-cache")
await cache.set("viewport", viewport_data)
```

### Zero Tolerance for Errors

-   100% type coverage with MyPy/Pyright
-   Zero linting warnings
-   Comprehensive error handling

## 🤝 Contributing

We welcome contributions! This project follows:

-   **Conventional Commits** for clear history
-   **Semantic Versioning** for releases
-   **Pre-commit hooks** for code quality
-   **Comprehensive testing** (80%+ coverage)

See [CONTRIBUTING.md](CONTRIBUTING.md) (coming soon) for detailed guidelines.

## 📊 Project Status

🚧 **Early Development** - Core libraries are being built. Watch this space for:

-   [ ] Rhino/Grasshopper plugin framework
-   [ ] Adobe Creative Suite integrations
-   [ ] Design file converters
-   [ ] Parametric component library

## 📜 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) for details.

## 🔗 Resources

-   [Documentation](https://github.com/bsamiee/Parametric_Arsenal/wiki) (coming soon)
-   [Issue Tracker](https://github.com/bsamiee/Parametric_Arsenal/issues)
-   [Discussions](https://github.com/bsamiee/Parametric_Arsenal/discussions)

---

<p align="center">
Built with ❤️ for the AEC community by <a href="https://github.com/bsamiee">Bardia Samiee</a>
</p>

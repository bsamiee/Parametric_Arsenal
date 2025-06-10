<!-- Badges -->

[![GitHub last commit](https://img.shields.io/github/last-commit/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub license](https://img.shields.io/github/license/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal/blob/main/LICENSE)
[![GitHub top language](https://img.shields.io/github/languages/top/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub repo size](https://img.shields.io/github/repo-size/bsamiee/Parametric_Arsenal?style=flat-square)](https://github.com/bsamiee/Parametric_Arsenal)
[![GitHub LFS enabled](https://img.shields.io/badge/Git%20LFS-enabled-brightgreen?style=flat-square)](https://git-lfs.github.com/)

---

# Parametric Arsenal

A curated and version-controlled mono-repo namespaced library powering architectural workflows and projects, including custom Grasshopper and Rhino parametric design workflows and assets.

## Continuous Integration

This project uses [pre-commit](https://pre-commit.com/), [Nox](https://nox.thea.codes/), and GitHub Actions for automated QA and CI/CD.

### Local setup

-   On first clone, run:

    ```sh
    git config core.hooksPath .githooks
    ```

    This will ensure pre-commit hooks are installed automatically on checkout (see `.githooks/post-checkout`).

-   Run the full QA suite locally with:

    ```sh
    poetry install
    nox
    ```

    This will install hooks (if not present) and run all checks.

### CI/CD

-   All checks are run in CI on every push and pull request to `master`.
-   Releases are handled by `.github/workflows/release.yml`.

### Keeping tools up to date

-   [pre-commit.ci](https://pre-commit.ci/) is enabled to keep hooks updated automatically.
-   All tool versions are pinned in `pyproject.toml` and `poetry.lock` for reproducibility.

### Caching

-   CI caches the following for speed:
    -   `~/.cache/pip`
    -   `~/.cache/pypoetry`
    -   `.cache/nox`
    -   `.cache/pre-commit`
    -   `~/.cache/ruff`
    -   `~/.cache/docformatter`
    -   `~/.cache/pytest`
    -   `~/.cache/yamllint`
    -   `~/.cache/shellcheck`

### Speed tweaks

-   In CI, pre-commit reuses its virtualenvs via `PRE_COMMIT_HOME=~/.cache/pre-commit`.
-   All caches are restored for faster runs.

See `.pre-commit-config.yaml`, `noxfile.py`, and `.github/workflows/ci.yml` for details.

"""
Title         : noxfile.py Author        : Bardia Samiee Project       : parametric_arsenal License       : MIT Path :
ROOT/noxfile.py.

Automates linting, type-checking, tests, docs, builds, and releases in re-usable virtual-envs under .cache/nox.  Uses
Poetry 2.1's `poetry sync` with --only so each session pulls exactly the dependency group(s) it needs.

"""

from __future__ import annotations

import os
from pathlib import Path

import nox


# ── Global options ────────────────────────────────────────────────────────────

nox.options.envdir = ".cache/nox"
nox.options.sessions = [
    "setup",  # Ensure dependencies are installed first
    "install_hooks",
    "lint",
    "pre_commit",
    "code_quality",
    "tests",
    "docs",
]
nox.options.reuse_existing_virtualenvs = True
nox.options.default_venv_backend = "virtualenv"

PYTHON_VERSIONS = ["3.13"]  # single-version matrix for now
SOURCE_PATHS = ("libs", "noxfile.py")
HOOK_INSTALL_ARGS = ("--install-hooks",)


# ── Dependency setup ──────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0], name="setup")
def setup(session: nox.Session) -> None:
    """Install all dependencies using Poetry into the Nox venv."""
    _ = session.run("poetry", "install", "--no-interaction", external=True)
    session.log("✅  All dependencies installed in the Nox venv.")


# ── Git-hook bootstrap ────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0], name="install_hooks")
def install_hooks(session: nox.Session) -> None:
    """Install *pre-commit* git hooks."""
    session.install("pre-commit")  # tiny; easier than syncing full dev group
    _ = session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)


# ── Quality-assurance sessions ────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run *pre-commit* hooks on the entire repo."""
    if not os.getenv("CI"):  # ensure hooks are available locally
        _ = session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)
    _ = session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure", "-v")


@nox.session(python=PYTHON_VERSIONS[0])
def lint(session: nox.Session) -> None:
    """Run all formatters and then linters/checkers for code and file hygiene."""
    # --- Formatters ---
    _ = session.run("ruff", "check", "--fix", "-v", *SOURCE_PATHS)
    _ = session.run("ruff", "format", "--check", "-v", *SOURCE_PATHS)
    if Path("pyproject.toml").exists():
        _ = session.run("toml-sort", "--in-place", "pyproject.toml")
    # Get all JSON/YAML files excluding cache directories
    json_yaml_files = [
        str(p) for p in Path().rglob("*.[jy][sa][mo][nl]") if not any(part.startswith(".cache") for part in p.parts)
    ]
    if json_yaml_files:
        _ = session.run("prettier", "--write", *json_yaml_files)
        json_files = [f for f in json_yaml_files if f.endswith(".json")]
        if json_files:
            _ = session.run("jsonlint", *json_files)  # Validation only, after prettier formatting

    # Get all Python files excluding cache directories
    py_files = [str(p) for p in Path().rglob("*.py") if not any(part.startswith(".cache") for part in p.parts)]
    if py_files:
        _ = session.run("pyupgrade", "--py3-plus", *py_files)

    # Get all Markdown files excluding cache directories
    md_files = [str(p) for p in Path().rglob("*.md") if not any(part.startswith(".cache") for part in p.parts)]
    if md_files:
        _ = session.run("mdformat", ".")
    # --- Linters ---
    _ = session.run("ruff", "check", "-v", *SOURCE_PATHS)
    _ = session.run("ruff", "format", "--check", "-v", *SOURCE_PATHS)
    _ = session.run("yamllint", ".")
    # Get all shell files excluding cache directories
    sh_files = [str(p) for p in Path().rglob("*.sh") if not any(part.startswith(".cache") for part in p.parts)]
    if sh_files:
        _ = session.run("shellcheck", "-f", "diff", *sh_files)


@nox.session(python=PYTHON_VERSIONS[0])
def tests(session: nox.Session) -> None:
    """Dummy test session placeholder."""
    session.log("🔎  Dummy test session: pytest not yet configured.")


@nox.session(python=PYTHON_VERSIONS[0])
def code_quality(session: nox.Session) -> None:
    """Dummy code quality session placeholder."""
    session.log("🔎  Dummy code quality session: vulture/secrets not yet configured.")


# ── Documentation ─────────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """
    Build or serve the documentation with Sphinx.

    Uses sphinx-autobuild for live reload if '--serve' is passed.

    """
    if "--serve" in session.posargs:
        _ = session.run(
            "sphinx-autobuild",
            "docs",
            "docs/_build/html",
            "--watch",
            "libs",
            "--port",
            "8000",
            "-q",
        )
    else:
        _ = session.run(
            "sphinx-build",
            "-b",
            "html",
            "-W",
            "-q",
            "docs",
            "docs/_build/html",
        )


# ── Build & release helpers ───────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def build(session: nox.Session) -> None:
    """Build distributable artefacts with Poetry."""
    _ = session.run("poetry", "build", external=True)


@nox.session(python=PYTHON_VERSIONS[0])
def release(session: nox.Session) -> None:
    """
    Publish a new version using python-semantic-release.

    Ensures all dev tools are installed and uses verbose output.

    """
    _ = session.run("semantic-release", "--verbose", "publish")


# ── House-keeping ─────────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Delete caches, build artefacts and other temporary files from the workspace."""
    _ = session.run(
        "rm",
        "-rf",
        "dist",
        "build",
        "docs/_build",
        "site",
        ".pytest_cache",
        "coverage.xml",
        external=True,
    )
    _ = session.run("find", ".", "-name", "__pycache__", "-delete", external=True)
    _ = session.run("find", ".", "-name", "*.pyc", "-delete", external=True)
    session.log("🧹  Workspace cleaned.")

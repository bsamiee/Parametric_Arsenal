"""
Title         : noxfile.py Author        : Bardia Samiee Project       : parametric_arsenal License       : MIT Path :
noxfile.py.

Description ------- A concise 1-3 sentence summary telling a new reader why this script exists.

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
    "mypy",
    "pre_commit",
    "code_quality",
    "tests",
    "docs",
]
nox.options.reuse_existing_virtualenvs = True
nox.options.default_venv_backend = "virtualenv"

PYTHON_VERSIONS = ["3.13"]  # single-version matrix for now
SOURCE_PATHS = ("libs", "noxfile.py")
HOOK_INSTALL_ARGS = ("--install-hooks", "--hook-type", "commit-msg")


# ── Dependency setup ──────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0], name="setup")
def setup(session: nox.Session) -> None:
    """Install all dependencies using Poetry into the Nox venv."""
    session.run("poetry", "install", "--no-interaction", external=True)
    session.log("✅  All dependencies installed in the Nox venv.")


# ── Git-hook bootstrap ────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0], name="install_hooks")
def install_hooks(session: nox.Session) -> None:
    """Install *pre-commit* and *commit-msg* git hooks."""
    session.install("pre-commit")  # tiny; easier than syncing full dev group
    session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)


# ── Quality-assurance sessions ────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run *pre-commit* hooks on the entire repo."""
    if not os.getenv("CI"):  # ensure hooks are available locally
        session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)
    session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure", "-v")


@nox.session(python=PYTHON_VERSIONS[0])
def lint(session: nox.Session) -> None:
    """Run all formatters and then linters/checkers for code and file hygiene."""
    # --- Formatters ---
    session.run("ruff", "check", "--fix", "-v", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", "-v", *SOURCE_PATHS)
    session.run(
        "docformatter",
        "--in-place",
        "--recursive",
        "--wrap-summaries=120",
        "--wrap-descriptions=120",
        "--pre-summary-newline",
        "--force-wrap",
        *SOURCE_PATHS,
    )
    if Path("pyproject.toml").exists():
        session.run("toml-sort", "--in-place", "pyproject.toml")
    # Get all JSON/YAML files excluding cache directories
    json_yaml_files = [
        str(p) for p in Path().rglob("*.[jy][sa][mo][nl]") if not any(part.startswith(".cache") for part in p.parts)
    ]
    if json_yaml_files:
        session.run("prettier", "--write", *json_yaml_files)
        json_files = [f for f in json_yaml_files if f.endswith(".json")]
        if json_files:
            session.run("jsonlint", *json_files)  # Validation only, after prettier formatting

    # Get all Python files excluding cache directories
    py_files = [str(p) for p in Path().rglob("*.py") if not any(part.startswith(".cache") for part in p.parts)]
    if py_files:
        session.run("pyupgrade", "--py3-plus", *py_files)

    # Get all Markdown files excluding cache directories
    md_files = [str(p) for p in Path().rglob("*.md") if not any(part.startswith(".cache") for part in p.parts)]
    if md_files:
        session.run("mdformat", ".")
        session.run("markdownlint", *md_files)  # Validation after formatting
    # --- Linters ---
    session.run("ruff", "check", "-v", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", "-v", *SOURCE_PATHS)
    session.run("yamllint", ".")
    # Get all shell files excluding cache directories
    sh_files = [str(p) for p in Path().rglob("*.sh") if not any(part.startswith(".cache") for part in p.parts)]
    if sh_files:
        session.run("shellcheck", "-f", "diff", *sh_files)


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """Type-check the codebase using MyPy with strict settings."""
    session.run("mypy", "-vv", *SOURCE_PATHS)


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
        session.run(
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
        session.run(
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
    session.run("poetry", "build", external=True)


@nox.session(python=PYTHON_VERSIONS[0])
def release(session: nox.Session) -> None:
    """
    Publish a new version using python-semantic-release.

    Ensures all dev tools are installed and uses verbose output.

    """
    session.run("semantic-release", "--verbose", "publish")


# ── House-keeping ─────────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Delete caches, build artefacts and other temporary files from the workspace."""
    session.run(
        "rm",
        "-rf",
        ".mypy_cache",
        "dist",
        "build",
        "docs/_build",
        "site",
        ".pytest_cache",
        "coverage.xml",
        external=True,
    )
    session.run("find", ".", "-name", "__pycache__", "-delete", external=True)
    session.run("find", ".", "-name", "*.pyc", "-delete", external=True)
    session.log("🧹  Workspace cleaned.")

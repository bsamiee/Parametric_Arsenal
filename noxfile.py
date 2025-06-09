"""
Title         : noxfile.py
Author        : Bardia Samiee
Project       : parametric_arsenal
License       : MIT
Path          : noxfile.py

Description
-------
Provides a single Nox configuration that automates the project's development workflow—tests, linting/formatting,
type-checking, security and dependency audits, docs, builds, and releases—inside reusable virtual environments under
.cache/nox. This ensures all quality-assurance and publication steps can be run consistently both locally and in CI with
a single command.
"""

import os
from pathlib import Path

import nox


# --- Global Nox Options -------------------------------------------------------

# Store virtualenvs under .cache so they are outside the working tree
nox.options.envdir = ".cache/nox"

# Default sessions when running plain `nox`
# (hooks are an explicit opt-in because they mutate .git/ and are not
#   required in CI where commits are already created)
nox.options.sessions = [
    "lint",
    "mypy",
    "pre_commit",
    "tests",
]

# Re-use existing envs to speed up local iteration
nox.options.reuse_existing_virtualenvs = True

# --- Constants  ---------------------------------------------------------------
SOURCE_PATHS = ("libs", "tests", "noxfile.py")
PYTHON_VERSIONS = ["3.13"]  # Add other versions e.g. "3.12" when needed

# Arguments used whenever we (re-)install the Git hooks
HOOK_INSTALL_ARGS = (
    "--install-hooks",
    "--hook-type",
    "commit-msg",
)

# --- Helper utilities ---------------------------------------------------------


def install_poetry_deps(session: nox.Session) -> None:
    """Install project dependencies into the Nox session via Poetry."""
    session.install("poetry")
    session.run("poetry", "install", external=True)


# --- Git-hook bootstrap -------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0], name="install_hooks")
def install_hooks(session: nox.Session) -> None:
    """Install *all* required Git hooks (pre-commit, commit-msg, prepare-commit-msg).

    Run this once per clone—or whenever the hook list changes—to guarantee
        * code-quality hooks fire before every commit, and
        * Commitizen can validate / rewrite messages for Conventional Commits.
    """
    session.install("pre-commit")
    # Always write the hooks; this session is intended for local use only.
    session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)


# --- Quality-assurance sessions ----------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run the full pre-commit suite (with optional hook install locally)."""
    session.install("pre-commit")

    # Skip actual hook installation inside CI to save a bit of time
    if not os.getenv("CI"):
        session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)

    session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure")


@nox.session(python=PYTHON_VERSIONS)
def tests(session: nox.Session) -> None:
    """Run the test suite with pytest and generate a coverage report (disabled for now)."""
    install_poetry_deps(session)
    # Uncomment once tests are in place.
    # session.run(
    #     "pytest",
    #     "--cov=libs",
    #     "--cov-report=term-missing",
    #     "--cov-report=xml",
    #     *session.posargs,
    # )
    session.log("Skipping pytest run for now. Uncomment the lines above to re-enable.")


@nox.session(python=PYTHON_VERSIONS[0])
def lint(session: nox.Session) -> None:
    """Static analysis and formatting checks via Ruff (no auto-fix)."""
    session.install("ruff")
    session.run("ruff", "check", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def format_code(session: nox.Session) -> None:
    """Auto-format code (Ruff) and sort pyproject.toml (toml-sort)."""
    session.install("ruff", "toml-sort")
    session.run("ruff", "check", *SOURCE_PATHS, "--fix")
    session.run("ruff", "format", *SOURCE_PATHS)
    session.run("toml-sort", "pyproject.toml", "--all", "--in-place")


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """Type-check the source tree with mypy."""
    install_poetry_deps(session)
    session.run("mypy", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def security(session: nox.Session) -> None:
    """Run a security audit using pip-audit."""
    session.install("poetry", "pip-audit")
    session.run(
        "poetry",
        "export",
        "--with",
        "dev",
        "--format=requirements.txt",
        "--output=requirements.txt",
        external=True,
    )
    session.run("pip-audit", "-r", "requirements.txt")
    Path("requirements.txt").unlink()


@nox.session(python=PYTHON_VERSIONS[0])
def dependencies(session: nox.Session) -> None:
    """Detect unused, missing, or transitive dependencies with deptry."""
    session.install("deptry")
    session.run("deptry", ".")


@nox.session(python=PYTHON_VERSIONS[0])
def analysis(session: nox.Session) -> None:
    """Compute maintainability & complexity metrics (vulture + radon)."""
    session.install("radon", "vulture")
    session.run("vulture", *SOURCE_PATHS, "--min-confidence", "80")
    session.run("radon", "mi", *SOURCE_PATHS, "-s")  # Maintainability Index
    session.run("radon", "cc", *SOURCE_PATHS, "-s", "-a")  # Cyclomatic Complexity


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """Build (or serve) the MkDocs documentation site."""
    session.install("mkdocs", "mkdocs-material")
    if "--serve" in session.posargs:
        session.run("mkdocs", "serve")
    else:
        session.run("mkdocs", "build", "--clean")
        session.log("Documentation built in site/ directory.")


# --- Build & release sessions -------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def build(session: nox.Session) -> None:
    """Build sdist and wheel distributions via Poetry."""
    session.install("poetry")
    session.run("poetry", "build", external=True)


@nox.session(python=PYTHON_VERSIONS[0])
def release(session: nox.Session) -> None:
    """Publish a new version using python-semantic-release (intended for CI)."""
    session.install(
        "python-semantic-release",
        "git-cliff",  # For changelog generation hook
        "poetry",  # Required by semantic-release build hook
    )
    session.run("semantic-release", "--verbose", "publish")


# --- House keeping ------------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Purge caches, build artefacts, and other transient files."""
    session.run("rm", "-rf", ".mypy_cache", "dist", "build", external=True)
    session.run("rm", "-rf", "site", ".pytest_cache", "coverage.xml", external=True)
    session.run(
        "find",
        ".",
        "-name",
        "'__pycache__'",
        "-exec",
        "rm",
        "-rf",
        "{}",
        "+",
        external=True,
    )
    session.run(
        "find",
        ".",
        "-name",
        "'*.pyc'",
        "-exec",
        "rm",
        "-rf",
        "{}",
        "+",
        external=True,
    )
    session.log("Cleaned up the project directory.")

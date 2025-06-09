"""
Title         : noxfile.py
Author        : Bardia Samiee
Project       : parametric_arsenal
License       : MIT
Path          : ROOT/noxfile.py

Automates linting, type-checking, tests, docs, builds, and releases in
re-usable virtual-envs under .cache/nox.  Uses Poetry 2.x's `poetry sync`
to create lean, reproducible environments that install only the dependency
groups each session needs.
"""

from __future__ import annotations

import os
from typing import TYPE_CHECKING


if TYPE_CHECKING:
    from collections.abc import Sequence
from pathlib import Path

import nox


# --- Global Nox Options -------------------------------------------------------

nox.options.envdir = ".cache/nox"  # keep venvs outside tree
nox.options.sessions = ["lint", "mypy", "pre_commit", "tests"]
nox.options.reuse_existing_virtualenvs = True  # speed up local runs

SOURCE_PATHS: Sequence[str] = ("libs", "tests", "noxfile.py")
PYTHON_VERSIONS = ["3.13"]  # extend list when needed
HOOK_INSTALL_ARGS = ("--install-hooks", "--hook-type", "commit-msg")

# --- Helper utilities ---------------------------------------------------------


def poetry_sync(session: nox.Session, *groups: str, root: bool = False) -> None:
    """
    Create / update the session venv to match poetry.lock *exactly*.

    Parameters
    ----------
    groups : str
        Optional dependency groups to include (e.g. "test", "type", "docs").
        If omitted, only runtime deps are installed.
    root : bool, default False
        Install the project itself (`--no-root` is the default for speed).
    """
    # For local dev we install Poetry into the venv; in CI it is pre-installed
    if not os.getenv("CI"):
        session.install("poetry")

    cmd = ["poetry", "sync", "--no-interaction", "--no-ansi"]
    if groups:
        cmd += ["--with", ",".join(groups)]
    if not root:
        cmd.append("--no-root")

    session.run(*cmd, external=True)


# --- Git-hook bootstrap -------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0], name="install_hooks")
def install_hooks(session: nox.Session) -> None:
    """Install *all* required Git hooks (pre-commit, commit-msg, etc.)."""
    session.install("pre-commit")
    session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)


# --- Quality-assurance sessions ----------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run the full pre-commit suite (and install hooks when not in CI)."""
    session.install("pre-commit")
    if not os.getenv("CI"):
        session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)
    session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure")


@nox.session(python=PYTHON_VERSIONS)
def tests(session: nox.Session) -> None:
    """Execute pytest with the *test* dependency group and the project itself."""
    poetry_sync(session, "test", root=True)
    # Uncomment when you have real tests written
    # session.run("pytest", "--cov=libs", "--cov-report=term-missing",
    #             "--cov-report=xml", *session.posargs)
    session.log("Skipping pytest run for now (no tests yet).")


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
    session.run("ruff", "check", "--fix", *SOURCE_PATHS)
    session.run("ruff", "format", *SOURCE_PATHS)
    session.run("toml-sort", "pyproject.toml", "--all", "--in-place")


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """Type-check the source tree."""
    poetry_sync(session, "type")
    session.run("mypy", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def security(session: nox.Session) -> None:
    """Run a dependency vulnerability audit."""
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
    """Detect unused or transitive dependencies with deptry."""
    session.install("deptry")
    session.run("deptry", ".")


@nox.session(python=PYTHON_VERSIONS[0])
def analysis(session: nox.Session) -> None:
    """Compute maintainability & complexity metrics (vulture + radon)."""
    session.install("vulture", "radon")
    session.run("vulture", *SOURCE_PATHS, "--min-confidence", "80")
    session.run("radon", "mi", *SOURCE_PATHS, "-s")
    session.run("radon", "cc", *SOURCE_PATHS, "-s", "-a")


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """Build (or serve) the MkDocs documentation site."""
    poetry_sync(session, "docs")
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
    session.install("python-semantic-release", "git-cliff", "poetry")
    session.run("semantic-release", "--verbose", "publish")


# --- House keeping ------------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Purge caches, build artefacts, and other transient files."""
    session.run("rm", "-rf", ".mypy_cache", "dist", "build", external=True)
    session.run("rm", "-rf", "site", ".pytest_cache", "coverage.xml", external=True)
    session.run("find", ".", "-name", "__pycache__", "-exec", "rm", "-rf", "{}", "+", external=True)
    session.run("find", ".", "-name", "*.pyc", "-exec", "rm", "-rf", "{}", "+", external=True)
    session.log("Cleaned up the project directory.")

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
from pathlib import Path
from typing import TYPE_CHECKING


if TYPE_CHECKING:
    from collections.abc import Sequence

import nox


# --- Global Nox Options -------------------------------------------------------

nox.options.envdir = ".cache/nox"
nox.options.sessions = ["lint", "mypy", "pre_commit", "tests"]
nox.options.reuse_existing_virtualenvs = True

PYTHON_VERSIONS = ["3.13"]
SOURCE_PATHS: Sequence[str] = ("libs", "tests", "noxfile.py")
HOOK_INSTALL_ARGS = ("--install-hooks", "--hook-type", "commit-msg")

# --- Helper utilities ---------------------------------------------------------


def poetry_sync(session: nox.Session, *groups: str, root: bool = False) -> None:
    """Synchronise the venv with poetry.lock (Poetry ≥ 2.1)."""
    session.run("poetry", "--version", external=True)  # diagnostic
    cmd = ["poetry", "-vv", "sync", "--no-interaction", "--ansi"]
    if groups:
        cmd += ["--with", ",".join(groups)]
    if not root:
        cmd.append("--no-root")
    session.run(*cmd, external=True)


# --- Git-hook bootstrap -------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0], name="install_hooks")
def install_hooks(session: nox.Session) -> None:
    """Install pre-commit and commit-msg hooks."""
    session.install("pre-commit")
    session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)


# --- Quality-assurance sessions ----------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run pre-commit hooks on all files."""
    session.install("pre-commit")
    if not os.getenv("CI"):
        session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)
    session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure", "-v")


@nox.session(python=PYTHON_VERSIONS)
def tests(session: nox.Session) -> None:
    """Run the test suite using pytest."""
    poetry_sync(session, "test", root=True)

    # ── Conditional skip ───────────────────────────────────────────────
    if not any(Path("tests").rglob("test_*.py")):
        session.log("🔎  No test files found - skipping pytest run.")
        return
    # ───────────────────────────────────────────────────────────────────

    session.log("▶️  Running pytest …")
    session.run(
        "pytest",
        "-vv",
        "-ra",
        "--durations=25",  # very verbose output
        *session.posargs,
    )


@nox.session(python=PYTHON_VERSIONS[0])
def lint(session: nox.Session) -> None:
    """Run Ruff for linting and formatting checks on the source code."""
    session.install("ruff")
    session.run("ruff", "check", "-vv", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", "-vv", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """Run MyPy for type-checking on the source code."""
    poetry_sync(session, "type")
    session.run("mypy", "-vvv", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """Build or serve the documentation using MkDocs."""
    poetry_sync(session, "docs")
    if "--serve" in session.posargs:
        session.run("mkdocs", "serve", "-v")
    else:
        session.run("mkdocs", "build", "--clean", "-v")


# --- Build & release sessions -------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def build(session: nox.Session) -> None:
    """Build the project using Poetry."""
    session.run("poetry", "--version", external=True)
    session.run("poetry", "build", "-vv", external=True)


@nox.session(python=PYTHON_VERSIONS[0])
def release(session: nox.Session) -> None:
    """Publish a new release using semantic-release and generate changelogs."""
    session.install("python-semantic-release", "git-cliff")
    session.run("semantic-release", "--verbose", "publish")


# --- House keeping ------------------------------------------------------------


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Clean up temporary files and directories created during the build and test processes."""
    session.run("rm", "-rf", ".mypy_cache", "dist", "build", "site", ".pytest_cache", "coverage.xml", external=True)
    session.run("find", ".", "-name", "__pycache__", "-delete", external=True)
    session.run("find", ".", "-name", "*.pyc", "-delete", external=True)
    session.log("Workspace cleaned.")

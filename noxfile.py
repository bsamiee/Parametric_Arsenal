"""
Project helper file executed by **Nox**.

It spins-up throw-away virtual-envs (under ``.cache/nox``) to run:

• pre-commit hooks
• linters / formatters
• type-checker
• test-suite (placeholder - skipped while no tests exist)
• Sphinx docs builder / live-server
• build / release helpers
• clean-up tasks

The environments are populated straight from *poetry.lock* via
``poetry sync --only <groups>`` so every session gets exactly the
dependencies it needs - nothing more.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import TYPE_CHECKING

import nox


if TYPE_CHECKING:
    from collections.abc import Sequence


# ── Global options ────────────────────────────────────────────────────────────

nox.options.envdir = ".cache/nox"
nox.options.sessions = [
    "install_hooks",  # Ensure hooks are always installed first
    "lint",
    "mypy",
    "pre_commit",
    "code_quality",
    "tests",
    "docs",
]
nox.options.reuse_existing_virtualenvs = True

PYTHON_VERSIONS = ["3.13"]  # single-version matrix for now
SOURCE_PATHS: Sequence[str] = ("libs", "tests", "noxfile.py")
HOOK_INSTALL_ARGS = ("--install-hooks", "--hook-type", "commit-msg")


# ── Helpers ───────────────────────────────────────────────────────────────────


def poetry_sync(session: nox.Session, *groups: str, root: bool = False) -> None:
    """
    Sync the venv with *poetry.lock* (Poetry ≥ 2.1).

    Only the requested dependency *groups* plus runtime code (``main``) are
    installed, keeping environments lean.
    """
    session.env.setdefault("POETRY_VIRTUALENVS_CREATE", "false")  # avoid nested .venv
    cmd = ["poetry", "sync", "--no-interaction", "--ansi"]

    if groups:
        cmd += ["--only", ",".join(groups)]

    if not root:
        cmd.append("--no-root")

    session.run(*cmd, external=True)


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
    session.install("pre-commit")
    if not os.getenv("CI"):  # ensure hooks are available locally
        session.run("pre-commit", "install", *HOOK_INSTALL_ARGS)
    session.run("pre-commit", "run", "--all-files", "--show-diff-on-failure", "-v")


@nox.session(python=PYTHON_VERSIONS)
def tests(session: nox.Session) -> None:
    """Execute the test-suite via *pytest* (skipped if no tests yet)."""
    poetry_sync(session, "main", "test", root=True)

    if not any(Path("tests").rglob("test_*.py")):
        session.log("🔎  No tests found - skipping pytest run.")
        return

    session.run("pytest", "-vv", "-ra", "--durations=25", *session.posargs)


@nox.session(python=PYTHON_VERSIONS[0])
def lint(session: nox.Session) -> None:
    """
    Run all formatters and then linters/checkers for code and file hygiene.
    Formatters: ruff (autofix), docformatter, mdformat, toml-sort, jsonlint, pyupgrade.
    Linters: ruff (check), yamllint, shellcheck.
    Only runs file-type tools if relevant files exist.
    """
    poetry_sync(session, "dev")
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
    json_yaml_files = [str(p) for p in Path().rglob("*.[jy][sa][mo][nl]")]  # Matches *.json, *.yaml, *.yml
    if json_yaml_files:
        session.run("prettier", "--write", *json_yaml_files)
        json_files = [f for f in json_yaml_files if f.endswith(".json")]
        if json_files:
            session.run("jsonlint", *json_files)  # Validation only, after prettier formatting
    py_files = [str(p) for p in Path().rglob("*.py")]
    if py_files:
        session.run("pyupgrade", "--py3-plus", *py_files)
    md_files = [str(p) for p in Path().rglob("*.md")]
    if md_files:
        session.run("mdformat", ".")
        session.run("markdownlint", *md_files)  # Validation after formatting
    # --- Linters ---
    session.run("ruff", "check", "-v", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", "-v", *SOURCE_PATHS)
    session.run("yamllint", ".")
    sh_files = [str(p) for p in Path().rglob("*.sh")]
    if sh_files:
        session.run("shellcheck", "-f", "diff", *sh_files)


@nox.session(python=PYTHON_VERSIONS[0])
def code_quality(session: nox.Session) -> None:
    """
    Run extra QA: dead-code detection (vulture) and secrets scan (detect-secrets).
    Only runs secrets scan if .secrets.baseline exists.
    """
    poetry_sync(session, "dev")
    session.run("vulture", "--min-confidence", "80", *SOURCE_PATHS)
    if Path(".secrets.baseline").exists():
        session.run("detect-secrets", "scan", "--baseline", ".secrets.baseline")


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """
    Type-check the codebase using MyPy with strict settings.
    Installs the project for import-based checking.
    """
    poetry_sync(session, "main", "type", root=True)
    session.run("mypy", "-vv", *SOURCE_PATHS)


# ── Documentation ─────────────────────────────────────────────────────────────


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """
    Build or serve the documentation with Sphinx.
    Uses sphinx-autobuild for live reload if '--serve' is passed.
    """
    poetry_sync(session, "main", "docs", root=True)
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
    poetry_sync(session, "dev")
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

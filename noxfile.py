# noxfile.py
from pathlib import Path

import nox


# Set the directory for Nox session environments to be inside a .cache folder
nox.options.envdir = ".cache/nox"

# Set default sessions to run when no specific sessions are chosen
nox.options.sessions = ["lint", "mypy", "tests"]

# Reuse virtual environments for faster consecutive runs
nox.options.reuse_existing_virtualenvs = True

# Define constants for source paths and Python versions to test against
SOURCE_PATHS = ("libs", "tests", "noxfile.py")
PYTHON_VERSIONS = ["3.13"]  # Add other Python versions like "3.12" if needed


def install_poetry_deps(session: nox.Session) -> None:
    """Install project dependencies using Poetry."""
    # This installs the poetry tool itself into the Nox session
    session.install("poetry")

    # This command installs all dependencies from poetry.lock
    # but will NOT remove poetry itself, resolving the conflict.
    session.run("poetry", "install", external=True)


@nox.session(python=PYTHON_VERSIONS)
def tests(session: nox.Session) -> None:
    """Run the test suite with pytest and generate a coverage report."""
    install_poetry_deps(session)
    # The following lines are commented out to temporarily disable the test run.
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
    """Check code for style issues and formatting using Ruff."""
    session.install("ruff")
    session.run("ruff", "check", *SOURCE_PATHS)
    session.run("ruff", "format", "--check", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def format_code(session: nox.Session) -> None:
    """Format the code using Ruff and sort pyproject.toml."""
    session.install("ruff", "toml-sort")
    session.run("ruff", "check", *SOURCE_PATHS, "--fix")
    session.run("ruff", "format", *SOURCE_PATHS)
    session.run("toml-sort", "pyproject.toml", "--all", "--in-place")


@nox.session(python=PYTHON_VERSIONS[0])
def mypy(session: nox.Session) -> None:
    """Run the static type checker mypy."""
    install_poetry_deps(session)
    session.run("mypy", *SOURCE_PATHS)


@nox.session(python=PYTHON_VERSIONS[0])
def security(session: nox.Session) -> None:
    """Run a security audit with pip-audit."""
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
    """Check for unused, missing, or transitive dependencies with deptry."""
    session.install("deptry")
    session.run("deptry", ".")


@nox.session(python=PYTHON_VERSIONS[0])
def analysis(session: nox.Session) -> None:
    """Run static analysis tools to check for dead code and code complexity."""
    session.install("radon", "vulture")
    session.run("vulture", *SOURCE_PATHS, "--min-confidence", "80")
    session.run("radon", "mi", *SOURCE_PATHS, "-s")  # Maintainability Index
    session.run("radon", "cc", *SOURCE_PATHS, "-s", "-a")  # Cyclomatic Complexity


@nox.session(python=PYTHON_VERSIONS[0])
def docs(session: nox.Session) -> None:
    """
    Build the documentation with MkDocs.

    Pass '--serve' to run a live-reloading server.
    Example: nox -s docs -- --serve
    """
    session.install("mkdocs", "mkdocs-material")
    if "--serve" in session.posargs:
        session.run("mkdocs", "serve")
    else:
        session.run("mkdocs", "build", "--clean")
        session.log("Documentation built in site/")


@nox.session(python=PYTHON_VERSIONS[0])
def pre_commit(session: nox.Session) -> None:
    """Run all pre-commit hooks."""
    session.install("pre-commit")
    session.run("pre-commit", "run", "--all-files")


@nox.session(python=PYTHON_VERSIONS[0])
def build(session: nox.Session) -> None:
    """Build the source and wheel distributions."""
    session.install("poetry")
    session.run("poetry", "build", external=True)


@nox.session(python=PYTHON_VERSIONS[0])
def release(session: nox.Session) -> None:
    """
    Create a new release using python-semantic-release.

    This session is designed to be run in a CI/CD environment.
    It reads the GH_TOKEN from the environment variables.
    """
    session.install(
        "python-semantic-release",
        "git-cliff",  # For the changelog generation hook
        "poetry",  # For the build command
    )
    session.run("semantic-release", "publish")


@nox.session(python=PYTHON_VERSIONS[0])
def clean(session: nox.Session) -> None:
    """Remove all temporary files and build artifacts."""
    session.run("rm", "-rf", ".mypy_cache", "dist", "build", external=True)
    session.run("rm", "-rf", "site", ".pytest_cache", "coverage.xml", external=True)
    session.run("find", ".", "-name", "'__pycache__'", "-exec", "rm", "-rf", "{}", "+", external=True)
    session.run("find", ".", "-name", "'*.pyc'", "-exec", "rm", "-rf", "{}", "+", external=True)
    session.log("Cleaned up the project directory.")

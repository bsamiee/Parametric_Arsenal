# noxfile.py
import nox


# A list of all source code paths to be checked.
SOURCE_PATHS = ("libs", "tests", "noxfile.py")

# Set the default sessions to run when you just type 'nox'
nox.options.sessions = ["lint", "mypy", "tests"]

# Define the default Python version
DEFAULT_PYTHON_VERSION = "3.13"

@nox.session(python=DEFAULT_PYTHON_VERSION)
def tests(session: nox.Session) -> None:
    """
    Run the test suite with pytest and generate a coverage report.

    Installs all project dependencies from poetry.lock before running.
    """
    session.install("poetry")
    # The --sync option ensures the venv is identical to the lock file
    session.run("poetry", "install", "--sync", external=True)
    # The --cov arguments activate coverage reporting.
    # The rest of the arguments are passed directly to pytest.
    session.run("pytest", "--cov=libs", "--cov-report=term-missing", *session.posargs)


@nox.session(python=DEFAULT_PYTHON_VERSION)
def lint(session: nox.Session) -> None:
    """
    Lint the code with ruff.

    Installs ruff independently of the project dependencies.
    """
    session.install("ruff")
    # Run the linter
    session.run("ruff", "check", *SOURCE_PATHS)
    # Run the formatter in check mode
    session.run("ruff", "format", "--check", *SOURCE_PATHS)


@nox.session(python=DEFAULT_PYTHON_VERSION)
def mypy(session: nox.Session) -> None:
    """
    Run the static type checker mypy.

    Installs all project dependencies from poetry.lock to resolve types.
    """
    session.install("poetry")
    session.run("poetry", "install", "--sync", external=True)
    session.run("mypy", *SOURCE_PATHS)


@nox.session(python=DEFAULT_PYTHON_VERSION)
def security(session: nox.Session) -> None:
    """
    Run a security audit with pip-audit.

    Exports the full dependency list from poetry to check for vulnerabilities.
    """
    session.install("poetry", "pip-audit")
    # Exporting dependencies to a requirements file for pip-audit
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
    session.run("rm", "requirements.txt", external=True)


@nox.session(python=DEFAULT_PYTHON_VERSION)
def dependencies(session: nox.Session) -> None:
    """Check for unused, missing, or transitive dependencies with deptry."""
    session.install("deptry")
    session.run("deptry", ".")


@nox.session(python=DEFAULT_PYTHON_VERSION)
def docs(session: nox.Session) -> None:
    """
    Build the documentation with MkDocs.

    Pass '--serve' to the session to run a live-reloading server.
    Example: nox -s docs -- --serve
    """
    session.install("mkdocs", "mkdocs-material")
    # Build the documentation
    build_command = ["mkdocs", "build", "--clean"]
    if "--serve" in session.posargs:
        # Run the live-reloading server
        session.log("Running mkdocs serve, press Ctrl+C to exit.")
        session.run("mkdocs", "serve")
    else:
        # Build the static site
        session.run(*build_command)
        session.log("Documentation built in site/ directory.")


@nox.session(python=DEFAULT_PYTHON_VERSION)
def analysis(session: nox.Session) -> None:
    """Run static analysis tools to check for dead code and code complexity."""
    session.install("radon", "vulture")
    # Check for dead code

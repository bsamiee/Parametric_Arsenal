"""
Title         : main.py Author        : Bardia Samiee Project       : parametric_arsenal License       : MIT Path :
libs/mzn_cli/main.py.

Description ------- A concise 1-3 sentence summary telling a new reader why this script exists.

"""

import importlib
import shutil
import subprocess

import typer


# Create main CLI app
cli = typer.Typer(name="mzn", help="Mazan Group CLI")


@cli.command()
def commit() -> None:
    """Run pre-commit hooks, then cz commit if hooks pass."""
    # Check if required tools are available
    pre_commit_path = shutil.which("pre-commit")
    cz_path = shutil.which("cz")

    if not pre_commit_path:
        typer.secho("pre-commit not found in PATH", fg=typer.colors.RED)
        raise typer.Exit(1)

    if not cz_path:
        typer.secho("cz (commitizen) not found in PATH", fg=typer.colors.RED)
        raise typer.Exit(1)

    try:
        typer.echo("Running pre-commit hooks...")
        result = subprocess.run([pre_commit_path, "run", "--hook-stage", "commit"], check=False)  # noqa: S603
        if result.returncode != 0:
            typer.secho("Pre-commit hooks failed. Please fix the issues above.", fg=typer.colors.RED)
            raise typer.Exit(1)
        typer.echo("Pre-commit hooks passed. Launching Commitizen...")
        cz_result = subprocess.run([cz_path, "commit"], check=False)  # noqa: S603
        if cz_result.returncode != 0:
            typer.secho("Commitizen failed.", fg=typer.colors.RED)
            raise typer.Exit(1)
    except FileNotFoundError as err:
        typer.secho(f"Required tool not found: {err}", fg=typer.colors.RED)
        raise typer.Exit(1) from err


def load_commands() -> None:
    """Auto-discover and load all Typer command modules listed in commands/__init__.py's __all__."""
    try:
        from apps.cli import commands  # type: ignore[import]
    except ImportError:
        import commands  # type: ignore[import,no-redef]

    command_names = getattr(commands, "__all__", [])

    if not command_names:
        msg = "commands/__init__.py must define __all__ for command discovery"
        raise RuntimeError(msg)

    for name in command_names:
        module_path = f"apps.cli.commands.{name}"
        module = importlib.import_module(module_path)

        if not hasattr(module, "app"):
            msg = f"Command module '{name}' must expose a Typer instance as `app`"
            raise AttributeError(msg)

        cli.add_typer(module.app, name=name)


# Load all commands dynamically
load_commands()


# Support `python -m apps.cli` by exposing a callable
def cli_entrypoint() -> None:
    """Entrypoint for running the CLI as a module."""
    cli()


if __name__ == "__main__":
    cli()

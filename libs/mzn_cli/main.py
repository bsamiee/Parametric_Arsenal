"""Parametric Arsenal CLI."""

import shutil
import subprocess

import typer


# Create main CLI app
cli = typer.Typer(name="pa", help="Parametric Arsenal CLI")


@cli.callback()
def main() -> None:
    """Parametric Arsenal CLI - Tools for architectural/parametric workflows."""


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


if __name__ == "__main__":
    cli()

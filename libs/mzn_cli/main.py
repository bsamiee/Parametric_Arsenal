"""Parametric Arsenal CLI."""


import typer


# Create main CLI app
cli = typer.Typer(name="pa", help="Parametric Arsenal CLI")


@cli.callback()
def main() -> None:
    """Parametric Arsenal CLI - Tools for architectural/parametric workflows."""


if __name__ == "__main__":
    cli()

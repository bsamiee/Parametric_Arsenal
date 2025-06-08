import typer
import importlib
import pkgutil
from typing import Optional
from pathlib import Path

# Create main CLI app
cli = typer.Typer(name="mzn", help="Mazan Group CLI")

# Auto-discover all modules listed in commands/__init__.py's __all__
def load_commands():
    try:
        from apps.cli import commands
    except ImportError:
        import commands  # fallback if running from cli/

    command_names = getattr(commands, "__all__", [])

    if not command_names:
        raise RuntimeError("commands/__init__.py must define __all__ for command discovery")

    for name in command_names:
        module_path = f"apps.cli.commands.{name}"
        module = importlib.import_module(module_path)

        if not hasattr(module, "app"):
            raise AttributeError(f"Command module '{name}' must expose a Typer instance as `app`")

        cli.add_typer(module.app, name=name)

# Load all commands dynamically
load_commands()

# Support `python -m apps.cli` by exposing a callable
def cli_entrypoint():
    cli()

if __name__ == "__main__":
    cli()

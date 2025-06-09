#!/usr/bin/env python3
"""
Title         : update_header.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : .tools/scripts/quality/update_header.py

Description
-------
A script to automatically add or update copyright/license headers in Python
files. It reads metadata from the root pyproject.toml and is designed to
be run via a pre-commit hook.
"""

import argparse
import sys
import tomllib
from pathlib import Path

from jinja2 import Template


# Placeholder text for the description when creating a new header.
NEW_FILE_DESCRIPTION_PLACEHOLDER = "A concise 1-3 sentence summary telling a new reader why this script exists."

HEADER_TEMPLATE = Template(
    '''"""
Title         : {{ file.name }}
Author        : {{ author }}
Project       : {{ project_name }}
License       : {{ license }}
Path          : {{ file_path }}

Description
-------
{{ description | wordwrap(120) }}
"""''',
    trim_blocks=True,
    lstrip_blocks=True,
)


def find_pyproject_toml(start_path: Path) -> Path | None:
    """Searches parent directories for pyproject.toml, starting from start_path."""
    current = start_path.resolve()
    while True:
        candidate = current / "pyproject.toml"
        if candidate.exists():
            return candidate
        if current.parent == current:
            return None
        current = current.parent


def get_project_metadata(root_path: Path) -> dict[str, str]:
    """Reads project metadata from the nearest pyproject.toml up the directory tree."""
    pyproject_path = find_pyproject_toml(root_path)
    if pyproject_path is None:
        sys.stderr.write("Error: pyproject.toml not found in any parent directory.\n")
        sys.exit(1)

    with pyproject_path.open("rb") as f:
        toml_data = tomllib.load(f)

    project_data = toml_data.get("project", {})
    authors = project_data.get("authors", [{"name": "Unknown Author"}])

    # All values must be str for the return type dict[str, str]
    return {
        "project_name": str(project_data.get("name", "Unknown Project")),
        "author": str(authors[0].get("name", "Unknown Author")),
        "license": (
            str(project_data.get("license", {}).get("text", "Not specified"))
            if isinstance(project_data.get("license", {}), dict)
            else str(project_data.get("license", "Not specified"))
        ),
    }


def process_file(file_path: Path, metadata: dict[str, str]) -> bool:
    """
    Adds or replaces the header in a single Python file, preserving the description.

    Returns True if the file was modified, False otherwise.
    """
    try:
        original_content = file_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return False  # File might have been deleted mid-run

    description_to_use = NEW_FILE_DESCRIPTION_PLACEHOLDER

    # If an old header exists, parse it to preserve the description.
    if original_content.startswith('"""'):
        try:
            # Isolate the full header block
            end_of_header_idx = original_content.index('"""', 3)
            header_content = original_content[3:end_of_header_idx]

            # Find the description within the block
            desc_marker = "Description\n-------\n"
            desc_start_idx = header_content.index(desc_marker) + len(desc_marker)
            description_to_use = header_content[desc_start_idx:].strip()
            # Find the end of the docstring to separate it from the code
            end_of_docstring_idx = end_of_header_idx + 3
        except ValueError:
            # Header is malformed or doesn't have a description; use placeholder
            end_of_docstring_idx = 0

        rest_of_file = original_content[end_of_docstring_idx:].lstrip()
    else:
        # No header exists, so the rest of the file is the original content
        rest_of_file = original_content

    # Create a copy of the metadata to add the description
    final_metadata = metadata.copy()
    final_metadata["description"] = description_to_use

    # Render the new header with file-specific info
    try:
        rel_file_path = file_path.relative_to(Path.cwd())
    except ValueError:
        rel_file_path = file_path
    new_header = HEADER_TEMPLATE.render(
        file=file_path,
        # Use a relative path for cleaner headers if possible
        file_path=rel_file_path,
        **final_metadata,
    )

    # Reconstruct the file content
    final_content = f"{new_header}\n\n{rest_of_file}"

    # Only write back to the file if content has actually changed
    if final_content != original_content:
        file_path.write_text(final_content, encoding="utf-8")
        sys.stdout.write(f"Updated header in: {file_path}\n")
        return True
    return False


def main() -> None:
    """Entry point for the update_header script."""
    parser = argparse.ArgumentParser(description="Update Python file headers.")
    parser.add_argument("files", nargs="*", help="Files to process.")
    args = parser.parse_args()

    repo_root = Path.cwd()  # This is now just the starting point
    metadata = get_project_metadata(repo_root)
    change_count = 0

    for file_str in args.files:
        file_path = Path(file_str)
        # Skip this script itself and any files outside the main projects folder
        if "scripts" in file_path.parts or not file_path.exists() or file_path.suffix != ".py":
            continue

        if process_file(file_path, metadata):
            change_count += 1

    if change_count > 0:
        # A non-zero exit code is important for pre-commit
        sys.exit(1)

    sys.exit(0)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
Refresh the mapping of TOML dependency names/import names to PyPI package names.
- Reads pyproject.toml and attempts to guess the correct PyPI name for each dependency.
- Updates scripts/pypi_name_map.py with new entries if needed.
- Keeps manual overrides at the top.
"""

import re
from pathlib import Path


PYPROJECT = Path(__file__).resolve().parents[1] / "pyproject.toml"
MAP_PY = Path(__file__).parent / "pypi_name_map.py"

# Manual overrides (keep in sync with pypi_name_map.py)
MANUAL_MAP = {
    "strawberry": "strawberry-graphql",
    "bs4": "beautifulsoup4",
    "yaml": "pyyaml",
    "dateutil": "python-dateutil",
    "dotenv": "python-dotenv",
    "magic": "python-magic",
    "pptx": "python-pptx",
    "docx": "python-docx",
    "jose": "python-jose",
    "PIL": "Pillow",
    "sqlalchemy": "SQLAlchemy",
    "duckdb": "DuckDB",
}

# Regex to extract dependency names from pyproject.toml
DEP_LINE_RE = re.compile(r"^\s*([A-Za-z0-9_.\-]+)\s*=")


def extract_toml_deps():
    deps = set()
    lines = PYPROJECT.read_text(encoding="utf-8").splitlines()
    in_deps = False
    for line in lines:
        if line.strip().startswith("[tool.poetry.dependencies]") or line.strip().startswith("[tool.poetry.group."):
            in_deps = True
            continue
        if in_deps and line.strip().startswith("["):
            in_deps = False
        if in_deps:
            m = DEP_LINE_RE.match(line)
            if m:
                deps.add(m.group(1))
    return deps


def main():
    deps = extract_toml_deps()
    mapping = dict(MANUAL_MAP)
    added = []
    for dep in deps:
        if dep not in mapping:
            mapping[dep] = dep  # Default: assume TOML key == PyPI name
            added.append(dep)
    # Write the mapping file
    with MAP_PY.open("w", encoding="utf-8") as f:
        f.write(
            '"""\nAuto-generated mapping from TOML dependency names/import names to PyPI package names.\nThis file is updated by scripts/refresh_pypi_name_map.py.\n"""\n',
        )
        f.write("PYPI_NAME_MAP = {\n")
        for k, v in sorted(mapping.items()):
            f.write(f'    "{k}": "{v}",\n')
        f.write("}\n")
    print(f"Updated {MAP_PY} with {len(mapping)} entries.")
    if added:
        print("Newly mapped dependencies (defaulted to TOML name):")
        for dep in added:
            print(f"  {dep} -> {dep}")
    else:
        print("No new dependencies were added to the mapping.")
    # Write a plain text mapping for shell scripts
    map_txt = MAP_PY.parent / "pypi_name_map.txt"
    with map_txt.open("w", encoding="utf-8") as ftxt:
        for k, v in sorted(mapping.items()):
            ftxt.write(f"{k} {v}\n")


if __name__ == "__main__":
    main()

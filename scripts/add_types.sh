#!/bin/bash

# A script to find untyped libraries and install their stubs with Poetry.
echo "🔍 Finding dependencies that are missing type stubs..."

PYPI_MAP_PY="$(dirname "$0")/pypi_name_map.py"

# Extract the mapping from the Python file (using Python for robust parsing)
PYPI_NAMES=$(python3 -c '
import sys
import ast
import os
map_path = os.path.join(os.path.dirname(sys.argv[1]), "pypi_name_map.py")
with open(map_path) as f:
    tree = ast.parse(f.read(), filename=map_path)
    for node in tree.body:
        if isinstance(node, ast.Assign) and node.targets[0].id == "PYPI_NAME_MAP":
            mapping = ast.literal_eval(node.value)
            for k, v in mapping.items():
                print(f"{k} {v}")
' "$PYPI_MAP_PY")

declare -A PYPI_MAP
while read -r toml pypi; do
    PYPI_MAP["$toml"]="$pypi"
done <<<"$PYPI_NAMES"

# Only check for stubs for dependencies in pyproject.toml (not local modules)
for dep in "${!PYPI_MAP[@]}"; do
    # Skip python itself
    if [[ "$dep" == "python" ]]; then
        continue
    fi
    # Check if types-<package> exists on PyPI (optional, can be removed for speed)
    types_pkg="types-${PYPI_MAP[$dep]}"
    echo "✅ Checking for type stubs for '$dep' (PyPI: ${PYPI_MAP[$dep]})..."
    poetry add --group dev "$types_pkg"
done

echo "🎉 Done."

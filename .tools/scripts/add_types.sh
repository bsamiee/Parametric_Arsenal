#!/usr/bin/env bash
# filepath: .tools/scripts/add_types.sh
#
# Professional script to find all dependencies in pyproject.toml,
# check for available type stubs on PyPI, and install them directly into the Poetry venv.
# Does NOT add them to pyproject.toml or poetry.lock.

set -euo pipefail

PYPROJECT="$(git rev-parse --show-toplevel)/pyproject.toml"

if ! command -v poetry >/dev/null; then
    echo "❌ Poetry is not installed. Aborting." >&2
    exit 1
fi

if ! command -v jq >/dev/null; then
    echo "❌ jq is required. Please install jq (brew install jq)." >&2
    exit 1
fi

# Get poetry venv path
VENV_PATH="$(poetry env info -p 2>/dev/null || true)"
if [[ -z "$VENV_PATH" ]]; then
    echo "❌ Poetry venv not found. Please run 'poetry install' first." >&2
    exit 1
fi
PIP="$VENV_PATH/bin/pip"

# Extract all dependencies (main, optional, and groups) from pyproject.toml using jq for robust TOML parsing
ALL_DEPS=$(jq -r '
  [
    (.tool.poetry.dependencies // {}),
    (.tool.poetry["group"] // {} | to_entries | map(.value.dependencies) | add | . // {}),
    (.project["optional-dependencies"] // {} | to_entries | map(.value) | add | . // [])
  ]
  | flatten | to_entries | map(.key) | unique | .[]' "$PYPROJECT")

# List of packages that are known to be typed or should be skipped
SKIP_PKGS=(python)

# Function to check if a value is in an array
in_array() {
    local val="$1"
    shift
    for item; do [[ "$item" == "$val" ]] && return 0; done
    return 1
}

FOUND=0
NOT_FOUND=0

for dep in $ALL_DEPS; do
    if in_array "$dep" "${SKIP_PKGS[@]}"; then
        continue
    fi
    types_pkg="types-$dep"
    # Check if types-<dep> exists on PyPI
    if curl -sSf "https://pypi.org/pypi/$types_pkg/json" | jq -e .info >/dev/null; then
        echo "✅ Installing type stubs for '$dep' ($types_pkg) ..."
        "$PIP" install "$types_pkg" && FOUND=$((FOUND + 1))
    else
        echo "❌ No type stubs found for '$dep'"
        NOT_FOUND=$((NOT_FOUND + 1))
    fi
    sleep 0.1 # Be nice to PyPI
done

printf "\n🎉 Done. Installed stubs for %d packages. %d had no stubs.\n" "$FOUND" "$NOT_FOUND"

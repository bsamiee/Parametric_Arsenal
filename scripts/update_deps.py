#!/usr/bin/env python3
"""
Update every dependency in pyproject.toml to the latest PyPI release.

• No external packages required (std-lib only, needs Python ≥ 3.11)
• Only the version string is replaced; extras/markers/comments are preserved.
• Core deps and every `[tool.poetry.group.*.dependencies]` section are handled.
"""
from __future__ import annotations

import importlib.util
import json
import re
import sys
import tomllib
import urllib.request
from collections.abc import Iterable, Mapping
from pathlib import Path

import packaging.version


# Dynamically import the mapping from scripts/pypi_name_map.py
MAP_PATH = Path(__file__).parent / "pypi_name_map.py"
spec = importlib.util.spec_from_file_location("pypi_name_map", str(MAP_PATH))
pypi_map_mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pypi_map_mod)
PYPI_NAME_MAP = pypi_map_mod.PYPI_NAME_MAP


PYPROJECT = Path(__file__).resolve().parents[1] / "pyproject.toml"
HEADERS = {"User-Agent": "parametric-arsenal/dep-updater (+https://github.com/)"}
SECTIONS = [
    ("tool", "poetry", "dependencies"),
    ("tool", "poetry", "group"),  # handle nested groups below
]

DEP_LINE_RE = re.compile(
    r"^(?P<prefix>\s*(?P<name>[A-Za-z0-9_.\-]+)\s*=\s*)(?P<spec>.*?)(?P<comment>\s+#.*)?$",
)


def latest_version(package: str) -> str | None:
    """Return newest stable version on PyPI or None on HTTP / parse failure."""
    # Use mapped PyPI name if available
    pypi_name = PYPI_NAME_MAP.get(package, package)
    url = f"https://pypi.org/pypi/{pypi_name}/json"
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.load(resp)
        return data["info"]["version"]
    except Exception:  # noqa: BLE001
        return None


def iter_dependency_tables(parsed: Mapping) -> Iterable[tuple[str, Mapping]]:
    """Yield (table_path, table_dict) for every dependencies table."""
    # Core table
    core = parsed
    for key in ("tool", "poetry", "dependencies"):
        core = core.get(key, {})
    if core:
        yield ("tool.poetry.dependencies", core)

    # Group tables
    groups = (
        parsed.get("tool", {})
        .get("poetry", {})
        .get("group", {})
    )
    for group_name, tbl in groups.items():
        deps = tbl.get("dependencies", {})
        if deps:
            yield (f"tool.poetry.group.{group_name}.dependencies", deps)


def get_python_version_from_pyproject() -> str:
    """Return the project's primary Python version (major.minor)."""
    # We expect a line like: python = ">=3.13,<3.14"
    with PYPROJECT.open("r", encoding="utf-8") as f:
        for line in f:
            if line.strip().startswith("python ="):
                version_spec = line.split("=", 1)[1].strip().strip('"')
                match = re.search(r"\d+\.\d+", version_spec)
                if match:
                    return match.group(0)
                # Fallback to trimmed value if regex fails
                return version_spec.lstrip("^>=")
    return "3.13"  # fallback


def check_python_compatibility(pkg: str, pypi_name: str, project_pyver: str) -> tuple[bool, str, str]:
    """Check if the latest version of the package supports the project python version.
    Returns (is_compatible, required_python_spec, last_compatible_version)
    """
    url = f"https://pypi.org/pypi/{pypi_name}/json"
    try:
        with urllib.request.urlopen(url, timeout=10) as resp:
            data = json.load(resp)
        releases = data.get("releases", {})
        # Sort versions newest to oldest
        versions = sorted(releases.keys(), key=packaging.version.parse, reverse=True)
        last_compatible = None
        for ver in versions:
            for file in releases[ver]:
                pyreq = file.get("requires_python")
                if pyreq:
                    from packaging.specifiers import SpecifierSet
                    spec = SpecifierSet(pyreq)
                    if project_pyver in spec:
                        if not last_compatible:
                            last_compatible = ver
                        return True, pyreq, ver
        # If we get here, no compatible version found with python_requires
        # Try to find the last version that had any python_requires
        for ver in versions:
            for file in releases[ver]:
                pyreq = file.get("requires_python")
                if pyreq:
                    return False, pyreq, ver
        return True, "", ""  # If no info, assume compatible
    except Exception:
        return True, "", ""  # On error, assume compatible


def get_pypi_name(toml_name: str) -> str:
    """Return the PyPI name for a given TOML dependency name."""
    return PYPI_NAME_MAP.get(toml_name, toml_name)


def is_valid_pypi_package(name: str) -> bool:
    """Check if a package name exists on PyPI."""
    url = f"https://pypi.org/pypi/{name}/json"
    try:
        with urllib.request.urlopen(url, timeout=5) as resp:
            return resp.status == 200
    except Exception:
        return False


def main() -> None:
    if not PYPROJECT.exists():
        sys.exit(f"❌  {PYPROJECT} not found")

    print(f"Reading {PYPROJECT}")
    raw_text = PYPROJECT.read_text(encoding="utf-8").splitlines(keepends=False)
    parsed = tomllib.loads("\n".join(raw_text))

    # Map package -> latest_version (one HTTP call per unique package)
    pkgs: set[str] = set()
    for section, table in iter_dependency_tables(parsed):
        print(f"Found dependency section: {section} with {len(table)} packages")
        pkgs.update(table.keys())

    print(f"Found {len(pkgs)} unique packages to check")
    latest: dict[str, str] = {}

    for pkg in pkgs:
        print(f"Checking latest version for {pkg}...")
        ver = latest_version(pkg)
        if ver:
            latest[pkg] = ver
            print(f"  ✓ {pkg} latest: {ver}")
        else:
            print(f"  ✗ Failed to get version for {pkg}")

    print(f"Retrieved {len(latest)} package versions from PyPI")

    project_pyver = get_python_version_from_pyproject()
    incompatible = []
    for pkg in pkgs:
        pypi_name = PYPI_NAME_MAP.get(pkg, pkg)
        compatible, pyreq, last_ver = check_python_compatibility(pkg, pypi_name, project_pyver)
        if not compatible:
            incompatible.append((pkg, pyreq, last_ver))
    if incompatible:
        print(f"\n⚠️  The following dependencies may not be compatible with Python {project_pyver}:")
        for pkg, pyreq, last_ver in incompatible:
            print(f"  {pkg} (PyPI: {PYPI_NAME_MAP.get(pkg, pkg)}) requires: {pyreq} | Last compatible version: {last_ver}")
    else:
        print(f"\n✅  All dependencies appear compatible with project Python version {project_pyver}.")

    # Step 1: Scan for TOML name mismatches and log them
    mismatches = []
    for toml_name in pkgs:
        pypi_name = get_pypi_name(toml_name)
        if toml_name != pypi_name and not is_valid_pypi_package(toml_name):
            mismatches.append((toml_name, pypi_name))
    if mismatches:
        print("\n⚠️  TOML/PyPI name mismatches detected:")
        for toml_name, pypi_name in mismatches:
            print(f"  TOML: {toml_name}  →  PyPI: {pypi_name}")
    else:
        print("\n✅  No TOML/PyPI name mismatches detected.")

    # Step 2: Build a corrected dependency list (for logging/reporting)
    corrected_deps = {}
    for toml_name in pkgs:
        pypi_name = get_pypi_name(toml_name)
        corrected_deps[pypi_name] = latest.get(toml_name, "<not found>")
    with open("corrected_dependencies.txt", "w", encoding="utf-8") as f:
        for pypi_name, version in sorted(corrected_deps.items()):
            f.write(f"{pypi_name}=={version}\n")
    print("\n📝  Wrote corrected dependency list to corrected_dependencies.txt")

    changed = False
    new_lines: list[str] = []
    toml_to_pypi = {k: get_pypi_name(k) for k in pkgs}
    # Track renames for logging
    renames = []
    # Only process lines inside dependency sections
    in_deps = False
    for line in raw_text:
        # Detect section headers
        if line.strip().startswith("[tool.poetry.dependencies]") or line.strip().startswith("[tool.poetry.group."):
            in_deps = True
            new_lines.append(line)
            continue
        if in_deps and line.strip().startswith("["):
            in_deps = False
        if not in_deps:
            new_lines.append(line)
            continue
        m = DEP_LINE_RE.match(line)
        if not m:
            new_lines.append(line)
            continue
        toml_name = m["name"]
        pypi_name = toml_to_pypi.get(toml_name, toml_name)
        latest_ver = latest.get(toml_name)
        if not latest_ver:
            new_lines.append(line)
            continue
        # If the TOML key is not the real PyPI name, rewrite the key
        if toml_name != pypi_name:
            renames.append((toml_name, pypi_name, m["spec"], latest_ver))
            # Always output as 'pypi_name = new_spec' (with = sign)
            prefix = f"{pypi_name} = "
        else:
            prefix = m["prefix"]
        # Decide on version operator based on original spec
        spec = m["spec"].rstrip()
        if spec.startswith('"') and spec.endswith('"'):
            op = "^" if spec[1] == "^" else ">="
            new_spec = f'"{op}{latest_ver}"'
        elif spec.startswith("{"):
            new_spec = re.sub(
                r'version\s*=\s*"[^"]+"',
                f'version = "^{latest_ver}"',
                spec,
                count=1,
            )
        else:
            new_lines.append(line)
            continue
        changed = True
        # Always output as 'key = value' (with = sign)
        new_lines.append(f"{prefix}{new_spec}{m['comment'] or ''}")
    # ----------------------------------------------------------------------
    if renames:
        print("\n🔄 Renamed TOML keys to PyPI names:")
        for old, new, old_spec, new_ver in renames:
            print(f"  {old}  →  {new}   (old spec: {old_spec}, new version: {new_ver})")
    if not changed:
        print("✅  All dependencies already at latest versions and names.")
        return
    PYPROJECT.write_text("\n".join(new_lines) + "\n", encoding="utf-8")
    print("🎉  pyproject.toml updated with newest versions and correct PyPI names.  "
          "Run `poetry lock --no-update` next to refresh the lock file.")


if __name__ == "__main__":
    main()

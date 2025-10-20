# r: numpy
"""
Title         : MultifoilArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/MultifoilArch.py

Description
----------------------------------------------------------------------------
Command for creating multifoil (cusped) arches with proper geometry.
"""

from __future__ import annotations

import importlib
import pathlib
import sys
from typing import Any

import Rhino.Geometry as rg
from libs import (
    ArchBuilderUtils,
    ArchCommandBase,
    ArchFamily,
    ArchSpec,
    MultifoilArchOptions,
    ProfileSelection,
    specs,
)
from libs.geometry import ProfileSegments, multifoil_profile

import Rhino


_script_dir = pathlib.Path(pathlib.Path(__file__).resolve()).parent
_plugin_root = pathlib.Path(_script_dir).parent
if _plugin_root not in sys.path:
    sys.path.insert(0, _plugin_root)

importlib.reload(specs)  # Force reload to ensure updated classes are recognized


# --- Multifoil Command Class ----------------------------------------------
class MultifoilArchCommand(ArchCommandBase):
    """Multifoil arch command using traditional geometric proportions."""

    options_type = MultifoilArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.MULTIFOIL, self.build_multifoil)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Calculate traditional multifoil arch proportions from geometry."""
        options = self.options_type.from_geometry(profile.span, profile.rise)
        return options.to_metadata()

    def build_multifoil(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed multifoil profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = multifoil_profile(
            spec.half_span,
            spec.rise,
            options,
            tolerance,
        )
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def MultifoilArch() -> None:
    """Execute the MultifoilArch command."""
    command = MultifoilArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: MultifoilArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        MultifoilArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    MultifoilArch()

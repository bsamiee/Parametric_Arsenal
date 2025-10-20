# r: numpy
"""
Title         : SemicircleArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/SemicircleArch.py

Description
----------------------------------------------------------------------------
Command for creating semicircular arches.
"""

from __future__ import annotations

import pathlib
import sys
from typing import Any

import Rhino.Geometry as rg
from libs import (
    ArchBuilderUtils,
    ArchCommandBase,
    ArchFamily,
    ArchSpec,
    EmptyArchOptions,
    ProfileSelection,
)
from libs.geometry import ProfileSegments, semicircle_profile

import Rhino


_script_dir = pathlib.Path(pathlib.Path(__file__).resolve()).parent
_plugin_root = pathlib.Path(_script_dir).parent
if _plugin_root not in sys.path:
    sys.path.insert(0, _plugin_root)


# --- Command Implementation -------------------------------------------------
class SemicircleArchCommand(ArchCommandBase):
    """Semicircular arch command using shared profile helpers."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.SEMICIRCLE, self.build_semicircle)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Semicircle arch has no additional parameters."""
        return self.options_type().to_metadata()

    def build_semicircle(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Return the closed semicircular profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()

        profile: ProfileSegments = semicircle_profile(spec.span)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def SemicircleArch() -> None:
    """Execute the SemicircleArch command."""
    command = SemicircleArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: SemicircleArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        SemicircleArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    SemicircleArch()

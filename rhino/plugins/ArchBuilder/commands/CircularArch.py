# r: numpy
"""
Title         : CircularArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/CircularArch.py

Description
----------------------------------------------------------------------------
Command for creating circular segment arches.
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
from libs.geometry import ProfileSegments, circular_segment_profile

import Rhino


_script_dir = pathlib.Path(pathlib.Path(__file__).resolve()).parent
_plugin_root = pathlib.Path(_script_dir).parent
if _plugin_root not in sys.path:
    sys.path.insert(0, _plugin_root)


# --- Command Implementation -------------------------------------------------
class CircularArchCommand(ArchCommandBase):
    """Circular segment arch command leveraging shared builders."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.CIRCULAR_SEGMENT, self.build_circular_segment)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Circular arches have no additional parameters."""
        return self.options_type().to_metadata()

    def build_circular_segment(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Build the circular segment profile with shared helpers."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        profile: ProfileSegments = circular_segment_profile(spec.half_span, spec.rise)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def CircularArch() -> None:
    """Execute the CircularArch command."""
    command = CircularArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: CircularArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        CircularArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    CircularArch()

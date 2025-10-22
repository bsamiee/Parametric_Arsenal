# r: numpy
"""
Title         : ThreeCenterArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/ThreeCenterArch.py

Description
----------------------------------------------------------------------------
Command for creating three-center (basket-handle) arches.
"""

from __future__ import annotations

from typing import Any

import Rhino.Geometry as rg
from libs.command_base import ArchCommandBase
from libs.geometry.profiles import ProfileSegments, three_center_profile
from libs.specs import ArchFamily, ArchSpec, ThreeCenterArchOptions
from libs.ui import ProfileSelection
from libs.utils import ArchBuilderUtils

import Rhino


# --- Three-Center Command Class -------------------------------------------
class ThreeCenterArchCommand(ArchCommandBase):
    """Three-center arch command using traditional geometric proportions."""

    options_type = ThreeCenterArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.THREE_CENTER, self.build_three_center)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Calculate traditional three-center arch proportions from geometry."""
        options = self.options_type.from_geometry(profile.span, profile.rise)
        return options.to_metadata()

    def build_three_center(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed three-center profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = three_center_profile(
            spec.span,
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
def ThreeCenterArch() -> None:
    """Execute the ThreeCenterArch command."""
    command = ThreeCenterArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: ThreeCenterArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        ThreeCenterArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    ThreeCenterArch()

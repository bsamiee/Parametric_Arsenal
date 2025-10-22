# r: numpy
"""
Title         : ParabolaArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/ParabolaArch.py

Description
----------------------------------------------------------------------------
Command for creating parabolic arches.
"""

from __future__ import annotations

from typing import Any

import Rhino.Geometry as rg
from libs.command_base import ArchCommandBase
from libs.geometry.profiles import ProfileSegments, parabola_profile
from libs.specs import ArchFamily, ArchSpec, EmptyArchOptions
from libs.ui import ProfileSelection
from libs.utils import ArchBuilderUtils

import Rhino


# --- Command Implementation -------------------------------------------------
class ParabolaArchCommand(ArchCommandBase):
    """Parabolic arch command using shared profile builders."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.PARABOLA, self.build_parabola)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Parabolic arch has no additional parameters."""
        return self.options_type().to_metadata()

    def build_parabola(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Return the closed parabolic profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()

        profile: ProfileSegments = parabola_profile(spec.half_span, spec.rise)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def ParabolaArch() -> None:
    """Execute the ParabolaArch command."""
    command = ParabolaArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: ParabolaArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        ParabolaArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    ParabolaArch()

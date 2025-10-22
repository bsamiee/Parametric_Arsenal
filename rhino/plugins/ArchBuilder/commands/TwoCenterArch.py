# r: numpy
"""
Title         : TwoCenterArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/TwoCenterArch.py

Description
----------------------------------------------------------------------------
Command for creating two-center (Tudor) arches.
"""

from __future__ import annotations

import sys
from pathlib import Path


sys.path.insert(0, str(Path(__file__).parent.parent / "libs"))

from typing import Any

import Rhino.Geometry as rg
from libs.command_base import ArchCommandBase
from libs.geometry.profiles import ProfileSegments, two_center_profile
from libs.specs import ArchFamily, ArchSpec, EmptyArchOptions
from libs.ui import ProfileSelection
from libs.utils import ArchBuilderUtils

import Rhino


# --- Command Implementation -------------------------------------------------
class TwoCenterArchCommand(ArchCommandBase):
    """Two-center arch command leveraging shared geometry helpers."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.TWO_CENTER, self.build_two_center)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Two-center arch has no additional parameters."""
        return self.options_type().to_metadata()

    def build_two_center(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Return the closed two-center profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()

        profile: ProfileSegments = two_center_profile(spec.half_span, spec.rise)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def TwoCenterArch() -> None:
    """Execute the TwoCenterArch command."""
    command = TwoCenterArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: TwoCenterArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        TwoCenterArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    TwoCenterArch()

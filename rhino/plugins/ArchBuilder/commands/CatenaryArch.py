# r: numpy
"""
Title         : CatenaryArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/CatenaryArch.py

Description
----------------------------------------------------------------------------
Command for creating catenary arches.
"""

from __future__ import annotations

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
from libs.geometry import ProfileSegments, catenary_profile

import Rhino


# --- Command Implementation -------------------------------------------------
class CatenaryArchCommand(ArchCommandBase):
    """Catenary arch command using shared profile builders."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.CATENARY, self.build_catenary)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Catenary arch has no additional parameters."""
        return self.options_type().to_metadata()

    def build_catenary(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Return the closed catenary profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        profile: ProfileSegments = catenary_profile(spec.half_span, spec.rise, tolerance)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def CatenaryArch() -> None:
    """Execute the CatenaryArch command."""
    command = CatenaryArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: CatenaryArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        CatenaryArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    CatenaryArch()

# r: numpy
"""
Title         : EllipseArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/EllipseArch.py

Description
----------------------------------------------------------------------------
Command for creating elliptical arches.
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
from libs.geometry import ProfileSegments, ellipse_profile

import Rhino


# --- Command Implementation -------------------------------------------------
class EllipseArchCommand(ArchCommandBase):
    """Elliptical arch command using shared profile generation."""

    options_type = EmptyArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.ELLIPSE, self.build_ellipse)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Ellipse arch has no custom parameters."""
        return self.options_type().to_metadata()

    def build_ellipse(self, spec: ArchSpec) -> rg.Curve:  # noqa: PLR6301
        """Return the closed elliptical profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        profile: ProfileSegments = ellipse_profile(spec.half_span, spec.rise)
        return ArchBuilderUtils.close_with_baseline(
            profile.segments,
            profile.start,
            profile.end,
            tolerance,
        )


# --- Command Entry Points -------------------------------------------------
def EllipseArch() -> None:
    """Execute the EllipseArch command."""
    command = EllipseArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: EllipseArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        EllipseArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    EllipseArch()

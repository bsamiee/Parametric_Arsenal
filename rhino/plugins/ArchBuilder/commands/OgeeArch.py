# r: numpy
"""
Title         : OgeeArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/OgeeArch.py

Description
----------------------------------------------------------------------------
Command for creating ogee (S-curve) arches from derived proportions.
"""

from __future__ import annotations

from typing import Any

import Rhino.Geometry as rg
from libs.command_base import ArchCommandBase
from libs.geometry.profiles import ProfileSegments, ogee_profile
from libs.specs import ArchFamily, ArchSpec, OgeeArchOptions
from libs.ui import ProfileSelection
from libs.utils import ArchBuilderUtils

import Rhino


# --- Ogee Command Class ---------------------------------------------------
class OgeeArchCommand(ArchCommandBase):
    """Ogee arch command using geometry-driven S-curve proportions."""

    options_type = OgeeArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.OGEE, self.build_ogee)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Derive ogee proportions heuristically from the selected geometry."""
        options = self.options_type.from_geometry(profile.span, profile.rise)
        return options.to_metadata()

    def build_ogee(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed ogee profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = ogee_profile(
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
def OgeeArch() -> None:
    """Execute the OgeeArch command."""
    command = OgeeArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: OgeeArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        OgeeArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    OgeeArch()

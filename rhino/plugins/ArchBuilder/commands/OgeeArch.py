# r: numpy
"""
Title         : OgeeArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/OgeeArch.py

Description
----------------------------------------------------------------------------
Command for creating ogee (S-curve) arches with direct control.
"""

from __future__ import annotations

from typing import Any

import Rhino.Geometry as rg
import Rhino.Input as ri
import Rhino.Input.Custom as ric
from libs import (
    ArchBuilderUtils,
    ArchCommandBase,
    ArchFamily,
    ArchSpec,
    OgeeArchOptions,
    ProfileSelection,
)
from libs.geometry import ProfileSegments, ogee_profile

import Rhino


# --- Ogee Command Class ---------------------------------------------------
class OgeeArchCommand(ArchCommandBase):
    """Ogee arch command with simplified S-curve control."""

    options_type = OgeeArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.OGEE, self.build_ogee)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Prompt for the inflection height and curvature strength."""
        defaults = self.options_type()

        go = ric.GetOption()
        go.SetCommandPrompt("Ogee arch parameters")
        go.AcceptNothing(True)

        opt_inflection = ric.OptionDouble(defaults.inflection_height, 0.2, 0.8)
        opt_strength = ric.OptionDouble(defaults.curve_strength, 0.1, 0.9)

        go.AddOptionDouble("InflectionHeight", opt_inflection)
        go.AddOptionDouble("CurveStrength", opt_strength)

        while True:
            result = go.Get()
            if result == ri.GetResult.Cancel:
                return None
            if result == ri.GetResult.Nothing:
                break
            if result == ri.GetResult.Option:
                continue
            break

        options = self.options_type(
            inflection_height=opt_inflection.CurrentValue,
            curve_strength=opt_strength.CurrentValue,
        )
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

# r: numpy
"""
Title         : HorseshoeArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/HorseshoeArch.py

Description
----------------------------------------------------------------------------
Command for creating horseshoe arches with direct geometric control.
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
    HorseshoeArchOptions,
    ProfileSelection,
)
from libs.geometry import ProfileSegments, horseshoe_profile

import Rhino


# --- Horseshoe Command Class ----------------------------------------------
class HorseshoeArchCommand(ArchCommandBase):
    """Horseshoe arch command with circular extension control."""

    options_type = HorseshoeArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.HORSESHOE, self.build_horseshoe)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Prompt for the sweep angle that defines how far the arc extends."""
        defaults = self.options_type()

        go = ric.GetOption()
        go.SetCommandPrompt("Horseshoe arch parameters")
        go.AcceptNothing(True)

        opt_extension = ric.OptionDouble(defaults.extension_degrees, 200.0, 270.0)
        go.AddOptionDouble("ExtensionDegrees", opt_extension)

        while True:
            result = go.Get()
            if result == ri.GetResult.Cancel:
                return None
            if result == ri.GetResult.Nothing:
                break
            if result == ri.GetResult.Option:
                continue
            break

        options = self.options_type(extension_degrees=opt_extension.CurrentValue)
        return options.to_metadata()

    def build_horseshoe(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed horseshoe profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = horseshoe_profile(
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
def HorseshoeArch() -> None:
    """Execute the HorseshoeArch command."""
    command = HorseshoeArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: HorseshoeArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        HorseshoeArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    HorseshoeArch()

# r: numpy
"""
Title         : FourCenterArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/FourCenterArch.py

Description
----------------------------------------------------------------------------
Command for creating four-center (depressed) arches.
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
    FourCenterArchOptions,
    ProfileSelection,
)
from libs.geometry import ProfileSegments, four_center_profile

import Rhino


# --- Four-Center Command Class --------------------------------------------
class FourCenterArchCommand(ArchCommandBase):
    """Four-center arch command with customizable parameters."""

    options_type = FourCenterArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.FOUR_CENTER, self.build_four_center)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Prompt for shoulder ratios that shape the depressed profile."""
        defaults = self.options_type()

        go = ric.GetOption()
        go.SetCommandPrompt("Four-center arch parameters")
        go.AcceptNothing(True)

        opt_shoulder = ric.OptionDouble(defaults.shoulder_ratio, 0.05, 0.95)
        opt_height = ric.OptionDouble(defaults.shoulder_height_ratio, 0.2, 0.95)

        go.AddOptionDouble("ShoulderRatio", opt_shoulder)
        go.AddOptionDouble("ShoulderHeight", opt_height)

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
            shoulder_ratio=opt_shoulder.CurrentValue,
            shoulder_height_ratio=opt_height.CurrentValue,
        )
        return options.to_metadata()

    def build_four_center(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed four-center profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = four_center_profile(
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
def FourCenterArch() -> None:
    """Execute the FourCenterArch command."""
    command = FourCenterArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: FourCenterArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        FourCenterArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    FourCenterArch()

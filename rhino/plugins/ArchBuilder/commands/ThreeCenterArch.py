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
import Rhino.Input as ri
import Rhino.Input.Custom as ric
from libs import (
    ArchBuilderUtils,
    ArchCommandBase,
    ArchFamily,
    ArchSpec,
    ProfileSelection,
    ThreeCenterArchOptions,
)
from libs.geometry import ProfileSegments, three_center_profile

import Rhino


# --- Three-Center Command Class -------------------------------------------
class ThreeCenterArchCommand(ArchCommandBase):
    """Three-center arch command with shoulder position control."""

    options_type = ThreeCenterArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.THREE_CENTER, self.build_three_center)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Prompt for the shoulder ratio that shapes the side arcs."""
        defaults = self.options_type()

        go = ric.GetOption()
        go.SetCommandPrompt("Three-center arch parameters")
        go.AcceptNothing(True)

        opt_shoulder = ric.OptionDouble(defaults.shoulder_ratio, 0.15, 0.45)
        go.AddOptionDouble("ShoulderRatio", opt_shoulder)

        while True:
            result = go.Get()
            if result == ri.GetResult.Cancel:
                return None
            if result == ri.GetResult.Nothing:
                break
            if result == ri.GetResult.Option:
                continue
            break

        options = self.options_type(shoulder_ratio=opt_shoulder.CurrentValue)
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

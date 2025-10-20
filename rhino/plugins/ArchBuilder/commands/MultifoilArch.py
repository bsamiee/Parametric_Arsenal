# r: numpy
"""
Title         : MultifoilArch.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/commands/MultifoilArch.py

Description
----------------------------------------------------------------------------
Command for creating multifoil (cusped) arches with proper geometry.
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
    MultifoilArchOptions,
    ProfileSelection,
)
from libs.geometry import ProfileSegments, multifoil_profile

import Rhino


# --- Multifoil Command Class ----------------------------------------------
class MultifoilArchCommand(ArchCommandBase):
    """Multifoil arch command with configurable lobes."""

    options_type = MultifoilArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.MULTIFOIL, self.build_multifoil)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Prompt for the number and relative size of cusps."""
        defaults = self.options_type()

        go = ric.GetOption()
        go.SetCommandPrompt("Multifoil arch parameters")
        go.AcceptNothing(True)

        opt_lobes = ric.OptionInteger(defaults.lobes, 3, 11)
        opt_size = ric.OptionDouble(defaults.lobe_size, 0.1, 1.0)

        go.AddOptionInteger("Lobes", opt_lobes)
        go.AddOptionDouble("LobeSize", opt_size)

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
            lobes=opt_lobes.CurrentValue,
            lobe_size=opt_size.CurrentValue,
        )
        return options.to_metadata()

    def build_multifoil(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed multifoil profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        profile: ProfileSegments = multifoil_profile(
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
def MultifoilArch() -> None:
    """Execute the MultifoilArch command."""
    command = MultifoilArchCommand()
    if command.execute():
        Rhino.RhinoApp.WriteLine("ArchBuilder: MultifoilArch created.")


def RunCommand(is_interactive: bool) -> int:
    try:
        MultifoilArch()
        return 0  # noqa: TRY300
    except RuntimeError as exc:
        Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
        return 0


if __name__ == "__main__":
    MultifoilArch()

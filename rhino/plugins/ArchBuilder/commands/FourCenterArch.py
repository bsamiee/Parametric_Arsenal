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
from libs.command_base import ArchCommandBase
from libs.geometry.profiles import ProfileSegments, four_center_profile
from libs.specs import ArchFamily, ArchSpec, FourCenterArchOptions
from libs.solvers import solve_four_center_parameters
from libs.ui import ProfileSelection
from libs.utils import ArchBuilderUtils

import Rhino


# --- Four-Center Command Class --------------------------------------------
class FourCenterArchCommand(ArchCommandBase):
    """Four-center arch command using traditional geometric proportions."""

    options_type = FourCenterArchOptions

    def __init__(self) -> None:
        super().__init__(ArchFamily.FOUR_CENTER, self.build_four_center)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:
        """Calculate traditional four-center arch proportions from geometry."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        default_options = self.options_type.from_geometry(profile.span, profile.rise)

        try:
            params = solve_four_center_parameters(
                profile.span,
                profile.rise,
                shoulder_ratio=default_options.shoulder_ratio,
                shoulder_height_ratio=default_options.shoulder_height_ratio,
                tolerance=tolerance,
            )
        except ValueError as exc:
            raise RuntimeError(
                (
                    "Unable to derive four-center proportions for the selected opening "
                    f"(span={profile.span:.3f}, rise={profile.rise:.3f}): {exc}"
                )
            ) from exc

        half_span = profile.span * 0.5
        shoulder_ratio = params.tangent_x / half_span if half_span else default_options.shoulder_ratio
        shoulder_height_ratio = params.tangent_y / profile.rise if profile.rise else default_options.shoulder_height_ratio

        clamped_ratio = max(0.05, min(0.95, shoulder_ratio))
        clamped_height = max(0.2, min(0.95, shoulder_height_ratio))

        if (
            abs(clamped_ratio - default_options.shoulder_ratio) > 1e-3
            or abs(clamped_height - default_options.shoulder_height_ratio) > 1e-3
        ):
            Rhino.RhinoApp.WriteLine(
                (
                    "ArchBuilder: adjusted four-center shoulder parameters to "
                    f"sr={clamped_ratio:.3f}, sh={clamped_height:.3f} to satisfy geometry."
                )
            )

        options = self.options_type(
            shoulder_ratio=clamped_ratio,
            shoulder_height_ratio=clamped_height,
        )
        return options.to_metadata()

    def build_four_center(self, spec: ArchSpec) -> rg.Curve:
        """Return the closed four-center profile for the requested spec."""
        tolerance = ArchBuilderUtils.model_absolute_tolerance()
        options = self.options_type.from_metadata(spec.metadata)

        try:
            profile: ProfileSegments = four_center_profile(
                spec.span,
                spec.rise,
                options,
                tolerance,
            )
        except ValueError as exc:
            raise RuntimeError(
                (
                    "Failed to construct four-center profile with resolved parameters "
                    f"(span={spec.span:.3f}, rise={spec.rise:.3f}): {exc}"
                )
            ) from exc
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

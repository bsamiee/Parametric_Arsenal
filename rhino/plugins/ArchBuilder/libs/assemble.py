"""
Title         : assemble.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/assemble.py

Description
----------------------------------------------------------------------------
Transform locally constructed arch profiles into document-space geometry.
"""

from __future__ import annotations

from dataclasses import dataclass

import Rhino.Geometry as rg

from .specs import ArchSpec
from .utils import ArchBuilderUtils


# --- Assembly Result Container --------------------------------------------
@dataclass
class ArchAssemblyResult:
    """Container for world-space profile results."""

    profile_world: rg.Curve
    all_curves: list[rg.Curve]


# --- Arch Assembler -------------------------------------------------------
class ArchAssembler:
    """Build final outline geometry from local arch profiles."""

    @staticmethod
    def build(spec: ArchSpec, profile_local: rg.Curve) -> ArchAssemblyResult:
        """Orient, transform, and package arch curves in world coordinates."""
        utils = ArchBuilderUtils

        oriented_profile = profile_local.DuplicateCurve()
        utils.orient_curve_outward(oriented_profile, spec.local_plane)

        profile_world = oriented_profile.DuplicateCurve()
        profile_world.Transform(spec.plane_transform)

        curves = [profile_world]

        return ArchAssemblyResult(
            profile_world=profile_world,
            all_curves=curves,
        )

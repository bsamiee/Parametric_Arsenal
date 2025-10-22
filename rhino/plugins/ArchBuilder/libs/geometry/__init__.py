"""
Title         : __init__.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/geometry/__init__.py

Description
----------------------------------------------------------------------------
Geometry package for arch construction calculations.
Exports mathematical utilities, curve construction functions, and arch solvers.
"""

from geometry.curves import arc_curve, arc_from_center_endpoints, circular_segment_radius, profile_curve
from geometry.math_utils import clamp, solve_newton
from geometry.parameters import (
    FoilArcParameters,
    FourCenterParameters,
    HorseshoeParameters,
    MultifoilParameters,
    OgeeParameters,
    ThreeCenterParameters,
)
from geometry.profiles import (
    ProfileSegments,
    catenary_profile,
    circular_segment_profile,
    ellipse_profile,
    four_center_profile,
    horseshoe_profile,
    multifoil_profile,
    ogee_profile,
    parabola_profile,
    semicircle_profile,
    three_center_profile,
    two_center_profile,
)


__all__ = [  # noqa: RUF022
    # Parameters
    "ThreeCenterParameters",
    "FourCenterParameters",
    "HorseshoeParameters",
    "OgeeParameters",
    "FoilArcParameters",
    "MultifoilParameters",
    # Curves
    "circular_segment_radius",
    "arc_from_center_endpoints",
    "arc_curve",
    "profile_curve",
    # Profiles
    "ProfileSegments",
    "catenary_profile",
    "parabola_profile",
    "ellipse_profile",
    "semicircle_profile",
    "circular_segment_profile",
    "two_center_profile",
    "three_center_profile",
    "four_center_profile",
    "ogee_profile",
    "horseshoe_profile",
    "multifoil_profile",
    # Math utilities
    "clamp",
    "solve_newton",
]

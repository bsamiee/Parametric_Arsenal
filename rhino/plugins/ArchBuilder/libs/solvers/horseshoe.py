"""
Title         : horseshoe.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/horseshoe.py

Description
----------------------------------------------------------------------------
Horseshoe arch parameter solver.
Computes the mathematical parameters needed to construct horseshoe arches.
"""

from __future__ import annotations

import math

from ..geometry.parameters import HorseshoeParameters


def solve_horseshoe_parameters(
    half_span: float,
    rise: float,
    extension_degrees: float,
    tolerance: float,
) -> HorseshoeParameters:
    """Solve for horseshoe arch parameters.

    Horseshoe arch extends beyond semicircle (>180°).

    Args:
        half_span: Half the horizontal span of the arch
        rise: Maximum vertical height of the arch (informational)
        extension_degrees: Total arc angle in degrees (must be > 180°)
        tolerance: Numerical tolerance for calculations

    Returns:
        Parameters for constructing the horseshoe arch

    Raises:
        ValueError: If parameters produce invalid geometry
    """
    if rise <= 0 or half_span <= 0:
        raise ValueError("Half-span and rise must be positive.")

    if extension_degrees <= 180.0:
        extension_degrees = 240.0  # Default horseshoe angle

    theta = math.radians(extension_degrees)
    half_theta = theta / 2.0

    # Radius from span and angle: span = 2R·sin(θ/2)
    radius = half_span / math.sin(half_theta)

    # Center position: y = -R·cos(θ/2)
    # For θ > 180°: cos(θ/2) < 0, so center_y > 0
    center_x = 0.0
    center_y = -radius * math.cos(half_theta)

    # Angles to base points
    angle_to_left = math.atan2(0.0 - center_y, -half_span)
    angle_to_right = math.atan2(0.0 - center_y, half_span)

    # Ensure arc > 180°
    if angle_to_right < angle_to_left:
        angle_to_right += 2.0 * math.pi

    return HorseshoeParameters(
        center_x=center_x,
        center_y=center_y,
        radius=radius,
        angle_left=angle_to_left,
        angle_right=angle_to_right,
    )

"""
Title         : multifoil.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/multifoil.py

Description
----------------------------------------------------------------------------
Multifoil arch parameter solver.
Computes the mathematical parameters needed to construct multifoil (cusped) arches.
"""

from __future__ import annotations

import math

from ..geometry.curves import circular_segment_radius
from ..geometry.parameters import FoilArcParameters, MultifoilParameters


def solve_multifoil_parameters(
    half_span: float,
    rise: float,
    lobes: int,
    lobe_size: float,
    tolerance: float,
) -> MultifoilParameters:
    """Solve for multifoil arch parameters.

    Creates cusped arcs distributed along the main arc.

    Args:
        half_span: Half the horizontal span of the arch
        rise: Maximum vertical height of the arch
        lobes: Number of foils/lobes (3-11)
        lobe_size: Size of each lobe relative to main arc (0.1-1.0)
        tolerance: Numerical tolerance for calculations

    Returns:
        Parameters for constructing the multifoil arch

    Raises:
        ValueError: If parameters produce invalid geometry
    """
    if rise <= 0 or half_span <= 0:
        raise ValueError("Half-span and rise must be positive.")

    if lobes < 3 or lobes > 11:
        raise ValueError("Number of lobes must be between 3 and 11.")

    if lobe_size < 0.1 or lobe_size > 1.0:
        raise ValueError("Lobe size must be between 0.1 and 1.0.")

    # Main arc parameters (circular segment)
    main_radius = circular_segment_radius(half_span, rise)
    main_center_y = rise - main_radius
    main_center_x = 0.0

    # Arc angles from left base to right base
    angle_to_left_base = math.atan2(0.0 - main_center_y, -half_span)
    angle_to_right_base = math.atan2(0.0 - main_center_y, half_span)
    total_angle = angle_to_right_base - angle_to_left_base

    foil_arcs = []
    for i in range(lobes):
        # Cusp points (division points on main arc)
        if i == 0:
            start_angle = angle_to_left_base
            start_pt_x = -half_span
            start_pt_y = 0.0
        else:
            start_angle = angle_to_left_base + (total_angle * i / lobes)
            start_pt_x = main_center_x + main_radius * math.cos(start_angle)
            start_pt_y = main_center_y + main_radius * math.sin(start_angle)

        if i == lobes - 1:
            end_angle = angle_to_right_base
            end_pt_x = half_span
            end_pt_y = 0.0
        else:
            end_angle = angle_to_left_base + (total_angle * (i + 1) / lobes)
            end_pt_x = main_center_x + main_radius * math.cos(end_angle)
            end_pt_y = main_center_y + main_radius * math.sin(end_angle)

        # Foil center positioned inside main arc to create inward cusps
        foil_center_angle = (start_angle + end_angle) / 2
        # Position foil center inside the main arc based on lobe_size
        # lobe_size controls how deep the cusps go inward
        foil_center_distance = main_radius * (1.0 - lobe_size * 0.3)  # 0.7 to 1.0 of main radius
        foil_center_x = main_center_x + foil_center_distance * math.cos(foil_center_angle)
        foil_center_y = main_center_y + foil_center_distance * math.sin(foil_center_angle)

        # Calculate radius to reach the cusp points on the main arc
        foil_radius = math.sqrt((start_pt_x - foil_center_x) ** 2 + (start_pt_y - foil_center_y) ** 2)
        # The radius should be the actual distance to the cusp points for proper geometry

        # Angles from foil center to cusp points
        vec_start_x = start_pt_x - foil_center_x
        vec_start_y = start_pt_y - foil_center_y
        vec_end_x = end_pt_x - foil_center_x
        vec_end_y = end_pt_y - foil_center_y

        angle_start = math.atan2(vec_start_y, vec_start_x)
        angle_end = math.atan2(vec_end_y, vec_end_x)

        # Ensure inward-curving arc
        angle_diff = angle_end - angle_start
        # Normalize to [-π, π]
        while angle_diff > math.pi:
            angle_diff -= 2 * math.pi
        while angle_diff <= -math.pi:
            angle_diff += 2 * math.pi

        if angle_diff < 0:
            angle_end = angle_start + angle_diff + 2 * math.pi

        foil_arcs.append(
            FoilArcParameters(
                center_x=foil_center_x,
                center_y=foil_center_y,
                radius=foil_radius,
                angle_start=angle_start,
                angle_end=angle_end,
            )
        )

    return MultifoilParameters(
        main_center_x=main_center_x,
        main_center_y=main_center_y,
        main_radius=main_radius,
        foil_arcs=foil_arcs,
    )

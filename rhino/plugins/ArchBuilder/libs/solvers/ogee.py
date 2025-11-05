"""
Title         : ogee.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/ogee.py

Description
----------------------------------------------------------------------------
Ogee (S-curve) arch parameter solver.
Computes the mathematical parameters needed to construct ogee arches.
"""

from __future__ import annotations

import math

from libs.geometry.parameters import OgeeParameters


def solve_ogee_parameters(  # noqa: PLR0912, PLR0915
    half_span: float,
    rise: float,
    inflection_height: float,
    curve_strength: float,
    tolerance: float,
) -> OgeeParameters:
    """Compute parameters for an ogee (S-curve) arch.

    Solves for arc centers that create a smooth S-curve with tangency
    at the inflection point where the curve changes from convex to concave.

    Args:
        half_span: Half the horizontal span of the arch
        rise: Maximum vertical height of the arch
        inflection_height: Height ratio of inflection point (0.2-0.8)
        curve_strength: Controls horizontal position of inflection (0.1-0.9)
        tolerance: Numerical tolerance for calculations

    Returns:
        Parameters for constructing the ogee arch

    Raises:
        ValueError: If parameters produce invalid geometry
    """
    if rise <= 0 or half_span <= 0:
        raise ValueError("Half-span and rise must be positive.")

    if inflection_height < 0.2 or inflection_height > 0.8:
        raise ValueError("Inflection height must be between 0.2 and 0.8.")

    if curve_strength < 0.1 or curve_strength > 0.9:
        raise ValueError("Curve strength must be between 0.1 and 0.9.")

    # Inflection point position
    inflection_y = rise * inflection_height
    inflection_x = half_span * (1.0 - curve_strength * 0.6)  # Stronger curve -> closer to center

    # Step 1: Find lower arc center (convex)
    # Arc passes through base (-half_span, 0) and inflection (-inflection_x, inflection_y)

    # Chord midpoint and direction
    mid_x = (-half_span - inflection_x) / 2.0
    mid_y = inflection_y / 2.0
    chord_dx = -inflection_x - (-half_span)
    chord_dy = inflection_y - 0.0

    # Perpendicular bisector direction
    perp_dx = -chord_dy
    perp_dy = chord_dx

    # Normalize perpendicular direction
    perp_length = math.sqrt(perp_dx * perp_dx + perp_dy * perp_dy)
    if perp_length <= tolerance:
        raise ValueError("Inflection point too close to base.")
    perp_dx /= perp_length
    perp_dy /= perp_length

    chord_length = math.sqrt(chord_dx * chord_dx + chord_dy * chord_dy)

    # Radius based on geometric mean of span and rise
    geometric_scale = math.sqrt(half_span * rise)
    radius_multiplier = 2.0 - curve_strength  # [1.9, 1.1] -> gentler to tighter curve
    desired_radius = geometric_scale * radius_multiplier

    # Find position on perpendicular bisector: r² = (chord/2)² + t²
    t_squared = desired_radius * desired_radius - (chord_length * 0.5) ** 2
    t = chord_length * 0.2 if t_squared < 0 else math.sqrt(t_squared)  # Fallback if radius too small

    # Place center for convex arc (center right of curve)
    if perp_dx > 0:
        cx_lower = mid_x + perp_dx * t
        cy_lower = mid_y + perp_dy * t
    else:
        cx_lower = mid_x - perp_dx * t  # Flip direction
        cy_lower = mid_y - perp_dy * t

    lower_radius = math.sqrt((cx_lower + half_span) ** 2 + cy_lower**2)

    # Step 2: Tangent at inflection (perpendicular to radius)
    radius_dx = -inflection_x - cx_lower
    radius_dy = inflection_y - cy_lower
    tangent_dx = -radius_dy
    tangent_dy = radius_dx

    # Normalize tangent direction
    tangent_length = math.sqrt(tangent_dx * tangent_dx + tangent_dy * tangent_dy)
    if tangent_length <= tolerance:
        raise ValueError("Invalid tangent calculation at inflection.")
    tangent_dx /= tangent_length
    tangent_dy /= tangent_length

    # Step 3: Find upper arc center (concave)
    # Center lies on line through inflection with tangent direction
    # Constraint: equal distance to inflection and apex

    # Solving |apex - center| = |inflection - center| = t
    # Reduces to linear equation (tangent is normalized):
    b = -2.0 * (inflection_x * tangent_dx + (rise - inflection_y) * tangent_dy)
    c = inflection_x * inflection_x + (rise - inflection_y) * (rise - inflection_y)

    if abs(b) < tolerance:
        raise ValueError("Unable to solve for upper center position.")

    t_upper = c / b

    # Ensure concave orientation (center left of curve)
    if t_upper < 0:
        tangent_dx = -tangent_dx
        tangent_dy = -tangent_dy
        b = -2.0 * (inflection_x * tangent_dx + (rise - inflection_y) * tangent_dy)
        t_upper = c / b

    cx_upper = -inflection_x + t_upper * tangent_dx
    cy_upper = inflection_y + t_upper * tangent_dy

    upper_radius = abs(t_upper)

    # Verify apex distance matches
    r_to_apex = math.sqrt(cx_upper * cx_upper + (rise - cy_upper) ** 2)
    if abs(upper_radius - r_to_apex) > tolerance:
        upper_radius = (upper_radius + r_to_apex) / 2.0  # Average if discrepancy

    if lower_radius <= tolerance:
        raise ValueError("Computed lower radius is invalid.")
    if upper_radius <= tolerance:
        raise ValueError("Computed upper radius is invalid.")

    # Verify tangency: radius vectors must be collinear and opposite
    v_lower_x = -inflection_x - cx_lower
    v_lower_y = inflection_y - cy_lower
    v_upper_x = -inflection_x - cx_upper
    v_upper_y = inflection_y - cy_upper

    cross_product = abs(v_lower_x * v_upper_y - v_lower_y * v_upper_x)
    dot_product = v_lower_x * v_upper_x + v_lower_y * v_upper_y

    v_lower_mag = math.sqrt(v_lower_x * v_lower_x + v_lower_y * v_lower_y)
    v_upper_mag = math.sqrt(v_upper_x * v_upper_x + v_upper_y * v_upper_y)

    if v_lower_mag > tolerance and v_upper_mag > tolerance:
        normalized_cross = cross_product / (v_lower_mag * v_upper_mag)
        if normalized_cross > tolerance * 10 or dot_product > 0:
            pass  # Continue with best approximation

    return OgeeParameters(
        tangent_x=inflection_x,
        tangent_y=inflection_y,
        lower_center_x=cx_lower,
        lower_center_y=cy_lower,
        lower_radius=lower_radius,
        upper_center_x=cx_upper,
        upper_center_y=cy_upper,
        upper_radius=upper_radius,
    )

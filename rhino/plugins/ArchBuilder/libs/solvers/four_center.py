"""
Title         : four_center.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/four_center.py

Description
----------------------------------------------------------------------------
Four-center (depressed/Tudor) arch parameter solver.
Computes the mathematical parameters needed to construct four-center arches.
"""

from __future__ import annotations

import math

from ..geometry.math_utils import clamp
from ..geometry.parameters import FourCenterParameters


def solve_four_center_parameters(
    span: float,
    rise: float,
    *,
    shoulder_ratio: float,
    shoulder_height_ratio: float,
    tolerance: float,
) -> FourCenterParameters:
    """Compute parameters for a four-center (depressed/Tudor) arch.

    Solves for the tangent points and arc radii that create a smooth
    four-arc approximation with specified shoulder geometry.

    The solution enforces tangency at the shoulder point by ensuring
    the radius vectors from adjacent arc centers are collinear.

    Args:
        span: Total horizontal span of the arch
        rise: Maximum vertical height of the arch
        shoulder_ratio: Horizontal position of shoulder point (0.05-0.95)
        shoulder_height_ratio: Vertical position of shoulder point (0.2-0.95)
        tolerance: Numerical tolerance for calculations

    Returns:
        Parameters for constructing the four-center arch

    Raises:
        ValueError: If parameters produce invalid geometry
    """
    if span <= 0 or rise <= 0:
        raise ValueError("Span and rise must be positive.")

    half_span = span * 0.5
    sr = clamp(shoulder_ratio, 0.05, 0.95)
    sh = clamp(shoulder_height_ratio, 0.2, 0.95)

    # Shoulder point (tangent point between lower and upper arcs)
    x_t = half_span * sr
    y_t = rise * sh

    # Lower arc center calculation
    # Center at (c, 0) must satisfy:
    # 1. Distance to base point (half_span, 0) equals radius
    # 2. Distance to shoulder point (x_t, y_t) equals radius
    # This gives: r1² = (half_span - c)² = (x_t - c)² + y_t²

    denominator = 2.0 * (half_span - x_t)
    if abs(denominator) <= tolerance:
        raise ValueError("Shoulder ratio is too close to the springing point.")

    c = (half_span * half_span - x_t * x_t - y_t * y_t) / denominator
    r1 = half_span - c

    if r1 <= tolerance:
        raise ValueError("Computed lower radius is non-positive.")

    # Verify lower arc geometry
    r1_check = math.sqrt((x_t - c) ** 2 + y_t**2)
    if abs(r1 - r1_check) > tolerance:
        raise ValueError("Lower arc geometry is inconsistent.")

    # Upper arc center calculation with tangency constraint
    # For tangency, the vectors from centers to shoulder must be collinear:
    # (x_t - c, y_t - 0) and (x_t - d, y_t - h2) are collinear
    # This means: (x_t - c)/y_t = (x_t - d)/(y_t - h2)
    # Rearranging: d = x_t - (x_t - c) * (y_t - h2) / y_t

    # Upper arc must also pass through apex (0, rise)
    # Distance formula: r2² = d² + (h2 - rise)² = (x_t - d)² + (y_t - h2)²

    # From tangency constraint: d - x_t = -(x_t - c) * (y_t - h2) / y_t
    # Let k = (x_t - c) / y_t (slope of lower radius at shoulder)
    k = (x_t - c) / y_t

    # Substituting into apex distance constraint and solving for h2:
    # This yields a quadratic equation in h2
    a_coef = 1.0 + k * k
    b_coef = -2.0 * rise + 2.0 * k * x_t
    c_coef = rise * rise + x_t * x_t - (x_t + k * y_t) ** 2

    discriminant = b_coef * b_coef - 4.0 * a_coef * c_coef
    if discriminant < -tolerance:
        raise ValueError("No valid upper center solution exists.")

    discriminant = max(0.0, discriminant)  # Handle numerical errors

    # Choose solution that places center above shoulder
    h2_1 = (-b_coef + math.sqrt(discriminant)) / (2.0 * a_coef)
    h2_2 = (-b_coef - math.sqrt(discriminant)) / (2.0 * a_coef)

    h2 = h2_1 if h2_1 > y_t else h2_2

    if h2 <= y_t + tolerance:
        raise ValueError("Upper center must lie above the shoulder point.")

    # Calculate d from tangency constraint
    d = x_t - k * (y_t - h2)

    # Calculate upper radius
    r2_from_shoulder = math.sqrt((x_t - d) ** 2 + (y_t - h2) ** 2)
    r2_from_apex = math.sqrt(d * d + (h2 - rise) ** 2)

    # Verify consistency
    if abs(r2_from_shoulder - r2_from_apex) > tolerance:
        # Try to adjust for numerical errors
        r2 = (r2_from_shoulder + r2_from_apex) * 0.5
    else:
        r2 = r2_from_shoulder

    if r2 <= tolerance:
        raise ValueError("Computed upper radius is invalid.")

    return FourCenterParameters(
        lower_center_offset=c,
        lower_radius=r1,
        upper_center_offset=d,
        upper_center_height=h2,
        upper_radius=r2,
        tangent_x=x_t,
        tangent_y=y_t,
    )

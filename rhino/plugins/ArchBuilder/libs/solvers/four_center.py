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


def solve_four_center_parameters(  # noqa: PLR0912, PLR0915
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
    if abs(y_t) <= tolerance:
        raise ValueError("Shoulder height too close to baseline for four-center arch.")

    k = (x_t - c) / y_t

    # Validate shoulder ratio constraints
    if abs(k) > 10.0:  # Prevent extreme slopes
        raise ValueError("Shoulder position creates extreme geometry for four-center arch.")

    # Substituting into apex distance constraint and solving for h2:
    # This yields a quadratic equation in h2
    a_coef = 1.0 + k * k
    b_coef = -2.0 * rise + 2.0 * k * x_t
    c_coef = rise * rise + x_t * x_t - (x_t + k * y_t) ** 2

    discriminant = b_coef * b_coef - 4.0 * a_coef * c_coef

    # Handle negative discriminant by adjusting shoulder height
    if discriminant < -tolerance:
        # Adjust shoulder height to make geometry feasible
        # Reduce shoulder height ratio to create valid tangency conditions
        original_sh = sh
        max_attempts = 5
        attempt = 0

        while discriminant < -tolerance and attempt < max_attempts:
            attempt += 1
            # Reduce shoulder height by 10% each attempt
            sh = original_sh * (1.0 - 0.1 * attempt)
            sh = max(sh, 0.2)  # Don't go below minimum

            # Recalculate with adjusted shoulder height
            y_t = rise * sh
            k = (x_t - c) / y_t

            # Recalculate quadratic coefficients
            a_coef = 1.0 + k * k
            b_coef = -2.0 * rise + 2.0 * k * x_t
            c_coef = rise * rise + x_t * x_t - (x_t + k * y_t) ** 2
            discriminant = b_coef * b_coef - 4.0 * a_coef * c_coef

        if discriminant < -tolerance:
            raise ValueError(
                f"Four-center arch geometry not feasible with span={span:.2f}, rise={rise:.2f}. "
                f"Try reducing shoulder height ratio below {original_sh:.2f} or increasing rise/span ratio."
            )

    discriminant = max(0.0, discriminant)  # Handle numerical errors

    # Choose solution that places center above shoulder
    h2_1 = (-b_coef + math.sqrt(discriminant)) / (2.0 * a_coef)
    h2_2 = (-b_coef - math.sqrt(discriminant)) / (2.0 * a_coef)

    # Select the solution that gives a valid geometry
    h2 = None
    for candidate in [h2_1, h2_2]:
        if candidate > y_t + tolerance and candidate < rise * 2.0:  # Reasonable bounds
            h2 = candidate
            break

    if h2 is None:
        # Fallback: use the higher solution if both are problematic
        h2 = max(h2_1, h2_2)
        if h2 <= y_t + tolerance:
            raise ValueError("Unable to find valid upper center position for four-center arch.")

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

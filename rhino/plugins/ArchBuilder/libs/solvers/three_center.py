"""
Title         : three_center.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/three_center.py

Description
----------------------------------------------------------------------------
Three-center (basket-handle) arch parameter solver.
Computes the mathematical parameters needed to construct three-center arches.
"""

from __future__ import annotations

import math

from ..geometry.math_utils import clamp
from ..geometry.parameters import ThreeCenterParameters


def solve_three_center_parameters(  # noqa: PLR0912, PLR0915
    span: float,
    rise: float,
    shoulder_ratio: float,
    tolerance: float,
) -> ThreeCenterParameters:
    """Compute parameters for a three-center (basket-handle) arch.

    Solves for the tangent point and arc radii that create a smooth
    three-arc approximation with specified shoulder position.

    Args:
        span: Total horizontal span of the arch
        rise: Maximum vertical height of the arch
        shoulder_ratio: Ratio controlling shoulder position (0.15-0.45)
        tolerance: Numerical tolerance for calculations

    Returns:
        Parameters for constructing the three-center arch

    Raises:
        ValueError: If parameters produce invalid geometry
    """
    if span <= 0 or rise <= 0:
        raise ValueError("Span and rise must be positive.")

    half_span = span * 0.5
    x_t = half_span * clamp(shoulder_ratio, 0.15, 0.45)

    def slope_difference(y_t: float) -> float:
        if y_t <= tolerance or y_t >= rise - tolerance:
            return math.copysign(math.inf, y_t - (rise * 0.5))

        numerator = x_t * x_t + y_t * y_t - rise * rise
        denominator = 2.0 * (y_t - rise)
        if abs(denominator) <= tolerance:
            return math.copysign(math.inf, denominator)
        c1 = numerator / denominator
        if c1 >= 0.0:
            return math.copysign(math.inf, c1 + tolerance)

        denom_d = 2.0 * (half_span - x_t)
        if abs(denom_d) <= tolerance:
            return math.copysign(math.inf, denom_d)
        d = (half_span * half_span - x_t * x_t - y_t * y_t) / denom_d
        if not (0.0 < d < half_span):
            direction = -1.0 if d <= 0.0 else 1.0
            return math.copysign(math.inf, direction)

        slope_central = x_t / (y_t - c1)
        slope_side = (x_t - d) / y_t
        return slope_central - slope_side

    y_min = max(tolerance * 10.0, rise * 0.1)
    y_max = min(rise - tolerance * 10.0, rise * 0.9)
    samples = 96
    prev_y = y_min
    prev_val = slope_difference(prev_y)
    root_interval: tuple[float, float] | None = None
    for i in range(1, samples + 1):
        y = y_min + (y_max - y_min) * (i / samples)
        val = slope_difference(y)
        if math.isfinite(prev_val) and math.isfinite(val) and prev_val * val <= 0:
            root_interval = (prev_y, y)
            break
        if abs(val) < abs(prev_val):
            prev_y, prev_val = y, val
    if root_interval is None:
        raise ValueError("Unable to determine tangency height for three-center arch.")

    low, high = root_interval
    f_low = slope_difference(low)
    _ = slope_difference(high)  # Initial f_high not used in bisection
    y_t = 0.5 * (low + high)
    for _ in range(64):
        mid = 0.5 * (low + high)
        f_mid = slope_difference(mid)
        if not math.isfinite(f_mid):
            break
        if abs(f_mid) <= tolerance:
            y_t = mid
            break
        if f_low * f_mid <= 0:
            high = mid  # Update high bound only
        else:
            low, f_low = mid, f_mid
        y_t = 0.5 * (low + high)

    numerator = x_t * x_t + y_t * y_t - rise * rise
    denominator = 2.0 * (y_t - rise)
    c1 = numerator / denominator
    if c1 >= -tolerance:
        raise ValueError("Central centre is not below baseline in three-center solution.")

    denom_d = 2.0 * (half_span - x_t)
    d = (half_span * half_span - x_t * x_t - y_t * y_t) / denom_d
    if not (0.0 < d < half_span):
        raise ValueError("Side centre lies outside valid range in three-center solution.")

    central_radius = rise - c1
    side_radius = half_span - d

    # Validate computed parameters
    if central_radius <= tolerance:
        raise ValueError("Computed central radius is non-positive.")
    if side_radius <= tolerance:
        raise ValueError("Computed side radius is non-positive.")

    # Verify that computed radii are consistent
    # Side arc should pass through both base and tangent point
    side_radius_check = math.sqrt((x_t - d) ** 2 + y_t**2)
    if abs(side_radius - side_radius_check) > tolerance:
        raise ValueError(f"Side arc geometry inconsistent: radius mismatch {abs(side_radius - side_radius_check)}")

    # Central arc should pass through tangent point and apex
    central_radius_from_tangent = math.sqrt(x_t**2 + (y_t - c1) ** 2)
    if abs(central_radius - central_radius_from_tangent) > tolerance:
        raise ValueError(
            f"Central arc geometry inconsistent: radius mismatch {abs(central_radius - central_radius_from_tangent)}"
        )

    return ThreeCenterParameters(
        side_center_offset=d,
        central_center_y=c1,
        tangent_x=x_t,
        tangent_y=y_t,
        central_radius=central_radius,
        side_radius=side_radius,
    )

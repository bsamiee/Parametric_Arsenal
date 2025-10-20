"""
Title         : catenary.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/catenary.py

Description
----------------------------------------------------------------------------
Catenary arch parameter solver for computing the catenary parameter 'a'
that produces an arch with specified span and rise.
"""

from __future__ import annotations

import math


def solve_catenary_parameter(half_span: float, rise: float, tolerance: float) -> float:  # noqa: PLR0912
    """Solve for the catenary parameter 'a' in y = a(cosh(x/a) - 1).

    Uses bracketing with safeguarded Newton-Raphson iteration.

    Args:
        half_span: Half the horizontal span of the arch
        rise: Maximum vertical height of the arch
        tolerance: Convergence tolerance

    Returns:
        The catenary parameter 'a'

    Raises:
        ValueError: If parameters are invalid or solver fails to converge
    """
    if rise <= 0 or half_span <= 0:
        raise ValueError("Half-span and rise must be positive.")

    def equation(a: float) -> float:
        """Target equation: a*cosh(half_span/a) - a - rise = 0."""
        x = half_span / a
        if x > 40.0:  # Prevent overflow
            return float("inf")
        return a * math.cosh(x) - a - rise

    def derivative(a: float) -> float:
        """Derivative with respect to 'a'."""
        x = half_span / a
        if x > 40.0:  # Prevent overflow
            return float("inf")
        return math.cosh(x) - x * math.sinh(x) - 1.0

    # Find bracketing interval
    a_low = max(min(half_span, rise), tolerance)
    f_low = equation(a_low)
    if f_low <= 0.0:
        for _ in range(64):
            a_low *= 0.5
            a_low = max(tolerance, a_low)
            f_low = equation(a_low)
            if f_low > 0.0:
                break
        else:
            raise ValueError("Unable to bracket catenary parameter (lower bound).")

    a_high = max(rise, half_span)
    f_high = equation(a_high)
    if f_high >= 0.0 or not math.isfinite(f_high):
        for _ in range(64):
            a_high *= 2.0
            f_high = equation(a_high)
            if f_high < 0.0:
                break
        else:
            raise ValueError("Unable to bracket catenary parameter (upper bound).")

    # Safeguarded Newton-Raphson iteration
    value = max(rise, half_span * 0.5)
    for _ in range(50):
        f_val = equation(value)
        if abs(f_val) <= tolerance:
            return value

        df = derivative(value)
        if not math.isfinite(df) or abs(df) < 1e-9:
            value = 0.5 * (a_low + a_high)  # Bisection fallback
        else:
            newton = value - f_val / df
            # Keep within bracket
            value = 0.5 * (a_low + a_high) if newton <= a_low or newton >= a_high else newton

        # Update bracket
        f_val = equation(value)
        if abs(f_val) <= tolerance:
            return value
        if f_val > 0.0:
            a_low = value
        else:
            a_high = value
    raise ValueError("Catenary parameter solver failed to converge.")

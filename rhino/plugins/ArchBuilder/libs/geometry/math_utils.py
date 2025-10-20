"""
Title         : math_utils.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/geometry/math_utils.py

Description
----------------------------------------------------------------------------
General-purpose mathematical utilities and numerical solvers.
"""

from __future__ import annotations

from typing import Callable


# --- Clamp ----------------------------------------------------------------
def clamp(value: float, minimum: float, maximum: float) -> float:
    """Clamp a value between minimum and maximum bounds.

    Args:
        value: The value to clamp
        minimum: Lower bound
        maximum: Upper bound

    Returns:
        Clamped value within [minimum, maximum]
    """
    return max(minimum, min(maximum, value))


# --- Solve Newton ---------------------------------------------------------
def solve_newton(
    func: Callable[[float], float],
    derivative: Callable[[float], float],
    initial_guess: float,
    tolerance: float,
    max_iterations: int = 50,
) -> float:
    """Solve a non-linear equation using Newton-Raphson method.

    Args:
        func: Function to find root of
        derivative: Derivative of the function
        initial_guess: Starting point for iteration
        tolerance: Convergence tolerance
        max_iterations: Maximum number of iterations

    Returns:
        Root of the equation

    Raises:
        ValueError: If solver fails to converge
    """
    value = initial_guess
    for _ in range(max_iterations):
        f_val = func(value)
        if abs(f_val) <= tolerance:
            return value
        df = derivative(value)
        if abs(df) < 1e-9:
            break
        value -= f_val / df
    raise ValueError("Newton solver failed to converge.")

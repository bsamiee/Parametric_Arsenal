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
from collections.abc import Iterator, Sequence

from libs.geometry.math_utils import clamp
from libs.geometry.parameters import FourCenterParameters


_MIN_SHOULDER_RATIO = 0.05
_MAX_SHOULDER_RATIO = 0.95
_MIN_SHOULDER_HEIGHT_RATIO = 0.2
_MAX_SHOULDER_HEIGHT_RATIO = 0.95
_MAX_FAILURE_SAMPLES = 6


def _round_key(value: float) -> float:
    """Return a rounded value suitable for set membership comparisons."""
    return round(value, 6)


def _prioritized_values(
    base: float,
    minimum: float,
    maximum: float,
    *,
    step: float,
    extras: Sequence[float] = (),
) -> list[float]:
    """Generate candidate values ordered by proximity to the base value."""
    base_clamped = clamp(base, minimum, maximum)
    values: list[float] = []
    seen: set[float] = set()

    def push(raw: float) -> None:
        clamped = clamp(raw, minimum, maximum)
        key = _round_key(clamped)
        if key in seen:
            return
        seen.add(key)
        values.append(clamped)

    push(base_clamped)

    for value in sorted(extras, key=lambda candidate: abs(candidate - base_clamped)):
        push(value)

    total_steps = max(1, round((maximum - minimum) / step))
    grid: set[float] = set()
    grid.update(_round_key(minimum + step * idx) for idx in range(total_steps + 1))
    grid.add(_round_key(maximum))

    ordered_grid = sorted(grid, key=lambda candidate: (abs(candidate - base_clamped), candidate))
    for value in ordered_grid:
        push(value)

    return values


def _candidate_pairs(
    shoulder_ratios: Sequence[float],
    shoulder_height_ratios: Sequence[float],
) -> Iterator[tuple[float, float]]:
    """Yield unique shoulder ratio combinations in priority order."""
    if not shoulder_ratios or not shoulder_height_ratios:
        return

    seen: set[tuple[float, float]] = set()
    base_sr = shoulder_ratios[0]
    base_sh = shoulder_height_ratios[0]

    def push(sr: float, sh: float) -> Iterator[tuple[float, float]]:
        key = (_round_key(sr), _round_key(sh))
        if key in seen:
            return
        seen.add(key)
        yield (sr, sh)

    # Start with the base combination
    yield from push(base_sr, base_sh)

    # Vary shoulder ratio while keeping shoulder height fixed first
    for sr in shoulder_ratios:
        yield from push(sr, base_sh)

    # Vary shoulder height while keeping shoulder ratio fixed
    for sh in shoulder_height_ratios:
        yield from push(base_sr, sh)

    # Explore the remaining grid
    for sh in shoulder_height_ratios:
        for sr in shoulder_ratios:
            yield from push(sr, sh)


def _solve_with_fixed_tangent(
    span: float,
    rise: float,
    shoulder_ratio: float,
    shoulder_height_ratio: float,
    tolerance: float,
) -> FourCenterParameters:
    """Solve the four-center system for a fixed shoulder configuration."""
    if span <= 0 or rise <= 0:
        raise ValueError("Span and rise must be positive.")

    half_span = span * 0.5
    sr = clamp(shoulder_ratio, _MIN_SHOULDER_RATIO, _MAX_SHOULDER_RATIO)
    sh = clamp(shoulder_height_ratio, _MIN_SHOULDER_HEIGHT_RATIO, _MAX_SHOULDER_HEIGHT_RATIO)

    x_t = half_span * sr
    y_t = rise * sh

    denominator = 2.0 * (half_span - x_t)
    if abs(denominator) <= tolerance:
        raise ValueError("Shoulder ratio produces a degenerate lower-center configuration.")

    c = (half_span * half_span - x_t * x_t - y_t * y_t) / denominator
    r1 = half_span - c
    if r1 <= tolerance:
        raise ValueError("Lower radius collapses for the requested proportions.")

    r1_check = math.sqrt((x_t - c) ** 2 + y_t**2)
    if abs(r1 - r1_check) > tolerance:
        raise ValueError("Lower arc geometry is inconsistent with span/rise.")

    if abs(y_t) <= tolerance:
        raise ValueError("Shoulder height sits on the baseline; no tangent solution.")

    k = (x_t - c) / y_t
    if abs(k) > 10.0:
        raise ValueError("Shoulder slope is extreme; try moving the tangent outward or upward.")

    a_coef = 1.0 + k * k
    b_coef = -2.0 * rise + 2.0 * k * x_t
    c_coef = rise * rise + x_t * x_t - (x_t + k * y_t) ** 2

    discriminant = b_coef * b_coef - 4.0 * a_coef * c_coef
    if discriminant < -tolerance:
        raise ValueError("Upper arc discriminant is negative; no real solution.")

    discriminant = max(0.0, discriminant)
    sqrt_disc = math.sqrt(discriminant)
    h2_candidates = (
        (-b_coef + sqrt_disc) / (2.0 * a_coef),
        (-b_coef - sqrt_disc) / (2.0 * a_coef),
    )

    h2 = next(
        (
            candidate
            for candidate in h2_candidates
            if candidate > y_t + tolerance and candidate < rise * 2.0
        ),
        None,
    )
    if h2 is None:
        h2 = max(h2_candidates)
        if h2 <= y_t + tolerance:
            raise ValueError("Upper centre collapses toward the tangent point.")

    d = x_t - k * (y_t - h2)

    r2_from_shoulder = math.sqrt((x_t - d) ** 2 + (y_t - h2) ** 2)
    r2_from_apex = math.sqrt(d * d + (h2 - rise) ** 2)
    r2 = (r2_from_shoulder + r2_from_apex) * 0.5

    if r2 <= tolerance:
        raise ValueError("Upper radius collapses for the requested proportions.")

    return FourCenterParameters(
        lower_center_offset=c,
        lower_radius=r1,
        upper_center_offset=d,
        upper_center_height=h2,
        upper_radius=r2,
        tangent_x=x_t,
        tangent_y=y_t,
    )


def _shoulder_ratio_extras(base: float, rise_ratio: float) -> list[float]:
    """Provide heuristically useful shoulder ratios."""
    heuristics = [
        0.18,
        0.22,
        0.25,
        0.28,
        0.3,
        0.32,
        0.35,
        0.38,
        0.4,
        0.45,
        base * 0.8,
        base * 0.9,
        base * 1.1,
        base * 1.2,
    ]
    adaptive = clamp(0.18 + 0.25 * rise_ratio, _MIN_SHOULDER_RATIO, 0.6)
    heuristics.append(adaptive)
    return heuristics


def _shoulder_height_extras(base: float, rise_ratio: float) -> list[float]:
    """Provide heuristically useful shoulder height ratios."""
    heuristics = [
        0.35,
        0.4,
        0.45,
        0.5,
        0.55,
        0.6,
        0.65,
        0.7,
        0.75,
        0.8,
        0.85,
        base * 0.9,
        base * 1.1,
    ]
    adaptive = clamp(0.45 + 0.35 * rise_ratio, _MIN_SHOULDER_HEIGHT_RATIO, _MAX_SHOULDER_HEIGHT_RATIO)
    heuristics.append(adaptive)
    return heuristics


def solve_four_center_parameters(
    span: float,
    rise: float,
    *,
    shoulder_ratio: float,
    shoulder_height_ratio: float,
    tolerance: float,
) -> FourCenterParameters:
    """Compute parameters for a four-center (depressed/Tudor) arch.

    Attempts the requested shoulder configuration first, then explores nearby
    configurations to find a viable tangent solution. Detailed error messages
    are provided if no combination succeeds.
    """
    if span <= 0 or rise <= 0:
        raise ValueError("Span and rise must be positive.")

    sr_pref = clamp(shoulder_ratio, _MIN_SHOULDER_RATIO, _MAX_SHOULDER_RATIO)
    sh_pref = clamp(shoulder_height_ratio, _MIN_SHOULDER_HEIGHT_RATIO, _MAX_SHOULDER_HEIGHT_RATIO)

    rise_ratio = rise / span if span > 0 else 0.5

    shoulder_candidates = _prioritized_values(
        sr_pref,
        _MIN_SHOULDER_RATIO,
        min(_MAX_SHOULDER_RATIO, 0.65),
        step=0.02,
        extras=_shoulder_ratio_extras(sr_pref, rise_ratio),
    )
    height_candidates = _prioritized_values(
        sh_pref,
        _MIN_SHOULDER_HEIGHT_RATIO,
        _MAX_SHOULDER_HEIGHT_RATIO,
        step=0.05,
        extras=_shoulder_height_extras(sh_pref, rise_ratio),
    )

    attempts = 0
    failure_samples: list[str] = []

    for sr, sh in _candidate_pairs(shoulder_candidates, height_candidates):
        attempts += 1
        try:
            return _solve_with_fixed_tangent(span, rise, sr, sh, tolerance)
        except ValueError as exc:
            if len(failure_samples) < _MAX_FAILURE_SAMPLES:
                failure_samples.append(f"sr={sr:.3f}, sh={sh:.3f}: {exc}")

    sample_text = "; ".join(failure_samples) if failure_samples else "No viable combinations found."
    raise ValueError(
        f"Unable to solve four-center arch for span={span:.3f}, rise={rise:.3f}. "
        f"Tried {attempts} shoulder configurations near sr={sr_pref:.3f}, sh={sh_pref:.3f}. "
        f"{sample_text}"
    )

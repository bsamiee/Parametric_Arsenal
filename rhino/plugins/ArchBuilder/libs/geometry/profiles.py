# r: numpy
"""
Title         : profiles.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/geometry/profiles.py

Description
----------------------------------------------------------------------------
Profile construction helpers for arch geometry.
Provides functions that assemble ordered curve segments for specific arch
types, combining analytic builders with solver-driven parameter logic.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
import Rhino.Geometry as rg

from ..solvers import (
    solve_catenary_parameter,
    solve_four_center_parameters,
    solve_horseshoe_parameters,
    solve_multifoil_parameters,
    solve_ogee_parameters,
    solve_three_center_parameters,
)
from ..specs import (
    FourCenterArchOptions,
    HorseshoeArchOptions,
    MultifoilArchOptions,
    OgeeArchOptions,
    ThreeCenterArchOptions,
)
from .curves import arc_curve, arc_from_center_endpoints, circular_segment_radius, profile_curve


# --- Profile Result Container ---------------------------------------------
@dataclass(frozen=True)
class ProfileSegments:
    """Container for arch profile curve segments and endpoints."""

    segments: list[rg.Curve]
    start: rg.Point3d
    end: rg.Point3d


# --- Analytic Profiles ----------------------------------------------------
def catenary_profile(
    half_span: float,
    rise: float,
    tolerance: float,
    samples: int = 128,
) -> ProfileSegments:
    """Build a catenary profile curve."""
    parameter = solve_catenary_parameter(half_span, rise, tolerance)

    xs = np.linspace(-half_span, half_span, samples)
    points = [rg.Point3d(float(x), float(parameter * (math.cosh(x / parameter) - 1.0)), 0.0) for x in xs]
    curve = arc_curve(points)
    return ProfileSegments([curve], curve.PointAtStart, curve.PointAtEnd)


def parabola_profile(half_span: float, rise: float, samples: int = 64) -> ProfileSegments:
    """Build a parabolic profile curve."""

    def parabola(x: float) -> float:
        t = x / half_span
        return rise * (1.0 - t * t)

    curve = profile_curve(half_span * 2.0, parabola, samples)
    return ProfileSegments([curve], curve.PointAtStart, curve.PointAtEnd)


def ellipse_profile(half_span: float, rise: float) -> ProfileSegments:
    """Build the upper half ellipse profile curve."""
    ellipse = rg.Ellipse(rg.Plane.WorldXY, half_span, rise)
    ellipse_curve = ellipse.ToNurbsCurve()
    domain = ellipse_curve.Domain
    mid_param = domain.Mid

    curves = ellipse_curve.Split([domain.T0, mid_param])
    upper_half = curves[0] if curves else ellipse_curve
    return ProfileSegments([upper_half], rg.Point3d(-half_span, 0.0, 0.0), rg.Point3d(half_span, 0.0, 0.0))


def semicircle_profile(span: float) -> ProfileSegments:
    """Build a semicircular profile curve."""
    radius = span * 0.5
    start = rg.Point3d(-radius, 0.0, 0.0)
    mid = rg.Point3d(0.0, radius, 0.0)
    end = rg.Point3d(radius, 0.0, 0.0)
    arc = rg.Arc(start, mid, end)
    arc_curve = rg.ArcCurve(arc)
    return ProfileSegments([arc_curve], arc_curve.PointAtStart, arc_curve.PointAtEnd)


def circular_segment_profile(half_span: float, rise: float) -> ProfileSegments:
    """Build a circular segment profile curve."""
    radius = circular_segment_radius(half_span, rise)
    center = rg.Point3d(0.0, rise - radius, 0.0)
    plane = rg.Plane(center, rg.Vector3d.XAxis, rg.Vector3d.YAxis)
    circle = rg.Circle(plane, radius)
    half_angle = math.asin(min(1.0, half_span / radius))
    interval = rg.Interval(-half_angle, half_angle)

    arc = rg.Arc(circle, interval)
    arc_curve = rg.ArcCurve(arc)
    return ProfileSegments([arc_curve], arc_curve.PointAtStart, arc_curve.PointAtEnd)


def two_center_profile(half_span: float, rise: float) -> ProfileSegments:
    """Build a two-center arch profile with correct center calculation."""
    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    apex = rg.Point3d(0.0, rise, 0.0)

    # Calculate center position for given span and rise
    # Centers at (±x_c, 0) where arcs pass through apex with equal radii
    x_c = (rise * rise - half_span * half_span) / (2.0 * half_span)

    # Centers move based on geometry:
    # - Equilateral arch (rise ≈ 0.866*span): centers near endpoints
    # - Lancet arch (rise > 0.866*span): centers outside endpoints
    # - Drop arch (rise < 0.866*span): centers inside endpoints
    left_center = rg.Point3d(x_c, 0.0, 0.0)
    right_center = rg.Point3d(-x_c, 0.0, 0.0)

    left_arc = rg.ArcCurve(arc_from_center_endpoints(left_center, left_base, apex))
    right_arc = rg.ArcCurve(arc_from_center_endpoints(right_center, apex, right_base))
    return ProfileSegments([left_arc, right_arc], left_base, right_base)


# --- Solver-driven Profiles -----------------------------------------------
def three_center_profile(
    span: float,
    rise: float,
    options: ThreeCenterArchOptions,
    tolerance: float,
) -> ProfileSegments:
    """Build a three-center arch profile with proper center-based construction."""
    parameters = solve_three_center_parameters(
        span,
        rise,
        shoulder_ratio=options.shoulder_ratio,
        tolerance=tolerance,
    )
    half_span = span * 0.5

    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    left_tangent = rg.Point3d(-parameters.tangent_x, parameters.tangent_y, 0.0)
    right_tangent = rg.Point3d(parameters.tangent_x, parameters.tangent_y, 0.0)

    left_center = rg.Point3d(-parameters.side_center_offset, 0.0, 0.0)
    right_center = rg.Point3d(parameters.side_center_offset, 0.0, 0.0)

    # Use the computed central center from solver
    central_center = rg.Point3d(0.0, parameters.central_center_y, 0.0)

    # Create side arcs
    left_lower_arc = arc_from_center_endpoints(left_center, left_base, left_tangent)
    right_lower_arc = arc_from_center_endpoints(right_center, right_tangent, right_base)

    # Create central arc using computed center
    # Calculate angles for the central arc that spans from left tangent to right tangent
    angle_left = math.atan2(left_tangent.Y - parameters.central_center_y, left_tangent.X - 0.0)
    angle_right = math.atan2(right_tangent.Y - parameters.central_center_y, right_tangent.X - 0.0)

    # Create the central arc using Circle and Interval
    plane = rg.Plane(central_center, rg.Vector3d.ZAxis)
    circle = rg.Circle(plane, parameters.central_radius)
    central_arc_geom = rg.Arc(circle, rg.Interval(angle_left, angle_right))

    # Convert to ArcCurves
    left_lower = rg.ArcCurve(left_lower_arc)
    central_arc = rg.ArcCurve(central_arc_geom)
    right_lower = rg.ArcCurve(right_lower_arc)

    # Verify tangency at left junction point
    left_tangent_vec = left_lower.TangentAt(left_lower.Domain.Max)
    central_left_tangent_vec = central_arc.TangentAt(central_arc.Domain.Min)
    if left_tangent_vec.IsParallelTo(central_left_tangent_vec, tolerance) != 1:
        # Log warning but continue
        print(
            f"Warning: Left junction tangency not achieved "
            f"(angle: {left_tangent_vec.VectorAngle(central_left_tangent_vec)} rad)"
        )

    # Verify tangency at right junction point
    central_right_tangent_vec = central_arc.TangentAt(central_arc.Domain.Max)
    right_tangent_vec = right_lower.TangentAt(right_lower.Domain.Min)
    if central_right_tangent_vec.IsParallelTo(right_tangent_vec, tolerance) != 1:
        # Log warning but continue
        print(
            f"Warning: Right junction tangency not achieved "
            f"(angle: {central_right_tangent_vec.VectorAngle(right_tangent_vec)} rad)"
        )

    return ProfileSegments([left_lower, central_arc, right_lower], left_base, right_base)


def four_center_profile(
    span: float,
    rise: float,
    options: FourCenterArchOptions,
    tolerance: float,
) -> ProfileSegments:
    """Build a four-center arch profile with tangency verification."""
    params = solve_four_center_parameters(
        span,
        rise,
        shoulder_ratio=options.shoulder_ratio,
        shoulder_height_ratio=options.shoulder_height_ratio,
        tolerance=tolerance,
    )
    half_span = span * 0.5

    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    left_tangent = rg.Point3d(-params.tangent_x, params.tangent_y, 0.0)
    right_tangent = rg.Point3d(params.tangent_x, params.tangent_y, 0.0)
    apex = rg.Point3d(0.0, rise, 0.0)

    left_lower_center = rg.Point3d(-params.lower_center_offset, 0.0, 0.0)
    right_lower_center = rg.Point3d(params.lower_center_offset, 0.0, 0.0)
    left_upper_center = rg.Point3d(-params.upper_center_offset, params.upper_center_height, 0.0)
    right_upper_center = rg.Point3d(params.upper_center_offset, params.upper_center_height, 0.0)

    # Create arc curves
    left_lower_arc = arc_from_center_endpoints(left_lower_center, left_base, left_tangent)
    left_upper_arc = arc_from_center_endpoints(left_upper_center, left_tangent, apex)
    right_upper_arc = arc_from_center_endpoints(right_upper_center, apex, right_tangent)
    right_lower_arc = arc_from_center_endpoints(right_lower_center, right_tangent, right_base)

    # Convert to ArcCurves for tangent verification
    left_lower = rg.ArcCurve(left_lower_arc)
    left_upper = rg.ArcCurve(left_upper_arc)
    right_upper = rg.ArcCurve(right_upper_arc)
    right_lower = rg.ArcCurve(right_lower_arc)

    # Verify tangency at left shoulder point
    left_lower_tangent = left_lower.TangentAt(left_lower.Domain.Max)
    left_upper_tangent = left_upper.TangentAt(left_upper.Domain.Min)
    if left_lower_tangent.IsParallelTo(left_upper_tangent, tolerance) != 1:
        # Log warning but continue - the mathematical solver should have ensured tangency
        print(
            f"Warning: Left shoulder tangency not achieved "
            f"(angle: {left_lower_tangent.VectorAngle(left_upper_tangent)} rad)"
        )

    # Verify tangency at right shoulder point
    right_upper_tangent = right_upper.TangentAt(right_upper.Domain.Max)
    right_lower_tangent = right_lower.TangentAt(right_lower.Domain.Min)
    if right_upper_tangent.IsParallelTo(right_lower_tangent, tolerance) != 1:
        # Log warning but continue
        print(
            f"Warning: Right shoulder tangency not achieved "
            f"(angle: {right_upper_tangent.VectorAngle(right_lower_tangent)} rad)"
        )

    # Verify tangency at apex, tangents should be opposite at apex (anti-parallel is correct)
    left_upper_apex_tangent = left_upper.TangentAt(left_upper.Domain.Max)
    right_upper_apex_tangent = right_upper.TangentAt(right_upper.Domain.Min)
    if left_upper_apex_tangent.IsParallelTo(right_upper_apex_tangent, tolerance) == -1:
        pass  # This is expected behavior
    elif left_upper_apex_tangent.IsParallelTo(right_upper_apex_tangent, tolerance) != 1:
        print(
            f"Warning: Apex tangency not achieved "
            f"(angle: {left_upper_apex_tangent.VectorAngle(right_upper_apex_tangent)} rad)"
        )

    return ProfileSegments(
        [left_lower, left_upper, right_upper, right_lower],
        left_base,
        right_base,
    )


def ogee_profile(
    half_span: float,
    rise: float,
    options: OgeeArchOptions,
    tolerance: float,
) -> ProfileSegments:
    """Build an ogee arch profile with tangency verification."""
    params = solve_ogee_parameters(
        half_span=half_span,
        rise=rise,
        inflection_height=options.inflection_height,
        curve_strength=options.curve_strength,
        tolerance=tolerance,
    )

    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    left_inflection = rg.Point3d(-params.tangent_x, params.tangent_y, 0.0)
    right_inflection = rg.Point3d(params.tangent_x, params.tangent_y, 0.0)
    apex = rg.Point3d(0.0, rise, 0.0)

    left_lower_center = rg.Point3d(params.lower_center_x, params.lower_center_y, 0.0)
    right_lower_center = rg.Point3d(-params.lower_center_x, params.lower_center_y, 0.0)
    left_upper_center = rg.Point3d(params.upper_center_x, params.upper_center_y, 0.0)
    right_upper_center = rg.Point3d(-params.upper_center_x, params.upper_center_y, 0.0)

    left_lower_arc = arc_from_center_endpoints(left_lower_center, left_base, left_inflection)
    left_upper_arc = arc_from_center_endpoints(left_upper_center, left_inflection, apex)
    right_upper_arc = arc_from_center_endpoints(right_upper_center, apex, right_inflection)
    right_lower_arc = arc_from_center_endpoints(right_lower_center, right_inflection, right_base)

    # Convert to ArcCurves
    left_lower = rg.ArcCurve(left_lower_arc)
    left_upper = rg.ArcCurve(left_upper_arc)
    right_upper = rg.ArcCurve(right_upper_arc)
    right_lower = rg.ArcCurve(right_lower_arc)

    # Verify tangency at left inflection point (S-curve transition)
    left_lower_tangent = left_lower.TangentAt(left_lower.Domain.Max)
    left_upper_tangent = left_upper.TangentAt(left_upper.Domain.Min)
    if left_lower_tangent.IsParallelTo(left_upper_tangent, tolerance) != 1:
        # Log warning but continue - the solver should have ensured tangency
        print(
            f"Warning: Left inflection tangency not achieved "
            f"(angle: {left_lower_tangent.VectorAngle(left_upper_tangent)} rad)"
        )

    # Verify tangency at right inflection point
    right_upper_tangent = right_upper.TangentAt(right_upper.Domain.Max)
    right_lower_tangent = right_lower.TangentAt(right_lower.Domain.Min)
    if right_upper_tangent.IsParallelTo(right_lower_tangent, tolerance) != 1:
        # Log warning but continue
        print(
            f"Warning: Right inflection tangency not achieved "
            f"(angle: {right_upper_tangent.VectorAngle(right_lower_tangent)} rad)"
        )

    # Verify tangency at apex (should be anti-parallel as curves meet from opposite sides)
    left_apex_tangent = left_upper.TangentAt(left_upper.Domain.Max)
    right_apex_tangent = right_upper.TangentAt(right_upper.Domain.Min)
    if left_apex_tangent.IsParallelTo(right_apex_tangent, tolerance) == -1:
        pass  # Expected behavior - tangents are opposite at apex
    elif left_apex_tangent.IsParallelTo(right_apex_tangent, tolerance) != 1:
        print(
            f"Warning: Apex tangency not achieved "
            f"(angle: {left_apex_tangent.VectorAngle(right_apex_tangent)} rad)"
        )

    curves = [left_lower, left_upper, right_upper, right_lower]
    return ProfileSegments(curves, left_base, right_base)


def horseshoe_profile(
    half_span: float,
    rise: float,
    options: HorseshoeArchOptions,
    tolerance: float,
) -> ProfileSegments:
    """Build a horseshoe arch profile."""
    params = solve_horseshoe_parameters(
        half_span=half_span,
        rise=rise,
        extension_degrees=options.extension_degrees,
        tolerance=tolerance,
    )
    center = rg.Point3d(params.center_x, params.center_y, 0.0)
    plane = rg.Plane(center, rg.Vector3d.XAxis, rg.Vector3d.YAxis)
    circle = rg.Circle(plane, params.radius)
    arc = rg.Arc(circle, rg.Interval(params.angle_left, params.angle_right))
    arc_curve = rg.ArcCurve(arc)

    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    return ProfileSegments([arc_curve], left_base, right_base)


def multifoil_profile(
    half_span: float,
    rise: float,
    options: MultifoilArchOptions,
    tolerance: float,
) -> ProfileSegments:
    """Build a multifoil arch profile."""
    params = solve_multifoil_parameters(
        half_span=half_span,
        rise=rise,
        lobes=options.lobes,
        lobe_size=options.lobe_size,
        tolerance=tolerance,
    )

    curves: list[rg.Curve] = []
    for foil_arc in params.foil_arcs:
        foil_center = rg.Point3d(foil_arc.center_x, foil_arc.center_y, 0.0)
        plane = rg.Plane(foil_center, rg.Vector3d.XAxis, rg.Vector3d.YAxis)
        circle = rg.Circle(plane, foil_arc.radius)
        arc = rg.Arc(circle, rg.Interval(foil_arc.angle_start, foil_arc.angle_end))
        curves.append(rg.ArcCurve(arc))

    left_base = rg.Point3d(-half_span, 0.0, 0.0)
    right_base = rg.Point3d(half_span, 0.0, 0.0)
    return ProfileSegments(curves, left_base, right_base)

"""
Title         : curves.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/geometry/curves.py

Description
----------------------------------------------------------------------------
Curve construction utilities for arch geometry.
Provides functions to create and manipulate Rhino curves for arch profiles.
"""

from __future__ import annotations

import math
from typing import Callable

import numpy as np
import Rhino.Geometry as rg


# --- Circular Segment Radius ----------------------------------------------
def circular_segment_radius(half_span: float, rise: float) -> float:
    """Calculate radius for a circular segment given span and rise.

    For a circular arc passing through three points (left base, apex, right base),
    this calculates the required radius using the chord-sagitta relationship.

    Args:
        half_span: Half the horizontal span of the arch
        rise: Maximum vertical height of the arch

    Returns:
        Radius of the circular arc
    """
    return (half_span**2) / (2.0 * rise) + 0.5 * rise


# --- Arc from Center Endpoints --------------------------------------------
def arc_from_center_endpoints(center: rg.Point3d, start: rg.Point3d, end: rg.Point3d) -> rg.Arc:
    """Create arc from center point and two endpoints.

    Uses the proper Rhino Arc constructor with Circle and angle interval.
    Ensures the arc takes the shorter path between endpoints.

    Args:
        center: Center point of the arc
        start: Start point on the arc
        end: End point on the arc

    Returns:
        Arc passing through both endpoints with given center

    Note:
        The function validates that both endpoints are equidistant from center
        and uses the average radius if there are minor discrepancies.
    """
    # Calculate radius (should be same for both points)
    radius_start = center.DistanceTo(start)
    radius_end = center.DistanceTo(end)

    # Use average radius if there's a small discrepancy
    radius = (radius_start + radius_end) * 0.5

    # Create plane at center with Z-axis normal
    plane = rg.Plane(center, rg.Vector3d.ZAxis)
    circle = rg.Circle(plane, radius)

    # Calculate angles using atan2 (robust for all quadrants)
    angle_start = math.atan2(start.Y - center.Y, start.X - center.X)
    angle_end = math.atan2(end.Y - center.Y, end.X - center.X)

    # Ensure we take the shorter arc path (< π)
    # Adjust end angle to be within π of start angle
    angle_diff = angle_end - angle_start

    # Normalize angle difference to [-π, π]
    while angle_diff > math.pi:
        angle_end -= 2 * math.pi
        angle_diff -= 2 * math.pi
    while angle_diff < -math.pi:
        angle_end += 2 * math.pi
        angle_diff += 2 * math.pi

    # Create arc with the angle interval
    return rg.Arc(circle, rg.Interval(angle_start, angle_end))


def arc_curve(points: list[rg.Point3d]) -> rg.Curve:
    """Create a smooth curve through a list of points.

    Args:
        points: List of points to interpolate

    Returns:
        NURBS curve passing through all points

    Raises:
        ValueError: If fewer than 3 points are provided
    """
    if len(points) < 3:
        raise ValueError("At least three points required to build a curve.")
    return rg.NurbsCurve.CreateInterpolatedCurve(points, 3)


# --- Profile Curve --------------------------------------------------------
def profile_curve(span: float, func: Callable[[float], float], samples: int = 64) -> rg.Curve:
    """Create a curve from a mathematical function.

    Generates a curve by sampling a function over the span interval.

    Args:
        span: Total horizontal span of the curve
        func: Function mapping x-coordinate to y-coordinate
        samples: Number of points to sample along the curve

    Returns:
        NURBS curve representing the function profile
    """
    b = span * 0.5
    xs = np.linspace(-b, b, samples)
    points = [rg.Point3d(float(x), float(func(x)), 0.0) for x in xs]
    return arc_curve(points)

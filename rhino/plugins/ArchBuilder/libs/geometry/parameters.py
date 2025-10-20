"""
Title         : parameters.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/geometry/parameters.py

Description
----------------------------------------------------------------------------
Parameter data structures for arch construction calculations.
These immutable dataclasses store the computed mathematical parameters
needed to construct various arch geometries.
"""

from __future__ import annotations

from dataclasses import dataclass


# --- Three Center Parameter -----------------------------------------------
@dataclass(frozen=True)
class ThreeCenterParameters:
    """Parameters for three-center (basket-handle) arch construction.

    Attributes:
        side_center_offset: Horizontal offset of side arc centers from origin
        central_center_y: Vertical position of central arc center (negative)
        tangent_x: X-coordinate of tangent points between arcs
        tangent_y: Y-coordinate of tangent points between arcs
        central_radius: Radius of the central arc
        side_radius: Radius of the side arcs
    """

    side_center_offset: float
    central_center_y: float
    tangent_x: float
    tangent_y: float
    central_radius: float
    side_radius: float


# --- Four Center Parameter ------------------------------------------------
@dataclass(frozen=True)
class FourCenterParameters:
    """Parameters for four-center (depressed/Tudor) arch construction.

    Attributes:
        lower_center_offset: Horizontal offset of lower arc centers
        lower_radius: Radius of lower arcs
        upper_center_offset: Horizontal offset of upper arc centers
        upper_center_height: Vertical position of upper arc centers
        upper_radius: Radius of upper arcs
        tangent_x: X-coordinate of tangent points between upper/lower arcs
        tangent_y: Y-coordinate of tangent points between upper/lower arcs
    """

    lower_center_offset: float
    lower_radius: float
    upper_center_offset: float
    upper_center_height: float
    upper_radius: float
    tangent_x: float
    tangent_y: float


# --- Ogee Parameter -------------------------------------------------------
@dataclass(frozen=True)
class OgeeParameters:
    """Parameters for ogee (S-curve) arch construction.

    Attributes:
        tangent_x: X-coordinate of inflection point
        tangent_y: Y-coordinate of inflection point
        lower_center_x: X-coordinate of lower arc center
        lower_center_y: Y-coordinate of lower arc center
        lower_radius: Radius of lower arc
        upper_center_x: X-coordinate of upper arc center
        upper_center_y: Y-coordinate of upper arc center
        upper_radius: Radius of upper arc
    """

    tangent_x: float
    tangent_y: float
    lower_center_x: float
    lower_center_y: float
    lower_radius: float
    upper_center_x: float
    upper_center_y: float
    upper_radius: float


# --- Horseshoe Parameter --------------------------------------------------
@dataclass(frozen=True)
class HorseshoeParameters:
    """Parameters for horseshoe arch construction.

    Attributes:
        center_x: X-coordinate of arc center
        center_y: Y-coordinate of arc center
        radius: Arc radius
        angle_left: Start angle for the arc (left side)
        angle_right: End angle for the arc (right side)
    """

    center_x: float
    center_y: float
    radius: float
    angle_left: float
    angle_right: float


# --- Multifoil Parameters -------------------------------------------------
@dataclass(frozen=True)
class FoilArcParameters:
    """Parameters for a single foil arc in a multifoil arch.

    Attributes:
        center_x: X-coordinate of foil arc center
        center_y: Y-coordinate of foil arc center
        radius: Foil arc radius
        angle_start: Start angle for the foil arc
        angle_end: End angle for the foil arc
    """

    center_x: float
    center_y: float
    radius: float
    angle_start: float
    angle_end: float


@dataclass(frozen=True)
class MultifoilParameters:
    """Parameters for multifoil arch construction.

    Attributes:
        main_center_x: X-coordinate of main arc center
        main_center_y: Y-coordinate of main arc center
        main_radius: Main arc radius
        foil_arcs: List of foil arc parameters
    """

    main_center_x: float
    main_center_y: float
    main_radius: float
    foil_arcs: list[FoilArcParameters]

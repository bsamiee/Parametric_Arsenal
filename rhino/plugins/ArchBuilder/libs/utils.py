"""
Title         : utils.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/utils.py

Description
----------------------------------------------------------------------------
Utility helpers shared across ArchBuilder components.
"""

from __future__ import annotations

from collections.abc import Iterable

import Rhino.Geometry as rg
import scriptcontext as sc


# --- Arch Builder Utilities -----------------------------------------------
class ArchBuilderUtils:
    """Shared utility routines for the ArchBuilder plugin."""

    @staticmethod
    def model_absolute_tolerance() -> float:
        """Return the document-wide absolute modelling tolerance."""
        return sc.doc.ModelAbsoluteTolerance

    @staticmethod
    def model_angle_tolerance() -> float:
        """Return the document-wide angular tolerance in degrees."""
        return sc.doc.ModelAngleToleranceDegrees

    # --- Curve Validation -------------------------------------------------
    @staticmethod
    def ensure_planar_closed_curve(curve: rg.Curve, tolerance: float) -> rg.Curve:
        """Validate that a curve is closed and planar within tolerance."""
        if not curve.IsClosed:
            raise ValueError("Curve must be closed.")
        ok, _plane = curve.TryGetPlane(tolerance)
        if not ok:
            raise ValueError("Curve must be planar.")
        return curve

    @staticmethod
    def largest_planar_closed_curve(curves: Iterable[rg.Curve], plane: rg.Plane) -> rg.Curve:
        """Return the closed curve with the largest area in the given plane."""
        selected: rg.Curve | None = None
        max_area = -1.0
        for candidate in curves:
            area_props = rg.AreaMassProperties.Compute(candidate)
            if area_props is None:
                continue
            area = area_props.Area
            if area > max_area:
                max_area = area
                selected = candidate
        if selected is None:
            raise ValueError("No valid offset curve returned.")
        return selected

    # --- Curve Orientation ------------------------------------------------
    @staticmethod
    def orient_curve_outward(curve: rg.Curve, plane: rg.Plane) -> rg.Curve:
        """Ensure a closed curve has counter-clockwise orientation."""
        orientation = curve.ClosedCurveOrientation(plane)
        if orientation == rg.CurveOrientation.Clockwise:
            curve.Reverse()
        return curve

    # --- Plane Extraction -------------------------------------------------
    @staticmethod
    def plane_from_curve(curve: rg.Curve, tolerance: float) -> rg.Plane:
        """Construct a plane from a planar curve or raise if none can be found."""
        ok, plane = curve.TryGetPlane(tolerance)
        if ok:
            return plane
        raise ValueError("Unable to derive plane from curve.")

    @staticmethod
    def span_rise_from_rectangle(rectangle: rg.Rectangle3d) -> tuple[float, float]:
        """Return span and rise measurements from a rectangle in its plane."""
        return rectangle.Width, rectangle.Height

    # --- Curve Transformation ---------------------------------------------
    @staticmethod
    def to_world(curve: rg.Curve, transform: rg.Transform) -> rg.Curve:
        """Duplicate a curve and transform it into world space."""
        duplicate = curve.DuplicateCurve()
        duplicate.Transform(transform)
        return duplicate

    # --- Curve Operations -------------------------------------------------
    @staticmethod
    def join_curves(curves: Iterable[rg.Curve], tolerance: float) -> rg.Curve:
        """Join curve segments and return the resulting curve."""
        joined = rg.Curve.JoinCurves(list(curves), tolerance)
        if not joined:
            raise ValueError("Failed to join curve segments.")
        return joined[0]

    @staticmethod
    def close_with_baseline(
        curve_segments: Iterable[rg.Curve],
        start: rg.Point3d,
        end: rg.Point3d,
        tolerance: float,
    ) -> rg.Curve:
        """Join segments with a straight baseline between the provided endpoints."""
        baseline = rg.LineCurve(end, start)
        collection = list(curve_segments)
        collection.append(baseline)
        return ArchBuilderUtils.join_curves(collection, tolerance)

    @staticmethod
    def offset_curve(
        curve: rg.Curve,
        plane: rg.Plane,
        distance: float,
        tolerance: float,
        angle_tolerance: float,
    ) -> rg.Curve:
        """Offset a curve and pick the largest closed result if multiple are returned."""
        offsets = curve.Offset(
            plane,
            distance,
            tolerance,
            angle_tolerance,
            False,
            rg.CurveOffsetCornerStyle.Sharp,
            rg.CurveOffsetEndStyle.Flat,
        )
        if not offsets:
            raise ValueError("Curve offset failed.")
        if len(offsets) == 1:
            return offsets[0]
        return ArchBuilderUtils.largest_planar_closed_curve(offsets, plane)

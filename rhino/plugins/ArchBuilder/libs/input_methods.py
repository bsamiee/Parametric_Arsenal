"""
Title         : input_methods.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/input_methods.py

Description
----------------------------------------------------------------------------
Input methods for arch creation - curve selection or point-to-point input.
Provides professional input experience matching native Rhino commands.
"""

from __future__ import annotations

from enum import Enum

import Rhino.DocObjects as rd
import Rhino.Geometry as rg
import Rhino.Input as ri
import Rhino.Input.Custom as ric
import scriptcontext as sc

import Rhino
from libs.ui import ProfileSelection


# --- Input Method Enum ----------------------------------------------------
class InputMethod(Enum):
    """Available input methods for arch creation."""

    CURVE = "Curve"
    POINTS = "Points"


# --- Arch Input Manager ---------------------------------------------------
class ArchInputManager:
    """Manages different input methods for arch creation."""

    @staticmethod
    def get_input_method() -> InputMethod | None:
        """Prompt user to select input method.

        Returns:
            Selected input method or None if cancelled.
        """
        go = ric.GetOption()
        go.SetCommandPrompt("Select input method")
        go.AcceptNothing(False)

        curve_idx = go.AddOption("Curve")
        points_idx = go.AddOption("Points")

        res = go.Get()
        if res != ri.GetResult.Option:
            return None

        if go.OptionIndex() == curve_idx:
            return InputMethod.CURVE
        if go.OptionIndex() == points_idx:
            return InputMethod.POINTS

        return None

    @staticmethod
    def get_curve_input(abs_tol: float) -> ProfileSelection | None:  # noqa: PLR0911
        """Get arch parameters from a closed planar curve.

        Args:
            abs_tol: Absolute tolerance for planarity checks.

        Returns:
            ProfileSelection with span, rise, and plane positioned at bottom of curve.
        """
        # Reuse an already-selected curve when possible for faster workflow.
        selected_objects = sc.doc.Objects.GetSelectedObjects(includeLights=False, includeGrips=False)

        for obj in selected_objects:
            if not isinstance(obj, rd.CurveObject):
                continue

            curve = obj.CurveGeometry
            if not isinstance(curve, rg.Curve) or not curve.IsClosed:
                continue

            success, plane = curve.TryGetPlane(abs_tol)
            if not (success and plane.IsValid):
                continue

            bbox = curve.GetBoundingBox(plane)
            box = rg.Box(plane, bbox)
            if not box.IsValid:
                continue

            span = box.X.Length
            rise = box.Y.Length
            if span > 0 and rise > 0:
                # Align the arch plane so span maps to +X and rise to +Y.
                bottom_center_local = rg.Point3d(box.Center.X, bbox.Min.Y, box.Center.Z)
                bottom_center_world = plane.PointAt(bottom_center_local.X, bottom_center_local.Y)
                oriented_plane = rg.Plane(bottom_center_world, plane.XAxis, plane.YAxis)

                return ProfileSelection(
                    span=span,
                    rise=rise,
                    plane=oriented_plane,
                    curve=curve,
                )

        # Otherwise prompt the user to choose a suitable profile curve.
        go = ric.GetObject()
        go.SetCommandPrompt("Select a closed planar curve")
        go.GeometryFilter = rd.ObjectType.Curve
        go.SetCustomGeometryFilter(_planar_closed_filter)
        go.DeselectAllBeforePostSelect = True
        go.SubObjectSelect = False

        rc = go.Get()
        if rc != ri.GetResult.Object:
            return None

        curve = go.Object(0).Curve()
        if curve is None:
            return None

        success, plane = curve.TryGetPlane(abs_tol)
        if not success:
            return None

        bbox = curve.GetBoundingBox(plane)
        box = rg.Box(plane, bbox)
        if not box.IsValid:
            return None

        span = box.X.Length
        rise = box.Y.Length
        if span > 0 and rise > 0:
            # Align the arch plane so span maps to +X and rise to +Y.
            bottom_center_local = rg.Point3d(box.Center.X, bbox.Min.Y, box.Center.Z)
            bottom_center_world = plane.PointAt(bottom_center_local.X, bottom_center_local.Y)
            oriented_plane = rg.Plane(bottom_center_world, plane.XAxis, plane.YAxis)

            return ProfileSelection(
                span=span,
                rise=rise,
                plane=oriented_plane,
                curve=curve,
            )

        return None

    @staticmethod
    def get_points_input(abs_tol: float) -> ProfileSelection | None:
        """Get arch parameters from point-to-point input with height.

        Mimics Rhino's Box command behavior with numeric input support.

        Args:
            abs_tol: Absolute tolerance for calculations.

        Returns:
            ProfileSelection with span, rise, and plane from points.
        """
        gp1 = ric.GetPoint()
        gp1.SetCommandPrompt("First corner of arch span")
        if gp1.Get() != ri.GetResult.Point:
            return None
        pt1 = gp1.Point()

        gp2 = ric.GetPoint()
        gp2.SetCommandPrompt("Second corner of arch span")
        gp2.SetBasePoint(pt1, True)
        gp2.DrawLineFromPoint(pt1, True)
        if gp2.Get() != ri.GetResult.Point:
            return None
        pt2 = gp2.Point()

        span = pt1.DistanceTo(pt2)
        if span <= abs_tol:
            Rhino.RhinoApp.WriteLine("Points too close together")
            return None

        mid_point = rg.Point3d((pt1.X + pt2.X) * 0.5, (pt1.Y + pt2.Y) * 0.5, (pt1.Z + pt2.Z) * 0.5)
        x_axis = pt2 - pt1
        x_axis.Unitize()

        # Use world Z when the span is near-horizontal; otherwise build a perpendicular frame.
        if abs(x_axis.Z) < 0.99:
            z_axis = rg.Vector3d.ZAxis
            y_axis = rg.Vector3d.CrossProduct(z_axis, x_axis)
            y_axis.Unitize()
            z_axis = rg.Vector3d.CrossProduct(x_axis, y_axis)
        else:
            y_axis = rg.Vector3d.YAxis
            z_axis = rg.Vector3d.CrossProduct(x_axis, y_axis)
            z_axis.Unitize()
            y_axis = rg.Vector3d.CrossProduct(z_axis, x_axis)

        # Collect height with dynamic preview feedback.
        gp3 = ric.GetPoint()
        gp3.SetCommandPrompt("Height (number or point)")
        gp3.AcceptNumber(True, False)  # Accept positive numbers only
        gp3.SetBasePoint(mid_point, True)

        # Constrain to vertical from midpoint
        height_line = rg.Line(mid_point, mid_point + y_axis * span)
        gp3.Constrain(height_line)

        def draw_preview(sender: object, args: ric.GetPointDrawEventArgs) -> None:
            # Preview the bounding rectangle so the user sees span/rise context.
            height = mid_point.DistanceTo(args.CurrentPoint)
            corners = [
                pt1,
                pt2,
                pt2 + y_axis * height,
                pt1 + y_axis * height,
                pt1,  # Close rectangle
            ]
            args.Display.DrawPolyline(corners, Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor)

        gp3.DynamicDraw += draw_preview

        result = gp3.Get()

        # Support both numeric and picked height.
        if result == ri.GetResult.Number:
            rise = gp3.Number()
        elif result == ri.GetResult.Point:
            rise = mid_point.DistanceTo(gp3.Point())
        else:
            return None

        if rise <= abs_tol:
            Rhino.RhinoApp.WriteLine("Height too small")
            return None

        # Place the construction plane at the baseline centre like the curve workflow.
        bottom_center = rg.Point3d((pt1.X + pt2.X) * 0.5, (pt1.Y + pt2.Y) * 0.5, (pt1.Z + pt2.Z) * 0.5)
        plane = rg.Plane(bottom_center, x_axis, y_axis)

        return ProfileSelection(
            span=span,
            rise=rise,
            plane=plane,
            curve=None,
        )


# --- Geometry Filters -----------------------------------------------------
def _planar_closed_filter(
    rh_object: rd.RhinoObject,
    geometry: rg.GeometryBase,
    _: rg.ComponentIndex,
) -> bool:
    """Filter for planar closed curves."""
    curve = geometry if isinstance(geometry, rg.Curve) else getattr(geometry, "CurveGeometry", None)
    if not isinstance(curve, rg.Curve) or not curve.IsClosed:
        return False
    success, plane = curve.TryGetPlane(sc.doc.ModelAbsoluteTolerance)
    return success and plane.IsValid

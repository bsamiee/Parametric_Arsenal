"""
Title         : alignment_tools.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/alignment_tools.py

Description
----------------------------------------------------------------------------
Tools for aligning Detail Views on a Layout Page
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc

import Rhino

from .camera_tools import CameraTools
from .constants import Constants, Strings
from .exceptions import DetailError, TransformError, UserCancelledError


# --- Alignment Tools ------------------------------------------------------
class AlignmentTools:
    """Tools for aligning Detail Views on a Layout Page."""

    @staticmethod
    def get_detail_object_id(prompt_message: str) -> object:
        """Prompt user for a Detail View, validate, and return its ID.

        Args:
            prompt_message: Message to display to the user.

        Returns:
            The detail object ID.

        Raises:
            UserCancelledError: If user cancels the selection.
            DetailError: If selected object is not a valid detail view.
        """
        if isinstance(sc.doc.Views.ActiveView, Rhino.Display.RhinoPageView):
            sc.doc.Views.ActiveView.SetPageAsActive()
        else:
            print("Warning: Alignment should ideally be run in a Layout view.")

        obj_id = rs.GetObject(prompt_message, rs.filter.detail, True, False)
        if not obj_id:
            raise UserCancelledError("Detail selection cancelled")

        rh_obj = rs.coercerhinoobject(obj_id)
        if rh_obj and isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            return obj_id

        raise DetailError(Strings.MSG_INVALID_DETAIL_SELECTED, context=obj_id)

    @staticmethod
    def get_point_on_page(prompt_message: str) -> object:
        """Prompt the user to pick a point constrained to the active Layout Page.

        Args:
            prompt_message: Message to display to the user.

        Returns:
            The selected point.

        Raises:
            UserCancelledError: If user cancels the point selection.
        """
        if isinstance(sc.doc.Views.ActiveView, Rhino.Display.RhinoPageView):
            sc.doc.Views.ActiveView.SetPageAsActive()
        else:
            print("Warning: Alignment should ideally be run in a Layout view.")

        point = rs.GetPoint(prompt_message)
        if not point:
            raise UserCancelledError("Point selection cancelled")

        return point

    @staticmethod
    def calculate_translation_vector(pt_parent: Any, pt_child: Any, align_choice: str) -> object | None:
        """Calculate the translation vector needed to align pt_child with pt_parent.

        Args:
            pt_parent: Parent point to align to.
            pt_child: Child point to be aligned.
            align_choice: Must be 'Horizontal' or 'Vertical'.

        Returns:
            Translation vector, or None if no translation needed (within tolerance).

        Raises:
            TransformError: If invalid points provided or invalid alignment choice.
        """
        if not all([
            isinstance(pt_parent, Rhino.Geometry.Point3d),
            isinstance(pt_child, Rhino.Geometry.Point3d),
        ]):
            raise TransformError(
                "Invalid points provided for alignment calculation",
                context={"pt_parent": pt_parent, "pt_child": pt_child},
            )

        if align_choice == "Horizontal":
            delta_y = pt_parent.Y - pt_child.Y
            if abs(delta_y) > Constants.TOLERANCE:
                return Rhino.Geometry.Vector3d(0, delta_y, 0)

        elif align_choice == "Vertical":
            delta_x = pt_parent.X - pt_child.X
            if abs(delta_x) > Constants.TOLERANCE:
                return Rhino.Geometry.Vector3d(delta_x, 0, 0)

        else:
            raise TransformError(
                f"Invalid alignment choice '{align_choice}'. Must be 'Horizontal' or 'Vertical'",
                context={"align_choice": align_choice},
            )

        return None

    @staticmethod
    def prompt_for_target_projection(current_view: str) -> object:
        """Prompt user to choose a new projection direction excluding the current view.

        Args:
            current_view: Current view name to exclude from options.

        Returns:
            Selected target view name.
        """
        options = ["Top", "Bottom", "Front", "Back", "Left", "Right"]
        if current_view in options:
            options.remove(current_view)

        return rs.ListBox(
            options,
            Strings.STEP2_PROMPT_TITLE.format(current_view),
            Strings.STEP2_PROMPT_TITLE.format(current_view),
        )

    @staticmethod
    def refresh_all_detail_metadata() -> None:
        """Refresh camera metadata for all Detail Views on the active Layout Page."""
        pageview = sc.doc.Views.ActiveView
        if not isinstance(pageview, Rhino.Display.RhinoPageView):
            print("Warning: Active view is not a Layout.")
            return

        details = pageview.GetDetailViews()
        for detail in details:
            CameraTools.set_camera_metadata(detail.Id)

        print(f"[DEBUG] Refreshed metadata on {len(details)} Detail View(s)")

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
from .common_utils import CommonUtils
from .constants import Constants, Strings


# --- Alignment Tools ------------------------------------------------------
class AlignmentTools:
    """Tools for aligning Detail Views on a Layout Page."""

    @staticmethod
    def get_detail_object_id(prompt_message: str) -> object | None:
        """Prompts user for a Detail View, validates, and returns its ID."""
        if isinstance(sc.doc.Views.ActiveView, Rhino.Display.RhinoPageView):
            sc.doc.Views.ActiveView.SetPageAsActive()
        else:
            print("Warning: Alignment should ideally be run in a Layout view.")

        obj_id = rs.GetObject(prompt_message, rs.filter.detail, True, False)
        if not obj_id:
            return None

        rh_obj = rs.coercerhinoobject(obj_id)
        if rh_obj and isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            return obj_id
        CommonUtils.alert_user(Strings.MSG_INVALID_DETAIL_SELECTED)
        return None

    @staticmethod
    def get_point_on_page(prompt_message: str) -> object | None:
        """Prompts the user to pick a point constrained to the active Layout Page."""
        if isinstance(sc.doc.Views.ActiveView, Rhino.Display.RhinoPageView):
            sc.doc.Views.ActiveView.SetPageAsActive()
        else:
            print("Warning: Alignment should ideally be run in a Layout view.")

        return rs.GetPoint(prompt_message)

    @staticmethod
    def get_object_name(obj_id: object) -> str:
        """Returns the object name, or the GUID as string if unnamed."""
        name = rs.ObjectName(obj_id)
        return name or str(obj_id)

    @staticmethod
    def calculate_translation_vector(pt_parent: Any, pt_child: Any, align_choice: str) -> object | None:
        """
        Calculates the translation vector needed to align pt_child with pt_parent.

        Align_choice must be 'Horizontal' or 'Vertical'.
        """
        if not all([
            isinstance(pt_parent, Rhino.Geometry.Point3d),
            isinstance(pt_child, Rhino.Geometry.Point3d),
        ]):
            print("Error: Invalid points provided for alignment calculation.")
            return None

        if align_choice == "Horizontal":
            delta_y = pt_parent.Y - pt_child.Y
            if abs(delta_y) > Constants.TOLERANCE:
                return Rhino.Geometry.Vector3d(0, delta_y, 0)

        elif align_choice == "Vertical":
            delta_x = pt_parent.X - pt_child.X
            if abs(delta_x) > Constants.TOLERANCE:
                return Rhino.Geometry.Vector3d(delta_x, 0, 0)

        else:
            print(f"Warning: Invalid alignment choice '{align_choice}'")

        return None

    @staticmethod
    def prompt_for_target_projection(current_view: str) -> object:
        """Prompts user to choose a new projection direction excluding the current view."""
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
        """Refreshes camera metadata for all Detail Views on the active Layout Page."""
        pageview = sc.doc.Views.ActiveView
        if not isinstance(pageview, Rhino.Display.RhinoPageView):
            print("Warning: Active view is not a Layout.")
            return

        details = pageview.GetDetailViews()
        for detail in details:
            CameraTools.set_camera_metadata(detail.Id)

        print(f"[DEBUG] Refreshed metadata on {len(details)} Detail View(s)")

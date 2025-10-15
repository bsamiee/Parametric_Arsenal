"""
Title         : CenterDetailView.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/CenterDetailView.py

Description
----------------------------------------------------------------------------
Centers selected objects inside a Detail View on a Layout.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Common_Utils, Strings

import Rhino


# --- Helper Class ---------------------------------------------------------
class CenterDetailHelper:
    """Helper functions for centering objects inside a Detail View."""

    @staticmethod
    def get_active_detail_id() -> object | None:
        """
        Detects and returns the ID of the currently active Detail View, if any.

        Returns
        -------
            guid or None: The ID of the active Detail View, or None if not found.
        """
        pageview = sc.doc.Views.ActiveView
        if isinstance(pageview, Rhino.Display.RhinoPageView):
            details = pageview.GetDetailViews()
            active_vp_id = pageview.ActiveViewportID
            for detail in details:
                if detail.Viewport.Id == active_vp_id:
                    return detail.Id
        return None

    @staticmethod
    def filter_selected_objects() -> list[Any]:
        """
        Returns selected object IDs, excluding Detail Views and unselectables.

        Returns
        -------
            list: List of selected object IDs.
        """
        selected_ids = rs.SelectedObjects() or []
        return [obj_id for obj_id in selected_ids if not rs.IsDetail(obj_id, False) and rs.IsObjectSelectable(obj_id)]

    @staticmethod
    def get_center_of_bounding_box(obj_ids: list[Any]) -> object | None:
        """
        Calculates center of bounding box for a set of objects.

        Args:
        ----
            obj_ids (list): List of object GUIDs.

        Returns:
        -------
            Point3d or None: The center point of the bounding box.
        """
        bbox = rs.BoundingBox(obj_ids)
        if not bbox:
            return None
        return Rhino.Geometry.Point3d(
            (bbox[0].X + bbox[6].X) / 2.0,
            (bbox[0].Y + bbox[6].Y) / 2.0,
            (bbox[0].Z + bbox[6].Z) / 2.0,
        )

    @staticmethod
    def format_point3d(pt: Any) -> str:
        """
        Returns a formatted string of a 3D point.

        Args:
        ----
            pt (Point3d): A Rhino 3D point.

        Returns:
        -------
            str: Formatted coordinate string.
        """
        return f"({pt.X:.2f}, {pt.Y:.2f}, {pt.Z:.2f})"


# --- Main Function --------------------------------------------------------
def main() -> None:
    """
    Main entry point to center selected objects inside a Detail View.

    Returns
    -------
        None
    """
    print("\n──────────── Center Detail View ────────────\n")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    detail_id = CenterDetailHelper.get_active_detail_id()

    if not detail_id:
        detail_id = rs.GetObject(Strings.PROMPT_SELECT_DETAIL_VIEW, rs.filter.detail)
        if not detail_id:
            print(Strings.MSG_USER_CANCELLED_DETAIL_SELECTION)
            return

    rh_detail = rs.coercerhinoobject(detail_id)
    if not rh_detail or not isinstance(rh_detail, Rhino.DocObjects.DetailViewObject):
        Common_Utils.alert_user(Strings.MSG_INVALID_DETAIL_SELECTED)
        return

    if rh_detail.IsLocked:
        print(Strings.MSG_DETAIL_LOCKED)
        return

    detail_name = rs.ObjectName(detail_id) or str(detail_id)
    print(Strings.INFO_SELECTED_DETAIL.format(detail_name))

    obj_ids = CenterDetailHelper.filter_selected_objects()

    if not obj_ids:
        obj_ids = rs.GetObjects(Strings.PROMPT_SELECT_OBJECTS, preselect=False)
        if not obj_ids:
            print(Strings.MSG_USER_CANCELLED_OBJECT_SELECTION)
            return

    center_pt = CenterDetailHelper.get_center_of_bounding_box(obj_ids)
    if not center_pt:
        print(Strings.MSG_FAILED_COMPUTE_BBOX)
        return

    print(Strings.INFO_CENTER_POINT.format(CenterDetailHelper.format_point3d(center_pt)))

    undo_record = sc.doc.BeginUndoRecord("Center Detail View")
    try:
        detail_vp = rh_detail.Viewport
        detail_vp.SetCameraTarget(center_pt, True)
        rh_detail.CommitViewportChanges()
        rh_detail.CommitChanges()
    finally:
        sc.doc.EndUndoRecord(undo_record)
        rs.UnselectAllObjects()
        sc.doc.Views.Redraw()

    print("\n[✓] Detail successfully centered\n")
    print("──────────── Script Completed ────────────\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

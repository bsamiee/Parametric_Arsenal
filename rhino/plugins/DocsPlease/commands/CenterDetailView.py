"""
Title         : CenterDetailView.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/CenterDetailView.py

Description
------------------------------------------------------------------------------
Centers selected objects inside a Detail View on a Layout.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.common_utils import require_user_selection, validate_detail_object
from libs.constants import Strings
from libs.exceptions import DetailError, UserCancelledError

import Rhino


def get_active_detail_id() -> object | None:
    """
    Detects and returns the ID of the currently active Detail View, if any.

    Returns:
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


def filter_selected_objects() -> list[Any]:
    """
    Returns selected object IDs, excluding Detail Views and unselectables.

    Returns:
        list: List of selected object IDs.
    """
    selected_ids = rs.SelectedObjects() or []
    return [obj_id for obj_id in selected_ids if not rs.IsDetail(obj_id, False) and rs.IsObjectSelectable(obj_id)]


def get_center_of_bounding_box(obj_ids: list[Any]) -> Rhino.Geometry.Point3d:
    """
    Calculates center of bounding box for a set of objects.

    Args:
        obj_ids: List of object GUIDs.

    Returns:
        Point3d: The center point of the bounding box.

    Raises:
        DetailError: If bounding box cannot be computed.
    """
    bbox = rs.BoundingBox(obj_ids)
    if not bbox:
        raise DetailError(Strings.MSG_FAILED_COMPUTE_BBOX)

    return Rhino.Geometry.Point3d(
        (bbox[0].X + bbox[6].X) / 2.0,
        (bbox[0].Y + bbox[6].Y) / 2.0,
        (bbox[0].Z + bbox[6].Z) / 2.0,
    )


def format_point3d(pt: Any) -> str:
    """
    Returns a formatted string of a 3D point.

    Args:
        pt: A Rhino 3D point.

    Returns:
        str: Formatted coordinate string.
    """
    return f"({pt.X:.2f}, {pt.Y:.2f}, {pt.Z:.2f})"


@rhino_command(requires_layout=True, undo_description="Center Detail View", print_start=False, print_end=False)
def center_detail_view() -> None:
    """
    Centers selected objects inside a Detail View on a Layout.

    Raises:
        UserCancelledError: If user cancels selection.
        DetailError: If detail validation fails or detail is locked.
    """
    print("\n──────────── Center Detail View ────────────\n")

    # Try to get active detail, otherwise prompt user
    detail_id = get_active_detail_id()
    if not detail_id:
        detail_id = require_user_selection(Strings.PROMPT_SELECT_DETAIL_VIEW, rs.filter.detail)

    # Validate detail object
    rh_detail = validate_detail_object(detail_id)

    # Check if detail is locked
    if rh_detail.IsLocked:
        raise DetailError(Strings.MSG_DETAIL_LOCKED)

    # Display selected detail info
    detail_name = rs.ObjectName(detail_id) or str(detail_id)
    print(Strings.INFO_SELECTED_DETAIL.format(detail_name))

    # Get objects to center (from selection or prompt)
    obj_ids = filter_selected_objects()
    if not obj_ids:
        obj_ids = rs.GetObjects(Strings.PROMPT_SELECT_OBJECTS, preselect=False)
        if not obj_ids:
            raise UserCancelledError("Object selection cancelled")

    # Calculate center point
    center_pt = get_center_of_bounding_box(obj_ids)
    print(Strings.INFO_CENTER_POINT.format(format_point3d(center_pt)))

    # Center the detail view on the calculated point
    detail_vp = rh_detail.Viewport
    detail_vp.SetCameraTarget(center_pt, True)
    rh_detail.CommitViewportChanges()
    rh_detail.CommitChanges()

    # Cleanup and feedback
    rs.UnselectAllObjects()
    print("\n[✓] Detail successfully centered\n")
    print("──────────── Script Completed ────────────\n")


# --- Rhino Plugin Entry Point -----------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino plugin entry point."""
    center_detail_view()
    return 0


# --- Script Entry Point -----------------------------------------------------
if __name__ == "__main__":
    center_detail_view()

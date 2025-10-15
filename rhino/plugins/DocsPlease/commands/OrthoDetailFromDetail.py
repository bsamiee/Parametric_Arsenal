"""
Title         : OrthoDetailFromDetail.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/OrthoDetailFromDetail.py

Description
----------------------------------------------------------------------------
Duplicates a Detail View and reorients it to a different orthographic projection.
"""

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Alignment_Tools, Camera_Tools, Common_Utils, Strings

import Rhino


# --- Utility Functions ----------------------------------------------------
def get_friendly_name(obj_id: object) -> str:
    name = rs.ObjectName(obj_id)
    if name:
        return name
    guid_str = str(obj_id)
    return "Detail {}".format(guid_str[:4] + "..." + guid_str[-4:])


def format_point(pt: Any) -> str:
    return f"({pt.X:.2f}, {pt.Y:.2f})"


# --- Main Function --------------------------------------------------------
def main() -> None:  # noqa: PLR0911
    """Creates an orthographic Detail View based on an existing one."""
    print("\n────────── Ortho Detail From Detail ──────────")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    Alignment_Tools.refresh_all_detail_metadata()

    detail_id = Alignment_Tools.get_detail_object_id(Strings.STEP1_PROMPT_SELECT_DETAIL)
    if not detail_id:
        return

    detail_obj = rs.coercerhinoobject(detail_id)
    if not detail_obj or not isinstance(detail_obj, Rhino.DocObjects.DetailViewObject):
        Common_Utils.alert_user(Strings.MSG_INVALID_DETAIL_SELECTED)
        return

    metadata = Camera_Tools.get_camera_metadata(detail_id)
    if not metadata:
        Common_Utils.alert_user(Strings.MSG_FAILED_RETRIEVE_CAMERA_METADATA)
        return

    if metadata.get("projection_mode") != "Parallel":
        Common_Utils.alert_user(Strings.MSG_MUST_BE_PARALLEL_PROJECTION)
        return

    current_view = Camera_Tools.map_camera_direction_to_named_view(metadata.get("direction"))
    if not current_view:
        Common_Utils.alert_user(Strings.MSG_INVALID_CAMERA_DIRECTION)
        return

    target_view = Alignment_Tools.prompt_for_target_projection(current_view)
    if not target_view:
        Common_Utils.alert_user(Strings.MSG_USER_CANCELLED_TARGET_VIEW)
        return

    # Get bounding box and calculate original top-left
    bbox = detail_obj.Geometry.GetBoundingBox(Rhino.Geometry.Plane.WorldXY)
    top_left = Rhino.Geometry.Point3d(bbox.Min.X, bbox.Max.Y, 0)

    insertion_pt = rs.GetPoint(Strings.STEP4_PROMPT_INSERTION_POINT)
    if not insertion_pt:
        Common_Utils.alert_user(Strings.MSG_USER_CANCELLED_INSERTION_POINT)
        return

    undo_record = sc.doc.BeginUndoRecord("Create Ortho Detail")

    try:
        # Translate based on top-left anchor
        translation_vector = Rhino.Geometry.Vector3d(insertion_pt.X - top_left.X, insertion_pt.Y - top_left.Y, 0)

        new_id = rs.CopyObject(detail_id, translation_vector)
        if not new_id:
            Common_Utils.alert_user(Strings.MSG_FAILED_CREATE_DETAIL)
            return

        new_obj = rs.coercerhinoobject(new_id)
        if not isinstance(new_obj, Rhino.DocObjects.DetailViewObject):
            Common_Utils.alert_user("Failed to coerce new Detail View object.")
            return

        if not Camera_Tools.set_camera_projection_for_named_view(new_id, str(target_view)):
            Common_Utils.alert_user(Strings.MSG_FAILED_SET_CAMERA)
            return

        Camera_Tools.set_camera_metadata(new_id)

    finally:
        sc.doc.EndUndoRecord(undo_record)
        sc.doc.Views.Redraw()

    print(f"[✓] Original Detail : {get_friendly_name(detail_id)}")
    print(f"[✓] New Projection  : {target_view}")
    print(f"[✓] Placement Point : {format_point(insertion_pt)}")
    print(f"[✓] New Detail Name : {get_friendly_name(new_id)}")
    print("──────────── Detail Created Successfully ────────────\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

"""
Title         : OrthoDetailFromDetail.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/OrthoDetailFromDetail.py

Description
------------------------------------------------------------------------------
Duplicates a Detail View and reorients it to a different orthographic projection.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
from libs.alignment_tools import AlignmentTools
from libs.camera_tools import CameraTools
from libs.command_framework import rhino_command
from libs.common_utils import require_user_point, validate_detail_object
from libs.constants import Strings
from libs.exceptions import CameraError, DetailError, UserCancelledError, ValidationError

import Rhino


def get_friendly_name(obj_id: object) -> str:
    """Returns a friendly name for a detail object."""
    name = rs.ObjectName(obj_id)
    if name:
        return name
    guid_str = str(obj_id)
    return f"Detail {guid_str[:4]}...{guid_str[-4:]}"


def format_point(pt: Any) -> str:
    """Formats a point for display."""
    return f"({pt.X:.2f}, {pt.Y:.2f})"


@rhino_command(requires_layout=True, undo_description="Create Ortho Detail", print_start=False)
def ortho_detail_from_detail() -> None:
    """Creates an orthographic Detail View based on an existing one.

    Raises:
        UserCancelledError: If user cancels any selection or input
        DetailError: If invalid detail view selected
        CameraError: If camera operations fail
        ValidationError: If projection mode is not parallel or camera direction invalid
    """
    print("\n────────── Ortho Detail From Detail ──────────")

    # Refresh all detail metadata
    AlignmentTools.refresh_all_detail_metadata()

    # Select source detail
    detail_id = AlignmentTools.get_detail_object_id(Strings.STEP1_PROMPT_SELECT_DETAIL)
    detail_obj = validate_detail_object(detail_id)

    # Get and validate camera metadata
    metadata = CameraTools.get_camera_metadata(detail_id)
    if not metadata:
        raise CameraError(Strings.MSG_FAILED_RETRIEVE_CAMERA_METADATA)

    if metadata.get("projection_mode") != "Parallel":
        raise ValidationError(Strings.MSG_MUST_BE_PARALLEL_PROJECTION)

    current_view = CameraTools.map_camera_direction_to_named_view(metadata.get("direction"))
    if not current_view:
        raise ValidationError(Strings.MSG_INVALID_CAMERA_DIRECTION)

    # Get target projection
    target_view = AlignmentTools.prompt_for_target_projection(current_view)
    if not target_view:
        raise UserCancelledError(Strings.MSG_USER_CANCELLED_TARGET_VIEW)

    # Get bounding box and calculate original top-left
    bbox = detail_obj.Geometry.GetBoundingBox(Rhino.Geometry.Plane.WorldXY)
    top_left = Rhino.Geometry.Point3d(bbox.Min.X, bbox.Max.Y, 0)

    # Get insertion point
    insertion_pt = require_user_point(Strings.STEP4_PROMPT_INSERTION_POINT)

    # Translate based on top-left anchor
    translation_vector = Rhino.Geometry.Vector3d(insertion_pt.X - top_left.X, insertion_pt.Y - top_left.Y, 0)

    # Copy detail to new location
    new_id = rs.CopyObject(detail_id, translation_vector)
    if not new_id:
        raise DetailError(Strings.MSG_FAILED_CREATE_DETAIL)

    new_obj = rs.coercerhinoobject(new_id)
    if not isinstance(new_obj, Rhino.DocObjects.DetailViewObject):
        raise DetailError("Failed to coerce new Detail View object")

    # Set camera projection for target view
    if not CameraTools.set_camera_projection_for_named_view(new_id, str(target_view)):
        raise CameraError(Strings.MSG_FAILED_SET_CAMERA)

    # Update camera metadata
    CameraTools.set_camera_metadata(new_id)

    # Print success information
    print(f"[✓] Original Detail : {get_friendly_name(detail_id)}")
    print(f"[✓] New Projection  : {target_view}")
    print(f"[✓] Placement Point : {format_point(insertion_pt)}")
    print(f"[✓] New Detail Name : {get_friendly_name(new_id)}")
    print("──────────── Detail Created Successfully ────────────\n")


# --- Rhino Plugin Entry Point -----------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    ortho_detail_from_detail()
    return 0


# --- Script Entry Point -----------------------------------------------------
if __name__ == "__main__":
    ortho_detail_from_detail()

"""
Title         : DetailCaption.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/DetailCaption.py

Description
----------------------------------------------------------------------------
Creates numbered detail captions with title and scale text positioned below Detail Views.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.common_utils import require_user_selection, require_user_string, validate_detail_object
from libs.constants import Constants, Strings
from libs.detail_tools import DetailTools


# --- Helper Functions -----------------------------------------------------
def count_existing_captions() -> int:
    """
    Counts existing Detail Captions on the caption layer.

    Returns:
        int: Number of Detail Captions found on the caption layer.
    """
    objs = rs.ObjectsByLayer(Constants.CAPTION_LAYER)
    count = 0
    if objs:
        for obj in objs:
            rh_obj = rs.coercerhinoobject(obj)
            if rh_obj and rs.IsText(rh_obj.Id) and rs.GetUserText(rh_obj.Id, "caption_type") == "DetailCaption":
                count += 1
    return count


def fetch_detail_scale(detail_id: object) -> str:
    """
    Fetches the stored or default scale string for a Detail View.

    Args:
        detail_id: The ID of the Detail View object.

    Returns:
        str: The scale string in upper case, or default if not found.
    """
    rh_obj = rs.coercerhinoobject(detail_id)
    if rh_obj:
        scale_text = rh_obj.Attributes.GetUserString("detail_scale")
        if scale_text:
            return scale_text.upper()
    return Strings.SCALE_NA


def create_caption_text(content: str, base_point: tuple[float, float, float], height: float) -> object | None:
    """
    Creates a text object at specified base point.

    Args:
        content: Text content.
        base_point: Insertion point (x, y, z).
        height: Text height.

    Returns:
        guid: The ID of the created text object, or None.
    """
    text_id = rs.AddText(content, base_point, height)
    if text_id:
        rs.ObjectLayer(text_id, Constants.CAPTION_LAYER)
    return text_id


def align_caption_elements(  # noqa: PLR0914
    detail_id: object, number_text: str, title_text: str, scale_text: str
) -> tuple[Any, Any, Any] | None:
    """
    Aligns number, title, and scale text objects under the Detail View.

    Args:
        detail_id: ID of the Detail View.
        number_text: Caption number.
        title_text: Caption title.
        scale_text: Caption scale.

    Returns:
        tuple: IDs of created text objects (number_id, title_id, scale_id), or None on failure.

    Raises:
        ValidationError: If text creation fails.
    """
    from libs.exceptions import ValidationError

    offset_below_detail = -0.15

    rh_detail = rs.coercerhinoobject(detail_id)
    bbox = rh_detail.Geometry.GetBoundingBox(sc.doc.Views.ActiveView.ActiveViewport.ConstructionPlane())

    center_x = (bbox.Min.X + bbox.Max.X) / 2.0
    base_y = bbox.Min.Y + offset_below_detail

    number_id = create_caption_text(number_text, (0, 0, 0), 0.6)
    title_id = create_caption_text(title_text, (0, 0, 0), 0.3)
    scale_id = create_caption_text(scale_text, (0, 0, 0), 0.15)

    if not all([number_id, title_id, scale_id]):
        raise ValidationError(Strings.MSG_FAILED_CREATE_TEXT)

    # Adjust positioning
    bbox_number = rs.BoundingBox(number_id)
    width_number = bbox_number[1][0] - bbox_number[0][0]

    title_offset = (width_number + 0.1, 0.075, 0)
    scale_offset = (width_number + 0.1, -(0.15 + 0.075), 0)

    rs.MoveObject(title_id, title_offset)
    rs.MoveObject(scale_id, scale_offset)

    temp_group = rs.AddGroup()
    rs.AddObjectsToGroup([number_id, title_id, scale_id], temp_group)

    # Center the group
    full_bbox = rs.BoundingBox([number_id, title_id, scale_id])
    full_center_x = (full_bbox[0][0] + full_bbox[6][0]) / 2.0
    full_center_y = (full_bbox[0][1] + full_bbox[6][1]) / 2.0

    caption_height = full_bbox[6][1] - full_bbox[0][1]

    move_vector = (
        center_x - full_center_x,
        base_y + (caption_height / 2.0) - full_center_y,
        0,
    )
    rs.MoveObjects([number_id, title_id, scale_id], move_vector)

    rs.DeleteGroup(temp_group)

    # Final Group with Metadata
    group_name = f"DetailCaption_{number_text}"
    rs.AddGroup(group_name)
    rs.AddObjectsToGroup([number_id, title_id, scale_id], group_name)

    # Metadata (attach only to Scale Text)
    rs.SetUserText(scale_id, "caption_type", "DetailCaption")
    rs.SetUserText(scale_id, "linked_detail_id", str(detail_id))

    return (number_id, title_id, scale_id)


# --- Main Function --------------------------------------------------------
@rhino_command(requires_layout=True, undo_description="Create Detail Caption")
def detail_caption() -> None:
    """
    Creates a numbered detail caption with title and scale text below a Detail View.

    Raises:
        UserCancelledError: If user cancels selection or input.
        DetailError: If selected object is not a valid detail view.
        ValidationError: If caption text creation fails.
    """
    # Get detail selection (preselect or prompt)
    selected = rs.SelectedObjects() or []
    if selected:
        detail_id = selected[0]
    else:
        detail_id = require_user_selection(Strings.PROMPT_SELECT_DETAIL, rs.filter.detail)

    # Validate detail object
    validate_detail_object(detail_id)

    # Ensure caption layer exists
    DetailTools.ensure_layer_exists(Constants.CAPTION_LAYER)

    # Generate caption number
    caption_count = count_existing_captions()
    number_text = str(caption_count + 1)

    # Get title from user
    title_text = require_user_string(Strings.PROMPT_ENTER_TITLE, "DETAIL", "Detail Caption")

    # Fetch scale from detail
    scale_text = fetch_detail_scale(detail_id)

    # Create and align caption elements
    align_caption_elements(detail_id, number_text, title_text, scale_text)


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    detail_caption()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    detail_caption()

"""
Title         : AlignDetailViews.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/AlignDetailViews.py

Description
----------------------------------------------------------------------------
Aligns two Detail Views horizontally or vertically based on user-picked reference points.
"""

from __future__ import annotations

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.alignment_tools import AlignmentTools
from libs.command_framework import rhino_command
from libs.constants import Strings
from libs.exceptions import ValidationError

import Rhino


@rhino_command(requires_layout=True, undo_description="Align Detail Views")
def align_detail_views() -> None:
    """Aligns two Detail Views horizontally or vertically based on user-picked points.

    Raises:
        UserCancelledError: If user cancels any selection or input
        DetailError: If invalid detail view selected
        ValidationError: If parent and child are the same or invalid alignment choice
        TransformError: If transformation fails
    """
    # Set page as active and clear selection
    active_view = sc.doc.Views.ActiveView
    active_view.SetPageAsActive()
    rs.UnselectAllObjects()

    # Select Parent Detail
    parent_detail_id = AlignmentTools.get_detail_object_id(Strings.PROMPT_SELECT_PARENT)
    rs.SelectObject(parent_detail_id)
    rs.UnselectAllObjects()

    # Select Child Detail
    child_detail_id = AlignmentTools.get_detail_object_id(Strings.PROMPT_SELECT_CHILD)
    rs.SelectObject(child_detail_id)

    # Validate parent and child are different
    if parent_detail_id == child_detail_id:
        raise ValidationError(Strings.MSG_PARENT_CHILD_SAME)

    rs.UnselectAllObjects()

    # Pick Points
    pt_parent = AlignmentTools.get_point_on_page(Strings.PROMPT_PICK_PARENT_POINT)
    pt_child = AlignmentTools.get_point_on_page(Strings.PROMPT_PICK_CHILD_POINT)

    # Choose Alignment Type
    align_choice = rs.GetString(Strings.PROMPT_DIRECTION, Strings.DEFAULT_DIRECTION, Strings.DIRECTION_OPTIONS)
    if not align_choice:
        from libs.exceptions import UserCancelledError

        raise UserCancelledError("Alignment type selection cancelled")

    align_choice = align_choice.capitalize()
    if align_choice not in Strings.DIRECTION_OPTIONS:
        raise ValidationError(Strings.MSG_INVALID_ALIGNMENT)

    # Calculate and apply transformation
    translation_vector = AlignmentTools.calculate_translation_vector(pt_parent, pt_child, align_choice)

    if translation_vector:
        transform = Rhino.Geometry.Transform.Translation(translation_vector)

        if not rs.TransformObject(child_detail_id, transform):
            from libs.exceptions import TransformError

            raise TransformError(Strings.MSG_FAILED_TRANSFORM)

        # Select child detail for further adjustment
        rs.SelectObject(child_detail_id)
        print("\nAlignment complete. Child Detail is selected for further adjustment.")
    else:
        print("\nPicked points already aligned. No movement necessary.")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    align_detail_views()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    align_detail_views()

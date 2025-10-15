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
from libs import Common_Utils, Constants, Strings


# --- Helper Class ---------------------------------------------------------
class DetailCaptionHelper:
    @staticmethod
    def ensure_layer_exists(layer_name: str) -> None:
        """
        Ensures the specified layer exists.

        Args:
        ----
            layer_name (str): Name of the layer to ensure exists.
        """
        if not rs.IsLayer(layer_name):
            rs.AddLayer(layer_name)

    @staticmethod
    def count_existing_captions() -> int:
        """
        Counts existing Detail Captions on the caption layer.

        Returns
        -------
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

    @staticmethod
    def fetch_detail_scale(detail_id: object) -> str:
        """
        Fetches the stored or default scale string for a Detail View.

        Args:
        ----
            detail_id (guid): The ID of the Detail View object.

        Returns:
        -------
            str: The scale string in upper case, or default if not found.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if rh_obj:
            scale_text = rh_obj.Attributes.GetUserString("detail_scale")
            if scale_text:
                return scale_text.upper()
        return Strings.SCALE_NA

    @staticmethod
    def create_caption_text(content: str, base_point: tuple[float, float, float], height: float) -> object | None:
        """
        Creates a text object at specified base point.

        Args:
        ----
            content (str): Text content.
            base_point (tuple): Insertion point (x, y, z).
            height (float): Text height.

        Returns:
        -------
            guid: The ID of the created text object, or None.
        """
        text_id = rs.AddText(content, base_point, height)
        if text_id:
            rs.ObjectLayer(text_id, Constants.CAPTION_LAYER)
        return text_id

    @staticmethod
    def align_caption_elements(  # noqa: PLR0914
        detail_id: object, number_text: str, title_text: str, scale_text: str
    ) -> tuple[Any, Any, Any] | None:
        """
        Aligns number, title, and scale text objects under the Detail View.

        Args:
        ----
            detail_id (guid): ID of the Detail View.
            number_text (str): Caption number.
            title_text (str): Caption title.
            scale_text (str): Caption scale.

        Returns:
        -------
            None
        """
        offset_below_detail = -0.15

        rh_detail = rs.coercerhinoobject(detail_id)
        bbox = rh_detail.Geometry.GetBoundingBox(sc.doc.Views.ActiveView.ActiveViewport.ConstructionPlane())

        center_x = (bbox.Min.X + bbox.Max.X) / 2.0
        base_y = bbox.Min.Y + offset_below_detail

        undo_record = sc.doc.BeginUndoRecord("Create Detail Caption")

        try:
            number_id = DetailCaptionHelper.create_caption_text(number_text, (0, 0, 0), 0.6)
            title_id = DetailCaptionHelper.create_caption_text(title_text, (0, 0, 0), 0.3)
            scale_id = DetailCaptionHelper.create_caption_text(scale_text, (0, 0, 0), 0.15)

            if not all([number_id, title_id, scale_id]):
                Common_Utils.alert_user(Strings.MSG_FAILED_CREATE_TEXT)
                return None

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

        finally:
            sc.doc.EndUndoRecord(undo_record)


# --- Main Function --------------------------------------------------------
def main() -> None:
    """
    Main entry point for creating a Detail Caption.

    Returns
    -------
        None
    """
    print("\n=== Detail Caption Script Started ===\n")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    selected = rs.SelectedObjects() or []
    detail_id = selected[0] if selected else rs.GetObject(Strings.PROMPT_SELECT_DETAIL, rs.filter.detail)

    if not detail_id:
        print("No Detail View selected.")
        return

    DetailCaptionHelper.ensure_layer_exists(Constants.CAPTION_LAYER)

    caption_count = DetailCaptionHelper.count_existing_captions()
    number_text = str(caption_count + 1)

    title_text = rs.StringBox(Strings.PROMPT_ENTER_TITLE, "DETAIL", "Detail Caption")
    if not title_text:
        print("User canceled title input.")
        return

    scale_text = DetailCaptionHelper.fetch_detail_scale(detail_id)

    DetailCaptionHelper.align_caption_elements(detail_id, number_text, title_text, scale_text)

    sc.doc.Views.Redraw()

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

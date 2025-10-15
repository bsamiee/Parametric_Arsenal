"""
Title         : RefreshDetailCaptions.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/RefreshDetailCaptions.py

Description
----------------------------------------------------------------------------
Updates scale text in existing Detail Captions to match current Detail View scales.
"""

import rhinoscriptsyntax as rs
import scriptcontext as sc
import System
from libs import Common_Utils, Constants, Detail_Tools, Strings

import Rhino


# --- Helper Class ---------------------------------------------------------
class RefreshDetailCaptionsHelper:
    """Helper functions for fetching current scale from linked Detail Views."""

    @staticmethod
    def fetch_current_scale(detail_id: object) -> str:
        """
        Fetches stored or live scale information for a Detail View.

        Args:
        ----
            detail_id (guid): The ID of the detail view.

        Returns:
        -------
            str: The most accurate scale label available, or a fallback.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if rh_obj and isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            # Try fetching user-stored scale label first
            scale_text = rh_obj.Attributes.GetUserString("detail_scale")
            if scale_text:
                # Normalize smart quotes if necessary
                scale_text = scale_text.replace(""", '"').replace(""", '"')
                scale_text = scale_text.replace("'", "'").replace("'", "'")
                return scale_text.strip()

            # If no user text, fallback to live PageToModelRatio
            ratio = getattr(rh_obj.DetailGeometry, "PageToModelRatio", None)
            if ratio and ratio > 0.0:
                return Detail_Tools.format_architectural_scale(1.0, 1.0 / ratio)

        return Constants.SCALE_NA_LABEL


# --- Main Function --------------------------------------------------------
def main() -> None:
    """
    Refreshes all Detail Captions on the active Layout by syncing their Scale text.

    Returns
    -------
        None
    """
    print("\n=== Refresh Detail Captions Script Started ===\n")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    objs = rs.ObjectsByLayer(Constants.CAPTION_LAYER)
    if not objs:
        print(Strings.MSG_NO_CAPTIONS_FOUND)
        return

    updated_count = 0
    skipped_count = 0

    undo_record = sc.doc.BeginUndoRecord("Refresh Detail Captions")

    try:
        for obj_id in objs:
            if not rs.IsText(obj_id):
                continue  # Skip non-text objects

            caption_type = rs.GetUserText(obj_id, "caption_type")
            linked_detail_id = rs.GetUserText(obj_id, "linked_detail_id")

            if caption_type != "DetailCaption" or not linked_detail_id:
                continue  # Skip irrelevant text objects

            try:
                linked_detail_id = System.Guid(linked_detail_id)
            except (ValueError, TypeError):
                skipped_count += 1
                continue

            rh_detail = sc.doc.Objects.Find(linked_detail_id)
            if not rh_detail:
                skipped_count += 1
                continue  # Detail view was deleted

            current_text = rs.TextObjectText(obj_id)
            if current_text and current_text.startswith("SCALE"):
                latest_scale = RefreshDetailCaptionsHelper.fetch_current_scale(linked_detail_id)
                if latest_scale and latest_scale != current_text:
                    rs.TextObjectText(obj_id, latest_scale)
                    updated_count += 1

    finally:
        sc.doc.EndUndoRecord(undo_record)

    sc.doc.Views.Redraw()

    print(Strings.MSG_CAPTION_UPDATE_SUMMARY)
    print(Strings.MSG_CAPTIONS_UPDATED.format(updated_count))
    print(Strings.MSG_CAPTIONS_SKIPPED.format(skipped_count))
    print(Strings.MSG_REFRESH_COMPLETE)


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

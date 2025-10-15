"""
Title         : EditPageScale.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/EditPageScale.py

Description
----------------------------------------------------------------------------
Edits the page scale metadata for the active Layout.
"""

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Common_Utils, Constants, Detail_Tools, Strings


# --- Helper Class ---------------------------------------------------------
class EditPageScaleHelper:
    @staticmethod
    def validate_environment() -> bool:
        if not Common_Utils.is_layout_view_active():
            Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
            return False
        model_units = Common_Utils.get_model_unit_system()
        if model_units not in Constants.SUPPORTED_UNIT_SYSTEMS:
            Common_Utils.alert_user(Strings.MSG_UNSUPPORTED_UNIT_SYSTEM)
            return False
        return True

    @staticmethod
    def get_scale_dictionary() -> dict[str, float]:
        # This correctly gets either Imperial or Metric Arch scales based on units
        return Detail_Tools.get_available_scales()


# --- Main Function --------------------------------------------------------
def main() -> None:
    """Main entry point for editing the Layout Page Scale metadata."""
    print("=== Edit Page Scale Script Started ===")

    if not EditPageScaleHelper.validate_environment():
        return

    scale_dict = EditPageScaleHelper.get_scale_dictionary()
    if not scale_dict:
        print("Error: Could not determine available scales for the current model units.")
        return

    # Determine the correct ordered list for the ListBox
    keys = []
    if scale_dict == Constants.ARCHITECTURAL_SCALES_IMPERIAL:
        keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
    elif scale_dict == Constants.ARCHITECTURAL_SCALES_METRIC:
        keys = sorted(scale_dict.keys())
    else:
        # Fallback if the dictionary is somehow unexpected
        keys = sorted(scale_dict.keys())

    if not keys:
        print("Error: No scale keys found to display.")
        return

    # Present the correctly ordered list
    choice = rs.ListBox(
        keys,  # Use the ordered list determined above
        Strings.PROMPT_EDIT_PAGE_SCALE,
        Strings.PROMPT_SET_PAGE_SCALE,  # Title for the list box
    )
    if not choice:
        print("User canceled scale selection.")
        return

    # Set the chosen scale metadata
    undo_record = sc.doc.BeginUndoRecord("Edit Page Scale")
    try:
        # The choice variable holds the selected scale label string
        Detail_Tools.set_page_scale_metadata(choice)
    finally:
        # Use the integer return value from EndUndoRecord
        sc.doc.EndUndoRecord(undo_record)
        # Optional: Check undo_result if needed

    print(f"New Page Scale set to: {choice}")
    print("=== Script Completed ===")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

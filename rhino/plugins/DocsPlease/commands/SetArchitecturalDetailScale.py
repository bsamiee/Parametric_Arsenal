"""
Title         : SetArchitecturalDetailScale.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/SetArchitecturalDetailScale.py

Description
----------------------------------------------------------------------------
Sets architectural scales on Detail Views with options for individual or batch operations.
"""

import sys
from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
import System
from libs import Common_Utils, Constants, Detail_Tools, Strings

import Rhino


# --- Helper Class ---------------------------------------------------------
class SetArchitecturalDetailScaleHelper:
    """Helper functions for setting architectural scales on Detail Views."""

    @staticmethod
    def validate_environment() -> bool:
        if not Common_Utils.is_layout_view_active():
            Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
            return False
        units = Common_Utils.get_model_unit_system()
        if units not in Constants.SUPPORTED_UNIT_SYSTEMS:
            Common_Utils.alert_user(Strings.MSG_UNSUPPORTED_UNIT_SYSTEM)
            return False
        return True

    @staticmethod
    def get_scale_dictionary() -> dict[str, float]:
        # Return appropriate scales based on model units for flexibility
        return Detail_Tools.get_available_scales()

    @staticmethod
    def set_details_to_scale(
        detail_ids: list[Any], page_length: float, model_length: float, scale_label: str
    ) -> list[str]:
        """Calls library function to set scale and label for multiple details."""
        log: list[str] = []
        for did in detail_ids:
            # Pass scale_label to the library function
            Detail_Tools.set_detail_scale(did, page_length, model_length, scale_label)
            # Log uses the same passed scale_label
            log.append(Strings.MSG_DETAIL_SET_TO_SCALE.format(str(did), scale_label))
        return log

    @staticmethod
    def batch_set_details_to_scale(page_length: float, model_length: float, scale_label: str) -> list[str]:
        """Calls library function to set scale and label for all details on the page."""
        log: list[str] = []
        pageview = sc.doc.Views.ActiveView
        # Ensure pageview is valid before getting details
        if not isinstance(pageview, Rhino.Display.RhinoPageView):
            Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
            return log
        details = pageview.GetDetailViews()
        if not details:
            Common_Utils.alert_user(Strings.MSG_NO_DETAILS_FOUND)
            return log
        for detail in details:
            # Pass scale_label to the library function
            Detail_Tools.set_detail_scale(detail.Id, page_length, model_length, scale_label)
            # Log uses the same passed scale_label
            log.append(Strings.MSG_DETAIL_SET_TO_SCALE.format(str(detail.Id), scale_label))
        return log


# --- Main Function --------------------------------------------------------
def main() -> None:  # noqa: PLR0911, PLR0912, PLR0915
    print("\n=== Set Architectural Detail Scale Script Started ===\n")

    if not SetArchitecturalDetailScaleHelper.validate_environment():
        return

    scale_dict = SetArchitecturalDetailScaleHelper.get_scale_dictionary()
    page_scale = Detail_Tools.get_page_scale_metadata()

    # If no stored page-scale metadata, prompt and store it
    if not page_scale:
        if scale_dict == Constants.ARCHITECTURAL_SCALES_IMPERIAL:
            keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
        elif scale_dict == Constants.ARCHITECTURAL_SCALES_METRIC:
            # Define a metric order list in Constants if needed, otherwise sort
            keys = sorted(scale_dict.keys())  # Or Constants.ARCHITECTURAL_SCALES_METRIC_ORDER
        else:
            # Fallback if scale_dict is unexpectedly different
            keys = sorted(scale_dict.keys()) if scale_dict else []

        if not keys:
            print("Error: No scales available to set page scale.")
            return

        choice = rs.ListBox(
            keys,  # Now uses the correctly determined ordered list
            Strings.PROMPT_SET_PAGE_SCALE,
            Strings.PROMPT_ARCHITECTURAL_DETAIL_SCALES,
        )
        if not choice:
            print(Strings.MSG_USER_CANCELLED_SCALE_SELECTION)
            return
        Detail_Tools.set_page_scale_metadata(choice)
        page_scale = choice  # Use the selected label for this session

    # Choose operation
    operation = rs.ListBox(
        Strings.OPTIONS_ARCHITECTURAL_OPERATIONS,  # Assumes this list is defined correctly in order
        Strings.PROMPT_CHOOSE_OPERATION,
        Strings.PROMPT_ARCHITECTURAL_DETAIL_SCALES,
    )
    if not operation:
        print(Strings.MSG_USER_CANCELLED_OPERATION)
        return

    confirmation_log = []
    undo = sc.doc.BeginUndoRecord("Architectural Detail Scaling")

    try:
        if operation == Strings.OP_SET_TO_CUSTOM_SCALE:
            ids = Detail_Tools.get_detail_objects(preselect_allowed=True)
            if not ids:
                Common_Utils.alert_user(Strings.MSG_NO_DETAILS_SELECTED)
                return
            # select_scale now returns label as 3rd item
            # This function correctly uses ordered lists internally
            sel_result = Detail_Tools.select_scale(
                scale_dict, mode="Architectural", title=Strings.PROMPT_SET_CUSTOM_SCALE
            )
            if not sel_result:
                return
            # Unpack all three return values
            p_len, m_len, selected_label = sel_result
            # Pass the selected_label directly to the helper
            confirmation_log = SetArchitecturalDetailScaleHelper.set_details_to_scale(
                ids, p_len, m_len, selected_label
            )  # Use selected_label

        # Use stored page scale on selected details
        elif operation == Strings.OP_SET_TO_PAGE_SCALE:
            ids = Detail_Tools.get_detail_objects(preselect_allowed=True)
            if not ids:
                Common_Utils.alert_user(Strings.MSG_NO_DETAILS_SELECTED)
                return
            key = page_scale.strip()  # 'key' is the label stored on the page
            m_len = scale_dict.get(key)
            if m_len is None:
                Common_Utils.alert_user(Strings.MSG_PAGE_SCALE_NOT_RECOGNIZED)
                return
            # Pass the page scale label ('key') directly to the helper
            confirmation_log = SetArchitecturalDetailScaleHelper.set_details_to_scale(ids, 1.0, m_len, key)  # Use key

        # Custom-scale on all details
        elif operation == Strings.OP_BATCH_CUSTOM_SCALE:
            # select_scale now returns label as 3rd item
            # This function correctly uses ordered lists internally
            sel_result = Detail_Tools.select_scale(
                scale_dict,
                mode="Architectural",
                title=Strings.PROMPT_SET_CUSTOM_SCALE_FOR_ALL,
            )
            if not sel_result:
                return
            # Unpack all three return values
            p_len, m_len, selected_label = sel_result
            # Pass the selected_label directly to the helper
            confirmation_log = SetArchitecturalDetailScaleHelper.batch_set_details_to_scale(
                p_len, m_len, selected_label
            )  # Use selected_label

        # Stored page-scale on all details
        elif operation == Strings.OP_BATCH_PAGE_SCALE:
            key = page_scale.strip()  # 'key' is the label stored on the page
            m_len = scale_dict.get(key)
            if m_len is None:
                Common_Utils.alert_user(Strings.MSG_PAGE_SCALE_NOT_RECOGNIZED)
                return
            # Pass the page scale label ('key') directly to the helper
            confirmation_log = SetArchitecturalDetailScaleHelper.batch_set_details_to_scale(1.0, m_len, key)  # Use key

    finally:
        # Use the integer return value from EndUndoRecord
        sc.doc.EndUndoRecord(undo)
        # Optional: Check undo_result if needed

    # Highlight and report
    if confirmation_log:
        # Attempt to extract GUIDs from the log messages for highlighting
        modified_ids = []
        for line in confirmation_log:
            guid_str = ""
            try:
                # Assumes format "Detail '{GUID}' set to Scale: ..."
                guid_str = line.split("'")[1]
                # Basic validation if it looks like a GUID structure (optional)
                if len(guid_str) == 36 and guid_str.count("-") == 4:
                    # Ensure System is imported for System.Guid
                    if "System" in sys.modules:
                        modified_ids.append(System.Guid(guid_str))  # Convert to Guid objects
                    else:
                        print("Warning: System module not imported, cannot create Guid objects.")
            except IndexError:
                print(f"Warning: Could not parse GUID from log line: {line}")
            except (ValueError, TypeError) as e:
                print(f"Warning: Error converting GUID string '{guid_str}': {e}")

        if modified_ids:
            Detail_Tools.highlight_details(modified_ids)  # Pass list of Guids

        print("\n----- Detail Scale Changes -----")
        for line in confirmation_log:
            print(line)
        print("---------------------------------")

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

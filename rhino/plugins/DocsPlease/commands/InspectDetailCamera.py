"""
Title         : InspectDetailCamera.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/InspectDetailCamera.py

Description
----------------------------------------------------------------------------
Inspects and captures camera metadata for all Detail Views on the active Layout.
"""

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Camera_Tools, Common_Utils, Strings

import Rhino


# --- Main Function --------------------------------------------------------
def main() -> None:
    """Inspects and captures camera metadata for all Detail Views on the active Layout Page."""
    print("\n=== Inspect All Detail Cameras Script Started ===\n")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    pageview = sc.doc.Views.ActiveView
    if not isinstance(pageview, Rhino.Display.RhinoPageView):
        print("[ERROR] Active view is not a Layout PageView.")
        return

    details = pageview.GetDetailViews()
    if not details:
        print("[INFO] No Detail Views found on this Layout.")
        return

    print(f"[INFO] Found {len(details)} Detail View(s) on Layout: '{pageview.PageName}'\n")

    for idx, detail in enumerate(details):
        detail_id = detail.Id
        detail_name = rs.ObjectName(detail_id) or str(detail_id)

        print(f"\n--- [{idx + 1}] Detail Name: {detail_name} ---")

        metadata = Camera_Tools.get_camera_metadata(detail_id)

        if not metadata:
            print(Strings.INFO_NO_CAMERA_METADATA)

            undo_record = sc.doc.BeginUndoRecord("Capture Camera Metadata")
            try:
                if Camera_Tools.set_camera_metadata(detail_id):
                    metadata = Camera_Tools.get_camera_metadata(detail_id)
                    if not metadata:
                        Common_Utils.alert_user(Strings.MSG_FAILED_CAPTURE_CAMERA)
                        continue
                else:
                    Common_Utils.alert_user(Strings.MSG_FAILED_SET_CAMERA)
                    continue
            finally:
                sc.doc.EndUndoRecord(undo_record)

        if metadata:
            print("\n--- Stored Camera Metadata ---")
            for key, value in metadata.items():
                print(f"{key} : {value}")
            print("--------------------------------")
        else:
            print("[ERROR] Failed to retrieve metadata after capture attempt.")

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

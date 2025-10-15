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

from __future__ import annotations

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.camera_tools import CameraTools
from libs.command_framework import rhino_command
from libs.constants import Strings
from libs.exceptions import CameraError


@rhino_command(requires_layout=True, undo_description="Capture Camera Metadata", print_start=False, print_end=False)
def inspect_detail_cameras() -> None:
    """
    Inspects and captures camera metadata for all Detail Views on the active Layout Page.

    Raises:
        CameraError: If camera metadata operations fail.
    """
    print("\n=== Inspect All Detail Cameras Script Started ===\n")

    # Get active page view
    pageview = sc.doc.Views.ActiveView

    # Get all detail views on the layout
    details = pageview.GetDetailViews()
    if not details:
        print("[INFO] No Detail Views found on this Layout.")
        return

    print(f"[INFO] Found {len(details)} Detail View(s) on Layout: '{pageview.PageName}'\n")

    # Process each detail view
    for idx, detail in enumerate(details):
        detail_id = detail.Id
        detail_name = rs.ObjectName(detail_id) or str(detail_id)

        print(f"\n--- [{idx + 1}] Detail Name: {detail_name} ---")

        # Try to get existing camera metadata
        try:
            metadata = CameraTools.get_camera_metadata(detail_id)
        except CameraError:
            # No metadata exists, capture it
            print(Strings.INFO_NO_CAMERA_METADATA)

            try:
                CameraTools.set_camera_metadata(detail_id)
                metadata = CameraTools.get_camera_metadata(detail_id)
            except CameraError as e:
                print(f"[ERROR] Failed to capture camera metadata: {e}")
                continue

        # Display metadata
        if metadata:
            print("\n--- Stored Camera Metadata ---")
            for key, value in metadata.items():
                print(f"{key} : {value}")
            print("--------------------------------")

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino plugin entry point."""
    inspect_detail_cameras()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    inspect_detail_cameras()

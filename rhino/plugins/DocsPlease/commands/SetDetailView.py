"""
Title         : SetDetailView.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/SetDetailView.py

Description
----------------------------------------------------------------------------
Set viewport projection for detail views with smart selection.
"""

from __future__ import annotations

import rhinoscriptsyntax as rs
from libs.camera_tools import CameraTools
from libs.command_framework import rhino_command
from libs.common_utils import require_user_choice
from libs.detail_tools import DetailTools
from libs.exceptions import CameraError, DetailError


@rhino_command(requires_layout=True, undo_description="Set Detail View Projection")
def set_detail_view() -> None:
    """
    Set viewport projection for detail views with smart selection handling.

    Raises:
        UserCancelledError: If user cancels selection or projection choice.
        CameraError: If camera projection fails.
        DetailError: If detail validation fails.
    """
    # Get detail objects (supports pre-selection)
    detail_ids = DetailTools.get_detail_objects(preselect_allowed=True)

    # Define projection options
    projections = [
        "Top",
        "Bottom",
        "Front",
        "Back",
        "Left",
        "Right",
        "SW Isometric",
        "SE Isometric",
        "NE Isometric",
        "NW Isometric",
    ]

    # Get projection choice from user
    projection = require_user_choice(projections, "Select View Projection", "Set Detail View")

    # Apply projection to each detail
    success_count = 0
    failed = []

    for detail_id in detail_ids:
        try:
            # Skip locked details
            if rs.IsObjectLocked(detail_id):
                failed.append((detail_id, "locked"))
                continue

            # Apply appropriate projection method
            if "Isometric" in projection:
                CameraTools.set_isometric_projection(detail_id, projection)
            else:
                CameraTools.set_camera_projection_for_named_view(detail_id, projection)

            # Update camera metadata
            CameraTools.set_camera_metadata(detail_id)
            success_count += 1

        except (CameraError, DetailError) as e:
            failed.append((detail_id, str(e)))

    # Print summary
    print(f"Applied {projection} to {success_count} detail(s)")
    if failed:
        print(f"Failed: {len(failed)} detail(s)")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino plugin entry point."""
    set_detail_view()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    set_detail_view()

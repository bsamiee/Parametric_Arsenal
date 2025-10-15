"""
Title         : QuickDetail.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/QuickDetail.py

Description
----------------------------------------------------------------------------
Creates a new Detail View by drawing a rectangle on the Layout.
"""

from __future__ import annotations

import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.constants import Constants, Strings
from libs.detail_tools import DetailTools


@rhino_command(requires_layout=True, undo_description="Quick Detail Creation")
def quick_detail() -> None:
    """
    Creates a new Detail View by drawing a rectangle on the Layout.

    Raises:
        UserCancelledError: If user cancels rectangle selection.
        DetailError: If detail creation fails.
    """
    # Ensure target layer exists
    DetailTools.ensure_layer_exists(Constants.DETAIL_LAYER, Constants.TARGET_LAYER_COLOR)

    # Get active page view
    pageview = sc.doc.Views.ActiveView

    # Get rectangle from user (raises UserCancelledError if cancelled)
    pt1, pt2 = DetailTools.get_detail_rectangle()

    # Create new detail view (raises DetailError if fails)
    new_detail = DetailTools.create_detail(pageview, pt1, pt2)

    # Move new detail to target layer
    DetailTools.move_detail_to_layer(new_detail.Id, Constants.DETAIL_LAYER)

    # Correct any existing details on wrong layers
    moved_existing = DetailTools.correct_existing_details(pageview, Constants.DETAIL_LAYER)

    # Print success messages
    print(f"\n{Strings.INFO_CREATED_NEW_DETAIL} '{Constants.DETAIL_LAYER}'")
    if moved_existing > 1:
        print(Strings.INFO_CORRECTED_EXISTING_DETAILS.format(moved_existing - 1, Constants.DETAIL_LAYER))
    print("---------------------------------------------")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino plugin entry point."""
    quick_detail()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    quick_detail()

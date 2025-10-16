"""
Title         : DetailCaption.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/DetailCaption.py

Description
----------------------------------------------------------------------------
Creates or updates numbered detail captions with title and scale text positioned below Detail Views.
Automatically detects existing captions and updates them instead of creating duplicates.
"""

from __future__ import annotations

import rhinoscriptsyntax as rs
from libs.command_framework import rhino_command
from libs.common_utils import require_user_selection, require_user_string, validate_detail_object
from libs.constants import Constants, Strings
from libs.detail_tools import DetailTools


@rhino_command(requires_layout=True, undo_description="Detail Caption")
def detail_caption() -> None:
    """
    Create or update detail caption with title and scale.

    Automatically detects if a caption already exists for the selected detail view
    and updates it instead of creating a duplicate. If no caption exists, creates
    a new numbered caption.

    Raises:
        UserCancelledError: If user cancels selection or input.
        DetailError: If selected object is not a valid detail view.
        ValidationError: If caption text creation or update fails.
    """
    # Get detail selection (preselect or prompt)
    selected = rs.SelectedObjects() or []
    detail_id = selected[0] if selected else require_user_selection(Strings.PROMPT_SELECT_DETAIL, rs.filter.detail)

    # Validate detail object
    validate_detail_object(detail_id)

    # Ensure caption layer exists
    DetailTools.ensure_layer_exists(Constants.CAPTION_LAYER)

    # Search for existing caption linked to this detail
    existing_caption = DetailTools.find_existing_caption(detail_id)

    if existing_caption:
        # Update existing caption
        current_title = DetailTools.get_caption_title(existing_caption)
        title_text = require_user_string(Strings.PROMPT_ENTER_TITLE, current_title, "Detail Caption")
        scale_text = DetailTools.get_detail_scale(detail_id)

        DetailTools.update_caption_text(existing_caption, title_text, scale_text)

        caption_number = rs.TextObjectText(existing_caption[0])
        print(f"Caption {caption_number} updated for detail view")
    else:
        # Create new caption
        caption_number = DetailTools.count_existing_captions() + 1
        title_text = require_user_string(Strings.PROMPT_ENTER_TITLE, "DETAIL", "Detail Caption")
        scale_text = DetailTools.get_detail_scale(detail_id)

        DetailTools.create_caption(detail_id, caption_number, title_text, scale_text)

        print(f"Caption {caption_number} created")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    detail_caption()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    detail_caption()

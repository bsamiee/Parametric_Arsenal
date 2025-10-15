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

from __future__ import annotations

from libs.command_framework import rhino_command
from libs.common_utils import require_user_choice, validate_environment_units
from libs.constants import Constants, Strings
from libs.detail_tools import DetailTools
from libs.exceptions import ValidationError


# --- Main Function --------------------------------------------------------
@rhino_command(requires_layout=True, undo_description="Edit Page Scale")
def edit_page_scale() -> None:
    """
    Edits the page scale metadata for the active Layout.

    Raises:
        EnvironmentError: If model units are not supported.
        ValidationError: If no scales are available for current units.
        UserCancelledError: If user cancels scale selection.
    """
    # Validate environment units
    validate_environment_units(Constants.SUPPORTED_UNIT_SYSTEMS)

    # Get available scales for current units
    scale_dict = DetailTools.get_available_scales()
    if not scale_dict:
        raise ValidationError("Could not determine available scales for the current model units.")

    # Determine the correct ordered list for the ListBox
    if scale_dict == Constants.ARCHITECTURAL_SCALES_IMPERIAL:
        keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
    elif scale_dict == Constants.ARCHITECTURAL_SCALES_METRIC:
        keys = sorted(scale_dict.keys())
    else:
        # Fallback if the dictionary is somehow unexpected
        keys = sorted(scale_dict.keys())

    if not keys:
        raise ValidationError("No scale keys found to display.")

    # Present the correctly ordered list
    choice = require_user_choice(
        keys,
        Strings.PROMPT_EDIT_PAGE_SCALE,
        Strings.PROMPT_SET_PAGE_SCALE,
    )

    # Set the chosen scale metadata
    DetailTools.set_page_scale_metadata(choice)

    print(f"New Page Scale set to: {choice}")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    edit_page_scale()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    edit_page_scale()

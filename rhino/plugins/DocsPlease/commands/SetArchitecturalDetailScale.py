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

from __future__ import annotations

import sys

import scriptcontext as sc
import System
from libs.command_framework import rhino_command
from libs.common_utils import require_user_choice, validate_environment_units
from libs.constants import Constants, Strings
from libs.detail_tools import DetailTools
from libs.exceptions import ScaleError, ValidationError


def set_details_to_scale(detail_ids: list[str], page_length: float, model_length: float, scale_label: str) -> list[str]:
    """Sets scale and label for multiple details.

    Returns:
        List of log messages for each detail scaled.
    """
    log: list[str] = []
    for did in detail_ids:
        DetailTools.set_detail_scale(did, page_length, model_length, scale_label)
        log.append(Strings.MSG_DETAIL_SET_TO_SCALE.format(str(did), scale_label))
    return log


def batch_set_details_to_scale(page_length: float, model_length: float, scale_label: str) -> list[str]:
    """Sets scale and label for all details on the active page.

    Returns:
        List of log messages for each detail scaled.

    Raises:
        ValidationError: If no details found on page.
    """
    log: list[str] = []
    pageview = sc.doc.Views.ActiveView
    details = pageview.GetDetailViews()

    if not details:
        raise ValidationError(Strings.MSG_NO_DETAILS_FOUND)

    for detail in details:
        DetailTools.set_detail_scale(detail.Id, page_length, model_length, scale_label)
        log.append(Strings.MSG_DETAIL_SET_TO_SCALE.format(str(detail.Id), scale_label))

    return log


@rhino_command(requires_layout=True, undo_description="Architectural Detail Scaling")
def set_architectural_detail_scale() -> None:
    """Sets architectural scales on Detail Views with options for individual or batch operations.

    Raises:
        UserCancelledError: If user cancels any selection or input
        EnvironmentError: If unit system is not supported
        ValidationError: If no details found or invalid scale
        ScaleError: If scale operations fail
    """
    # Validate environment
    validate_environment_units(Constants.SUPPORTED_UNIT_SYSTEMS)

    scale_dict = DetailTools.get_available_scales()
    page_scale = DetailTools.get_page_scale_metadata()

    # If no stored page-scale metadata, prompt and store it
    if not page_scale:
        if scale_dict == Constants.ARCHITECTURAL_SCALES_IMPERIAL:
            keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
        elif scale_dict == Constants.ARCHITECTURAL_SCALES_METRIC:
            keys = sorted(scale_dict.keys())
        else:
            keys = sorted(scale_dict.keys()) if scale_dict else []

        if not keys:
            raise ScaleError("No scales available to set page scale")

        choice = require_user_choice(keys, Strings.PROMPT_SET_PAGE_SCALE, Strings.PROMPT_ARCHITECTURAL_DETAIL_SCALES)
        DetailTools.set_page_scale_metadata(choice)
        page_scale = choice

    # Choose operation
    operation = require_user_choice(
        Strings.OPTIONS_ARCHITECTURAL_OPERATIONS,
        Strings.PROMPT_CHOOSE_OPERATION,
        Strings.PROMPT_ARCHITECTURAL_DETAIL_SCALES,
    )

    confirmation_log = []

    if operation == Strings.OP_SET_TO_CUSTOM_SCALE:
        ids = DetailTools.get_detail_objects(preselect_allowed=True)
        p_len, m_len, selected_label = DetailTools.select_scale(
            scale_dict, mode="Architectural", title=Strings.PROMPT_SET_CUSTOM_SCALE
        )
        confirmation_log = set_details_to_scale(ids, p_len, m_len, selected_label)

    elif operation == Strings.OP_SET_TO_PAGE_SCALE:
        ids = DetailTools.get_detail_objects(preselect_allowed=True)
        key = page_scale.strip()
        m_len_opt = scale_dict.get(key)
        if m_len_opt is None:
            raise ValidationError(Strings.MSG_PAGE_SCALE_NOT_RECOGNIZED)
        m_len = m_len_opt
        confirmation_log = set_details_to_scale(ids, 1.0, m_len, key)

    elif operation == Strings.OP_BATCH_CUSTOM_SCALE:
        p_len, m_len, selected_label = DetailTools.select_scale(
            scale_dict,
            mode="Architectural",
            title=Strings.PROMPT_SET_CUSTOM_SCALE_FOR_ALL,
        )
        confirmation_log = batch_set_details_to_scale(p_len, m_len, selected_label)

    elif operation == Strings.OP_BATCH_PAGE_SCALE:
        key = page_scale.strip()
        m_len_opt = scale_dict.get(key)
        if m_len_opt is None:
            raise ValidationError(Strings.MSG_PAGE_SCALE_NOT_RECOGNIZED)
        m_len = m_len_opt
        confirmation_log = batch_set_details_to_scale(1.0, m_len, key)

    # Highlight and report
    if confirmation_log:
        # Extract GUIDs from log messages for highlighting
        modified_ids = []
        for line in confirmation_log:
            try:
                guid_str = line.split("'")[1]
                if len(guid_str) == 36 and guid_str.count("-") == 4:
                    if "System" in sys.modules:
                        modified_ids.append(System.Guid(guid_str))
            except (IndexError, ValueError, TypeError):
                print(f"Warning: Could not parse GUID from log line: {line}")

        if modified_ids:
            DetailTools.highlight_details(modified_ids)

        print("\n----- Detail Scale Changes -----")
        for line in confirmation_log:
            print(line)
        print("---------------------------------")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    set_architectural_detail_scale()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    set_architectural_detail_scale()

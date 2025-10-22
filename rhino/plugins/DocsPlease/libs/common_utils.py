"""
Title         : common_utils.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/common_utils.py

Description
----------------------------------------------------------------------------
Shared utility functions and validation logic
"""

from __future__ import annotations

import re

import rhinoscriptsyntax as rs
import scriptcontext as sc

import Rhino

from .constants import Strings
from .exceptions import DetailError, EnvironmentError, UserCancelledError  # noqa: A004


# --- Common Utilities -----------------------------------------------------
class CommonUtils:
    """Shared utility functions for Rhino plugin operations."""

    @staticmethod
    def alert_user(message: str) -> None:
        """Print an informational message to the command line.

        Args:
            message: The message to display.
        """
        print(f"[INFO] {message}")

    @staticmethod
    def is_layout_view_active() -> bool:
        """Check if the active view is a Layout (Page) View.

        Returns:
            True if active view is a layout view, False otherwise.
        """
        view = sc.doc.Views.ActiveView
        return isinstance(view, Rhino.Display.RhinoPageView)

    @staticmethod
    def get_model_unit_system() -> Rhino.UnitSystem:
        """Return the model's unit system (e.g., Inches, Meters).

        Returns:
            The current model unit system.
        """
        return sc.doc.ModelUnitSystem


# --- Validation Helpers ---------------------------------------------------
SHEET_NUM_PATTERN = re.compile(r"^\d+\.\d+$")  # e.g. "1.2", "101.03"


def validate_sheet_number(number: str) -> bool:
    """Validate if number matches sheet number pattern '#.#'.

    Args:
        number: The sheet number string to validate.

    Returns:
        True if number matches pattern (e.g., "1.2", "101.03"), False otherwise.
    """
    return bool(SHEET_NUM_PATTERN.match(str(number)))


# --- Validation Functions with Exceptions --------------------------------
def validate_detail_object(detail_id: object) -> Rhino.DocObjects.DetailViewObject:
    """Validate and return detail object or raise DetailError.

    Args:
        detail_id: Object ID to validate.

    Returns:
        The validated detail view object.

    Raises:
        DetailError: If object is not a valid detail view.
    """

    rh_obj = rs.coercerhinoobject(detail_id)
    if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
        raise DetailError(Strings.MSG_INVALID_DETAIL_SELECTED)
    return rh_obj


def require_user_selection(prompt: str, filter_type: int) -> object:
    """Get user selection or raise UserCancelledError.

    Args:
        prompt: User prompt message.
        filter_type: Rhino object filter type.

    Returns:
        Selected object ID.

    Raises:
        UserCancelledError: If user cancels or makes no selection.
    """

    selection = rs.GetObject(prompt, filter_type)
    if not selection:
        raise UserCancelledError("No selection made")
    return selection


def require_user_point(prompt: str) -> object:
    """Get user point selection or raise UserCancelledError.

    Args:
        prompt: User prompt message.

    Returns:
        Selected point.

    Raises:
        UserCancelledError: If user cancels point selection.
    """

    point = rs.GetPoint(prompt)
    if not point:
        raise UserCancelledError("Point selection cancelled")
    return point


def require_user_string(prompt: str, default: str = "", title: str = "Input", allow_empty: bool = False) -> str:
    """Get user string input or raise UserCancelledError.

    Args:
        prompt: User prompt message.
        default: Default value.
        title: Dialog title.
        allow_empty: Whether to allow empty strings.

    Returns:
        User input string.

    Raises:
        UserCancelledError: If user cancels or provides invalid input.
    """

    result = rs.StringBox(prompt, default, title)
    if result is None:
        raise UserCancelledError("String input cancelled")

    result = result.strip()
    if not allow_empty and not result:
        raise UserCancelledError("Empty input not allowed")

    return result


def require_user_choice(options: list[str], prompt: str, title: str = "Select") -> str:
    """Get user choice from list or raise UserCancelledError.

    Args:
        options: List of options to choose from.
        prompt: User prompt message.
        title: Dialog title.

    Returns:
        Selected option.

    Raises:
        UserCancelledError: If user cancels selection.
    """

    choice = rs.ListBox(options, prompt, title)
    if not choice:
        raise UserCancelledError("Selection cancelled")
    return choice


def validate_environment_units(supported_units: list[Rhino.UnitSystem]) -> None:
    """Validate that current model units are supported.

    Args:
        supported_units: List of supported unit systems.

    Raises:
        EnvironmentError: If current units are not supported.
    """

    current_units = CommonUtils.get_model_unit_system()
    if current_units not in supported_units:
        raise EnvironmentError(Strings.MSG_UNSUPPORTED_UNIT_SYSTEM)

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


# --- Common Utilities -----------------------------------------------------
class CommonUtils:
    @staticmethod
    def alert_user(message: str) -> None:
        """Prints an informational message to the command line."""
        print(f"[INFO] {message}")

    @staticmethod
    def is_layout_view_active() -> bool:
        """Checks if the active view is a Layout (Page) View."""
        view = sc.doc.Views.ActiveView
        return isinstance(view, Rhino.Display.RhinoPageView)

    @staticmethod
    def get_model_unit_system() -> Rhino.UnitSystem:
        """Returns the model's unit system (e.g., Inches, Meters)."""
        return sc.doc.ModelUnitSystem

    @staticmethod
    def set_detail_scale_label(detail_id: object, label: str) -> bool:
        """Sets the user string 'detail_scale' on a Detail View."""
        rh_obj = rs.coercerhinoobject(detail_id)
        if rh_obj and isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            rh_obj.Attributes.SetUserString("detail_scale", label)
            sc.doc.Objects.ModifyAttributes(rh_obj.Id, rh_obj.Attributes, True)
            return True
        return False

    @staticmethod
    def prompt_string(default_value: str = "", title: str = "Input", header: str | None = None) -> str | None:
        hdr = header or "Enter value:"
        return rs.StringBox(hdr, default_value, title)

    @staticmethod
    def prompt_list(
        options: list[str],
        title: str = "Select",
        header: str | None = None,
        default: str | None = None,
    ) -> str | None:
        hdr = header or "Select option:"
        return rs.ListBox(options, default, title, hdr)

    @staticmethod
    def dynamic_title(label: str, current: str | None = None) -> str:
        return f"{label}{f'  (Current: {current})' if current else ''}"


# --- Validation Helpers ---------------------------------------------------
SHEET_NUM_PATTERN = re.compile(r"^\d+\.\d+$")  # e.g. "1.2", "101.03"


def validate_sheet_number(number: str) -> bool:
    """Return True if *number* matches '#.#' pattern."""
    return bool(SHEET_NUM_PATTERN.match(str(number)))

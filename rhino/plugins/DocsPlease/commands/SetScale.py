"""
Title         : SetScale.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/SetScale.py

Description
----------------------------------------------------------------------------
Unified scale command for setting detail view scales. Handles all scale types
(Architectural, Engineering, Metric) and operations (selected details or all
details on page). Integrates with project-level document set configuration.
"""

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.common_utils import CommonUtils, require_user_choice, validate_environment_units
from libs.constants import Constants, Metadata
from libs.detail_tools import DetailTools
from libs.exceptions import ValidationError
from libs.project_config_tools import ProjectConfigTools

import Rhino


# --- Main Scale Command ---------------------------------------------------
@rhino_command(requires_layout=True, undo_description="Set Detail Scale")
def set_scale() -> None:  # noqa: PLR0912, PLR0915
    """Unified scale command for detail views with smart selection handling.

    Supports pre-selection of details to skip operation prompt. Handles all scale types
    (Architectural, Engineering, Metric) and integrates with project-level document set
    configuration.

    Raises:
        UserCancelledError: If user cancels selection or scale choice.
        ValidationError: If no details found on layout.
        EnvironmentError: If unit system is not supported.
    """
    # Validate units
    validate_environment_units(Constants.SUPPORTED_UNIT_SYSTEMS)
    units = CommonUtils.get_model_unit_system()

    # Check for pre-selected details
    preselected = rs.SelectedObjects(include_lights=False, include_grips=False) or []
    preselected_details = [obj_id for obj_id in preselected if rs.IsDetail(obj_id)]

    # Determine scale types based on model units
    if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
        scale_types = ["Architectural", "Engineering"]
    else:
        scale_types = ["Metric"]

    # Select scale type
    scale_type = require_user_choice(scale_types, "Select scale type", "Set Scale")

    # Get scale dictionary and mode based on scale type
    if scale_type == "Architectural":
        if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
            scale_dict = Constants.ARCHITECTURAL_SCALES_IMPERIAL
        else:
            scale_dict = Constants.ARCHITECTURAL_SCALES_METRIC
        mode = "Architectural"
    elif scale_type == "Engineering":
        scale_dict = Constants.ENGINEERING_SCALES_IMPERIAL
        mode = "Engineering"
    else:  # Metric
        scale_dict = Constants.ARCHITECTURAL_SCALES_METRIC
        mode = "Architectural"

    # Select scale
    p_len, m_len, label = DetailTools.select_scale(scale_dict, mode, "Select scale")

    # Smart operation selection based on pre-selection
    if preselected_details:
        # Use pre-selected details, skip operation prompt
        ids = preselected_details
        for did in ids:
            DetailTools.set_detail_scale(did, p_len, m_len, label)
        print(f"Applied scale {label} to {len(ids)} detail(s)")
    else:
        # No pre-selection, show operation prompt
        operation = require_user_choice(
            ["Set Selected Details", "Set All Details on Page"], "Select operation", "Set Scale"
        )

        # Apply scale based on operation
        if operation == "Set Selected Details":
            ids = DetailTools.get_detail_objects(preselect_allowed=True)
            for did in ids:
                DetailTools.set_detail_scale(did, p_len, m_len, label)
            print(f"Applied scale {label} to {len(ids)} detail(s)")
        else:  # Set All Details on Page
            pageview = sc.doc.Views.ActiveView
            details = pageview.GetDetailViews()
            if not details:
                raise ValidationError("No details found on this layout")
            for detail in details:
                DetailTools.set_detail_scale(detail.Id, p_len, m_len, label)
            print(f"Applied scale {label} to all {len(details)} detail(s) on page")

    # Check if scale differs from document set default and update SCALE_INHERITED flag
    vp = sc.doc.Views.ActiveView.ActiveViewport
    sheet_indicator = vp.GetUserString(Metadata.SHEET_INDICATOR)

    if sheet_indicator:
        doc_set = ProjectConfigTools.get_document_set(sheet_indicator)
        if doc_set:
            default_scale = doc_set.get("default_scale")
            if default_scale and label != default_scale:
                vp.SetUserString(Metadata.SCALE_INHERITED, "false")
                print(f"[INFO] Scale overridden from document set default ({default_scale})")
            elif default_scale and label == default_scale:
                vp.SetUserString(Metadata.SCALE_INHERITED, "true")
                print("[INFO] Scale matches document set default")


# --- Command Entry Point --------------------------------------------------
def SetScale() -> int:
    """Rhino command entry point for SetScale.

    Returns:
        0 for success, 1 for error.
    """
    return set_scale()


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    SetScale()

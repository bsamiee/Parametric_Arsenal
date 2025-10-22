"""
Title         : ProjectManager.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/ProjectManager.py

Description
------------------------------------------------------------------------------
Unified project management command for configuring project settings and
applying them to layout pages.
"""

from __future__ import annotations

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.common_utils import (
    CommonUtils,
    require_user_choice,
    require_user_string,
    validate_sheet_number,
)
from libs.constants import DISCIPLINE_CHOICES, L2_CHOICES_BY_MASTER, Constants, Metadata
from libs.exceptions import LayoutError, ProjectConfigError, ValidationError
from libs.layout_tools import LayoutTools
from libs.project_config_tools import ProjectConfigTools

import Rhino


# --- Template Management ----------------------------------------------------
def apply_template(template_name: str) -> None:
    """Apply a project template by enabling document sets with default scales.

    Args:
        template_name: Name of template from Constants.PROJECT_TEMPLATES.

    Raises:
        ProjectConfigError: If template not found or application fails.
    """
    template = ProjectConfigTools.get_template_config(template_name)
    if not template:
        raise ProjectConfigError(f"Template '{template_name}' not found")

    # Determine default scale based on model units
    units = CommonUtils.get_model_unit_system()
    if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
        default_scale = template.get("default_scale_imperial", 'SCALE: 1/4" = 1\'-0"')
    else:
        default_scale = template.get("default_scale_metric", "SCALE: 1:50")

    # Enable document sets from template
    document_sets = template.get("document_sets", [])
    designation_level = template.get("designation_level", "L2")

    for disc_code in document_sets:
        config = {"designation_level": designation_level, "default_scale": default_scale, "enabled": True}
        ProjectConfigTools.set_document_set(disc_code, config)

    # Store template name
    rs.SetDocumentUserText("project_config_template", template_name)

    print(f"[INFO] Applied template '{template_name}' with {len(document_sets)} document sets")


# --- First-Time Setup -------------------------------------------------------
def first_time_setup() -> None:
    """Handle first-time project setup workflow.

    Prompts user for template selection, applies template, and sets project name.

    Raises:
        UserCancelledError: If user cancels any step.
        ProjectConfigError: If setup fails.
    """
    print("\n=== First-Time Project Setup ===")
    print("Welcome! Let's configure your project.\n")

    # Prompt for template selection
    template_options = list(Constants.PROJECT_TEMPLATES.keys())
    template = require_user_choice(template_options, "Select a project template to get started", "Project Setup")

    # Apply template
    apply_template(template)

    # Prompt for project name
    project_name = require_user_string("Enter project name", "", "Project Name")

    # Store project name
    ProjectConfigTools.set_project_name(project_name)

    print(f"\n[SUCCESS] Project '{project_name}' configured successfully!")
    print("Next steps:")
    print("  1. Create a layout page in Rhino (standard Rhino button)")
    print("  2. Run ProjectManager again and select 'Apply to Layout'")
    print("  3. Configure your sheet details\n")


# --- Project Configuration Menu ---------------------------------------------
def configure_project_menu() -> None:
    """Display and handle the Configure Project submenu.

    Allows editing project settings and document sets.
    """
    while True:
        options = ["Edit Project Name", "Manage Document Sets", "View Current Configuration", "Back to Main Menu"]

        choice = require_user_choice(options, "Configure Project", "Project Configuration")

        if choice == "Back to Main Menu":
            break
        if choice == "Edit Project Name":
            edit_project_name()
        elif choice == "Manage Document Sets":
            manage_document_sets()
        elif choice == "View Current Configuration":
            view_current_configuration()


# --- Project Settings Management --------------------------------------------
def edit_project_name() -> None:
    """Edit the project name."""
    current_name = ProjectConfigTools.get_project_name() or ""

    new_name = require_user_string("Enter new project name", current_name, "Edit Project Name")

    ProjectConfigTools.set_project_name(new_name)
    print(f"[INFO] Project name updated to: {new_name}")


# --- Document Set Management ------------------------------------------------
def manage_document_sets() -> None:
    """Manage document sets (enable/disable, configure scales)."""
    while True:
        options = ["Enable Document Sets", "Configure Document Set", "Disable Document Set", "Back"]

        choice = require_user_choice(options, "Manage Document Sets", "Document Set Management")

        if choice == "Back":
            break
        if choice == "Enable Document Sets":
            enable_document_sets()
        elif choice == "Configure Document Set":
            configure_document_set()
        elif choice == "Disable Document Set":
            disable_document_set()


def enable_document_sets() -> None:
    """Enable one or more document sets."""
    # Get all discipline options
    disc_options = [f"{code} - {name}" for code, name in DISCIPLINE_CHOICES]

    # Get currently enabled sets
    enabled = {code for code, _ in ProjectConfigTools.get_enabled_document_sets()}

    # Filter out already enabled
    available_options = [opt for opt in disc_options if opt.split(" - ")[0] not in enabled]

    if not available_options:
        print("[INFO] All document sets are already enabled")
        return

    # Select discipline to enable
    choice = require_user_choice(available_options, "Select document set to enable", "Enable Document Set")

    disc_code = choice.split(" - ")[0]

    # Prompt for designation level
    level_choice = require_user_choice(["L1", "L2"], "Select designation level", "Designation Level")

    # Determine default scale based on units
    units = CommonUtils.get_model_unit_system()
    if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
        default_scale = 'SCALE: 1/4" = 1\'-0"'
    else:
        default_scale = "SCALE: 1:50"

    # Create config
    config = {"designation_level": level_choice, "default_scale": default_scale, "enabled": True}

    ProjectConfigTools.set_document_set(disc_code, config)
    print(f"[INFO] Enabled document set: {disc_code} ({level_choice})")


def configure_document_set() -> None:
    """Configure an existing document set's settings."""
    enabled_sets = ProjectConfigTools.get_enabled_document_sets()

    if not enabled_sets:
        print("[INFO] No document sets enabled. Enable a document set first.")
        return

    # Select document set to configure
    options = [f"{code} - {get_discipline_name(code)}" for code, _ in enabled_sets]
    choice = require_user_choice(options, "Select document set to configure", "Configure Document Set")

    disc_code = choice.split(" - ")[0]
    current_config = ProjectConfigTools.get_document_set(disc_code)

    if not current_config:
        print(f"[ERROR] Document set '{disc_code}' not found")
        return

    # Configure options
    config_options = ["Change Designation Level", "Change Default Scale", "Back"]

    config_choice = require_user_choice(config_options, f"Configure {disc_code}", "Document Set Configuration")

    if config_choice == "Back":
        return
    if config_choice == "Change Designation Level":
        new_level = require_user_choice(["L1", "L2"], "Select new designation level", "Designation Level")
        current_config["designation_level"] = new_level
        ProjectConfigTools.set_document_set(disc_code, current_config)
        print(f"[INFO] Updated {disc_code} designation level to {new_level}")

    elif config_choice == "Change Default Scale":
        # Get available scales based on units
        units = CommonUtils.get_model_unit_system()
        if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
            scale_dict = Constants.ARCHITECTURAL_SCALES_IMPERIAL
            scale_options = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
        else:
            scale_dict = Constants.ARCHITECTURAL_SCALES_METRIC
            scale_options = list(scale_dict.keys())

        new_scale = require_user_choice(scale_options, "Select new default scale", "Default Scale")
        current_config["default_scale"] = new_scale
        ProjectConfigTools.set_document_set(disc_code, current_config)
        print(f"[INFO] Updated {disc_code} default scale to {new_scale}")


def disable_document_set() -> None:
    """Disable a document set."""
    enabled_sets = ProjectConfigTools.get_enabled_document_sets()

    if not enabled_sets:
        print("[INFO] No document sets enabled")
        return

    # Select document set to disable
    options = [f"{code} - {get_discipline_name(code)}" for code, _ in enabled_sets]
    choice = require_user_choice(options, "Select document set to disable", "Disable Document Set")

    disc_code = choice.split(" - ")[0]
    config = ProjectConfigTools.get_document_set(disc_code)

    if not config:
        print(f"[ERROR] Document set '{disc_code}' not found")
        return

    config["enabled"] = False

    ProjectConfigTools.set_document_set(disc_code, config)
    print(f"[INFO] Disabled document set: {disc_code}")


def view_current_configuration() -> None:
    """Display current project configuration."""
    print("\n=== Current Project Configuration ===")
    print(f"Project Name: {ProjectConfigTools.get_project_name()}")
    print(f"Template: {rs.GetDocumentUserText('project_config_template') or 'Custom'}")

    enabled_sets = ProjectConfigTools.get_enabled_document_sets()
    print(f"\nEnabled Document Sets: {len(enabled_sets)}")

    for disc_code, config in enabled_sets:
        disc_name = get_discipline_name(disc_code)
        level = config.get("designation_level", "N/A")
        scale = config.get("default_scale", "N/A")
        print(f"  [{disc_code}] {disc_name}")
        print(f"      Level: {level}, Default Scale: {scale}")

    print("=====================================\n")


# --- Helper Functions -------------------------------------------------------
def get_discipline_name(disc_code: str) -> str:
    """Get discipline name from code.

    Args:
        disc_code: Discipline code (e.g., "A", "E", "S").

    Returns:
        Discipline name or "Unknown" if not found.
    """
    for code, name in DISCIPLINE_CHOICES:
        if code == disc_code:
            return name
    return "Unknown"


# --- Layout Application -----------------------------------------------------
def apply_to_layout() -> None:
    """Apply project settings to active layout page.

    Validates user is in layout view, prompts for document set selection,
    shows appropriate discipline picker, and applies all metadata to viewport.

    Raises:
        LayoutError: If not in layout view.
        ValidationError: If no document sets enabled or invalid input.
    """
    # Validate in layout view
    if not CommonUtils.is_layout_view_active():
        raise LayoutError("Must be in a layout view to apply settings")

    # Get enabled document sets
    doc_sets = ProjectConfigTools.get_enabled_document_sets()
    if not doc_sets:
        raise ValidationError("No document sets enabled. Configure project first.")

    # Select document set
    options = [f"{code} - {get_discipline_name(code)}" for code, _ in doc_sets]
    choice = require_user_choice(options, "Select document set for this layout", "Apply to Layout")
    disc_code = choice.split(" - ")[0]
    doc_set = ProjectConfigTools.get_document_set(disc_code)

    if not doc_set:
        raise ValidationError(f"Document set '{disc_code}' not found")

    # Get designation level and show appropriate picker
    level = doc_set["designation_level"]
    final_code = disc_code

    if level == "L2" and disc_code in L2_CHOICES_BY_MASTER:
        # Show L2 sub-discipline picker
        sub_options = [f"{c2} - {short}" for c2, short, _ in L2_CHOICES_BY_MASTER[disc_code]]
        sub_choice = require_user_choice(
            sub_options, f"Select {get_discipline_name(disc_code)} sub-discipline", "Apply to Layout"
        )
        final_code = sub_choice.split(" - ")[0]

    # Get sheet info
    sheet_name = require_user_string("Enter sheet name (e.g., Floor Plan)", "", "Sheet Name")

    sheet_number = require_user_string("Enter sheet number (e.g., 1.2)", "", "Sheet Number")

    # Validate sheet number format
    if not validate_sheet_number(sheet_number):
        raise ValidationError(
            f"Invalid sheet number format: '{sheet_number}'. Expected format: #.# (e.g., 1.2, 101.03)"
        )

    # Build sheet ID and check uniqueness
    sheet_id = final_code + sheet_number
    existing_ids = LayoutTools.existing_sheet_ids()

    if sheet_id in existing_ids:
        raise ValidationError(
            f"Sheet ID '{sheet_id}' already exists. Please use a different sheet number."
        )

    # Apply to viewport
    vp = sc.doc.Views.ActiveView.ActiveViewport
    project_name = ProjectConfigTools.get_project_name()

    vp.SetUserString(Metadata.PROJECT_NAME, project_name)
    vp.SetUserString(Metadata.PAGE_SCALE, doc_set["default_scale"])
    vp.SetUserString(Metadata.DESIGNATION_LEVEL, level)
    vp.SetUserString(Metadata.SHEET_INDICATOR, final_code)
    vp.SetUserString(Metadata.SHEET_NAME, sheet_name)
    vp.SetUserString(Metadata.SHEET_NUMBER, sheet_number)
    vp.SetUserString(Metadata.SHEET_ID_FULL, sheet_id)
    vp.SetUserString(Metadata.SCALE_INHERITED, "true")
    vp.SetUserString(Metadata.COMPLETION_FLAG, "true")

    print("\n[SUCCESS] Layout configured successfully!")
    print(f"  Sheet ID: {sheet_id}")
    print(f"  Sheet Name: {sheet_name}")
    print(f"  Default Scale: {doc_set['default_scale']}")
    print(f"  Designation Level: {level}\n")


# --- Project Overview -------------------------------------------------------
def view_overview() -> None:
    """Display project overview organized by document sets.

    Shows project name, template, and all enabled document sets with their
    layouts. Highlights document sets with no layouts.
    """
    print("\n" + "=" * 60)
    print("PROJECT OVERVIEW")
    print("=" * 60)

    # Project info
    project_name = ProjectConfigTools.get_project_name()
    template = rs.GetDocumentUserText("project_config_template") or "Custom"

    print(f"\nProject: {project_name}")
    print(f"Template: {template}")

    # Group layouts by discipline
    page_views = sc.doc.Views.GetPageViews()
    layouts_by_disc: dict[str, list[dict[str, str | bool]]] = {}

    for view in page_views:
        vp = view.ActiveViewport
        disc = vp.GetUserString(Metadata.SHEET_INDICATOR)

        if disc:
            if disc not in layouts_by_disc:
                layouts_by_disc[disc] = []

            layouts_by_disc[disc].append({
                "id": vp.GetUserString(Metadata.SHEET_ID_FULL),
                "name": vp.GetUserString(Metadata.SHEET_NAME),
                "scale": vp.GetUserString(Metadata.PAGE_SCALE),
                "inherited": vp.GetUserString(Metadata.SCALE_INHERITED) == "true",
            })

    # Display document sets
    print("\n" + "-" * 60)
    print("DOCUMENT SETS")
    print("-" * 60)

    enabled_sets = ProjectConfigTools.get_enabled_document_sets()

    if not enabled_sets:
        print("\n[WARNING] No document sets enabled")
    else:
        for disc_code, config in enabled_sets:
            disc_name = get_discipline_name(disc_code)
            level = config.get("designation_level", "N/A")
            default_scale = config.get("default_scale", "N/A")

            print(f"\n[{disc_code}] {disc_name} ({level})")
            print(f"    Default Scale: {default_scale}")

            layouts = layouts_by_disc.get(disc_code, [])
            print(f"    Layouts: {len(layouts)}")

            if layouts:
                for layout in layouts:
                    status = "inherited" if layout["inherited"] else f"overridden: {layout['scale']}"
                    print(f"      • {layout['id']}: {layout['name']} ({status})")
            else:
                print("      [WARNING] No layouts for this document set")

    print("\n" + "=" * 60 + "\n")


# --- Main Command Entry Point -----------------------------------------------
@rhino_command(requires_layout=False, undo_description="Project Management")
def project_manager() -> None:
    """Main entry point for ProjectManager command.

    Handles both first-time setup and main menu workflows based on
    whether project configuration exists.
    """
    # Check if project config exists
    if not ProjectConfigTools.validate_project_config():
        # First-time setup
        first_time_setup()
    else:
        # Main menu
        main_menu()


# --- Main Menu --------------------------------------------------------------
def main_menu() -> None:
    """Display and handle main project management menu.

    Shows options for configuring project, applying to layout,
    viewing overview, or exiting.
    """
    while True:
        options = ["Configure Project", "Apply to Layout", "View Overview", "Exit"]

        choice = require_user_choice(options, "Project Manager - Select an option", "Project Manager")

        if choice == "Exit":
            print("[INFO] Exiting Project Manager")
            break
        if choice == "Configure Project":
            configure_project_menu()
        elif choice == "Apply to Layout":
            apply_to_layout()
        elif choice == "View Overview":
            view_overview()


# --- Script Entry Point -----------------------------------------------------
if __name__ == "__main__":
    project_manager()

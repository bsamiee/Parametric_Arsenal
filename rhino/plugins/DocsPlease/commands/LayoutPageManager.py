"""
Title         : LayoutPageManager.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/LayoutPageManager.py

Description
----------------------------------------------------------------------------
Comprehensive metadata editor and layout setup tool for managing Layout pages.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs.command_framework import rhino_command
from libs.common_utils import CommonUtils, require_user_choice, require_user_string, validate_sheet_number
from libs.constants import (
    DESIGNATION_LEVEL_CHOICES,
    DISCIPLINE_CHOICES,
    L2_CHOICES_BY_MASTER,
    Metadata,
    Strings,
)
from libs.detail_tools import DetailTools
from libs.exceptions import UserCancelledError, ValidationError
from libs.layout_tools import LayoutTools


# --- Constants Section ----------------------------------------------------
DOC_PROJECT_NAME_KEY = "doc_project_name"  # stored on the document
META_COMPLETION_FLAG = Metadata.COMPLETION_FLAG  # "meta_page_setup_complete"
ALL_KEYS = Metadata.all()  # always up to date

EDITOR_MENU_OPTIONS = [
    "Edit Project Name",
    "Edit Page Scale",
    "Edit Sheet Discipline",  # now includes L1/L2 step
    "Edit Sheet Name",
    "Edit Sheet Number",
    "Edit Revision Number",
    "Clear All Metadata",
    "Exit Editor",
]


# --- Wrappers -------------------------------------------------------------
def print_metadata_snapshot(vp: Any) -> None:
    """Prints current metadata snapshot for the viewport."""
    print("\n--- Metadata Snapshot --------------------------------")
    for k in ALL_KEYS:
        v = vp.GetUserString(k)
        if v:
            print(f"{k}: {v}")
    print("-------------------------------------------------------\n")


def sync_project_name_to_all_layouts() -> int:
    """Syncs project name from document to all layouts."""
    proj = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY)
    return LayoutTools.sync_project_name_to_all_layouts(proj) if proj else 0


# --- Editor Mode ----------------------------------------------------------
class MetadataEditor:
    """Interactive metadata editor for layout pages with existing metadata."""

    def __init__(self, page_view: Any) -> None:
        self.view = page_view
        self.vp = page_view.ActiveViewport

    # Menu Dispatcher
    def launch(self) -> None:
        """Launches the metadata editor menu loop.

        Raises:
            UserCancelledError: If user exits the editor.
        """
        actions = {
            "Edit Project Name": self.project_name,
            "Edit Page Scale": self.page_scale,
            "Edit Sheet Discipline": self.sheet_discipline,
            "Edit Sheet Name": self.sheet_name,
            "Edit Sheet Number": self.sheet_number,
            "Edit Revision Number": self.revision_number,
            "Clear All Metadata": self.clear_all,
        }

        while True:
            choice = rs.ListBox(
                EDITOR_MENU_OPTIONS,
                "Select a metadata field to edit",
                "Layout Metadata Editor",
            )
            if not choice or choice == "Exit Editor":
                raise UserCancelledError("Editor exited")

            undo = sc.doc.BeginUndoRecord(f"Metadata Edit - {choice}")
            try:
                actions[choice]()
            finally:
                sc.doc.EndUndoRecord(undo)
                sc.doc.Views.Redraw()
                print_metadata_snapshot(self.vp)

    # Individual Editors
    def project_name(self) -> None:
        """Edits project name metadata.

        Raises:
            UserCancelledError: If user cancels input.
            ValidationError: If project name is empty.
        """
        current = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY) or ""
        new = require_user_string(Strings.PROMPT_PROJECT_NAME, current, "Project Name")

        rs.SetDocumentUserText(DOC_PROJECT_NAME_KEY, new)
        self.vp.SetUserString(Metadata.PROJECT_NAME, new)
        n = sync_project_name_to_all_layouts()
        CommonUtils.alert_user(f"Project name synced to {n} layout(s).")

    def page_scale(self) -> None:
        """Edits page scale metadata.

        Raises:
            UserCancelledError: If user cancels scale selection.
            ValidationError: If no scales available.
        """
        scales = DetailTools.get_available_scales()
        if not scales:
            raise ValidationError("No available scales detected")

        current = self.vp.GetUserString(Metadata.PAGE_SCALE) or "N/A"
        picked = DetailTools.select_scale(
            scales,
            mode="Architectural",
            title=f"Select Page Scale (current: {current})",
        )
        self.vp.SetUserString(Metadata.PAGE_SCALE, picked[2])

    # Combined Designation-Level + Discipline Picker
    def sheet_discipline(self) -> None:
        """Edits sheet discipline metadata.

        Raises:
            UserCancelledError: If user cancels any selection.
        """
        # --- 1. Pick Designation Level ------------------------------------
        lvl_opts = [f"{c} - {n}" for c, n in DESIGNATION_LEVEL_CHOICES]
        lvl_sel = require_user_choice(lvl_opts, Strings.PROMPT_DESIGNATION_LEVEL, "Designation Level")
        level_code = lvl_sel.split(" - ")[0]
        self.vp.SetUserString(Metadata.DESIGNATION_LEVEL, level_code)

        # --- 2. Pick Master Discipline ------------------------------------
        mast_opts = [f"{c} - {n}" for c, n in DISCIPLINE_CHOICES]
        mast_sel = require_user_choice(
            mast_opts,
            Strings.PROMPT_SHEET_DISCIPLINE,
            f"Sheet Discipline (L{level_code[-1]})",
        )
        master_code, master_full = mast_sel.split(" - ", 1)

        # --- 3. L2 → Pick Sub-discipline ----------------------------------
        if level_code == "L2":
            entries = L2_CHOICES_BY_MASTER.get(master_code, [])
            if entries:
                sub_opts = [f"{c2} - {short}" for c2, short, _ in entries]
                sub_sel = require_user_choice(sub_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sub-discipline")
                disc_code = sub_sel.split(" - ")[0]
                disc_full = next(f for c2, _, f in entries if c2 == disc_code)
            else:  # master has no children - fall back to master itself
                disc_code, disc_full = master_code, master_full
        else:  # L1
            disc_code, disc_full = master_code, master_full

        # --- 4. Write Metadata & Update Full ID ---------------------------
        self.vp.SetUserString(Metadata.SHEET_INDICATOR, disc_code)
        self.vp.SetUserString(Metadata.SUBDISCIPLINE_CODE, disc_full)
        self._update_full_id()

    def sheet_name(self) -> None:
        """Edits sheet name metadata.

        Raises:
            UserCancelledError: If user cancels input.
        """
        cur = self.vp.GetUserString(Metadata.SHEET_NAME) or ""
        new = require_user_string(Strings.PROMPT_SHEET_NAME, cur, "Sheet Name")
        self.vp.SetUserString(Metadata.SHEET_NAME, new)

    def sheet_number(self) -> None:
        """Edits sheet number metadata.

        Raises:
            UserCancelledError: If user cancels input.
            ValidationError: If sheet number format is invalid or ID already exists.
        """
        while True:
            cur = self.vp.GetUserString(Metadata.SHEET_NUMBER) or ""
            new = require_user_string(Strings.PROMPT_SHEET_NUMBER, cur, "Sheet Number")

            if not validate_sheet_number(new):
                raise ValidationError("Format must be digits[.digits] (e.g. 1 or 1.2)")

            full_id = (self.vp.GetUserString(Metadata.SHEET_INDICATOR) or "") + new
            if full_id in LayoutTools.existing_sheet_ids():
                raise ValidationError(f"Sheet ID '{full_id}' already exists")

            self.vp.SetUserString(Metadata.SHEET_NUMBER, new)
            self._update_full_id()
            break

    def revision_number(self) -> None:
        """Edits revision number metadata.

        Raises:
            UserCancelledError: If user cancels input.
        """
        cur = self.vp.GetUserString(Metadata.REVISION_NUMBER) or "0"
        new = require_user_string("Enter Revision Number / Letter", cur, "Revision", allow_empty=True)
        self.vp.SetUserString(Metadata.REVISION_NUMBER, new or "0")

    def clear_all(self) -> None:
        """Clears all metadata for the layout."""
        if rs.MessageBox("Clear ALL metadata for this layout?", 4 | 32, "Confirm") == 6:
            for k in ALL_KEYS:
                self.vp.SetUserString(k, None)
            CommonUtils.alert_user("All metadata cleared.")

    # --- Helper -----------------------------------------------------------
    def _update_full_id(self) -> None:
        ind = self.vp.GetUserString(Metadata.SHEET_INDICATOR) or ""
        num = self.vp.GetUserString(Metadata.SHEET_NUMBER) or ""
        if ind and num and validate_sheet_number(num):
            self.vp.SetUserString(Metadata.SHEET_ID_FULL, ind + num)


# --- SETUP MODE -----------------------------------------------------------
class MetadataSetup:
    """Runs when the viewport has never been initialised."""

    def __init__(self, page_view: Any) -> None:
        self.view = page_view
        self.vp = page_view.ActiveViewport
        self.meta: dict[str, str] = {}
        self.existing_ids = LayoutTools.existing_sheet_ids()

    # Metadata
    def run(self) -> dict[str, str]:
        """Runs the setup wizard.

        Returns:
            Dictionary of metadata key-value pairs.

        Raises:
            UserCancelledError: If user cancels any step.
            ValidationError: If validation fails.
        """
        self._project()
        self._scale()
        self._discipline()

        self.meta.update({
            Metadata.REVISION_NUMBER: "0",
            Metadata.DRAWN_BY: "",
            Metadata.CHECKED_BY: "",
            Metadata.DATE_ISSUED: "",
            META_COMPLETION_FLAG: "true",
        })
        return self.meta

    # Step-by-Step Prompts
    def _project(self) -> None:
        """Prompts for project name.

        Raises:
            UserCancelledError: If user cancels input.
        """
        name = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY)
        if not name:
            name = require_user_string(Strings.PROMPT_PROJECT_NAME, "", "Project Name")
            rs.SetDocumentUserText(DOC_PROJECT_NAME_KEY, name)
        self.meta[Metadata.PROJECT_NAME] = name

    def _scale(self) -> None:
        """Prompts for page scale.

        Raises:
            UserCancelledError: If user cancels selection.
            ValidationError: If no scales available.
        """
        scales = DetailTools.get_available_scales()
        if not scales:
            raise ValidationError("No available scales; aborting")

        picked = DetailTools.select_scale(scales, "Architectural", Strings.PROMPT_SET_PAGE_SCALE)
        self.meta[Metadata.PAGE_SCALE] = picked[2]

    def _discipline(self) -> None:
        """Prompts for discipline, sheet name, and sheet number.

        Raises:
            UserCancelledError: If user cancels any input.
            ValidationError: If sheet number format invalid or ID already exists.
        """
        # Designation Level
        lvl_opts = [f"{c} - {n}" for c, n in DESIGNATION_LEVEL_CHOICES]
        lvl_sel = require_user_choice(lvl_opts, Strings.PROMPT_DESIGNATION_LEVEL, "Designation Level")
        level_code = lvl_sel.split(" - ")[0]
        self.meta[Metadata.DESIGNATION_LEVEL] = level_code

        # Master Discipline
        mast_opts = [f"{c} - {n}" for c, n in DISCIPLINE_CHOICES]
        mast_sel = require_user_choice(mast_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sheet Discipline")
        master_code, master_full = mast_sel.split(" - ", 1)

        # L2 Sub-discipline (if applicable)
        if level_code == "L2":
            entries = L2_CHOICES_BY_MASTER.get(master_code, [])
            if entries:
                sub_opts = [f"{c2} - {short}" for c2, short, _ in entries]
                sub_sel = require_user_choice(sub_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sub-discipline")
                disc_code = sub_sel.split(" - ")[0]
                disc_full = next(f for c2, _, f in entries if c2 == disc_code)
            else:
                disc_code, disc_full = master_code, master_full
        else:
            disc_code, disc_full = master_code, master_full

        self.meta[Metadata.SHEET_INDICATOR] = disc_code
        self.meta[Metadata.SUBDISCIPLINE_CODE] = disc_full

        # Sheet Name
        name = require_user_string(Strings.PROMPT_SHEET_NAME, "", "Sheet Name")
        self.meta[Metadata.SHEET_NAME] = name

        # Sheet Number
        while True:
            num = require_user_string(Strings.PROMPT_SHEET_NUMBER, "", "Sheet Number")

            if not validate_sheet_number(num):
                raise ValidationError("Format must be digits[.digits] (e.g. 1 or 1.2)")

            if (disc_code + num) in self.existing_ids:
                raise ValidationError("Sheet ID already exists")

            break

        self.meta[Metadata.SHEET_NUMBER] = num
        self.meta[Metadata.SHEET_ID_FULL] = disc_code + num


# --- Helpers --------------------------------------------------------------
def apply_metadata(view: Any, meta: dict[str, str]) -> None:
    """Applies metadata dictionary to the viewport."""
    vp = view.ActiveViewport
    for k in ALL_KEYS:
        vp.SetUserString(k, meta.get(k))


@rhino_command(
    requires_layout=True,
    undo_description="Layout Page Management",
    auto_redraw=True,
    print_start=False,
    print_end=False,
)
def layout_page_manager() -> None:
    """Comprehensive metadata editor and layout setup tool for managing Layout pages.

    Raises:
        UserCancelledError: If user cancels any operation
        ValidationError: If validation fails
    """
    view = sc.doc.Views.ActiveView
    vp = view.ActiveViewport

    if vp.GetUserString(META_COMPLETION_FLAG) == "true":
        # Editor mode
        MetadataEditor(view).launch()
        return

    # Setup mode
    meta = MetadataSetup(view).run()
    apply_metadata(view, meta)
    CommonUtils.alert_user("Layout metadata configured.")

    n = sync_project_name_to_all_layouts()
    if n:
        CommonUtils.alert_user(f"Project name synced to {n} other layout(s).")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    """Rhino command entry point."""
    layout_page_manager()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    layout_page_manager()

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

import traceback
from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc

import Rhino


try:
    from libs import (  # data tables; helpers / utilities
        DESIGNATION_LEVEL_CHOICES,
        DISCIPLINE_CHOICES,
        L2_CHOICES_BY_MASTER,
        Common_Utils,
        Detail_Tools,
        Layout_Tools,
        Metadata,
        Strings,
        validate_sheet_number,
    )
except ImportError:
    rs.MessageBox(
        "[ERROR] Failed to import Layout_Toolkit_Library.\n"
        "Fix the library path / flush-left the designation dictionaries and retry.",
        0,
        "Fatal Error",
    )
    raise

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
    print("\n--- Metadata Snapshot --------------------------------")
    for k in ALL_KEYS:
        v = vp.GetUserString(k)
        if v:
            print(f"{k}: {v}")
    print("-------------------------------------------------------\n")


def sync_project_name_to_all_layouts() -> int:
    proj = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY)
    return Layout_Tools.sync_project_name_to_all_layouts(proj) if proj else 0


# --- Editor Mode ----------------------------------------------------------
class MetadataEditor:
    def __init__(self, page_view: Any) -> None:
        self.view = page_view
        self.vp = page_view.ActiveViewport

    # Menu Dispatcher
    def launch(self) -> None:
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
                break

            undo = sc.doc.BeginUndoRecord(f"Metadata Edit - {choice}")
            try:
                actions[choice]()
            except (ValueError, RuntimeError, AttributeError) as exc:
                Common_Utils.alert_user(f"Error: {exc}")
                traceback.print_exc()
            finally:
                sc.doc.EndUndoRecord(undo)
                sc.doc.Views.Redraw()
                print_metadata_snapshot(self.vp)

    # Individual Editors
    def project_name(self) -> None:
        current = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY) or ""
        new = rs.StringBox(Strings.PROMPT_PROJECT_NAME, current, "Project Name")
        if new is None:
            return
        new = new.strip()
        if not new:
            Common_Utils.alert_user("Project name cannot be empty.")
            return
        rs.SetDocumentUserText(DOC_PROJECT_NAME_KEY, new)
        self.vp.SetUserString(Metadata.PROJECT_NAME, new)
        n = sync_project_name_to_all_layouts()
        Common_Utils.alert_user(f"Project name synced to {n} layout(s).")

    def page_scale(self) -> None:
        scales = Detail_Tools.get_available_scales()
        if not scales:
            Common_Utils.alert_user("No available scales detected.")
            return

        current = self.vp.GetUserString(Metadata.PAGE_SCALE) or "N/A"
        picked = Detail_Tools.select_scale(
            scales,
            mode="Architectural",
            title=f"Select Page Scale (current: {current})",
        )
        if picked:
            self.vp.SetUserString(Metadata.PAGE_SCALE, picked[2])

    # Combined Designation-Level + Discipline Picker
    def sheet_discipline(self) -> None:
        # --- 1. Pick Designation Level ------------------------------------
        lvl_opts = [f"{c} - {n}" for c, n in DESIGNATION_LEVEL_CHOICES]
        lvl_sel = rs.ListBox(lvl_opts, Strings.PROMPT_DESIGNATION_LEVEL, "Designation Level")
        if not lvl_sel:
            return
        level_code = lvl_sel.split(" - ")[0]
        self.vp.SetUserString(Metadata.DESIGNATION_LEVEL, level_code)

        # --- 2. Pick Master Discipline ------------------------------------
        mast_opts = [f"{c} - {n}" for c, n in DISCIPLINE_CHOICES]
        mast_sel = rs.ListBox(
            mast_opts,
            Strings.PROMPT_SHEET_DISCIPLINE,
            f"Sheet Discipline (L{level_code[-1]})",
        )
        if not mast_sel:
            return
        master_code, master_full = mast_sel.split(" - ", 1)

        # --- 3. L2 → Pick Sub-discipline ----------------------------------
        if level_code == "L2":
            entries = L2_CHOICES_BY_MASTER.get(master_code, [])
            if entries:
                sub_opts = [f"{c2} - {short}" for c2, short, _ in entries]
                sub_sel = rs.ListBox(sub_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sub-discipline")
                if not sub_sel:
                    return
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
        cur = self.vp.GetUserString(Metadata.SHEET_NAME) or ""
        new = rs.StringBox(Strings.PROMPT_SHEET_NAME, cur, "Sheet Name")
        if new is not None and new.strip():
            self.vp.SetUserString(Metadata.SHEET_NAME, new.strip())

    def sheet_number(self) -> None:
        while True:
            cur = self.vp.GetUserString(Metadata.SHEET_NUMBER) or ""
            new = rs.StringBox(Strings.PROMPT_SHEET_NUMBER, cur, "Sheet Number")
            if new is None:
                return
            new = new.strip()
            if not validate_sheet_number(new):
                Common_Utils.alert_user("Format must be digits[.digits] (e.g. 1 or 1.2).")
                continue
            full_id = (self.vp.GetUserString(Metadata.SHEET_INDICATOR) or "") + new
            if full_id in Layout_Tools.existing_sheet_ids():
                Common_Utils.alert_user(f"Sheet ID '{full_id}' already exists.")
                continue
            self.vp.SetUserString(Metadata.SHEET_NUMBER, new)
            self._update_full_id()
            break

    def revision_number(self) -> None:
        cur = self.vp.GetUserString(Metadata.REVISION_NUMBER) or "0"
        new = rs.StringBox("Enter Revision Number / Letter", cur, "Revision")
        if new is not None:
            self.vp.SetUserString(Metadata.REVISION_NUMBER, new.strip() or "0")

    def clear_all(self) -> None:
        if rs.MessageBox("Clear ALL metadata for this layout?", 4 | 32, "Confirm") == 6:
            for k in ALL_KEYS:
                self.vp.SetUserString(k, None)
            Common_Utils.alert_user("All metadata cleared.")

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
        self.existing_ids = Layout_Tools.existing_sheet_ids()

    # Metadata
    def run(self) -> dict[str, str] | None:
        if not (self._project() and self._scale() and self._discipline()):
            return None
        self.meta.update({
            Metadata.REVISION_NUMBER: "0",
            Metadata.DRAWN_BY: "",
            Metadata.CHECKED_BY: "",
            Metadata.DATE_ISSUED: "",
            META_COMPLETION_FLAG: "true",
        })
        return self.meta

    # Step-by-Step Prompts
    def _project(self) -> bool:
        name = rs.GetDocumentUserText(DOC_PROJECT_NAME_KEY)
        if not name:
            name = rs.StringBox(Strings.PROMPT_PROJECT_NAME, "", "Project Name")
            if name is None or not name.strip():
                return False
            rs.SetDocumentUserText(DOC_PROJECT_NAME_KEY, name.strip())
        self.meta[Metadata.PROJECT_NAME] = name.strip()
        return True

    def _scale(self) -> bool:
        scales = Detail_Tools.get_available_scales()
        if not scales:
            Common_Utils.alert_user("No available scales; aborting.")
            return False
        picked = Detail_Tools.select_scale(scales, "Architectural", Strings.PROMPT_SET_PAGE_SCALE)
        if not picked:
            return False
        self.meta[Metadata.PAGE_SCALE] = picked[2]
        return True

    def _discipline(self) -> bool:  # noqa: PLR0912
        # Designation Level
        lvl_opts = [f"{c} - {n}" for c, n in DESIGNATION_LEVEL_CHOICES]
        lvl_sel = rs.ListBox(lvl_opts, Strings.PROMPT_DESIGNATION_LEVEL, "Designation Level")
        if not lvl_sel:
            return False
        level_code = lvl_sel.split(" - ")[0]
        self.meta[Metadata.DESIGNATION_LEVEL] = level_code

        # Master Discipline
        mast_opts = [f"{c} - {n}" for c, n in DISCIPLINE_CHOICES]
        mast_sel = rs.ListBox(mast_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sheet Discipline")
        if not mast_sel:
            return False
        master_code, master_full = mast_sel.split(" - ", 1)

        # L2 Sub-discipline (if applicable)
        if level_code == "L2":
            entries = L2_CHOICES_BY_MASTER.get(master_code, [])
            if entries:
                sub_opts = [f"{c2} - {short}" for c2, short, _ in entries]
                sub_sel = rs.ListBox(sub_opts, Strings.PROMPT_SHEET_DISCIPLINE, "Sub-discipline")
                if not sub_sel:
                    return False
                disc_code = sub_sel.split(" - ")[0]
                disc_full = next(f for c2, _, f in entries if c2 == disc_code)
            else:
                disc_code, disc_full = master_code, master_full
        else:
            disc_code, disc_full = master_code, master_full

        self.meta[Metadata.SHEET_INDICATOR] = disc_code
        self.meta[Metadata.SUBDISCIPLINE_CODE] = disc_full

        # Sheet Name
        while True:
            name = rs.StringBox(Strings.PROMPT_SHEET_NAME, "", "Sheet Name")
            if name is None:
                return False
            name = name.strip()
            if name:
                break
            Common_Utils.alert_user("Sheet name cannot be empty.")
        self.meta[Metadata.SHEET_NAME] = name

        # Sheet Number
        while True:
            num = rs.StringBox(Strings.PROMPT_SHEET_NUMBER, "", "Sheet Number")
            if num is None:
                return False
            num = num.strip()
            if not validate_sheet_number(num):
                Common_Utils.alert_user("Format must be digits[.digits] (e.g. 1 or 1.2).")
                continue
            if (disc_code + num) in self.existing_ids:
                Common_Utils.alert_user("Sheet ID already exists.")
                continue
            break
        self.meta[Metadata.SHEET_NUMBER] = num
        self.meta[Metadata.SHEET_ID_FULL] = disc_code + num
        return True


# --- Helpers --------------------------------------------------------------
def apply_metadata(view: Any, meta: dict[str, str] | None) -> bool:
    vp = view.ActiveViewport
    if meta:
        for k in ALL_KEYS:
            vp.SetUserString(k, meta.get(k))
    return True


# --- Main Function --------------------------------------------------------
def main() -> None:
    view = sc.doc.Views.ActiveView
    if not isinstance(view, Rhino.Display.RhinoPageView):
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    vp = view.ActiveViewport
    if vp.GetUserString(META_COMPLETION_FLAG) == "true":
        MetadataEditor(view).launch()
        return

    # --- Setup Mode ---
    undo = sc.doc.BeginUndoRecord("Layout Page Setup")
    try:
        meta = MetadataSetup(view).run()
        if not meta:
            Common_Utils.alert_user("Setup cancelled.")
            return
        apply_metadata(view, meta)
        Common_Utils.alert_user("Layout metadata configured.")
    finally:
        sc.doc.EndUndoRecord(undo)
        sc.doc.Views.Redraw()

    n = sync_project_name_to_all_layouts()
    if n:
        Common_Utils.alert_user(f"Project name synced to {n} other layout(s).")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

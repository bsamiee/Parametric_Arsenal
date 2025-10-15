"""
Title         : layout_tools.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/layout_tools.py

Description
----------------------------------------------------------------------------
Layout page management and sheet ID operations
"""

from __future__ import annotations

from typing import Any

import scriptcontext as sc

from .common_utils import validate_sheet_number
from .constants import Metadata


# --- Layout Tools ---------------------------------------------------------
class LayoutTools:
    @staticmethod
    def sync_project_name_to_all_layouts(project_name: str, doc: Any | None = None) -> int:
        """Propagate project name to every PageView; returns count."""
        doc = doc or sc.doc
        count = 0
        if doc and doc.Views:
            page_views = doc.Views.GetPageViews()
            for view in page_views or []:
                vp = view.ActiveViewport
                if vp and vp.GetUserString(Metadata.PROJECT_NAME) != project_name:
                    vp.SetUserString(Metadata.PROJECT_NAME, project_name)
                    count += 1
        return count

    @staticmethod
    def build_sheet_id(indicator: str, number: str) -> str:
        if not validate_sheet_number(number):
            msg = f"Sheet number must look like '1.2' - got '{number}'"
            raise ValueError(msg)
        return indicator + number

    @staticmethod
    def existing_sheet_ids(doc: Any | None = None) -> set[Any]:
        doc = doc or sc.doc
        if doc and doc.Views:
            page_views = doc.Views.GetPageViews()
            return {
                v.ActiveViewport.GetUserString(Metadata.SHEET_ID_FULL)
                for v in page_views or []
                if v and v.ActiveViewport and v.ActiveViewport.GetUserString(Metadata.SHEET_ID_FULL)
            }
        return set()

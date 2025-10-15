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
from .exceptions import ValidationError


# --- Layout Tools ---------------------------------------------------------
class LayoutTools:
    """Tools for layout page management and sheet ID operations."""

    @staticmethod
    def sync_project_name_to_all_layouts(project_name: str, doc: Any | None = None) -> int:
        """Propagate project name to every PageView.

        Args:
            project_name: The project name to sync to all layouts.
            doc: Optional document reference (defaults to sc.doc).

        Returns:
            Number of layouts updated.

        Raises:
            ValidationError: If document or views are not accessible.
        """
        doc = doc or sc.doc
        if not doc:
            raise ValidationError("No document available", context={"project_name": project_name})
        if not doc.Views:
            raise ValidationError("Document views not accessible", context={"project_name": project_name})

        count = 0
        page_views = doc.Views.GetPageViews()
        for view in page_views or []:
            vp = view.ActiveViewport
            if vp and vp.GetUserString(Metadata.PROJECT_NAME) != project_name:
                vp.SetUserString(Metadata.PROJECT_NAME, project_name)
                count += 1
        return count

    @staticmethod
    def existing_sheet_ids(doc: Any | None = None) -> set[Any]:
        """Get set of existing sheet IDs from all layout pages.

        Args:
            doc: Optional document reference (defaults to sc.doc).

        Returns:
            Set of existing sheet ID strings.
        """
        doc = doc or sc.doc
        if doc and doc.Views:
            page_views = doc.Views.GetPageViews()
            return {
                v.ActiveViewport.GetUserString(Metadata.SHEET_ID_FULL)
                for v in page_views or []
                if v and v.ActiveViewport and v.ActiveViewport.GetUserString(Metadata.SHEET_ID_FULL)
            }
        return set()

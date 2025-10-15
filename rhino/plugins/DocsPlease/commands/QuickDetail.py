"""
Title         : QuickDetail.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/QuickDetail.py

Description
----------------------------------------------------------------------------
Creates a new Detail View by drawing a rectangle on the Layout.
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Common_Utils, Constants, Strings

import Rhino


# --- Helper Class ---------------------------------------------------------
class QuickDetailHelper:
    @staticmethod
    def ensure_layer_exists(layer_name: str, color: Any) -> None:
        """
        Ensures the specified layer exists with the given color.

        Args:
        ----
            layer_name (str): Name of the layer to check or create.
            color (System.Drawing.Color): Color to assign if layer is created.

        Returns:
        -------
            None
        """
        if not rs.IsLayer(layer_name):
            rs.AddLayer(layer_name, color)

    @staticmethod
    def get_detail_rectangle() -> tuple[Any | None, Any | None]:
        """
        Prompts user to draw a rectangle and returns its two opposite points.

        Returns
        -------
            tuple: (pt1, pt2) as Point2d objects, or (None, None) on cancel.
        """
        rect = rs.GetRectangle()
        if not rect:
            return None, None
        pt1 = Rhino.Geometry.Point2d(rect[0].X, rect[0].Y)
        pt2 = Rhino.Geometry.Point2d(rect[2].X, rect[2].Y)
        return pt1, pt2

    @staticmethod
    def create_detail(pageview: Any, pt1: Any, pt2: Any) -> Any | None:
        """
        Creates a new Detail View given two corner points.

        Args:
        ----
            pageview (Rhino.Display.RhinoPageView): The target layout page.
            pt1 (Point2d): First corner of the rectangle.
            pt2 (Point2d): Opposite corner of the rectangle.

        Returns:
        -------
            DetailView or None: The new detail view object, if created.
        """
        detail = pageview.AddDetailView("Detail", pt1, pt2, Rhino.Display.DefinedViewportProjection.Top)
        if detail:
            detail.IsActive = False
            detail.CommitChanges()
            return detail
        return None

    @staticmethod
    def move_detail_to_layer(detail_id: object, target_layer: str) -> None:
        """
        Moves a detail view to the specified layer.

        Args:
        ----
            detail_id (guid): The ID of the detail view.
            target_layer (str): The name of the destination layer.

        Returns:
        -------
            None
        """
        rs.ObjectLayer(detail_id, target_layer)

    @staticmethod
    def correct_existing_details(pageview: Any, target_layer: str) -> int:
        """
        Moves all existing Detail Views on the layout to the correct target layer.

        Args:
        ----
            pageview (Rhino.Display.RhinoPageView): The active layout page.
            target_layer (str): Layer to assign to all detail views.

        Returns:
        -------
            int: Number of detail views moved.
        """
        moved_count = 0
        details = pageview.GetDetailViews()
        for detail in details:
            current_layer = rs.ObjectLayer(detail.Id)
            if current_layer != target_layer:
                QuickDetailHelper.move_detail_to_layer(detail.Id, target_layer)
                moved_count += 1
        return moved_count


# --- Main Function --------------------------------------------------------
def quick_detail() -> None:
    """
    Main entry point for quickly creating a new Detail View.

    Returns
    -------
        None
    """
    print("\n=== Quick Detail Creation Script Started ===")

    if not Common_Utils.is_layout_view_active():
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    QuickDetailHelper.ensure_layer_exists(Constants.DETAIL_LAYER, Constants.TARGET_LAYER_COLOR)

    pageview = sc.doc.Views.ActiveView

    pt1, pt2 = QuickDetailHelper.get_detail_rectangle()
    if not pt1 or not pt2:
        print(Strings.MSG_DETAIL_CREATION_CANCELLED)
        return

    undo_record = sc.doc.BeginUndoRecord("Quick Detail Creation")
    try:
        new_detail = QuickDetailHelper.create_detail(pageview, pt1, pt2)
        if not new_detail:
            Common_Utils.alert_user(Strings.MSG_FAILED_CREATE_DETAIL)
            return

        QuickDetailHelper.move_detail_to_layer(new_detail.Id, Constants.DETAIL_LAYER)
        moved_existing = QuickDetailHelper.correct_existing_details(pageview, Constants.DETAIL_LAYER)

    finally:
        sc.doc.EndUndoRecord(undo_record)

    sc.doc.Views.Redraw()

    print(f"\n{Strings.INFO_CREATED_NEW_DETAIL} '{Constants.DETAIL_LAYER}'")
    if moved_existing > 1:
        print(Strings.INFO_CORRECTED_EXISTING_DETAILS.format(moved_existing - 1, Constants.DETAIL_LAYER))
    print("---------------------------------------------")

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    quick_detail()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    quick_detail()

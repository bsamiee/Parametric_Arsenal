"""
Title         : AlignDetailViews.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/commands/AlignDetailViews.py

Description
----------------------------------------------------------------------------
Aligns two Detail Views horizontally or vertically based on user-picked reference points.
"""

import rhinoscriptsyntax as rs
import scriptcontext as sc
from libs import Alignment_Tools, Common_Utils, Strings

import Rhino


# --- Custom Exceptions ---------------------------------------------------
class DetailTransformError(RuntimeError):
    """Raised when detail transformation fails."""


# --- Main Function --------------------------------------------------------
def main() -> None:  # noqa: PLR0911, PLR0912, PLR0915
    """Aligns two Detail Views horizontally or vertically based on user-picked points."""
    print("\n=== Align Detail Views Script Started ===")

    active_view = sc.doc.Views.ActiveView
    if not isinstance(active_view, Rhino.Display.RhinoPageView):
        Common_Utils.alert_user(Strings.MSG_LAYOUT_VIEW_REQUIRED)
        return

    active_view.SetPageAsActive()
    rs.UnselectAllObjects()

    # Select Parent Detail
    parent_detail_id = Alignment_Tools.get_detail_object_id(Strings.PROMPT_SELECT_PARENT)
    if not parent_detail_id:
        print("Selection cancelled by user.")
        return

    parent_name = Alignment_Tools.get_object_name(parent_detail_id)
    rs.SelectObject(parent_detail_id)
    rs.UnselectAllObjects()
    sc.doc.Views.Redraw()

    # Select Child Detail
    child_detail_id = Alignment_Tools.get_detail_object_id(Strings.PROMPT_SELECT_CHILD)
    if not child_detail_id:
        print("Selection cancelled by user.")
        return

    child_name = Alignment_Tools.get_object_name(child_detail_id)
    rs.SelectObject(child_detail_id)

    if parent_detail_id == child_detail_id:
        Common_Utils.alert_user(Strings.MSG_PARENT_CHILD_SAME)
        return

    rs.UnselectAllObjects()

    # Pick Points
    pt_parent = Alignment_Tools.get_point_on_page(Strings.PROMPT_PICK_PARENT_POINT)
    if not pt_parent:
        print("Point picking cancelled by user.")
        return

    pt_child = Alignment_Tools.get_point_on_page(Strings.PROMPT_PICK_CHILD_POINT)
    if not pt_child:
        print("Point picking cancelled by user.")
        return

    # Choose Alignment Type
    align_choice = rs.GetString(Strings.PROMPT_DIRECTION, Strings.DEFAULT_DIRECTION, Strings.DIRECTION_OPTIONS)
    if not align_choice:
        print("Alignment type selection cancelled by user.")
        return

    align_choice = align_choice.capitalize()
    if align_choice not in Strings.DIRECTION_OPTIONS:
        Common_Utils.alert_user(Strings.MSG_INVALID_ALIGNMENT)
        return

    # Begin Undo Record
    undo_record = sc.doc.BeginUndoRecord(f"Align Detail Views: {parent_name} -> {child_name}")
    content_aligned = False

    try:
        translation_vector = Alignment_Tools.calculate_translation_vector(pt_parent, pt_child, align_choice)

        if translation_vector:
            transform = Rhino.Geometry.Transform.Translation(translation_vector)

            if not rs.TransformObject(child_detail_id, transform):
                raise DetailTransformError(Strings.MSG_FAILED_TRANSFORM)  # noqa: TRY301
            content_aligned = True
            sc.doc.Views.Redraw()
        else:
            print("\nPicked points already aligned. No movement necessary.")

    except DetailTransformError as e:
        print(f"\nOperation failed: {e}")
        sc.doc.EndUndoRecord(undo_record)
        active_view.SetPageAsActive()
        sc.doc.Views.Redraw()
        return

    finally:
        if undo_record:
            sc.doc.EndUndoRecord(undo_record)

        active_view.SetPageAsActive()
        sc.doc.Views.Redraw()

        if content_aligned:
            rs.SelectObject(child_detail_id)
            print("\nAlignment complete. Child Detail is selected for further adjustment.")
        else:
            print("\nNo alignment necessary.")

    print("\n=== Script Completed ===\n")


# --- Rhino Plugin Entry Point ---------------------------------------------
def RunCommand(is_interactive: bool) -> int:
    main()
    return 0


# --- Script Entry Point ---------------------------------------------------
if __name__ == "__main__":
    main()

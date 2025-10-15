"""
Title         : detail_tools.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/detail_tools.py

Description
----------------------------------------------------------------------------
All detail view manipulation, scaling, and geometry operations
"""

from __future__ import annotations

from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc

import Rhino

from .common_utils import CommonUtils
from .constants import Constants


# --- Detail Tools ---------------------------------------------------------
class DetailTools:
    @staticmethod
    def get_available_scales() -> dict[str, float]:
        """Returns appropriate scale dictionary based on model units."""
        units = CommonUtils.get_model_unit_system()
        if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
            return Constants.ARCHITECTURAL_SCALES_IMPERIAL
        if units in {Rhino.UnitSystem.Millimeters, Rhino.UnitSystem.Meters}:
            return Constants.ARCHITECTURAL_SCALES_METRIC
        return {}

    @staticmethod
    def select_scale(
        scale_dict: dict[str, float],
        mode: str = "Architectural",
        title: str = "Select a Scale",
    ) -> tuple[float, float, str] | None:
        """
        Presents a ListBox of available scales in correct order based on the mode provided.

        Returns (page_length, model_length, scale_label).
        """
        if mode == "Architectural":
            keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
        elif mode == "Engineering":
            keys = Constants.ENGINEERING_SCALES_IMPERIAL_ORDER
        else:
            keys = sorted(scale_dict.keys()) if scale_dict else []

        if not keys:
            print("Warning: No scale keys available for selection.")
            return None

        choice = rs.ListBox(keys, title, title)
        if not choice:
            return None

        model_length = scale_dict.get(choice)
        if model_length is None:
            print(f"Error: Scale value not found for choice '{choice}'")
            return None

        page_length = 1.0  # Always assume page length = 1.0
        return (page_length, model_length, choice)

    @staticmethod
    def set_detail_scale(detail_id: object, page_length: float, model_length: float, scale_label: str) -> bool:
        """Sets the scale of a detail view and stores the scale label as user metadata."""
        success = rs.DetailScale(detail_id, page_length=page_length, model_length=model_length)
        if success:
            rh_obj = rs.coercerhinoobject(detail_id)
            if rh_obj and isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
                try:
                    rh_obj.Attributes.SetUserString("detail_scale", scale_label)
                except (AttributeError, RuntimeError) as e:
                    print(f"Error setting user string for detail {detail_id}: {e}")
            else:
                print(f"Warning: Could not coerce detail {detail_id} to Rhino object after scaling.")
        else:
            print(f"Warning: rs.DetailScale failed for detail {detail_id}.")
        return success

    @staticmethod
    def format_architectural_scale(page_length: float, model_length: float) -> str:
        """
        Formats a scale ratio (page_length / model_length) into a standard architectural scale string (e.g., "SCALE:
        1/4" = 1'-0"") based on model units.

        Returns "SCALE: Custom" if it doesn't match a standard scale. Assumes page_length is 1.0.
        """
        if model_length <= 0.0:
            return Constants.SCALE_NA_LABEL  # Handle division by zero or invalid length

        ratio = model_length / page_length  # Calculate the model units per page unit

        units = CommonUtils.get_model_unit_system()
        scale_dict = {}

        if units in {Rhino.UnitSystem.Inches, Rhino.UnitSystem.Feet}:
            scale_dict = Constants.ARCHITECTURAL_SCALES_IMPERIAL
        elif units in {Rhino.UnitSystem.Millimeters, Rhino.UnitSystem.Meters}:
            scale_dict = Constants.ARCHITECTURAL_SCALES_METRIC
        else:
            return "SCALE: Unsupported Units"

        # Find the closest matching scale in the dictionary
        # We'll iterate through the dictionary values (model_length per page_length)
        # and find if the calculated ratio is close to one of the standard ratios.
        # Since the dict stores model_length for page_length=1, we compare our ratio
        # directly to the dict values.
        for label, standard_ratio in scale_dict.items():
            if abs(ratio - standard_ratio) < Constants.TOLERANCE:
                return label  # Found a match, return the standard label

        # If no standard scale matches closely, return "Custom"
        # We can try to represent imperial scales as fractions, but it gets complex.
        # Sticking to "Custom" is simpler and more reliable here.
        return "SCALE: Custom"

    # --- Metadata Storage -------------------------------------------------
    @staticmethod
    def set_page_scale_metadata(scale_text: str) -> None:
        """Sets 'page_scale' user string on the active Layout Page View."""
        pageview = sc.doc.Views.ActiveView
        if isinstance(pageview, Rhino.Display.RhinoPageView):
            vp = pageview.ActiveViewport
            if vp:
                vp.SetUserString("page_scale", scale_text)
            else:
                print("Warning: Could not access ActiveViewport for the PageView.")
        else:
            print("Warning: Not currently in a Layout (Page) View.")

    @staticmethod
    def get_page_scale_metadata() -> str | None:
        """Gets 'page_scale' user string from the active Layout Page View."""
        pageview = sc.doc.Views.ActiveView
        if isinstance(pageview, Rhino.Display.RhinoPageView):
            vp = pageview.ActiveViewport
            if vp:
                return vp.GetUserString("page_scale")
            print("Warning: Could not access ActiveViewport for the PageView.")
        return None

    # --- Detail Object Selection ------------------------------------------
    @staticmethod
    def get_detail_objects(preselect_allowed: bool = True) -> list[Any]:
        """
        Gets selected Detail View objects, prompting user if none are selected.

        Returns a list of GUIDs.
        """
        selected_ids = rs.SelectedObjects(include_lights=False, include_grips=False) if preselect_allowed else []
        detail_ids = []

        if selected_ids:
            detail_ids.extend(obj_id for obj_id in selected_ids if rs.IsDetail(obj_id))

        if not detail_ids:
            ids = rs.GetObjects("Select Detail Views", rs.filter.detail, preselect=False, select=True)
            return ids or []

        return detail_ids

    @staticmethod
    def highlight_details(ids: list[Any]) -> None:
        """Unselects all objects and selects the specified detail objects."""
        rs.UnselectAllObjects()
        if ids:
            try:
                rs.SelectObjects(ids)
            except (AttributeError, RuntimeError) as e:
                print(f"Error selecting objects: {e}")
                print(f"IDs provided: {ids}")
        sc.doc.Views.Redraw()

    # --- Layout Geometry and Detail Creation ------------------------------
    @staticmethod
    def get_detail_dimensions(detail_id: object) -> tuple[float | None, float | None]:
        """Returns (width, height) of a Detail View in page coordinates, accurately handling any rotation or offset."""
        dv = rs.coercerhinoobject(detail_id)
        if not dv or not isinstance(dv, Rhino.DocObjects.DetailViewObject):
            return None, None

        boundary = dv.Geometry
        if boundary is None:
            return None, None

        xform = dv.WorldToPageTransform
        boundary_copy = boundary.Duplicate()
        boundary_copy.Transform(xform)

        bbox = boundary_copy.GetBoundingBox(Rhino.Geometry.Plane.WorldXY)
        width = bbox.Max.X - bbox.Min.X
        height = bbox.Max.Y - bbox.Min.Y

        return width, height

    @staticmethod
    def create_detail_from_basepoint(base_pt: object, width: float, height: float, name: str) -> object | None:
        """
        Creates a new Detail View on the active Layout Page from a base point and dimensions.

        Returns the new Detail View object or None.
        """
        pageview = sc.doc.Views.ActiveView
        if not isinstance(pageview, Rhino.Display.RhinoPageView):
            return None

        pt1 = Rhino.Geometry.Point2d(base_pt.X, base_pt.Y)
        pt2 = Rhino.Geometry.Point2d(base_pt.X + width, base_pt.Y + height)

        detail = pageview.AddDetailView(name, pt1, pt2, Rhino.Display.DefinedViewportProjection.Top)

        if detail:
            detail.IsActive = False
            detail.CommitChanges()
            sc.doc.Views.Redraw()
            return detail

        return None

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
from .exceptions import DetailError, ScaleError, UserCancelledError


# --- Detail Tools ---------------------------------------------------------
class DetailTools:
    """Tools for detail view manipulation, scaling, and geometry operations."""
    @staticmethod
    def get_available_scales() -> dict[str, float]:
        """Return appropriate scale dictionary based on model units.

        Returns:
            Dictionary mapping scale labels to scale ratios.
        """
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
    ) -> tuple[float, float, str]:
        """Present a ListBox of available scales in correct order based on the mode provided.

        Args:
            scale_dict: Dictionary of scale labels to ratios.
            mode: Scale mode ("Architectural" or "Engineering").
            title: Dialog title.

        Returns:
            Tuple of (page_length, model_length, scale_label).

        Raises:
            UserCancelledError: If user cancels scale selection.
            ScaleError: If no scales are available or scale value not found.
        """
        if mode == "Architectural":
            keys = Constants.ARCHITECTURAL_SCALES_IMPERIAL_ORDER
        elif mode == "Engineering":
            keys = Constants.ENGINEERING_SCALES_IMPERIAL_ORDER
        else:
            keys = sorted(scale_dict.keys()) if scale_dict else []

        if not keys:
            raise ScaleError("No scale keys available for selection")

        choice = rs.ListBox(keys, title, title)
        if not choice:
            raise UserCancelledError("Scale selection cancelled")

        model_length = scale_dict.get(choice)
        if model_length is None:
            raise ScaleError(f"Scale value not found for choice '{choice}'", context={"choice": choice})

        page_length = 1.0  # Always assume page length = 1.0
        return (page_length, model_length, choice)

    @staticmethod
    def set_detail_scale(detail_id: object, page_length: float, model_length: float, scale_label: str) -> None:
        """Set the scale of a detail view and store the scale label as user metadata.

        Args:
            detail_id: Detail view object ID.
            page_length: Page length for scale ratio.
            model_length: Model length for scale ratio.
            scale_label: Human-readable scale label.

        Raises:
            ScaleError: If scale operation fails or metadata cannot be set.
        """
        success = rs.DetailScale(detail_id, page_length=page_length, model_length=model_length)
        if not success:
            raise ScaleError(
                "Failed to set detail scale",
                context={"detail_id": detail_id, "page_length": page_length, "model_length": model_length},
            )

        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            raise ScaleError("Could not coerce detail to Rhino object after scaling", context={"detail_id": detail_id})

        try:
            rh_obj.Attributes.SetUserString("detail_scale", scale_label)
        except (AttributeError, RuntimeError) as e:
            raise ScaleError(
                f"Failed to set user string for detail: {e}",
                context={"detail_id": detail_id, "scale_label": scale_label},
            )

    @staticmethod
    def format_architectural_scale(page_length: float, model_length: float) -> str:
        """Format a scale ratio into a standard architectural scale string.

        Formats a scale ratio (page_length / model_length) into a standard architectural
        scale string (e.g., "SCALE: 1/4" = 1'-0"") based on model units.

        Args:
            page_length: Page length for scale ratio (typically 1.0).
            model_length: Model length for scale ratio.

        Returns:
            Formatted scale string, or "SCALE: Custom" if non-standard.
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

        for label, standard_ratio in scale_dict.items():
            if abs(ratio - standard_ratio) < Constants.TOLERANCE:
                return label  # Found a match, return the standard label

        return "SCALE: Custom"

    # --- Metadata Storage -------------------------------------------------
    @staticmethod
    def set_page_scale_metadata(scale_text: str) -> None:
        """Set 'page_scale' user string on the active Layout Page View.

        Args:
            scale_text: Scale text to store.
        """
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
        """Get 'page_scale' user string from the active Layout Page View.

        Returns:
            Scale text if found, None otherwise.
        """
        pageview = sc.doc.Views.ActiveView
        if isinstance(pageview, Rhino.Display.RhinoPageView):
            vp = pageview.ActiveViewport
            if vp:
                return vp.GetUserString("page_scale")
            print("Warning: Could not access ActiveViewport for the PageView.")
        return None

    # --- Layer Management -------------------------------------------------
    @staticmethod
    def ensure_layer_exists(layer_name: str, color: Any = None) -> None:
        """Ensure the specified layer exists with the given color.

        Args:
            layer_name: Name of the layer to check or create.
            color: Optional color to assign if layer is created (System.Drawing.Color).
                  If None, uses default layer color.
        """
        if not rs.IsLayer(layer_name):
            if color is not None:
                rs.AddLayer(layer_name, color)
            else:
                rs.AddLayer(layer_name)

    # --- Detail Object Selection ------------------------------------------
    @staticmethod
    def get_detail_objects(preselect_allowed: bool = True) -> list[Any]:
        """Get selected Detail View objects, prompting user if none are selected.

        Args:
            preselect_allowed: Whether to check for pre-selected objects.

        Returns:
            List of detail view GUIDs.

        Raises:
            UserCancelledError: If no details are selected or user cancels selection.
        """
        selected_ids = rs.SelectedObjects(include_lights=False, include_grips=False) if preselect_allowed else []
        detail_ids: list[str] = []

        if selected_ids:
            detail_ids.extend(obj_id for obj_id in selected_ids if rs.IsDetail(obj_id))

        if not detail_ids:
            ids = rs.GetObjects("Select Detail Views", rs.filter.detail, preselect=False, select=True)
            if not ids:
                raise UserCancelledError("No detail views selected")
            return ids

        return detail_ids

    @staticmethod
    def highlight_details(ids: list[Any]) -> None:
        """Unselect all objects and select the specified detail objects.

        Args:
            ids: List of detail view object IDs to select.
        """
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
    def get_detail_rectangle() -> tuple[Any, Any]:
        """Prompt user to draw a rectangle and return its two opposite points as Point2d objects.

        Returns:
            Tuple of (pt1, pt2) as Point2d objects.

        Raises:
            UserCancelledError: If user cancels rectangle selection.
        """
        rect = rs.GetRectangle()
        if not rect:
            raise UserCancelledError("Rectangle selection cancelled")

        pt1 = Rhino.Geometry.Point2d(rect[0].X, rect[0].Y)
        pt2 = Rhino.Geometry.Point2d(rect[2].X, rect[2].Y)
        return pt1, pt2

    @staticmethod
    def create_detail(pageview: Any, pt1: Any, pt2: Any, name: str = "Detail") -> Any:
        """Create a new Detail View given two corner points.

        Args:
            pageview: The target layout page (Rhino.Display.RhinoPageView).
            pt1: First corner of the rectangle (Point2d).
            pt2: Opposite corner of the rectangle (Point2d).
            name: Name for the detail view.

        Returns:
            The new detail view object.

        Raises:
            DetailError: If detail creation fails.
        """
        detail = pageview.AddDetailView(name, pt1, pt2, Rhino.Display.DefinedViewportProjection.Top)
        if not detail:
            raise DetailError("Failed to create detail view", context={"name": name, "pt1": str(pt1), "pt2": str(pt2)})

        detail.IsActive = False
        detail.CommitChanges()
        return detail

    @staticmethod
    def move_detail_to_layer(detail_id: object, target_layer: str) -> None:
        """Move a detail view to the specified layer.

        Args:
            detail_id: The ID of the detail view.
            target_layer: The name of the destination layer.
        """
        rs.ObjectLayer(detail_id, target_layer)

    @staticmethod
    def correct_existing_details(pageview: Any, target_layer: str) -> int:
        """Move all existing Detail Views on the layout to the correct target layer.

        Args:
            pageview: The active layout page (Rhino.Display.RhinoPageView).
            target_layer: Layer to assign to all detail views.

        Returns:
            Number of detail views moved.
        """
        moved_count = 0
        details = pageview.GetDetailViews()
        for detail in details:
            current_layer = rs.ObjectLayer(detail.Id)
            if current_layer != target_layer:
                rs.ObjectLayer(detail.Id, target_layer)
                moved_count += 1
        return moved_count

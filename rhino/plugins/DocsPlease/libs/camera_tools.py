"""
Title         : camera_tools.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/camera_tools.py

Description
----------------------------------------------------------------------------
Camera metadata capture, storage, and restoration for detail views
"""

from __future__ import annotations

import math
import traceback
from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc
import System

import Rhino

from .constants import Constants


# --- Camera Tools ---------------------------------------------------------
class CameraTools:
    """Tools for reading and writing camera metadata to Detail View objects."""

    # --- Internal String Conversion Utilities -----------------------------
    @staticmethod
    def _point3d_to_string(point: object) -> str | None:
        """Converts Rhino.Geometry.Point3d to a comma-separated string."""
        if not isinstance(point, Rhino.Geometry.Point3d):
            return None
        try:
            culture = System.Globalization.CultureInfo.InvariantCulture
            return f"{point.X.ToString(culture)},{point.Y.ToString(culture)},{point.Z.ToString(culture)}"
        except (AttributeError, TypeError):
            return f"{point.X},{point.Y},{point.Z}"

    @staticmethod
    def _vector3d_to_string(vector: object) -> str | None:
        """Converts Rhino.Geometry.Vector3d to a comma-separated string."""
        if not isinstance(vector, Rhino.Geometry.Vector3d):
            return None
        try:
            culture = System.Globalization.CultureInfo.InvariantCulture
            return f"{vector.X.ToString(culture)},{vector.Y.ToString(culture)},{vector.Z.ToString(culture)}"
        except (AttributeError, TypeError):
            return f"{vector.X},{vector.Y},{vector.Z}"

    @staticmethod
    def _string_to_point3d(s: str) -> object | None:
        """Converts a comma-separated string back to Rhino.Geometry.Point3d."""
        if not s:
            return None
        try:
            parts = s.split(",")
            if len(parts) == 3:
                x = float(parts[0])
                y = float(parts[1])
                z = float(parts[2])
                return Rhino.Geometry.Point3d(x, y, z)
        except (ValueError, IndexError) as e:
            print(f"Error converting string '{s}' to Point3d: {e}")
        return None

    @staticmethod
    def _string_to_vector3d(s: str) -> object | None:
        """Converts a comma-separated string back to Rhino.Geometry.Vector3d."""
        if not s:
            return None
        try:
            parts = s.split(",")
            if len(parts) == 3:
                x = float(parts[0])
                y = float(parts[1])
                z = float(parts[2])
                return Rhino.Geometry.Vector3d(x, y, z)
        except (ValueError, IndexError) as e:
            print(f"Error converting string '{s}' to Vector3d: {e}")
        return None

    # --- Camera Metadata Read ---------------------------------------------
    @staticmethod
    def get_camera_metadata(detail_id: object) -> dict[str, Any] | None:  # noqa: PLR0912
        """
        Reads stored camera parameters from a Detail View's user strings.

        Returns a dictionary with keys matching Constants.CAMERA_METADATA_KEYS.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            print("Error: Invalid Detail ID or object type provided to get_camera_metadata.")
            return None

        metadata = {}
        missing_keys = []
        conversion_failed = False

        for key, user_key in Constants.CAMERA_METADATA_KEYS.items():
            value_str = rh_obj.Attributes.GetUserString(user_key)
            if value_str is None:
                missing_keys.append(user_key)
                continue

            converted_value = None
            try:
                if key in {"location", "target"}:
                    converted_value = CameraTools._string_to_point3d(value_str)
                elif key in {"direction", "up"}:
                    converted_value = CameraTools._string_to_vector3d(value_str)
                elif key in {"lens_length", "page_to_model_ratio"}:
                    converted_value = float(value_str)
                elif key == "projection_mode":
                    converted_value = value_str
                else:
                    converted_value = value_str

                if converted_value is None and key in {
                    "location",
                    "target",
                    "direction",
                    "up",
                }:
                    print(f"Warning: Conversion failed for key '{key}', value '{value_str}'")
                    conversion_failed = True
                else:
                    metadata[key] = converted_value

            except (ValueError, TypeError) as e:
                print(f"Error converting key '{key}': {e}")
                conversion_failed = True

        if missing_keys or conversion_failed:
            return None

        if len(metadata) != len(Constants.CAMERA_METADATA_KEYS):
            return None

        return metadata

    # --- Camera Metadata Write --------------------------------------------
    @staticmethod
    def set_camera_metadata(detail_id: object) -> bool:  # noqa: PLR0911, PLR0914
        """Captures the current camera state of a Detail View and stores it as user strings using ModifyAttributes."""
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            print("Error: Invalid Detail ID or object type provided to set_camera_metadata.")
            return False

        detail_vp = rh_obj.Viewport
        if not detail_vp:
            print(f"Error: Could not retrieve ViewportInfo for detail {detail_id}.")
            return False

        try:
            location = detail_vp.CameraLocation
            direction = detail_vp.CameraDirection
            up = detail_vp.CameraUp
            target = detail_vp.CameraTarget
            lens_length = detail_vp.Camera35mmLensLength
            projection_mode = "Perspective" if detail_vp.IsPerspectiveProjection else "Parallel"
            ratio = rh_obj.DetailGeometry.PageToModelRatio

        except (AttributeError, RuntimeError) as e:
            print(f"Error accessing camera properties for detail {detail_id}: {e}")
            return False

        try:
            attribs = rh_obj.Attributes.Duplicate()

            loc_str = CameraTools._point3d_to_string(location)
            dir_str = CameraTools._vector3d_to_string(direction)
            up_str = CameraTools._vector3d_to_string(up)
            tgt_str = CameraTools._point3d_to_string(target)
            lens_str = str(lens_length)
            ratio_str = str(ratio)

            if None in {loc_str, dir_str, up_str, tgt_str}:
                print("Error: Failed to convert one or more camera geometry fields to string.")
                return False

            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["location"], loc_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["direction"], dir_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["up"], up_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["target"], tgt_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["lens_length"], lens_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["projection_mode"], projection_mode)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["page_to_model_ratio"], ratio_str)

            if sc.doc.Objects.ModifyAttributes(rh_obj.Id, attribs, True):
                print(f"Successfully captured and stored camera metadata for detail {detail_id}.")
                return True
            print(f"Warning: Failed to modify attributes for detail {detail_id}")
            return False  # noqa: TRY300

        except (AttributeError, RuntimeError) as e:
            print(f"Error setting/storing camera metadata for detail {detail_id}: {e}")
            print(traceback.format_exc())
            return False

    # --- Camera Projection Utilities --------------------------------------
    @staticmethod
    def map_camera_direction_to_named_view(vector: object) -> str | None:
        """
        Maps a nearly-axis-aligned camera vector to a standard named view (Top, Bottom, Front, Back, Left, Right).

        Returns a string or None.
        """
        if not isinstance(vector, Rhino.Geometry.Vector3d):
            return None

        x, y, z = vector.X, vector.Y, vector.Z
        abs_x, abs_y, abs_z = abs(x), abs(y), abs(z)

        if abs_x >= abs_y and abs_x >= abs_z:
            v = Rhino.Geometry.Vector3d(math.copysign(1, x), 0, 0)
        elif abs_y >= abs_x and abs_y >= abs_z:
            v = Rhino.Geometry.Vector3d(0, math.copysign(1, y), 0)
        else:
            v = Rhino.Geometry.Vector3d(0, 0, math.copysign(1, z))

        key = (round(v.X), round(v.Y), round(v.Z))

        mapping = {
            (0, 0, -1): "Top",
            (0, 0, 1): "Bottom",
            (0, -1, 0): "Front",
            (0, 1, 0): "Back",
            (-1, 0, 0): "Left",
            (1, 0, 0): "Right",
        }

        return mapping.get(key)

    @staticmethod
    def set_camera_projection_for_named_view(detail_id: object, target_view: str) -> bool:
        """Sets the camera projection of a Detail View to match the specified named view."""
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            return False

        vp = rh_obj.Viewport

        mapping = {
            "Top": (
                Rhino.Geometry.Vector3d(0, 0, -1),
                Rhino.Geometry.Vector3d(0, 1, 0),
            ),
            "Bottom": (
                Rhino.Geometry.Vector3d(0, 0, 1),
                Rhino.Geometry.Vector3d(0, 1, 0),
            ),
            "Front": (
                Rhino.Geometry.Vector3d(0, -1, 0),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "Back": (
                Rhino.Geometry.Vector3d(0, 1, 0),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "Left": (
                Rhino.Geometry.Vector3d(-1, 0, 0),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "Right": (
                Rhino.Geometry.Vector3d(1, 0, 0),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
        }

        if target_view not in mapping:
            return False

        direction, up = mapping[target_view]
        location = vp.CameraLocation
        target = location + direction

        vp.ChangeToParallelProjection(True)
        vp.SetCameraDirection(direction, True)
        vp.CameraUp = up
        vp.SetCameraTarget(target, True)

        rh_obj.CommitViewportChanges()
        rh_obj.CommitChanges()
        sc.doc.Views.Redraw()

        return True

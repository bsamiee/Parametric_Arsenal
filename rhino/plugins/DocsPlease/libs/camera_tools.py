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
from .exceptions import CameraError


# --- Camera Tools ---------------------------------------------------------
class CameraTools:
    """Tools for reading and writing camera metadata to Detail View objects."""

    # --- Internal String Conversion Utilities -----------------------------
    @staticmethod
    def _point3d_to_string(point: object) -> str:
        """Convert Rhino.Geometry.Point3d to a comma-separated string.

        Args:
            point: Point3d object to convert.

        Returns:
            Comma-separated string representation.

        Raises:
            CameraError: If point is not a valid Point3d.
        """
        if not isinstance(point, Rhino.Geometry.Point3d):
            raise CameraError(f"Invalid point type: expected Point3d, got {type(point).__name__}", context=point)
        try:
            culture = System.Globalization.CultureInfo.InvariantCulture
            return f"{point.X.ToString(culture)},{point.Y.ToString(culture)},{point.Z.ToString(culture)}"
        except (AttributeError, TypeError):
            return f"{point.X},{point.Y},{point.Z}"

    @staticmethod
    def _vector3d_to_string(vector: object) -> str:
        """Convert Rhino.Geometry.Vector3d to a comma-separated string.

        Args:
            vector: Vector3d object to convert.

        Returns:
            Comma-separated string representation.

        Raises:
            CameraError: If vector is not a valid Vector3d.
        """
        if not isinstance(vector, Rhino.Geometry.Vector3d):
            raise CameraError(f"Invalid vector type: expected Vector3d, got {type(vector).__name__}", context=vector)
        try:
            culture = System.Globalization.CultureInfo.InvariantCulture
            return f"{vector.X.ToString(culture)},{vector.Y.ToString(culture)},{vector.Z.ToString(culture)}"
        except (AttributeError, TypeError):
            return f"{vector.X},{vector.Y},{vector.Z}"

    @staticmethod
    def _string_to_point3d(s: str) -> Rhino.Geometry.Point3d:
        """Convert a comma-separated string back to Rhino.Geometry.Point3d.

        Args:
            s: Comma-separated string of coordinates.

        Returns:
            Point3d object.

        Raises:
            CameraError: If string cannot be converted to Point3d.
        """
        if not s:
            raise CameraError("Cannot convert empty string to Point3d")
        try:
            parts = s.split(",")
            if len(parts) != 3:
                raise CameraError(f"Invalid Point3d string format: expected 3 values, got {len(parts)}", context=s)
            x = float(parts[0])
            y = float(parts[1])
            z = float(parts[2])
            return Rhino.Geometry.Point3d(x, y, z)
        except (ValueError, IndexError) as e:
            raise CameraError(f"Error converting string '{s}' to Point3d: {e}", context=s) from e

    @staticmethod
    def _string_to_vector3d(s: str) -> Rhino.Geometry.Vector3d:
        """Convert a comma-separated string back to Rhino.Geometry.Vector3d.

        Args:
            s: Comma-separated string of coordinates.

        Returns:
            Vector3d object.

        Raises:
            CameraError: If string cannot be converted to Vector3d.
        """
        if not s:
            raise CameraError("Cannot convert empty string to Vector3d")
        try:
            parts = s.split(",")
            if len(parts) != 3:
                raise CameraError(f"Invalid Vector3d string format: expected 3 values, got {len(parts)}", context=s)
            x = float(parts[0])
            y = float(parts[1])
            z = float(parts[2])
            return Rhino.Geometry.Vector3d(x, y, z)
        except (ValueError, IndexError) as e:
            raise CameraError(f"Error converting string '{s}' to Vector3d: {e}", context=s) from e

    # --- Camera Metadata Read ---------------------------------------------
    @staticmethod
    def get_camera_metadata(detail_id: object) -> dict[str, Any]:
        """Read stored camera parameters from a Detail View's user strings.

        Args:
            detail_id: Detail view object ID.

        Returns:
            Dictionary with keys matching Constants.CAMERA_METADATA_KEYS.

        Raises:
            CameraError: If detail is invalid or camera metadata cannot be retrieved.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            raise CameraError("Invalid Detail ID or object type provided to get_camera_metadata", context=detail_id)

        metadata = {}
        missing_keys = []

        for key, user_key in Constants.CAMERA_METADATA_KEYS.items():
            value_str = rh_obj.Attributes.GetUserString(user_key)
            if value_str is None:
                missing_keys.append(user_key)
                continue

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

                metadata[key] = converted_value

            except (ValueError, TypeError, CameraError) as e:
                raise CameraError(
                    f"Error converting camera metadata key '{key}': {e}",
                    context={"detail_id": detail_id, "key": key, "value": value_str},
                ) from e

        if missing_keys:
            raise CameraError(
                f"Missing camera metadata keys: {', '.join(missing_keys)}",
                context={"detail_id": detail_id, "missing_keys": missing_keys},
            )

        if len(metadata) != len(Constants.CAMERA_METADATA_KEYS):
            raise CameraError(
                f"Incomplete camera metadata: expected {len(Constants.CAMERA_METADATA_KEYS)} keys, got {len(metadata)}",
                context={"detail_id": detail_id, "metadata": metadata},
            )

        return metadata

    # --- Camera Metadata Write --------------------------------------------
    @staticmethod
    def set_camera_metadata(detail_id: object) -> None:
        """Capture the current camera state of a Detail View and store it as user strings.

        Args:
            detail_id: Detail view object ID.

        Raises:
            CameraError: If detail is invalid or camera metadata cannot be stored.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            raise CameraError("Invalid Detail ID or object type provided to set_camera_metadata", context=detail_id)

        detail_vp = rh_obj.Viewport
        if not detail_vp:
            raise CameraError("Could not retrieve ViewportInfo for detail", context=detail_id)

        try:
            location = detail_vp.CameraLocation
            direction = detail_vp.CameraDirection
            up = detail_vp.CameraUp
            target = detail_vp.CameraTarget
            lens_length = detail_vp.Camera35mmLensLength
            projection_mode = "Perspective" if detail_vp.IsPerspectiveProjection else "Parallel"
            ratio = rh_obj.DetailGeometry.PageToModelRatio

        except (AttributeError, RuntimeError) as e:
            raise CameraError(f"Error accessing camera properties for detail: {e}", context=detail_id) from e

        try:
            attribs = rh_obj.Attributes.Duplicate()

            loc_str = CameraTools._point3d_to_string(location)
            dir_str = CameraTools._vector3d_to_string(direction)
            up_str = CameraTools._vector3d_to_string(up)
            tgt_str = CameraTools._point3d_to_string(target)
            lens_str = str(lens_length)
            ratio_str = str(ratio)

            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["location"], loc_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["direction"], dir_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["up"], up_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["target"], tgt_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["lens_length"], lens_str)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["projection_mode"], projection_mode)
            attribs.SetUserString(Constants.CAMERA_METADATA_KEYS["page_to_model_ratio"], ratio_str)

            if not sc.doc.Objects.ModifyAttributes(rh_obj.Id, attribs, True):
                raise CameraError("Failed to modify attributes for detail", context=detail_id)

        except CameraError:
            raise
        except (AttributeError, RuntimeError) as e:
            raise CameraError(
                f"Error setting/storing camera metadata for detail: {e}\n{traceback.format_exc()}", context=detail_id
            ) from e

    # --- Camera Projection Utilities --------------------------------------
    @staticmethod
    def map_camera_direction_to_named_view(vector: object) -> str:
        """Map a nearly-axis-aligned camera vector to a standard named view.

        Args:
            vector: Camera direction vector.

        Returns:
            Named view string (Top, Bottom, Front, Back, Left, or Right).

        Raises:
            CameraError: If vector is invalid or cannot be mapped to a named view.
        """
        if not isinstance(vector, Rhino.Geometry.Vector3d):
            raise CameraError(f"Invalid vector type: expected Vector3d, got {type(vector).__name__}", context=vector)

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

        result = mapping.get(key)
        if result is None:
            raise CameraError("Cannot map camera direction to named view", context={"vector": vector, "key": key})
        return result

    @staticmethod
    def set_camera_projection_for_named_view(detail_id: object, target_view: str) -> None:
        """Set the camera projection of a Detail View to match the specified named view.

        Args:
            detail_id: Detail view object ID.
            target_view: Target named view (Top, Bottom, Front, Back, Left, or Right).

        Raises:
            CameraError: If detail is invalid or camera projection cannot be set.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            raise CameraError(
                "Invalid Detail ID or object type provided to set_camera_projection_for_named_view", context=detail_id
            )

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
            raise CameraError(
                f"Invalid target view: '{target_view}'. Must be one of: {', '.join(mapping.keys())}",
                context={"detail_id": detail_id, "target_view": target_view},
            )

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

    @staticmethod
    def set_isometric_projection(detail_id: object, iso_type: str) -> None:
        """Set detail view to isometric projection.

        Uses standard isometric angles: camera at 45° horizontal, 35.264° from horizontal plane.
        Based on Rhino's standard isometric views (NE/NW/SE/SW).

        Args:
            detail_id: Detail view object ID.
            iso_type: "SW Isometric", "SE Isometric", "NE Isometric", or "NW Isometric".

        Raises:
            CameraError: If detail is invalid or iso_type is not recognized.
        """
        rh_obj = rs.coercerhinoobject(detail_id)
        if not rh_obj or not isinstance(rh_obj, Rhino.DocObjects.DetailViewObject):
            raise CameraError(
                "Invalid Detail ID or object type provided to set_isometric_projection", context=detail_id
            )

        vp = rh_obj.Viewport

        mapping = {
            "SW Isometric": (
                Rhino.Geometry.Vector3d(-1, -1, 1),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "SE Isometric": (
                Rhino.Geometry.Vector3d(1, -1, 1),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "NE Isometric": (
                Rhino.Geometry.Vector3d(1, 1, 1),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
            "NW Isometric": (
                Rhino.Geometry.Vector3d(-1, 1, 1),
                Rhino.Geometry.Vector3d(0, 0, 1),
            ),
        }

        if iso_type not in mapping:
            raise CameraError(
                f"Invalid isometric type: '{iso_type}'. Must be one of: {', '.join(mapping.keys())}",
                context={"detail_id": detail_id, "iso_type": iso_type},
            )

        direction, up = mapping[iso_type]
        direction.Unitize()
        location = vp.CameraLocation
        target = location + direction

        vp.ChangeToParallelProjection(True)
        vp.SetCameraDirection(direction, True)
        vp.CameraUp = up
        vp.SetCameraTarget(target, True)

        rh_obj.CommitViewportChanges()
        rh_obj.CommitChanges()
        sc.doc.Views.Redraw()

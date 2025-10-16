"""
Title         : exceptions.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/exceptions.py

Description
----------------------------------------------------------------------------
Centralized exception hierarchy for DocsPlease plugin commands. All exceptions
inherit from DocsPluginError and support optional context information for debugging.
"""

from __future__ import annotations

from abc import ABC
from typing import Any


# --- Base Exception -------------------------------------------------------
class DocsPluginError(Exception, ABC):
    """Base exception for all DocsPlease plugin errors.

    All plugin exceptions inherit from this class and support optional
    context information for debugging purposes.

    Attributes:
        message: Human-readable error description
        context: Optional context information (object IDs, parameters, etc.)

    Example:
        >>> raise DocsPluginError("Operation failed", context={"id": detail_id})
    """

    def __init__(self, message: str, context: Any | None = None) -> None:
        """Initialize plugin error with message and optional context.

        Args:
            message: Error description shown to user
            context: Optional context information for debugging (e.g., object IDs,
                    parameters, state information)
        """
        super().__init__(message)
        self.message = message
        self.context = context

    def __str__(self) -> str:
        """Return formatted error message."""
        return self.message


# --- Validation Exceptions ------------------------------------------------
class ValidationError(DocsPluginError):
    """Raised when validation fails.

    Base class for all validation-related errors. Use specific subclasses
    (LayoutError, DetailError, etc.) when possible for better error handling.

    Example:
        >>> if not is_valid_format(value):
        ...     raise ValidationError(f"Invalid format: {value}")
    """


class LayoutError(ValidationError):
    """Raised when layout view is required but not active.

    This exception is raised when a command requires a layout (page) view
    but the active view is a model view or no view is active.

    Example:
        >>> if not is_layout_view_active():
        ...     raise LayoutError("This command requires a layout view")
    """


class DetailError(ValidationError):
    """Raised when detail view operations fail.

    This exception covers detail view validation failures, invalid detail
    objects, locked details, or any detail-specific operation errors.

    Example:
        >>> if not isinstance(obj, DetailViewObject):
        ...     raise DetailError("Selected object is not a detail view")
        >>> if detail.IsLocked:
        ...     raise DetailError("Detail view is locked", context=detail_id)
    """


# --- Operation Exceptions -------------------------------------------------
class UserCancelledError(DocsPluginError):
    """Raised when user cancels an operation.

    This exception is used for user-initiated cancellations (ESC key,
    cancel button, etc.). The @rhino_command decorator treats this as
    a normal exit, not an error condition.

    Example:
        >>> result = rs.GetObject("Select object")
        >>> if not result:
        ...     raise UserCancelledError("User cancelled selection")
    """


class EnvironmentError(ValidationError):  # noqa: A001
    """Raised when environment prerequisites are not met.

    This exception is raised when the Rhino environment doesn't meet
    command requirements (unsupported units, missing layers, etc.).

    Example:
        >>> if unit_system not in SUPPORTED_UNITS:
        ...     raise EnvironmentError(f"Unsupported unit system: {unit_system}")
    """


class ScaleError(ValidationError):
    """Raised when scale operations fail.

    This exception covers scale validation failures, invalid scale values,
    or errors during scale application to detail views.

    Example:
        >>> if scale_value <= 0:
        ...     raise ScaleError(f"Scale must be positive: {scale_value}")
        >>> if not detail.SetScale(scale):
        ...     raise ScaleError("Failed to apply scale", context={"detail": detail_id, "scale": scale})
    """


class CameraError(DocsPluginError):
    """Raised when camera metadata operations fail.

    This exception is raised when camera capture, restoration, or
    manipulation operations fail.

    Example:
        >>> if not capture_camera_metadata(detail):
        ...     raise CameraError("Failed to capture camera metadata", context=detail_id)
        >>> if not restore_camera_metadata(detail, metadata):
        ...     raise CameraError("Failed to restore camera", context=metadata)
    """


class TransformError(DocsPluginError):
    """Raised when geometric transformations fail.

    This exception covers failures in moving, rotating, scaling, or
    otherwise transforming objects or detail views.

    Example:
        >>> if not rs.MoveObject(obj_id, vector):
        ...     raise TransformError("Failed to move object", context={"id": obj_id, "vector": vector})
        >>> if not calculate_valid_vector(pt1, pt2):
        ...     raise TransformError("Invalid transformation vector")
    """


class ProjectConfigError(ValidationError):
    """Raised when project configuration operations fail.

    This exception is raised when project-level configuration validation
    fails, configuration is missing, or configuration operations encounter
    errors.

    Example:
        >>> if not project_name:
        ...     raise ProjectConfigError("Project name is required")
        >>> if not validate_project_config():
        ...     raise ProjectConfigError("Project configuration not found")
    """

"""
Title         : __init__.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/__init__.py

Description
----------------------------------------------------------------------------
Package initializer that exports all public classes and functions
"""

from .alignment_tools import AlignmentTools
from .camera_tools import CameraTools
from .command_framework import require_layout_view, rhino_command, safe_undo_block
from .common_utils import (
    CommonUtils,
    require_user_choice,
    require_user_point,
    require_user_selection,
    require_user_string,
    validate_detail_object,
    validate_environment_units,
    validate_sheet_number,
)
from .constants import (
    DESIGNATION_LEVEL_CHOICES,
    DISCIPLINE_CHOICES,
    L2_CHOICES_BY_MASTER,
    Constants,
    Metadata,
    Strings,
)
from .detail_tools import DetailTools
from .exceptions import (
    CameraError,
    DetailError,
    DocsPluginError,
    EnvironmentError,
    LayoutError,
    ScaleError,
    TransformError,
    UserCancelledError,
    ValidationError,
)
from .layout_tools import LayoutTools


__all__ = [
    # Data constants
    "DESIGNATION_LEVEL_CHOICES",
    "DISCIPLINE_CHOICES",
    "L2_CHOICES_BY_MASTER",
    # Main tool classes
    "AlignmentTools",
    "CameraTools",
    "CommonUtils",
    "Constants",
    "DetailTools",
    "LayoutTools",
    "Metadata",
    "Strings",
    # Command framework
    "rhino_command",
    "require_layout_view",
    "safe_undo_block",
    # Validation functions
    "require_user_choice",
    "require_user_point",
    "require_user_selection",
    "require_user_string",
    "validate_detail_object",
    "validate_environment_units",
    "validate_sheet_number",
    # Exception classes
    "CameraError",
    "DetailError",
    "DocsPluginError",
    "EnvironmentError",
    "LayoutError",
    "ScaleError",
    "TransformError",
    "UserCancelledError",
    "ValidationError",
]

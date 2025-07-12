"""
Title         : enums.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/errors/enums.py.

Description ----------- Minimal, focused error enumerations for the new error system. Only includes truly universal
enums, enabling domain-specific flexibility.

"""

from __future__ import annotations

from aenum import IntEnum, StrEnum

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


# --- Error Severity Levels ----------------------------------------------------


@Build.enum(
    base_type=IntEnum,
    description="Universal severity levels for all error types.",
    tags=(SYSTEM.ERROR,),
)
class ErrorSeverity(IntEnum):
    """
    Universal severity levels applicable across all domains.

    These levels are intentionally generic to work with any error type, from debug traces to critical failures.

    """

    DEBUG = 0       # Debug-level information
    INFO = 1        # Informational message
    WARNING = 2     # Warning that may require attention
    ERROR = 3       # Error requiring handling
    CRITICAL = 4    # Critical failure requiring immediate action


# --- Error Categories ---------------------------------------------------------


@Build.enum(
    base_type=StrEnum,
    description="Abstract error categories for high-level classification.",
    tags=(SYSTEM.ERROR,),
)
class ErrorCategory(StrEnum):
    """
    Abstract error categories for classification and routing.

    These are intentionally high-level to avoid coupling to specific domains. Each domain can further subcategorize
    within these abstract categories.

    """

    VALIDATION = "validation"       # Data validation and constraint violations
    CONFIGURATION = "configuration"  # Configuration and setup issues
    RUNTIME = "runtime"             # Runtime execution failures
    RESOURCE = "resource"           # Resource availability or access issues
    EXTERNAL = "external"           # External service or dependency failures


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ErrorCategory",
    "ErrorSeverity",
]

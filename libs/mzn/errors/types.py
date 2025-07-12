"""
Title         : types.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/errors/types.py.

Description ----------- Central type imports for the error system.

This module re-exports all type assets from mzn.types.packages.errors/ as well as general types used by the error
system.

All code within the errors package should import types from this module, never directly from mzn.types.packages/.

"""

from __future__ import annotations

# Re-export error-specific type aliases
from mzn.types.packages.errors.aliases import (
    ErrorCode,
    ErrorMessage,
    RecoveryHint,
)

# Re-export error-specific enums
from mzn.types.packages.errors.enums import (
    ErrorCategory,
    ErrorSeverity,
)

# Re-export error-specific models
from mzn.types.packages.errors.models import (
    ErrorContext,
)

# Re-export general types used by errors
from mzn.types.packages.general.aliases import (
    RequestID,
)


# --- Exports ------------------------------------------------------------------

__all__ = [  # noqa: RUF022
    # Error aliases
    "ErrorCode",
    "ErrorMessage",
    "RecoveryHint",
    # Error enums
    "ErrorCategory",
    "ErrorSeverity",
    # Error models
    "ErrorContext",
    # General aliases
    "RequestID",
]

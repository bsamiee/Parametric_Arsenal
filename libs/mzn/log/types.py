"""
Title         : types.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/log/types.py.

Description ----------- Type re-exports for the log package, following the TYPES FIRST POLICY. All types are imported
from mzn.types.packages.log to ensure validation, security features, and sophisticated type system benefits.

This module serves as the single source of truth for all log-related types used throughout the package, providing clean
separation from the type system while leveraging all its sophisticated features.

"""

from __future__ import annotations

from typing import TYPE_CHECKING  # Python 3.13 type helpers for LogLevel operations

# --- General aliases ---------------------------------------------------------
from mzn.types.packages.general.aliases import (
    FilePath,  # Validated file system paths
    TimestampUTC,  # UTC timestamps with proper timezone handling
)

# --- Log-specific aliases ----------------------------------------------------
from mzn.types.packages.log.aliases import (
    LogContext,  # Structured context with size limits and key validation
    LoggerName,  # Hierarchical dot notation with validation
    LogMessage,  # Security-hardened with PII masking and HTML stripping
    LogRecordID,  # UUID v4 with auto-generation
)

# --- Log-specific enums ------------------------------------------------------
from mzn.types.packages.log.enums import (
    LogLevel,  # Standard levels with colors, symbols, verbosity
    OutputFormat,  # Output format types (json, human, rich, etc.)
    OutputTarget,  # Output destination types (console, file, both, null)
)


if TYPE_CHECKING:
    type LogLevelLike = LogLevel | int  # Help mypy understand LogLevel is comparable with int

# --- Log-specific models -----------------------------------------------------
from mzn.types.packages.log.models import (
    LogRecord,  # Immutable core record with tracing integration
)


# --- Exports ------------------------------------------------------------------

__all__ = [  # noqa: RUF022
    # Aliases
    "LogContext",
    "LogMessage",
    "LogRecordID",
    "LoggerName",
    # Enums
    "LogLevel",
    "OutputFormat",
    "OutputTarget",
    # Type helpers
    "LogLevelLike",
    # Models
    "LogRecord",
    # General types
    "FilePath",
    "TimestampUTC",
]

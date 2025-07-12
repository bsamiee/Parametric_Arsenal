"""
Title         : enums.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/log/enums.py.

Description ----------- Enumerations for the log package.

Provides rich enums for log levels, handler types, formats, and circuit states. All enums include metadata for enhanced
functionality.

"""

from __future__ import annotations

import aenum

from mzn.types._core.core_builders import Build


@Build.enum(base_type=aenum.IntEnum, enable_caching=True)
class LogLevel(aenum.IntEnum):
    """
    Standard Python logging levels with numeric comparison support.

    Being an IntEnum allows for numeric comparisons:     level >= LogLevel.WARNING     level < LogLevel.ERROR

    """

    NOTSET = 0      # No level set
    DEBUG = 10      # Detailed debug information
    INFO = 20       # General informational messages (default)
    WARNING = 30    # Warning messages
    ERROR = 40      # Error messages
    CRITICAL = 50   # Critical error messages


@Build.enum(
    base_type=aenum.StrEnum,
    enable_caching=True,
    description="Log output format types with metadata for configuration.",
)
class OutputFormat(aenum.StrEnum):
    """Log output format types with metadata for configuration."""

    JSON = "json"       # Structured JSON output
    HUMAN = "human"     # Human-readable text format (default)
    RICH = "rich"       # Rich console formatting with colors
    REPORT = "report"   # Detailed report format for analysis
    COMPACT = "compact"  # Minimal space-efficient format
    DEBUG = "debug"     # Verbose debug format with source info


@Build.enum(
    base_type=aenum.StrEnum,
    enable_caching=True,
    description="Log output destination types.",
)
class OutputTarget(aenum.StrEnum):
    """Log output destination types."""

    CONSOLE = "console"  # Console/terminal output (default)
    FILE = "file"       # File-based output
    BOTH = "both"       # Both console and file output
    NULL = "null"       # Discard all output

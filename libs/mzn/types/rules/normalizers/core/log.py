"""
Title         : log.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/core/log.py

Description
-----------
Domain-specific normalizers for the log package.

Provides normalization rules for log messages, contexts, and exceptions.
"""
# mypy: warn-redundant-casts=False
from __future__ import annotations

import inspect
import re
import traceback
import uuid
from typing import TYPE_CHECKING, Annotated, Any, cast

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

# Type alias for level types
type LevelType = int | str


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.sanitize_message,
    description="Sanitize log messages for safe output",
    tags=(SYSTEM.SECURITY, SYSTEM.LOGGING.output),
)
async def sanitize_message(value: Annotated[str, "Message to sanitize"], info: ValidationInfo) -> str:
    """
    Clean and sanitize log messages for safe output.

    Operations:
    - Remove null bytes
    - Escape control characters
    - Normalize whitespace
    - Trim if exceeds length limit
    """
    # Remove null bytes completely
    sanitized = value.replace("\0", "")

    # Escape control characters except common ones
    control_map = {
        "\x00": "\\x00",
        "\x01": "\\x01",
        "\x02": "\\x02",
        "\x03": "\\x03",
        "\x04": "\\x04",
        "\x05": "\\x05",
        "\x06": "\\x06",
        "\x07": "\\x07",
        "\x08": "\\x08",
        "\x0b": "\\x0b",
        "\x0c": "\\x0c",
        "\x0e": "\\x0e",
        "\x0f": "\\x0f",
        "\x10": "\\x10",
        "\x11": "\\x11",
        "\x12": "\\x12",
        "\x13": "\\x13",
        "\x14": "\\x14",
        "\x15": "\\x15",
        "\x16": "\\x16",
        "\x17": "\\x17",
        "\x18": "\\x18",
        "\x19": "\\x19",
        "\x1a": "\\x1a",
        "\x1b": "\\x1b",
        "\x1c": "\\x1c",
        "\x1d": "\\x1d",
        "\x1e": "\\x1e",
        "\x1f": "\\x1f",
    }

    for char, escape in control_map.items():
        sanitized = sanitized.replace(char, escape)

    # Normalize excessive whitespace
    sanitized = re.sub(r"\s+", " ", sanitized)
    sanitized = re.sub(r"^\s+|\s+$", "", sanitized)

    # Trim if too long (preserve end for error messages)
    max_length = info.context.get("max_length", 10_000) if info.context else 10_000
    if len(sanitized) > max_length:
        # Keep start and end
        half = (max_length - 20) // 2
        sanitized = f"{sanitized[:half]}...[TRUNCATED]...{sanitized[-half:]}"

    return sanitized


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.flatten_context,
    description="Flatten nested context to single level with dot notation",
    tags=(SYSTEM.LOGGING.context, SYSTEM.INFRA.performance),
)
async def flatten_context(
    value: Annotated[dict[str, Any], "Context dict to flatten"], info: ValidationInfo
) -> dict[str, Any]:
    """
    Flatten nested dictionaries using dot notation for keys.

    Benefits:
    - Easier to query in log aggregation systems
    - Better performance for serialization
    - Consistent key naming

    Example:
        {"user": {"id": 123, "name": "Alice"}}
        → {"user.id": 123, "user.name": "Alice"}
    """

    def flatten(obj: dict[str, Any], prefix: str = "") -> dict[str, Any]:
        result: dict[str, Any] = {}

        for key, val in obj.items():
            # Skip private keys (starting with underscore)
            if key.startswith("_"):
                if prefix:
                    result[f"{prefix}.{key}"] = val
                else:
                    result[key] = val
                continue

            # Build the full key
            full_key = f"{prefix}.{key}" if prefix else key

            # Recursively flatten dicts, but not beyond depth limit
            if isinstance(val, dict) and len(full_key.split(".")) < 3:
                result.update(flatten(cast("dict[str, Any]", val), full_key))
            else:
                result[full_key] = val

        return result

    return flatten(value)


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.enrich_with_caller,
    description="Add caller information to log context",
    tags=(SYSTEM.LOGGING.context, SYSTEM.DEBUG),
)
async def enrich_with_caller(
    value: Annotated[dict[str, Any], "Context to enrich"], info: ValidationInfo
) -> dict[str, Any]:
    """
    Enrich context with caller information for debugging.

    Adds:
    - Module name
    - Function/method name
    - Line number
    - File path (relative)

    Only added for DEBUG and ERROR levels by default.
    """
    # inspect already imported at top

    # Check if we should add caller info
    level = info.context.get("level") if info.context else None
    if level not in {"DEBUG", "ERROR", 10, 40}:  # Support both string and int
        return value

    # Get caller frame (skip internal frames)
    frame = None
    for f in inspect.stack()[2:]:  # Skip this function and its caller
        if not f.filename.endswith(("logging.py", "log.py", "_log.py")):
            frame = f
            break

    if frame:
        # Create a copy to avoid modifying input
        enriched = value.copy()

        # Add caller info under special key
        enriched["_caller"] = {
            "module": frame.frame.f_globals.get("__name__", "unknown"),
            "function": frame.function,
            "line": frame.lineno,
            "file": frame.filename.split("/")[-1],  # Just filename, not full path
        }

        return enriched

    return value


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.format_exception_info,
    description="Format exception information for structured logging",
    tags=(SYSTEM.LOGGING.context, SYSTEM.DEBUG),
)
async def format_exception_info(
    value: Annotated[tuple[type[BaseException], BaseException, Any] | None, "Exception info tuple or None"],
    info: ValidationInfo,
) -> dict[str, Any] | None:
    """
    Format exception info into structured data.

    Converts sys.exc_info() tuple into a structured dict with:
    - Exception type name
    - Exception message
    - Formatted traceback (limited depth)
    - Exception attributes (if any)
    """
    if not value:
        return None

    exc_type, exc_value, exc_tb = value

    # Format traceback with depth limit
    tb_lines: list[str] = []
    if exc_tb:
        tb_lines = traceback.format_tb(exc_tb, limit=10)  # Limit depth

    # Build structured exception info
    exc_info: dict[str, Any] = {
        "type": exc_type.__name__ if exc_type else "Unknown",
        "message": str(exc_value) if exc_value else "",
        "traceback": tb_lines,
    }

    # Add exception attributes if it has useful ones
    if exc_value and hasattr(exc_value, "__dict__"):
        attrs: dict[str, Any] = {
            k: v
            for k, v in exc_value.__dict__.items()
            if not k.startswith("_") and isinstance(v, (str, int, float, bool, list, dict))
        }
        if attrs:
            exc_info["attributes"] = attrs

    return exc_info


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.optimize_context_size,
    description="Optimize context size by removing redundant data",
    tags=(SYSTEM.LOGGING.context, SYSTEM.INFRA.performance),
)
async def optimize_context_size(
    value: Annotated[dict[str, Any], "Context to optimize"], info: ValidationInfo
) -> dict[str, Any]:
    """
    Optimize context size by removing redundant or oversized data.

    Operations:
    - Remove None values
    - Truncate long strings
    - Limit list sizes
    - Remove empty collections
    """
    max_string_length = info.context.get("max_string_length", 1000) if info.context else 1000
    max_list_size = info.context.get("max_list_size", 100) if info.context else 100

    def optimize_dict(d: dict[str, Any]) -> dict[str, Any]:
        """
        Optimize a dictionary by removing keys with None values and truncating large string or list values.

        - Removes any key-value pairs where the value is None.
        - Truncates string values longer than `max_string_length`, appending "...[truncated]".
        - Truncates lists longer than `max_list_size` by keeping the first and last halves,
          inserting "...[truncated]..." in the middle.
        - Recursively optimizes nested dictionaries.
        - Only includes non-empty nested dictionaries.
        - Leaves other types unchanged unless they are empty lists or dicts, which are omitted.

        Args:
            d (dict[str, Any]): The dictionary to optimize.

        Returns:
            dict[str, Any]: The optimized dictionary.
        """
        """Optimize a dictionary by removing None values and truncating large values."""
        optimized: dict[str, Any] = {}

        for key, val in d.items():
            if val is None:
                continue

            if isinstance(val, str):
                if len(val) > max_string_length:
                    optimized[key] = f"{val[:max_string_length]}...[truncated]"
                else:
                    optimized[key] = val
            elif isinstance(val, list):
                list_val: list[Any] = cast("list[Any]", val)
                if len(list_val) > max_list_size:
                    optimized[key] = [
                        *list_val[:max_list_size // 2],
                        "...[truncated]...",
                        *list_val[-max_list_size // 2:]
                    ]
                else:
                    optimized[key] = list_val
            elif isinstance(val, dict):
                # Cast to help type checker with dict type
                dict_val = cast("dict[str, Any]", val)
                nested = optimize_dict(dict_val)
                if nested:  # Only include non-empty dicts
                    optimized[key] = nested
            elif val not in ([], {}):
                optimized[key] = val

        return optimized

    return optimize_dict(value)


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.generate_record_id,
    description="Generate UUID v4 for log record if missing",
    tags=(SYSTEM.LOGGING.context,),
)
async def generate_record_id(
    value: Annotated[str | None, "Existing record ID or None"], info: ValidationInfo
) -> str:
    """
    Generate a UUID v4 for log record identification.

    Used for:
    - Deduplication
    - Correlation across systems
    - Unique identification in storage
    """
    if value:
        return value

    return str(uuid.uuid4())


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.normalize_level_names,
    description="Normalize log level names to standard values",
    tags=(SYSTEM.LOGGING.level,),
)
async def normalize_level_names(value: Annotated[LevelType, "Level name or number"], info: ValidationInfo) -> int:
    """
    Normalize various log level representations to standard numeric values.

    Handles:
    - String names (case insensitive)
    - Alternate names (WARN -> WARNING, FATAL -> CRITICAL)
    - Numeric values
    """
    # Standard level mapping
    level_map = {
        # Standard names
        "NOTSET": 0,
        "DEBUG": 10,
        "INFO": 20,
        "WARNING": 30,
        "ERROR": 40,
        "CRITICAL": 50,
        # Alternate names
        "WARN": 30,
        "FATAL": 50,
        "TRACE": 5,  # Below DEBUG
        "VERBOSE": 15,  # Between DEBUG and INFO
    }

    if isinstance(value, str):
        normalized = value.upper().strip()
        return level_map.get(normalized, 0)  # Default to NOTSET if unknown

    # Handle int case - no need for isinstance since type is LevelType = int | str
    # Clamp to valid range
    return max(0, min(50, value))


@Build.normalizer(
    register_as=NORM.DOMAINS.LOG.normalize_metric_names,
    description="Normalize metric names to valid identifiers",
    tags=(SYSTEM.METRICS.performance,),
)
async def normalize_metric_names(value: Annotated[str, "Metric name"], info: ValidationInfo) -> str:
    """
    Normalize metric names for consistency across monitoring systems.

    Operations:
    - Convert to lowercase
    - Replace invalid characters with underscores
    - Remove duplicate underscores
    - Ensure valid identifier
    """
    # Convert to lowercase
    normalized = value.lower()

    # Replace invalid characters with underscore
    normalized = re.sub(r"[^a-z0-9_]", "_", normalized)

    # Remove duplicate underscores
    normalized = re.sub(r"_+", "_", normalized)

    # Remove leading/trailing underscores
    normalized = normalized.strip("_")

    # Ensure it starts with a letter (prepend 'metric_' if not)
    if normalized and not normalized[0].isalpha():
        normalized = f"metric_{normalized}"

    # Default if empty
    if not normalized:
        normalized = "unknown_metric"

    return normalized

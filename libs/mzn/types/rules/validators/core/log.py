"""
Title         : log.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/rules/validators/core/log.py.

Description ----------- Domain-specific validators for the log package.

Provides validation rules for log levels, contexts, messages, and configurations.

"""

from __future__ import annotations

import re
from datetime import UTC, datetime
from typing import TYPE_CHECKING, Annotated, Any, TypeGuard, cast

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from collections.abc import Sequence

    from pydantic import ValidationInfo


@Build.validator(
    register_as=VALID.DOMAINS.LOG.is_valid_level_transition,
    error_template=(
        "Invalid level transition from {current} to {new}. De-escalation requires at least 2 levels difference."
    ),
    description="Validates log level transitions follow proper escalation rules",
    tags=(SYSTEM.LOGGING.level,),
)
async def is_valid_level_transition(
    value: Annotated[tuple[int, int], "Tuple of (current_level, new_level)"], info: ValidationInfo
) -> bool:
    """
    Ensure log level transitions follow proper escalation patterns.

    Rules: - Can always escalate to higher severity (lower number to higher) - De-escalation requires at least 20 points
    difference (2 levels) - This prevents confusing flows like ERROR→WARNING→ERROR

    """
    current, new = value

    # Always allow escalation
    if new >= current:
        return True

    # De-escalation requires significant gap
    return (current - new) >= 20  # At least 2 levels down


@Build.validator(
    register_as=VALID.DOMAINS.LOG.has_valid_context_depth,
    error_template="Context nesting too deep: {depth} levels (max 3)",
    description="Validates context dict doesn't nest too deeply",
    tags=(SYSTEM.LOGGING.context, SYSTEM.INFRA.performance),
)
async def has_valid_context_depth(
    value: Annotated[dict[str, Any], "Context dictionary to validate"], info: ValidationInfo
) -> bool:
    """
    Ensure context doesn't nest too deeply for performance and readability.

    Maximum depth of 3 levels prevents: - Stack overflow in recursive operations - Excessive JSON nesting - Poor log
    readability

    """

    def check_depth(obj: object, depth: int = 0) -> int:
        if not isinstance(obj, dict) or depth > 3:
            return depth

        max_depth = depth
        dict_obj = cast("dict[str, Any]", obj)  # We've already checked isinstance(obj, dict)
        for v in dict_obj.values():
            if isinstance(v, dict):
                max_depth = max(max_depth, check_depth(cast("dict[str, Any]", v), depth + 1))

        return max_depth

    return check_depth(value) <= 3


@Build.validator(
    register_as=VALID.DOMAINS.LOG.is_safe_for_output,
    error_template="Message contains unsafe characters: {unsafe_chars}",
    description="Validates message is safe for log output",
    tags=(SYSTEM.SECURITY, SYSTEM.LOGGING.output),
)
async def is_safe_for_output(value: Annotated[str, "Message string to validate"], info: ValidationInfo) -> bool:
    r"""
    Ensure log message is safe for output to various destinations.

    Checks for: - No null bytes (can truncate logs) - No ANSI escape sequences (unless handler supports) - No unescaped
    control characters (except \n, \r, \t)

    """
    # Check for null bytes
    if "\0" in value:
        return False

    # Check for ANSI escape sequences
    # Allow if handler explicitly supports color
    handler_supports_color = info.context and info.context.get("supports_color", False)
    if not handler_supports_color:
        ansi_pattern = re.compile(r"\x1b\[[0-9;]*m")
        if ansi_pattern.search(value):
            return False

    # Check for other control characters (0x00-0x1F) except common ones
    allowed_control = {"\n", "\r", "\t"}
    return all(not (ord(char) < 32 and char not in allowed_control) for char in value)


@Build.validator(
    register_as=VALID.DOMAINS.LOG.has_valid_handler_chain,
    error_template="Invalid handler configuration: {reason}",
    description="Validates handler configuration is coherent",
    tags=(SYSTEM.CONFIG, SYSTEM.LOGGING),
)
async def has_valid_handler_chain(
    value: Annotated[Sequence[dict[str, Any]], "List of handler configurations"], info: ValidationInfo
) -> bool:
    """
    Validate handler chain configuration for consistency.

    Checks: - No duplicate handler IDs - Compatible format/handler combinations - No circular batch handler references

    """
    handler_ids: set[str] = set()
    batch_targets: set[str] = set()

    for handler in value:
        handler_id = handler.get("handler_id")
        handler_type = handler.get("type")

        # Check for duplicates
        if handler_id is None:
            return False  # Handler must have an ID
        if handler_id in handler_ids:
            return False
        handler_ids.add(handler_id)

        # Validate format compatibility
        if handler_type == "NULL" and handler.get("format") not in {None, "null"}:
            return False  # NULL handler shouldn't have format

        # Track batch handler targets
        if handler_type == "BATCH":
            target = handler.get("target_handler")
            if isinstance(target, str) and target:
                if target == handler_id:  # Self-reference
                    return False
                batch_targets.add(target)

    # Ensure batch targets exist
    return batch_targets.issubset(handler_ids)


@Build.validator(
    register_as=VALID.DOMAINS.LOG.has_valid_context_size,
    error_template="Context data too large: {size} bytes (max {max_size})",
    description="Validates total context size to prevent memory issues",
    tags=(SYSTEM.LOGGING.context, SYSTEM.INFRA.performance),
)
async def has_valid_context_size(
    value: Annotated[dict[str, Any], "Context dictionary to validate"], info: ValidationInfo
) -> bool:
    """
    Ensure context doesn't exceed reasonable size limits.

    Prevents: - Memory exhaustion from large contexts - Serialization performance issues - Network transmission problems

    """
    max_size = info.context.get("max_context_size", 100_000) if info.context else 100_000

    # Non-recursive approach: estimate size based on string representation
    # This avoids complex type recursion issues
    try:
        # Convert to string representation and measure
        str_repr = str(value)
        total_size = len(str_repr.encode("utf-8"))
    except (TypeError, UnicodeEncodeError):
        # Fallback to a rough estimate based on dict size
        total_size = len(value) * 256  # Assume average 256 bytes per key-value pair
    return total_size <= max_size


@Build.validator(
    register_as=VALID.DOMAINS.LOG.has_valid_rotation_config,
    error_template="Invalid rotation config: {reason}",
    description="Validates file rotation settings are coherent",
    tags=(SYSTEM.CONFIG, SYSTEM.LOGGING),
)
async def has_valid_rotation_config(
    value: Annotated[dict[str, Any], "Handler configuration"], info: ValidationInfo
) -> bool:
    """
    Validate file rotation configuration.

    Checks: - Max bytes is reasonable (not too small or large) - Backup count is reasonable - Rotation pattern is valid
    if time-based

    """
    handler_type = value.get("type")
    if handler_type != "FILE":
        return True  # Only validate for file handlers

    max_bytes = value.get("max_bytes")
    backup_count = value.get("backup_count", 0)

    # Validate size-based rotation
    if max_bytes is not None:
        # Minimum 1KB, maximum 1GB
        if max_bytes < 1024 or max_bytes > 1_073_741_824:
            return False

        # If rotating, need backups
        if max_bytes > 0 and backup_count == 0:
            return False

    # Validate time-based rotation pattern if present
    rotation_pattern = value.get("rotation_pattern")
    if rotation_pattern:
        valid_patterns = {"S", "M", "H", "D", "W0-6", "midnight"}
        if not any(rotation_pattern.startswith(p) for p in valid_patterns):
            return False

    return True


@Build.validator(
    register_as=VALID.DOMAINS.LOG.is_valid_correlation_id,
    error_template="Invalid correlation: trace_id and span_id must both be present or both absent",
    description="Validates trace/span ID correlation",
    tags=(SYSTEM.LOGGING.context,),
)
async def is_valid_correlation_id(
    value: Annotated[dict[str, Any], "Log record data"], info: ValidationInfo
) -> bool:
    """
    Validate correlation between trace and span IDs.

    Rules: - Both must be present or both must be absent - Cannot have span without trace

    """
    trace_id = value.get("trace_id")
    span_id = value.get("span_id")

    # Both present or both absent is valid
    if (trace_id is None) == (span_id is None):
        return True

    # Having span without trace is invalid
    return trace_id is not None


@Build.validator(
    register_as=VALID.DOMAINS.LOG.has_valid_batch_config,
    error_template="Invalid batch config: {reason}",
    description="Validates batch handler configuration",
    tags=(SYSTEM.CONFIG, SYSTEM.LOGGING, SYSTEM.INFRA.performance),
)
async def has_valid_batch_config(
    value: Annotated[dict[str, Any], "Batch handler configuration"], info: ValidationInfo
) -> bool:
    """
    Validate batch handler settings for performance.

    Checks: - Batch size is optimal for handler type - Flush interval is reasonable - Target handler exists and supports
    batching

    """
    handler_type = value.get("type")
    if handler_type != "BATCH":
        return True  # Only validate batch handlers

    batch_size = value.get("batch_size", 100)
    flush_interval = value.get("flush_interval", 1.0)
    target_handler = value.get("target_handler")

    # Validate batch size
    if batch_size < 1 or batch_size > 10_000:
        return False

    # Validate flush interval
    if flush_interval <= 0 or flush_interval > 300:  # Max 5 minutes
        return False

    # Must have target
    if not target_handler:
        return False

    # Get target handler info if available
    if info.context:
        all_handlers = info.context.get("all_handlers", {})
        target_info = all_handlers.get(target_handler, {})

        # NULL handler shouldn't be a batch target
        if target_info.get("type") == "NULL":
            return False

    return True


def _is_valid_strftime(format_str: str) -> TypeGuard[str]:
    """Type guard to check if string is valid strftime format."""
    try:
        _ = datetime.now(UTC).strftime(format_str)
    except (ValueError, TypeError):
        return False
    else:
        return True


@Build.validator(
    register_as=VALID.DOMAINS.LOG.is_valid_timestamp_format,
    error_template="Invalid timestamp format: {format}",
    description="Validates strftime format strings",
    tags=(SYSTEM.CONFIG, SYSTEM.LOGGING),
)
async def is_valid_timestamp_format(
    value: Annotated[str, "Timestamp format string"], info: ValidationInfo
) -> bool:
    """
    Validate timestamp format is valid strftime pattern.

    Common formats: - "%Y-%m-%d %H:%M:%S" - "%Y-%m-%dT%H:%M:%S.%fZ" - "%d/%b/%Y:%H:%M:%S %z"

    """
    return _is_valid_strftime(value)

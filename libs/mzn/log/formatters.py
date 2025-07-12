"""
Title         : formatters.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/log/formatters.py.

Description ----------- Structlog processors for event transformation and output formatting. Modern Python 3.13+ with
full type safety.

"""

from __future__ import annotations

import json
from typing import Any
from uuid import uuid4

from structlog.typing import EventDict, WrappedLogger  # noqa: TC002

from mzn.log.types import (
    LogContext,
    LogLevel,
    LogMessage,
    LogRecordID,
    TimestampUTC,
)


# --- Core Processors ----------------------------------------------------------


def validate_types_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> EventDict:
    """Validate all event data using our type system."""
    # Message validation - structlog uses 'event' key for main message
    if "event" in event_dict:
        event_dict["message"] = LogMessage(str(event_dict.pop("event")))

    # Level from method name
    event_dict["level"] = LogLevel[method_name.upper()]

    # Context validation - everything not standard becomes context
    standard_keys = {"message", "timestamp", "logger", "level", "record_id"}
    context_data = {k: v for k, v in event_dict.items() if k not in standard_keys}
    if context_data:
        event_dict["context"] = LogContext(context_data)
        # Remove from top level
        for key in context_data:
            del event_dict[key]
    else:
        event_dict["context"] = LogContext({})

    return event_dict


def add_metadata_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> EventDict:
    """Add standard metadata."""
    event_dict["timestamp"] = TimestampUTC.now()
    event_dict["record_id"] = LogRecordID(str(uuid4()))
    return event_dict


# --- Output Processors --------------------------------------------------------


def json_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:
    """JSON output processor - terminal processor that returns string."""
    data: dict[str, Any] = {
        "timestamp": event_dict["timestamp"].isoformat(),
        "level": event_dict["level"].name,
        "logger": str(event_dict.get("logger", "unknown")),
        "message": str(event_dict["message"]),
        "record_id": str(event_dict["record_id"]),
    }

    if event_dict["context"]:
        data["context"] = dict(event_dict["context"])

    return json.dumps(data, default=str)


def human_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:
    """Human-readable output processor."""
    parts: list[str] = [
        event_dict["timestamp"].strftime("%Y-%m-%d %H:%M:%S"),
        f"[{event_dict['level'].name:8}]",
        f"{event_dict.get('logger', 'unknown')}:",
        str(event_dict["message"]),
    ]

    formatted = " ".join(parts)

    if event_dict["context"]:
        context_parts = [f"{k}={v}" for k, v in dict(event_dict["context"]).items()]
        formatted += f" {{{', '.join(context_parts)}}}"

    return formatted


def rich_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:
    """Rich markup output processor."""
    level_color = event_dict["level"].metadata.get("color", "white")
    level_symbol = event_dict["level"].metadata.get("symbol", "●")

    parts: list[str] = [
        f"[dim]{event_dict['timestamp']:%H:%M:%S}[/dim]",
        f"[{level_color}]{level_symbol} {event_dict['level'].name:8}[/{level_color}]",
        str(event_dict["message"]),
    ]

    if event_dict["context"]:
        context_parts = [f"[dim]{k}[/dim]=[yellow]{v}[/yellow]" for k, v in dict(event_dict["context"]).items()]
        parts.append(f"[dim]{{[/dim]{' '.join(context_parts)}[dim]}}[/dim]")

    return " ".join(parts)


def report_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:
    """Report format output processor."""
    delimiter = "|"

    # Escape delimiter in message
    message = str(event_dict["message"]).replace(delimiter, f"\\{delimiter}")

    # Build fields
    fields = [
        event_dict["timestamp"].isoformat(),
        event_dict["level"].name,
        str(event_dict.get("logger", "unknown")),
        message,
        json.dumps(dict(event_dict["context"]), default=str) if event_dict["context"] else "",
    ]

    return delimiter.join(fields)


def compact_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:
    """Compact output processor."""
    level_initial = event_dict["level"].name[0]
    timestamp = event_dict["timestamp"].strftime("%H%M%S")
    return f"{timestamp} {level_initial} {event_dict['message']}"


def debug_output_processor(logger: WrappedLogger, method_name: str, event_dict: EventDict) -> str:  # noqa: PLR0912
    """Debug output processor with enhanced context."""
    parts = [
        f"[{event_dict['level'].name:>8}]",
        event_dict["timestamp"].strftime("%H:%M:%S.%f")[:-3],
    ]

    # Work with context copy
    ctx = dict(event_dict["context"]) if event_dict["context"] else {}

    # Add source location if available
    if "_source" in ctx:
        source = str(ctx.pop("_source"))
        source = source.rsplit("/", maxsplit=1)[-1]  # Compact path
        parts.append(f"{source}")

    # Add function name if available
    if "_function" in ctx:
        func = ctx.pop("_function")
        parts.append(f"in {func}()")

    # Add debug name if present
    if "_debug_name" in ctx:
        debug_name = ctx.pop("_debug_name")
        parts.append(f"[{debug_name}]")

    # Remove internal debug fields
    ctx.pop("_debug_start", None)

    # Add message
    parts.append(str(event_dict["message"]))

    # Format special debug fields
    if ctx:
        debug_fields: list[str] = []

        if "duration_ms" in ctx:
            duration_val = ctx.pop("duration_ms")
            if isinstance(duration_val, (int, float)):
                debug_fields.append(f"⏱ {duration_val:.1f}ms")

        if "elapsed_ms" in ctx:
            elapsed_val = ctx.pop("elapsed_ms")
            if isinstance(elapsed_val, (int, float)):
                debug_fields.append(f"⏱ {elapsed_val:.1f}ms")

        if "memory_delta_mb" in ctx:
            delta_val = ctx.pop("memory_delta_mb")
            if isinstance(delta_val, (int, float)):
                sign = "+" if delta_val >= 0 else ""
                debug_fields.append(f"💾 {sign}{delta_val:.1f}MB")

        if "trace_id" in ctx:
            trace_id = str(ctx.pop("trace_id"))
            debug_fields.append(f"🔗 {trace_id[:8]}...")

        if debug_fields:
            parts.append(" | ".join(debug_fields))
        if ctx:
            parts.append(f"| {ctx!r}")

    return " ".join(parts)


# --- Exports ------------------------------------------------------------------

__all__ = [
    "add_metadata_processor",
    "compact_output_processor",
    "debug_output_processor",
    "human_output_processor",
    "json_output_processor",
    "report_output_processor",
    "rich_output_processor",
    "validate_types_processor",
]

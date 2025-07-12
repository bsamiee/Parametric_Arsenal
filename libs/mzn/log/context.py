"""
Title         : context.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/log/context.py

Description
-----------
Context propagation using structlog's contextvars.
Simplified integration with native structlog features.
"""

from __future__ import annotations

import contextlib
import time
from typing import TYPE_CHECKING, Annotated, Any

import psutil
from structlog.contextvars import (
    bind_contextvars,
    clear_contextvars,
    get_contextvars,
    unbind_contextvars,
)

from mzn.log.types import LogContext


if TYPE_CHECKING:
    from collections.abc import AsyncGenerator
    from typing import Protocol

    class Logger(Protocol):
        """Logger protocol for type hints only."""

        async def debug(self, message: str, **context: Any) -> None: ...

# --- Context Management -------------------------------------------------------


async def get_context() -> LogContext:
    """Get current logging context from structlog."""
    return LogContext(get_contextvars())


async def set_context(context: Annotated[dict[str, Any], "Context data"]) -> None:
    """Set logging context using structlog."""
    clear_contextvars()
    _ = bind_contextvars(**context)


async def clear_context() -> None:
    """Clear logging context."""
    clear_contextvars()


@contextlib.asynccontextmanager
async def context(**kwargs: Annotated[Any, "Context values"]) -> AsyncGenerator[None]:
    """
    Add temporary context for logging.

    Example:
        async with context(request_id="123", user_id=456):
            await logger.info("Processing")  # Includes context
    """
    _ = bind_contextvars(**kwargs)
    try:
        yield
    finally:
        unbind_contextvars(*kwargs.keys())


@contextlib.asynccontextmanager
async def isolated_context(**kwargs: Annotated[Any, "Context values"]) -> AsyncGenerator[None]:
    """
    Set isolated context (ignores existing).

    Example:
        async with isolated_context(operation="cleanup"):
            await logger.info("Cleaning")  # Only has operation context
    """
    # Save current context
    saved_context = get_contextvars()
    clear_contextvars()
    _ = bind_contextvars(**kwargs)
    try:
        yield
    finally:
        # Restore saved context
        clear_contextvars()
        _ = bind_contextvars(**saved_context)


@contextlib.asynccontextmanager
async def debug_context(
    name: Annotated[str, "Debug operation name"],
    *,
    include_timing: Annotated[bool, "Track elapsed time"] = True,
    include_memory: Annotated[bool, "Track memory usage"] = False,
    logger: Annotated[Logger | None, "Logger to use for completion message"] = None,
) -> AsyncGenerator[None]:
    """
    Add debug context with automatic timing and memory tracking.

    Automatically logs completion with timing/memory info at DEBUG level if logger provided.

    Args:
        name: Name of the operation being debugged
        include_timing: Track and log elapsed time
        include_memory: Track and log memory usage delta
        logger: Optional logger for completion message (if None, no message logged)

    Example:
        logger = await Log.console("myapp").build()
        async with debug_context("data_processing", include_memory=True, logger=logger):
            data = await fetch_data()
            result = await process_data(data)
        # Automatically logs: "data_processing completed" with timing and memory
    """
    # Track start conditions
    start_time = time.perf_counter() if include_timing else None
    start_memory = psutil.Process().memory_info().rss if include_memory else None

    # Add debug context
    debug_info: dict[str, Any] = {"_debug_name": name}
    if start_time:
        debug_info["_debug_start"] = start_time

    async with context(**debug_info):
        try:
            yield
        finally:
            # Log completion with debug info if logger provided
            if logger is not None:
                completion_context: dict[str, Any] = {}
                if include_timing and start_time:
                    elapsed = time.perf_counter() - start_time
                    completion_context["elapsed_ms"] = elapsed * 1000

                if include_memory and start_memory:
                    current_memory = psutil.Process().memory_info().rss
                    memory_delta = (current_memory - start_memory) / 1024 / 1024
                    completion_context["memory_delta_mb"] = memory_delta

                await logger.debug(f"{name} completed", **completion_context)  # noqa: G004


# --- Exports ------------------------------------------------------------------

__all__ = [
    "clear_context",
    "context",
    "debug_context",
    "get_context",
    "isolated_context",
    "set_context",
]

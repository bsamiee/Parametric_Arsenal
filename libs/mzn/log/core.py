"""
Title         : core.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/log/core.py.

Description ----------- Logger implementation with native structlog integration. Modern Python 3.13+ with zero-tolerance
type safety.

"""

from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING, Annotated, Any, cast

import structlog
from structlog.typing import FilteringBoundLogger, Processor  # noqa: TC002

from mzn.log.formatters import add_metadata_processor, validate_types_processor
from mzn.log.types import LogContext, LoggerName, LogLevel, LogRecord


if TYPE_CHECKING:
    from mzn.log.handlers import Handler


class Logger:
    """Async logger built on structlog."""

    def __init__(
        self,
        name: Annotated[LoggerName, "Validated logger name"],
        *,
        level: Annotated[LogLevel, "Minimum log level"] = LogLevel.INFO,
        handlers: Annotated[list[Handler], "List of handlers"] | None = None,
        processors: Annotated[list[Processor], "Additional processors"] | None = None,
    ) -> None:
        """Initialize logger with structlog integration."""
        super().__init__()
        self._name = name
        self._level = level
        self._handlers = handlers or []

        # Build processor chain
        self._processors = [
            structlog.contextvars.merge_contextvars,
            add_metadata_processor,
            validate_types_processor,
            *(processors or []),
            self._emit_processor,
        ]

        # Configure structlog for this logger
        structlog.configure(
            processors=cast("list[Processor]", self._processors),
            wrapper_class=structlog.make_filtering_bound_logger(self._level.value),
            logger_factory=structlog.BytesLoggerFactory(),
            cache_logger_on_first_use=True,
        )

        self._logger: FilteringBoundLogger = structlog.get_logger(str(name))

    def _emit_processor(self, _logger: Any, _method_name: str, event_dict: dict[str, Any]) -> dict[str, Any]:  # noqa: ANN401
        """Terminal processor that emits to handlers."""
        # Create LogRecord from event_dict
        record = LogRecord(
            timestamp=event_dict["timestamp"],
            logger_name=self._name,
            level=event_dict["level"],
            message=event_dict["message"],
            record_id=event_dict["record_id"],
            context=event_dict.get("context", LogContext({})),
        )

        # Schedule async emission
        _ = asyncio.create_task(self._emit_to_handlers(record))  # noqa: RUF006

        # Return event_dict for potential further processing
        return event_dict

    async def _emit_to_handlers(self, record: LogRecord) -> None:
        """Emit to all enabled handlers."""
        for handler in self._handlers:
            if handler.is_enabled_for(record.level):
                await handler.emit(record)

    @property
    def name(self) -> LoggerName:
        """Logger name."""
        return self._name

    @property
    def level(self) -> LogLevel:
        """Minimum log level for this logger."""
        return self._level

    @level.setter
    def level(self, value: Annotated[LogLevel, "New log level"]) -> None:
        """Update minimum log level."""
        self._level = value
        # Reconfigure with new level
        structlog.configure(
            processors=cast("list[Processor]", self._processors),
            wrapper_class=structlog.make_filtering_bound_logger(value.value),
            logger_factory=structlog.BytesLoggerFactory(),
            cache_logger_on_first_use=True,
        )

    @property
    def handlers(self) -> list[Handler]:
        """Logger handlers."""
        return self._handlers

    def is_enabled_for(self, level: Annotated[LogLevel, "Level to check"]) -> bool:
        """Zero-cost check if logging is enabled for level."""
        return level.value >= self._level.value

    async def log(
        self,
        level: Annotated[LogLevel, "Log level"],
        message: Annotated[str, "Message to log"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log with structlog processing."""
        # Add logger name to event dict
        context["logger"] = str(self._name)

        # Use structlog's sync API - it handles the processor chain
        method = getattr(self._logger, level.name.lower())
        method(message, **context)

    async def debug(
        self,
        message: Annotated[str, "Debug message"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log debug message."""
        await self.log(LogLevel.DEBUG, message, **context)

    async def info(
        self,
        message: Annotated[str, "Info message"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log info message."""
        await self.log(LogLevel.INFO, message, **context)

    async def warning(
        self,
        message: Annotated[str, "Warning message"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log warning message."""
        await self.log(LogLevel.WARNING, message, **context)

    async def error(
        self,
        message: Annotated[str, "Error message"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log error message."""
        await self.log(LogLevel.ERROR, message, **context)

    async def critical(
        self,
        message: Annotated[str, "Critical message"],
        **context: Annotated[Any, "Additional context"],
    ) -> None:
        """Log critical message."""
        await self.log(LogLevel.CRITICAL, message, **context)

    async def add_handler(self, handler: Annotated[Handler, "Handler to add"]) -> None:
        """Add handler to logger."""
        if handler not in self._handlers:
            self._handlers.append(handler)

    async def remove_handler(self, handler: Annotated[Handler, "Handler to remove"]) -> None:
        """Remove handler from logger."""
        if handler in self._handlers:
            self._handlers.remove(handler)

    async def close(self) -> None:
        """Close all handlers."""
        for handler in self._handlers:
            await handler.close()


# --- Exports ------------------------------------------------------------------

__all__ = [
    "Logger",
]

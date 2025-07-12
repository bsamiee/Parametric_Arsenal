"""
Title         : namespace.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/log/namespace.py

Description
-----------
Streamlined Log namespace with integrated functionality.
Single export pattern with modern Python 3.13+ async design.
"""

from __future__ import annotations

import inspect
import sys
import time
from functools import wraps
from typing import TYPE_CHECKING, Annotated, Any, ClassVar, Self, TypeVar, override

import psutil
from beartype import beartype
from opentelemetry import trace

from mzn.errors.namespace import Error
from mzn.log.context import (
    clear_context,
    context,
    debug_context,
    get_context,
    isolated_context,
    set_context,
)
from mzn.log.core import Logger
from mzn.log.exceptions import ConfigError, FormatError, HandlerError, LogError
from mzn.log.formatters import (
    compact_output_processor,
    debug_output_processor,
    human_output_processor,
    json_output_processor,
    report_output_processor,
    rich_output_processor,
)
from mzn.log.handlers import ConsoleHandler, FileHandler, Handler, NullHandler
from mzn.log.types import (
    FilePath,
    LogContext,
    LoggerName,
    LogLevel,
    LogMessage,
    LogRecord,
    OutputFormat,
    OutputTarget,
)


if TYPE_CHECKING:
    from collections.abc import Awaitable, Callable

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Log Builder --------------------------------------------------------------


class LogBuilder:
    """
    Fluent builder for logger configuration.

    Provides a chainable API for configuring logger instances
    with validation at each step.
    """

    def __init__(
        self,
        output_target: Annotated[OutputTarget, "Output destination type"],
        name: Annotated[str, "Logger instance name"],
    ) -> None:
        """Initialize builder with output target and name.

        Args:
            output_target: Where logs should be output (console, file, both, null)
            name: Unique identifier for the logger instance
        """
        super().__init__()
        self._output_target = output_target
        self._name = LoggerName(name)
        self._level: LogLevel = LogLevel.INFO
        self._format: OutputFormat = OutputFormat.HUMAN
        self._file_path: FilePath | None = None

    @beartype
    def level(self, log_level: Annotated[LogLevel, "Minimum log level"]) -> Self:
        """Set minimum log level.

        Args:
            log_level: Minimum level to process (DEBUG, INFO, WARNING, ERROR, CRITICAL)

        Returns:
            Builder instance for method chaining
        """
        self._level = log_level
        return self

    @beartype
    def path(self, file_path: Annotated[str, "File path for log output"]) -> Self:
        """Set log file path.

        Args:
            file_path: Absolute path to log file

        Returns:
            Builder instance for method chaining
        """
        self._file_path = FilePath(file_path)
        return self

    # Convenience methods for log levels
    def debug(self) -> Self:
        """Set log level to DEBUG with debug format.

        Returns:
            Builder instance for method chaining
        """
        self._level = LogLevel.DEBUG
        self._format = OutputFormat.DEBUG
        return self

    def warning(self) -> Self:
        """Set log level to WARNING.

        Returns:
            Builder instance for method chaining
        """
        self._level = LogLevel.WARNING
        return self

    def error(self) -> Self:
        """Set log level to ERROR.

        Returns:
            Builder instance for method chaining
        """
        self._level = LogLevel.ERROR
        return self

    def critical(self) -> Self:
        """Set log level to CRITICAL.

        Returns:
            Builder instance for method chaining
        """
        self._level = LogLevel.CRITICAL
        return self

    # Convenience methods for formats
    def json(self) -> Self:
        """Use JSON output format.

        Returns:
            Builder instance for method chaining
        """
        self._format = OutputFormat.JSON
        return self

    def human(self) -> Self:
        """Use human-readable format.

        Returns:
            Builder instance for method chaining
        """
        self._format = OutputFormat.HUMAN
        return self

    def rich(self) -> Self:
        """Use Rich console format with colors.

        Returns:
            Builder instance for method chaining
        """
        self._format = OutputFormat.RICH
        return self

    def report(self) -> Self:
        """Use detailed report format.

        Returns:
            Builder instance for method chaining
        """
        self._format = OutputFormat.REPORT
        return self

    def compact(self) -> Self:
        """Use compact space-efficient format.

        Returns:
            Builder instance for method chaining
        """
        self._format = OutputFormat.COMPACT
        return self

    @beartype
    async def build(self) -> Logger:
        """Build the configured logger instance.

        Returns:
            Fully configured and initialized logger instance
        """
        # Create output processor based on format type
        output_processor = self._create_output_processor()

        # Create handlers based on output target
        handlers: list[Handler] = []

        if self._output_target in {OutputTarget.CONSOLE, OutputTarget.BOTH}:
            console_handler = ConsoleHandler(
                level=self._level,
                output_processor=output_processor,
            )
            handlers.append(console_handler)

        if self._output_target in {OutputTarget.FILE, OutputTarget.BOTH}:
            if not self._file_path:
                error = Error.create("log.config_invalid", message="File path required for file output")
                raise ConfigError(error.context)
            file_handler = FileHandler(
                self._file_path,
                level=self._level,
                output_processor=output_processor,
            )
            handlers.append(file_handler)

        if self._output_target == OutputTarget.NULL:
            handlers.append(NullHandler(level=self._level))

        # Create base logger
        base_logger = Logger(
            self._name,
            level=self._level,
            handlers=handlers,
        )

        # Apply debug enhancements if DEBUG format
        logger = self._create_debug_logger(base_logger) if self._format == OutputFormat.DEBUG else base_logger

        # Register the logger
        return await Log.create_from_config(str(self._name), logger)

    def _create_output_processor(self) -> Any:  # noqa: ANN401
        """Create appropriate output processor for the configured format type."""
        processors: dict[OutputFormat, Any] = {
            OutputFormat.JSON: json_output_processor,
            OutputFormat.HUMAN: human_output_processor,
            OutputFormat.RICH: rich_output_processor,
            OutputFormat.REPORT: report_output_processor,
            OutputFormat.COMPACT: compact_output_processor,
            OutputFormat.DEBUG: debug_output_processor,
        }
        return processors.get(self._format, human_output_processor)

    @staticmethod
    def _create_debug_logger(base_logger: Logger) -> Logger:
        """Create enhanced debug logger with automatic context enrichment."""

        class DebugLogger(Logger):
            """Logger wrapper that adds debug context."""

            def __init__(self, wrapped: Logger) -> None:
                # Copy essential attributes from wrapped logger
                super().__init__(wrapped.name, level=wrapped.level, handlers=wrapped.handlers)
                self._wrapped = wrapped

            @override
            async def log(
                self,
                level: LogLevel,
                message: str,
                **context: Any,
            ) -> None:
                # Add trace context for debug logging
                span = trace.get_current_span()
                if span:
                    span_ctx = span.get_span_context()
                    if span_ctx.trace_id:
                        context["trace_id"] = format(span_ctx.trace_id, "032x")
                        context["span_id"] = format(span_ctx.span_id, "016x")

                # Add source info for DEBUG level
                if level == LogLevel.DEBUG:
                    # Skip wrapper frames to get actual caller
                    frame = inspect.currentframe()
                    if frame and frame.f_back and frame.f_back.f_back:
                        caller_frame = frame.f_back.f_back
                        context["_source"] = f"{caller_frame.f_code.co_filename}:{caller_frame.f_lineno}"
                        context["_function"] = caller_frame.f_code.co_name

                # Call parent's log method
                await super().log(level, message, **context)

        return DebugLogger(base_logger)


# --- Log Class ----------------------------------------------------------------


class Log:
    """
    Unified namespace for all logging functionality.

    Provides intelligent, async-first logging with multiple outputs
    and a clean, simple API.

    Example:
        # Fluent API
        logger = await Log.console("app").json().info().build()
        logger = await Log.file("audit").report().path("/logs/audit.log").build()
        logger = await Log.both("debug-app").debug().rich().auto_trace().build()

        # Context manager
        async with Log.console("temp").json().build() as logger:
            await logger.info("Processing...")

        # Use decorator
        @Log.logged
        async def expensive_operation(x: int) -> str:
            return f"Result: {x}"
    """

    # --- Core Implementation --------------------------------------------------
    _loggers: ClassVar[dict[str, Logger]] = {}

    # --- Types ----------------------------------------------------------------
    Level = LogLevel
    Format = OutputFormat
    Target = OutputTarget
    Name = LoggerName
    Message = LogMessage
    Context = LogContext
    Record = LogRecord

    # --- Exceptions -----------------------------------------------------------
    Error = LogError
    ConfigError = ConfigError
    HandlerError = HandlerError
    FormatError = FormatError

    # --- Context Managers -----------------------------------------------------
    context = staticmethod(context)
    debug_context = staticmethod(debug_context)
    isolated_context = staticmethod(isolated_context)

    # --- Context Functions ----------------------------------------------------
    get_context = staticmethod(get_context)
    set_context = staticmethod(set_context)
    clear_context = staticmethod(clear_context)

    # --- Base Classes for Extensibility ---------------------------------------
    Handler = Handler

    # --- Fluent Factory Methods ----------------------------------------------

    @classmethod
    @beartype
    def console(cls, name: Annotated[str, "Logger instance identifier"]) -> LogBuilder:
        """Create fluent builder for console logging.

        Args:
            name: Unique logger instance identifier

        Returns:
            LogBuilder configured for console output
        """
        return LogBuilder(OutputTarget.CONSOLE, name)

    @classmethod
    @beartype
    def file(cls, name: Annotated[str, "Logger instance identifier"]) -> LogBuilder:
        """Create fluent builder for file logging.

        Args:
            name: Unique logger instance identifier

        Returns:
            LogBuilder configured for file output
        """
        return LogBuilder(OutputTarget.FILE, name)

    @classmethod
    @beartype
    def both(cls, name: Annotated[str, "Logger instance identifier"]) -> LogBuilder:
        """Create fluent builder for console and file logging.

        Args:
            name: Unique logger instance identifier

        Returns:
            LogBuilder configured for both console and file output
        """
        return LogBuilder(OutputTarget.BOTH, name)

    @classmethod
    @beartype
    def null(cls, name: Annotated[str, "Logger instance identifier"]) -> LogBuilder:
        """Create fluent builder for null logging (discard all output).

        Args:
            name: Unique logger instance identifier

        Returns:
            LogBuilder configured to discard all output
        """
        return LogBuilder(OutputTarget.NULL, name)

    # --- Internal Factory Method ------------------------------------------------

    @classmethod
    async def create_from_config(
        cls, name: Annotated[str, "Logger instance name"], logger: Annotated[Logger, "Configured logger instance"]
    ) -> Logger:
        """Create logger from pre-configured instance.

        Args:
            name: Unique logger instance identifier
            logger: Pre-configured logger instance

        Returns:
            Logger instance registered in cache
        """
        # Check if already cached
        if existing := cls._loggers.get(name):
            # Warn if configuration differs
            if existing.level != logger.level:
                warning_msg = (
                    f"Warning: Logger '{name}' already exists with level {existing.level.name}, "
                    f"ignoring new level {logger.level.name}"
                )
                _ = sys.stderr.write(f"{warning_msg}\n")
            return existing

        # Store in registry
        cls._loggers[name] = logger
        return logger

    @classmethod
    @beartype
    def get(cls, name: Annotated[str, "Logger instance identifier"]) -> Logger | None:
        """Get an existing logger by name.

        Args:
            name: Logger instance identifier

        Returns:
            Logger instance if found, None otherwise
        """
        return cls._loggers.get(name)

    @classmethod
    @beartype
    async def close(cls, name: Annotated[str, "Logger instance identifier"]) -> bool:
        """Close and remove a logger instance.

        Args:
            name: Logger instance identifier

        Returns:
            True if logger was closed, False if not found
        """
        if name in cls._loggers:
            logger = cls._loggers[name]
            await logger.close()
            del cls._loggers[name]
            return True
        return False

    @classmethod
    @beartype
    async def close_all(cls) -> None:
        """Close all logger instances.

        Closes all cached instances and clears the registry.
        Useful for cleanup in tests or application shutdown.
        """
        for logger in cls._loggers.values():
            await logger.close()
        cls._loggers.clear()

    # --- Decorator ------------------------------------------------------------

    @staticmethod
    def logged(  # noqa: PLR0913, PLR0915
        func: Callable[..., Awaitable[T]] | None = None,
        *,
        level: Annotated[LogLevel, "Log level for calls"] = LogLevel.INFO,
        logger_name: Annotated[str | None, "Override logger name"] = None,
        include_args: Annotated[bool, "Log function arguments"] = True,
        include_result: Annotated[bool, "Log function result"] = True,
        include_timing: Annotated[bool | None, "Add elapsed time to logs (auto for DEBUG)"] = None,
        include_memory: Annotated[bool | None, "Add memory usage to logs (auto for DEBUG)"] = None,
        include_trace: Annotated[bool | None, "Add OpenTelemetry trace context (auto for DEBUG)"] = None,
    ) -> Callable[..., Awaitable[T]] | Callable[[Callable[..., Awaitable[T]]], Callable[..., Awaitable[T]]]:
        """
        Decorator for automatic async function logging with debug features.

        Can be used with or without parentheses:
            @Log.logged
            async def func(): ...

            @Log.logged(level=LogLevel.DEBUG)  # Auto-enables debug features
            async def func(): ...

        Note: Only supports async functions.

        Args:
            func: Async function to decorate (when used without parentheses)
            level: Log level for function calls
            logger_name: Override logger name (default: function module)
            include_args: Include function arguments in logs
            include_result: Include function result in logs
            include_timing: Add elapsed time in milliseconds (None = auto for DEBUG)
            include_memory: Add memory usage delta in MB (None = auto for DEBUG)
            include_trace: Add OpenTelemetry trace context (None = auto for DEBUG)

        Returns:
            Decorated async function or decorator
        """

        def decorator(fn: Callable[..., Awaitable[T]]) -> Callable[..., Awaitable[T]]:  # noqa: PLR0915
            @wraps(fn)
            async def wrapper(*args: Any, **kwargs: Any) -> T:  # noqa: PLR0912, PLR0914
                # Auto-enable debug features when level is DEBUG
                is_debug = level == LogLevel.DEBUG
                timing_enabled = include_timing if include_timing is not None else is_debug
                memory_enabled = include_memory if include_memory is not None else is_debug
                trace_enabled = include_trace if include_trace is not None else is_debug

                # Get or create logger
                name = logger_name or fn.__module__
                # Try to reuse existing logger if level is compatible
                existing = Log.get(name)
                if existing and existing.level <= level:
                    logger = existing
                else:
                    logger = await Log.console(name).level(level).build()

                # Build context
                context: dict[str, Any] = {"function": fn.__name__}
                if include_args and (args or kwargs):
                    if args:
                        context["args"] = args
                    if kwargs:
                        context["kwargs"] = kwargs

                # Add trace context if requested
                if trace_enabled:
                    span = trace.get_current_span()
                    if span:
                        span_ctx = span.get_span_context()
                        context["trace_id"] = format(span_ctx.trace_id, "032x")
                        context["span_id"] = format(span_ctx.span_id, "016x")

                # Track timing and memory if requested
                start_time: float | None = time.perf_counter() if timing_enabled else None
                start_memory: int | None = psutil.Process().memory_info().rss if memory_enabled else None

                call_msg = f"Calling {fn.__name__}"
                await logger.log(level, call_msg, **context)

                try:
                    result = await fn(*args, **kwargs)
                except Exception as e:
                    error_msg = f"Error in {fn.__name__}"
                    error_context: dict[str, Any] = {"exception": str(e), "exc_info": str(e)}

                    # Add debug info to error if requested
                    if timing_enabled and start_time is not None:
                        error_context["duration_ms"] = (time.perf_counter() - start_time) * 1000
                    if memory_enabled and start_memory is not None:
                        current_memory = psutil.Process().memory_info().rss
                        error_context["memory_delta_mb"] = (current_memory - start_memory) / 1024 / 1024

                    await logger.error(error_msg, **error_context)  # noqa: TRY400
                    raise
                else:
                    if include_result or timing_enabled or memory_enabled:
                        complete_context: dict[str, Any] = {}

                        if include_result:
                            complete_context["result"] = result

                        if timing_enabled and start_time is not None:
                            complete_context["duration_ms"] = (time.perf_counter() - start_time) * 1000

                        if memory_enabled and start_memory is not None:
                            current_memory = psutil.Process().memory_info().rss
                            complete_context["memory_delta_mb"] = (current_memory - start_memory) / 1024 / 1024

                        complete_msg = f"Completed {fn.__name__}"
                        await logger.log(level, complete_msg, **complete_context)

                    return result

            return wrapper

        # Handle usage with/without parentheses
        if func is None:
            return decorator
        return decorator(func)


# --- Exports ------------------------------------------------------------------

__all__ = ["Log"]

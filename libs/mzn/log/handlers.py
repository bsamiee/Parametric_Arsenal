"""
Title         : handlers.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/log/handlers.py

Description
-----------
Simplified handler implementations with format selection.
Modern Python 3.13+ async-first design.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from pathlib import Path
from typing import TYPE_CHECKING, Annotated, TextIO, override

import aiofiles
import aiofiles.os
from rich.console import Console

from mzn.errors.namespace import Error
from mzn.log.exceptions import HandlerError
from mzn.log.types import FilePath, LogLevel, LogRecord


if TYPE_CHECKING:
    from structlog.typing import Processor

# --- Base Handler -------------------------------------------------------------


class Handler(ABC):
    """Base handler that receives LogRecord objects."""

    def __init__(
        self,
        *,
        level: Annotated[LogLevel, "Minimum log level"] = LogLevel.NOTSET,
        output_processor: Annotated[Processor | None, "Output processor"] = None,
    ) -> None:
        """Initialize handler with level and output processor."""
        super().__init__()
        self._level = level
        self._output_processor = output_processor

    @property
    def level(self) -> LogLevel:
        """Handler's minimum log level."""
        return self._level

    def is_enabled_for(self, level: Annotated[LogLevel, "Level to check"]) -> bool:
        """Check if handler processes this level."""
        if self._level == LogLevel.NOTSET:
            return True
        return level.value >= self._level.value

    def format(self, record: Annotated[LogRecord, "Record to format"]) -> str:
        """Format record using output processor if available."""
        if self._output_processor:
            # Convert LogRecord to event_dict for processor
            event_dict = {
                "timestamp": record.timestamp,
                "level": record.level,
                "logger": record.logger_name,
                "message": record.message,
                "record_id": record.record_id,
                "context": record.context,
            }
            result = self._output_processor(None, record.level.name.lower(), event_dict)
            return str(result)
        # Default format
        return f"{record.timestamp} [{record.level.name}] {record.logger_name}: {record.message}"

    @abstractmethod
    async def emit(self, record: Annotated[LogRecord, "Record to emit"]) -> None:
        """Emit the log record."""
        ...

    async def close(self) -> None:  # noqa: B027
        """Close handler and release resources."""
        # Default implementation does nothing

# --- Console Handler ----------------------------------------------------------


class ConsoleHandler(Handler):
    """Console output using Rich."""

    def __init__(
        self,
        *,
        level: Annotated[LogLevel, "Minimum log level"] = LogLevel.NOTSET,
        output_processor: Annotated[Processor | None, "Output processor"] = None,
        stream: Annotated[TextIO | None, "Output stream"] = None,
    ) -> None:
        """Initialize console handler with Rich console."""
        super().__init__(level=level, output_processor=output_processor)
        # Rich console handles stderr/stdout routing based on level automatically
        self._console = Console(file=stream)

    @override
    async def emit(self, record: Annotated[LogRecord, "Record to emit"]) -> None:
        """Emit to console using Rich."""
        formatted = self.format(record)
        style = self._get_rich_style(record.level)
        self._console.print(formatted, style=style)

    @staticmethod
    def _get_rich_style(level: Annotated[LogLevel, "Log level"]) -> str:
        """Get Rich style from level metadata."""
        return str(level.metadata.get("color", "white"))

# --- File Handler -------------------------------------------------------------


class FileHandler(Handler):
    """File output with async I/O and rotation support."""

    def __init__(
        self,
        filename: Annotated[FilePath | str, "Output file path"],
        *,
        level: Annotated[LogLevel, "Minimum log level"] = LogLevel.NOTSET,
        output_processor: Annotated[Processor | None, "Output processor"] = None,
        max_bytes: Annotated[int, "Max file size before rotation"] = 10_485_760,  # 10MB
        backup_count: Annotated[int, "Number of backup files"] = 5,
    ) -> None:
        """Initialize file handler with rotation settings."""
        super().__init__(level=level, output_processor=output_processor)
        self._filename = FilePath(str(filename)) if isinstance(filename, str) else filename
        self._max_bytes = max_bytes
        self._backup_count = backup_count
        self._current_size = 0
        self._initialized = False

    async def _ensure_initialized(self) -> None:
        """Ensure file exists and get current size."""
        if not self._initialized:
            try:
                path = Path(str(self._filename))
                if path.exists():
                    stat = await aiofiles.os.stat(path)
                    self._current_size = stat.st_size
                else:
                    # Ensure parent directory exists
                    path.parent.mkdir(parents=True, exist_ok=True)
                    self._current_size = 0
                self._initialized = True
            except OSError as e:
                error = Error.create("log.file_init_failed", filename=str(self._filename))
                raise HandlerError(error.context) from e

    @override
    async def emit(self, record: Annotated[LogRecord, "Record to emit"]) -> None:
        """Emit to file with rotation check."""
        try:
            await self._ensure_initialized()

            formatted = self.format(record) + "\n"
            message_bytes = formatted.encode("utf-8")

            # Check if rotation needed
            if self._current_size + len(message_bytes) > self._max_bytes:
                await self._rotate()

            # Write to file
            async with aiofiles.open(str(self._filename), mode="a", encoding="utf-8") as f:
                _ = await f.write(formatted)

            self._current_size += len(message_bytes)
        except OSError as e:
            error = Error.create("log.file_write_failed", filename=str(self._filename))
            raise HandlerError(error.context) from e

    async def _rotate(self) -> None:
        """Rotate log files."""
        try:
            base_path = Path(str(self._filename))

            # Remove oldest backup if at limit
            oldest = base_path.with_suffix(f".{self._backup_count}")
            if oldest.exists():
                await aiofiles.os.remove(oldest)

            # Rotate existing backups
            for i in range(self._backup_count - 1, 0, -1):
                src = base_path.with_suffix(f".{i}")
                dst = base_path.with_suffix(f".{i + 1}")
                if src.exists():
                    await aiofiles.os.rename(src, dst)

            # Move current file to .1
            if base_path.exists():
                await aiofiles.os.rename(base_path, base_path.with_suffix(".1"))

            self._current_size = 0
        except OSError as e:
            error = Error.create("log.rotation_failed", filename=str(self._filename))
            raise HandlerError(error.context) from e

# --- Null Handler -------------------------------------------------------------


class NullHandler(Handler):
    """Handler that discards all records."""

    @override
    async def emit(self, record: Annotated[LogRecord, "Record to discard"]) -> None:
        """Discard the record."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ConsoleHandler",
    "FileHandler",
    "Handler",
    "NullHandler",
]

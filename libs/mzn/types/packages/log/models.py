"""
Title         : models.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/log/models.py.

Description ----------- Pydantic models for the log package.

Provides structured models for log records, configurations, and statistics. All models use validated types from the
aliases module.

"""

from __future__ import annotations

import uuid
from typing import TYPE_CHECKING

from pydantic import BaseModel, Field

from mzn.types._core.core_builders import Build
from mzn.types.packages.log.aliases import (
    LogContext,
    LoggerName,
    LogMessage,
    LogRecordID,
)


if TYPE_CHECKING:
    from mzn.types.packages.general.aliases import TimestampUTC
    from mzn.types.packages.log.enums import LogLevel


@Build.model(
    description="Immutable structured log record",
    model_config={"frozen": True},  # Immutable for thread safety
)
class LogRecord(BaseModel):
    """
    Structured log record with all metadata.

    Frozen to ensure thread safety and prevent accidental modification.

    """

    # Required fields
    timestamp: TimestampUTC = Field(description="When the log was created")
    logger_name: LoggerName = Field(description="Hierarchical logger name")
    level: LogLevel = Field(description="Log severity level")
    message: LogMessage = Field(description="The log message")

    # Fields with defaults
    record_id: LogRecordID = Field(
        default_factory=lambda: LogRecordID(str(uuid.uuid4())),
        description="Unique record identifier"
    )
    context: LogContext = Field(
        default_factory=lambda: LogContext({}),
        description="Additional context data"
    )

    # Exception information
    exception: dict[str, object] | None = Field(default=None, description="Formatted exception info")

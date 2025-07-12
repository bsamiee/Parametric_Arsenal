from typing import Any, ClassVar

from pydantic import BaseModel, ConfigDict

from mzn.types.packages.general.aliases import TimestampUTC
from mzn.types.packages.log.aliases import (
    LogContext,
    LoggerName,
    LogMessage,
    LogRecordID,
)
from mzn.types.packages.log.enums import (
    LogLevel,
)

class LogRecord(BaseModel):
    model_config: ClassVar[ConfigDict] = {"frozen": True}

    # Required fields
    timestamp: TimestampUTC
    logger_name: LoggerName
    level: LogLevel
    message: LogMessage

    # Fields with defaults (NOT optional)
    record_id: LogRecordID
    context: LogContext

    # Exception information
    exception: dict[str, Any] | None = None

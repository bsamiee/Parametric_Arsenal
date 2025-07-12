from typing import Any, Self, overload, override

from aenum import IntEnum, StrEnum

class LogLevel(IntEnum):
    NOTSET: LogLevel
    DEBUG: LogLevel
    INFO: LogLevel
    WARNING: LogLevel
    ERROR: LogLevel
    CRITICAL: LogLevel

    # IntEnum constructor support with proper type narrowing
    @overload
    def __new__(cls, value: int) -> Self: ...
    @overload
    def __new__(cls, value: LogLevel) -> Self: ...

    # Rich enum functionality from MZN type system
    @property
    def metadata(self) -> dict[str, Any]: ...

    # Explicit IntEnum operations for mypy
    def __int__(self) -> int: ...
    def __lt__(self, other: LogLevel | int) -> bool: ...
    def __le__(self, other: LogLevel | int) -> bool: ...
    def __gt__(self, other: LogLevel | int) -> bool: ...
    def __ge__(self, other: LogLevel | int) -> bool: ...
    @override
    def __eq__(self, other: object) -> bool: ...
    @override
    def __ne__(self, other: object) -> bool: ...

class OutputFormat(StrEnum):
    JSON: OutputFormat
    HUMAN: OutputFormat
    RICH: OutputFormat
    REPORT: OutputFormat
    COMPACT: OutputFormat
    DEBUG: OutputFormat
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...
    @property
    def metadata(self) -> dict[str, Any]: ...

class OutputTarget(StrEnum):
    CONSOLE: OutputTarget
    FILE: OutputTarget
    BOTH: OutputTarget
    NULL: OutputTarget
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...
    @property
    def metadata(self) -> dict[str, Any]: ...

__all__ = [
    "LogLevel",
    "OutputFormat",
    "OutputTarget",
]

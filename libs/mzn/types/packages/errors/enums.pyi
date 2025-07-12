from aenum import IntEnum, StrEnum

class ErrorSeverity(IntEnum):
    DEBUG: ErrorSeverity
    INFO: ErrorSeverity
    WARNING: ErrorSeverity
    ERROR: ErrorSeverity
    CRITICAL: ErrorSeverity
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

class ErrorCategory(StrEnum):
    VALIDATION: ErrorCategory
    CONFIGURATION: ErrorCategory
    RUNTIME: ErrorCategory
    RESOURCE: ErrorCategory
    EXTERNAL: ErrorCategory
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

__all__ = [
    "ErrorCategory",
    "ErrorSeverity",
]

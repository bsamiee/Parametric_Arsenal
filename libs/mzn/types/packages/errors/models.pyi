
from pydantic import BaseModel

from mzn.types.packages.errors.aliases import ErrorCode, ErrorMessage, RecoveryHint
from mzn.types.packages.errors.enums import ErrorCategory, ErrorSeverity
from mzn.types.packages.general.aliases import RequestID

class ErrorContext(BaseModel):
    code: ErrorCode
    message: ErrorMessage
    severity: ErrorSeverity
    category: ErrorCategory
    recovery_hint: RecoveryHint | None
    request_id: RequestID | None

    def format(self, *, details: bool = False) -> str: ...

__all__ = [
    "ErrorContext",
]

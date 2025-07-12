"""
Title         : models.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/errors/models.py.

Description ----------- Minimal error models for the new domain-aware error system.

"""

from __future__ import annotations

from typing import TYPE_CHECKING

from pydantic import BaseModel

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


if TYPE_CHECKING:
    from mzn.types.packages.errors.aliases import ErrorCode, ErrorMessage, RecoveryHint
    from mzn.types.packages.errors.enums import ErrorCategory, ErrorSeverity
    from mzn.types.packages.general.aliases import RequestID


# --- Error Context ------------------------------------------------------------


@Build.model(
    description="Complete error context with message and recovery information.",
    tags=(SYSTEM.ERROR,),
    model_config={"extra": "allow"},  # Allow arbitrary fields for flexibility
)
class ErrorContext(BaseModel):
    """
    Complete error context for tracking and presentation.

    This single model replaces ErrorContext + ErrorReport from the old system. The 'extra' config allows domains to add
    their own context fields.

    """

    # Core error information
    code: ErrorCode
    message: ErrorMessage
    severity: ErrorSeverity
    category: ErrorCategory

    # Optional recovery guidance
    recovery_hint: RecoveryHint | None = None

    # Optional tracking
    request_id: RequestID | None = None

    def format(self, *, details: bool = False) -> str:
        """Format error for display."""
        lines = [f"[{self.severity.name}] {self.code}: {self.message}"]

        if self.recovery_hint:
            lines.append(f"  → {self.recovery_hint}")

        if details:
            # Show any extra fields
            extra = self.model_dump(exclude={"code", "message", "severity", "category", "recovery_hint"})
            for key, value in extra.items():
                if value is not None:
                    lines.append(f"  {key}: {value}")

        return "\n".join(lines)


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ErrorContext",
]

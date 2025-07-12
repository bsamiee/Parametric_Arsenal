"""
Title         : exceptions.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/errors/exceptions.py

Description
-----------
Core exception class for the error system.
Simple wrapper around ErrorContext with direct property access.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, override


if TYPE_CHECKING:
    from mzn.errors.types import ErrorCategory, ErrorContext, ErrorSeverity


class MznError(Exception):
    """
    Base exception for all MZN errors.

    Simple wrapper around ErrorContext that provides direct access
    to error properties without indirection.
    """

    def __init__(
        self,
        context: Annotated[ErrorContext, "Error context with all details"]
    ) -> None:
        """Initialize with error context."""
        self._context = context
        # Initialize parent with formatted message
        super().__init__(str(context.message))

    @property
    def context(self) -> ErrorContext:
        """The complete error context."""
        return self._context

    @property
    def code(self) -> str:
        """The domain-qualified error code."""
        return str(self._context.code)

    @property
    def message(self) -> str:
        """The error message."""
        return str(self._context.message)

    @property
    def severity(self) -> ErrorSeverity:
        """The error severity level."""
        return self._context.severity

    @property
    def category(self) -> ErrorCategory:
        """The error category."""
        return self._context.category

    @property
    def recovery_hint(self) -> str | None:
        """Optional recovery guidance."""
        return str(self._context.recovery_hint) if self._context.recovery_hint else None

    @property
    def request_id(self) -> str | None:
        """Optional request ID for tracking."""
        return str(self._context.request_id) if self._context.request_id else None

    @override
    def __str__(self) -> str:
        """Return formatted error string."""
        return self._context.format()

    @override
    def __repr__(self) -> str:
        """Return detailed representation."""
        return f"MznError(code={self.code!r}, severity={self.severity.name}, message={self.message!r})"

    def format(self, *, details: bool = False) -> str:
        """
        Format error for display.

        Args:
            details: Include extra context fields

        Returns:
            Formatted error string
        """
        return self._context.format(details=details)


# --- Exports ------------------------------------------------------------------

__all__ = ["MznError"]

"""
Title         : namespace.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/errors/namespace.py.

Description ----------- Streamlined Error namespace with integrated functionality. Single export pattern with modern
Python 3.13+ design.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any, ClassVar

from mzn.errors.context import ErrorBoundary, ErrorCollector, suppress_errors
from mzn.errors.decorator import error_aware
from mzn.errors.exceptions import MznError
from mzn.errors.factory import create_error, from_exception
from mzn.errors.types import (
    ErrorCategory,
    ErrorCode,
    ErrorContext,
    ErrorMessage,
    ErrorSeverity,
    RecoveryHint,
    RequestID,
)


if TYPE_CHECKING:
    from uuid import UUID


# --- Error Class --------------------------------------------------------------


class Error:
    """
    Unified namespace for all error functionality.

    Provides intelligent error management with domain-qualified codes, flexible context, and a clean, simple API.

    Example:     # Create errors     error = Error.create("cache.not_found", message="Key not found")     error =
    Error.from_exception(e, code="db.connection_failed")

    # Use decorator @Error.aware(domain="cache", operation="get") async def get_item(key: str) -> Any:     ...

    # Context managers with Error.boundary("api.request_failed") as boundary:     risky_operation()

    with Error.collector() as collector:     collector.try_("op1", lambda: operation1())     collector.try_("op2",
    lambda: operation2())

    with Error.suppress("fs.not_found", "fs.permission_denied"):     optional_file_read()

    """

    # --- Core Exception -------------------------------------------------------
    Base = MznError

    # --- Types ----------------------------------------------------------------
    Code = ErrorCode
    Message = ErrorMessage
    Hint = RecoveryHint
    Context = ErrorContext
    ReqID = RequestID

    # --- Enumerations ---------------------------------------------------------
    Severity = ErrorSeverity
    Category = ErrorCategory

    # --- Factory Methods ------------------------------------------------------

    @classmethod
    def create(  # noqa: PLR0913
        cls,
        code: Annotated[str | ErrorCode, "Domain-qualified error code"],
        *,
        message: Annotated[str | ErrorMessage | None, "Error message"] = None,
        severity: Annotated[ErrorSeverity | None, "Error severity"] = None,
        category: Annotated[ErrorCategory | None, "Error category"] = None,
        recovery_hint: Annotated[str | RecoveryHint | None, "Recovery guidance"] = None,
        request_id: Annotated[UUID | str | RequestID | None, "Request tracking ID"] = None,
        **extra: Annotated[Any, "Additional context fields"],
    ) -> MznError:
        """
        Create a new MznError with the given context.

        Args:     code: Domain-qualified error code (e.g., "cache.backend_failure")     message: Human-readable error
        message     severity: Error severity level     category: Error category for classification     recovery_hint:
        Guidance for error recovery     request_id: Request tracking identifier     **extra: Additional context fields

        Returns:     MznError instance with the specified context

        Example:     error = Error.create(         "validation.invalid_format",         message="Email format is
        invalid",         severity=Error.Severity.WARNING,         field="email",         value="not-an-email"     )

        """
        return create_error(
            code=code,
            message=message,
            severity=severity,
            category=category,
            recovery_hint=recovery_hint,
            request_id=request_id,
            **extra,
        )

    @classmethod
    def from_exception(
        cls,
        exception: Annotated[Exception, "Exception to convert"],
        *,
        code: Annotated[str | ErrorCode | None, "Error code override"] = None,
        **extra: Annotated[Any, "Additional context fields"],
    ) -> MznError:
        """
        Create MznError from an existing exception.

        Args:     exception: The exception to convert     code: Override error code (auto-generated if not provided)
        **extra: Additional context fields

        Returns:     MznError wrapping the original exception

        Example:     try:         risky_operation()     except ValueError as e:         error = Error.from_exception(e,
        code="validation.failed")

        """
        if code is None:
            return from_exception(exception, **extra)
        return from_exception(exception, code=code, **extra)

    # --- Decorator ------------------------------------------------------------

    aware = staticmethod(error_aware)

    # --- Context Managers -----------------------------------------------------

    @staticmethod
    def boundary(
        code: Annotated[str | ErrorCode, "Default error code"],
        *,
        suppress: Annotated[bool, "Suppress the exception"] = False,
    ) -> ErrorBoundary:
        """
        Create error boundary context manager.

        Args:     code: Default error code for caught exceptions     suppress: If True, suppress the exception after
        catching

        Returns:     ErrorBoundary context manager

        Example:     with Error.boundary("api.request_failed") as boundary:         make_api_request()     if
        boundary.error:         print(f"Request failed: {boundary.error}")

        """
        return ErrorBoundary(code, suppress=suppress)

    @staticmethod
    def collector() -> ErrorCollector:
        """
        Create error collector context manager.

        Returns:     ErrorCollector for collecting multiple errors

        Example:     with Error.collector() as collector:         collector.try_("parse_config", lambda: parse_config())
        collector.try_("validate_data", lambda: validate_data())         collector.try_("save_results", lambda:
        save_results())

        if collector.errors:     print(f"Failed operations: {len(collector.errors)}")

        """
        return ErrorCollector()

    @staticmethod
    def suppress(
        *codes: Annotated[str | ErrorCode, "Error codes to suppress"],
    ) -> Any:  # noqa: ANN401
        """
        Create context manager to suppress specific error codes.

        Args:     *codes: Error codes to suppress

        Returns:     Context manager that suppresses matching errors

        Example:     with Error.suppress("fs.not_found", "fs.permission_denied") as suppressed:         content =
        read_optional_file()

        if suppressed:     print("Using default configuration")

        """
        return suppress_errors(*codes)

    # --- Registry Management --------------------------------------------------

    _errors: ClassVar[list[MznError]] = []
    _max_history: ClassVar[int] = 100

    @classmethod
    def history(
        cls,
        *,
        limit: Annotated[int | None, "Maximum errors to return"] = None,
        code: Annotated[str | ErrorCode | None, "Filter by error code"] = None,
        severity: Annotated[ErrorSeverity | None, "Filter by severity"] = None,
    ) -> list[MznError]:
        """
        Get error history with optional filtering.

        Args:     limit: Maximum number of errors to return     code: Filter by specific error code     severity: Filter
        by minimum severity level

        Returns:     List of errors matching the criteria

        Example:     # Get last 10 errors     recent = Error.history(limit=10)

        # Get all critical errors critical = Error.history(severity=Error.Severity.CRITICAL)

        # Get specific error type cache_errors = Error.history(code="cache.backend_failure")

        """
        errors = cls._errors

        # Apply filters
        if code is not None:
            code_str = str(ErrorCode(code) if isinstance(code, str) else code)
            errors = [e for e in errors if str(e.code) == code_str]

        if severity is not None:
            errors = [e for e in errors if e.severity.value >= severity.value]

        # Apply limit
        if limit is not None:
            errors = errors[-limit:]

        return errors.copy()

    @classmethod
    def clear_history(cls) -> None:
        """
        Clear the error history.

        Useful for testing or when starting a new operation sequence.

        """
        cls._errors.clear()

    @classmethod
    def _record_error(cls, error: MznError) -> None:
        """
        Record an error to the history (internal use).

        Args:     error: Error to record

        """
        cls._errors.append(error)
        # Maintain max history size
        if len(cls._errors) > cls._max_history:
            cls._errors = cls._errors[-cls._max_history :]


# --- Exports ------------------------------------------------------------------

__all__ = ["Error"]

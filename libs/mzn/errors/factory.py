"""
Title         : factory.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/errors/factory.py

Description
-----------
Synchronous factory functions for error creation.
No async required - works in both sync and async contexts.
"""

from __future__ import annotations

import inspect
from pathlib import Path
from typing import Annotated, Any
from uuid import NAMESPACE_DNS, UUID, uuid5

from mzn.errors.exceptions import MznError
from mzn.errors.types import (
    ErrorCategory,
    ErrorCode,
    ErrorContext,
    ErrorMessage,
    ErrorSeverity,
    RecoveryHint,
    RequestID,
)


# --- Error Creation Factory -----------------------------------------------


def create_error(  # noqa: PLR0913, PLR0912
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
    Create an MznError with the given context.

    This is a synchronous function that works in both sync and async contexts.
    No asyncio.run() workarounds needed!

    Args:
        code: Domain-qualified error code (e.g., "cache.backend_failure")
        message: Error message (auto-generated if not provided)
        severity: Error severity level (defaults to ERROR) - use ErrorSeverity enum
        category: Error category (auto-inferred from code if not provided) - use ErrorCategory enum
        recovery_hint: Optional recovery guidance
        request_id: Optional request tracking ID
        **extra: Additional domain-specific context fields

    Returns:
        MznError instance ready to be raised

    Example:
        raise create_error(
            "cache.key_not_found",
            key="user:123",
            backend="redis"
        )
    """
    # Ensure code is ErrorCode type
    error_code = ErrorCode(code) if isinstance(code, str) else code

    # Handle message conversion
    if message is None:
        # Auto-generate message from code
        parts = str(error_code).split(".")
        if len(parts) == 2:
            domain, error_type = parts
            message_str = f"{domain.title()} error: {error_type.replace('_', ' ')}"
        else:
            message_str = f"Error: {error_code}"
        error_message = ErrorMessage(message_str)
    elif isinstance(message, ErrorMessage):
        error_message = message
    else:
        # message is a string
        error_message = ErrorMessage(message)

    # Determine severity (default to ERROR)
    error_severity = severity if severity is not None else ErrorSeverity.ERROR

    # Auto-infer category from code if not provided
    if category is not None:
        error_category = category
    else:
        # Map common domain prefixes to categories
        domain = str(error_code).split(".")[0]
        category_map = {
            # Runtime domains
            "cache": ErrorCategory.RUNTIME,
            "log": ErrorCategory.RUNTIME,
            "process": ErrorCategory.RUNTIME,
            "proc": ErrorCategory.RUNTIME,
            "engine": ErrorCategory.RUNTIME,
            # Resource domains
            "fs": ErrorCategory.RESOURCE,
            "file": ErrorCategory.RESOURCE,
            "disk": ErrorCategory.RESOURCE,
            "memory": ErrorCategory.RESOURCE,
            # Configuration domains
            "config": ErrorCategory.CONFIGURATION,
            "settings": ErrorCategory.CONFIGURATION,
            "env": ErrorCategory.CONFIGURATION,
            # Validation domains
            "validation": ErrorCategory.VALIDATION,
            "format": ErrorCategory.VALIDATION,
            "parse": ErrorCategory.VALIDATION,
            "schema": ErrorCategory.VALIDATION,
            # External domains
            "network": ErrorCategory.EXTERNAL,
            "net": ErrorCategory.EXTERNAL,
            "api": ErrorCategory.EXTERNAL,
            "http": ErrorCategory.EXTERNAL,
            "db": ErrorCategory.EXTERNAL,
        }
        error_category = category_map.get(domain, ErrorCategory.RUNTIME)

    # Add caller info to extra context
    frame = inspect.currentframe()
    if frame and frame.f_back:
        caller_frame = frame.f_back
        caller_info = {
            "_file": Path(caller_frame.f_code.co_filename).name,
            "_line": caller_frame.f_lineno,
            "_function": caller_frame.f_code.co_name,
        }
        extra.update(caller_info)

    # Build context with all fields
    context_fields: dict[str, Any] = {
        "code": error_code,
        "message": error_message,
        "severity": error_severity,
        "category": error_category,
    }

    # Add optional fields if provided
    if recovery_hint:
        if isinstance(recovery_hint, RecoveryHint):
            context_fields["recovery_hint"] = recovery_hint
        else:
            context_fields["recovery_hint"] = RecoveryHint(recovery_hint)
    if request_id:
        if isinstance(request_id, RequestID):
            context_fields["request_id"] = request_id
        elif isinstance(request_id, str):
            try:
                # Try to parse as UUID string
                uuid_val = UUID(request_id)
                context_fields["request_id"] = RequestID(uuid_val)
            except ValueError:
                # If not a valid UUID string, generate a new UUID with the string as namespace
                # This allows domain-specific request IDs while maintaining UUID type safety
                uuid_val = uuid5(NAMESPACE_DNS, request_id)
                context_fields["request_id"] = RequestID(uuid_val)
        else:
            # Already a UUID
            context_fields["request_id"] = RequestID(request_id)

    # Add extra fields
    context_fields.update(extra)

    # Create context
    context = ErrorContext(**context_fields)

    # Return MznError
    return MznError(context)

# --- Exception Factory --------------------------------------------------------


def from_exception(
    exc: Annotated[Exception, "Original exception"],
    code: Annotated[str | ErrorCode, "Domain-qualified error code"],
    **extra: Annotated[Any, "Additional context fields"],
) -> MznError:
    """
    Create an MznError from an existing exception.

    Args:
        exc: The original exception
        code: Domain-qualified error code
        **extra: Additional context fields

    Returns:
        MznError wrapping the original exception

    Example:
        try:
            open("/invalid/path")
        except IOError as e:
            raise from_exception(e, "fs.read_failed", path="/invalid/path")
    """
    # Extract message from original exception
    message = str(exc)

    # Add exception info to extra
    extra["_original_type"] = type(exc).__name__
    extra["_original_module"] = type(exc).__module__

    # Create error with extracted info
    return create_error(
        code=code,
        message=message,
        **extra
    )


# --- Exports ------------------------------------------------------------------

__all__ = [
    "create_error",
    "from_exception",
]

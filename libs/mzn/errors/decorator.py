"""
Title         : decorator.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/errors/decorator.py

Description
-----------
Modern, minimal error-aware decorator with domain inference.
Supports both sync and async functions with automatic context capture.
"""

from __future__ import annotations

import functools
import inspect
from typing import TYPE_CHECKING, Annotated, Any, ParamSpec, TypeVar, overload

from mzn.errors.exceptions import MznError
from mzn.errors.factory import create_error, from_exception
from mzn.errors.types import ErrorCategory, ErrorCode, ErrorSeverity


if TYPE_CHECKING:
    from collections.abc import Callable

# --- Type Variables -----------------------------------------------------------

P = ParamSpec("P")
T = TypeVar("T")


# --- Decorator ----------------------------------------------------------------


@overload
def error_aware(
    func: Callable[..., Any],
) -> Callable[..., Any]: ...


@overload
def error_aware(
    *,
    domain: Annotated[str | None, "Error domain"] = None,
    operation: Annotated[str | None, "Operation name"] = None,
    severity: Annotated[ErrorSeverity | None, "Default severity"] = None,
    category: Annotated[ErrorCategory | None, "Default category"] = None,
) -> Callable[[Callable[..., Any]], Callable[..., Any]]: ...


def error_aware(
    func: Callable[..., Any] | None = None,
    *,
    domain: Annotated[str | None, "Error domain"] = None,
    operation: Annotated[str | None, "Operation name"] = None,
    severity: Annotated[ErrorSeverity | None, "Default severity"] = None,
    category: Annotated[ErrorCategory | None, "Default category"] = None,
) -> Any:
    """
    Modern error-aware decorator for sync and async functions.

    Automatically captures context and transforms exceptions into MznErrors
    with domain-qualified error codes.

    Args:
        func: Function to decorate (when used without parentheses)
        domain: Error domain (e.g., "cache", "validation")
        operation: Operation name (e.g., "get", "validate")
        severity: Default severity for errors
        category: Default category for errors

    Returns:
        Decorated function that transforms exceptions

    Example:
        @error_aware(domain="cache", operation="get")
        async def get_item(key: str) -> Any:
            # Errors become: "cache.get_failed"
            ...
    """
    def decorator(f: Callable[..., Any]) -> Callable[..., Any]:
        # Auto-infer domain and operation if not provided
        inferred_domain = domain
        inferred_operation = operation

        if inferred_domain is None:
            # Try to infer from module name
            module = inspect.getmodule(f)
            if module and module.__name__:
                parts = module.__name__.split(".")
                # Look for common domain names in module path
                for part in parts:
                    if part in {"cache", "log", "errors", "validation", "network", "fs", "db"}:
                        inferred_domain = part
                        break

        if inferred_operation is None:
            # Use function name as operation
            inferred_operation = f.__name__.replace("_", ".")

        # Build error code pattern
        error_code_base = f"{inferred_domain or 'app'}.{inferred_operation or 'operation'}"

        # Check if function is async
        if inspect.iscoroutinefunction(f):
            @functools.wraps(f)
            async def async_wrapper(*args: Any, **kwargs: Any) -> Any:  # noqa: ANN401
                try:
                    return await f(*args, **kwargs)
                except MznError:
                    # Already an MznError, re-raise
                    raise
                except Exception as e:
                    # Transform to MznError
                    # Extract function arguments for context
                    sig = inspect.signature(f)
                    bound = sig.bind(*args, **kwargs)
                    bound.apply_defaults()

                    # Build context from arguments (exclude self/cls)
                    context = {
                        k: v for k, v in bound.arguments.items()
                        if k not in {"self", "cls"} and not k.startswith("_")
                    }

                    # Generate appropriate error code
                    error_code = ErrorCode(f"{error_code_base}_failed")

                    # Create error with optional severity/category
                    error = from_exception(e, code=error_code, **context)

                    # If severity/category were specified, we need to modify the error
                    if severity is not None or category is not None:
                        # Create a new error with the specified values
                        raise create_error(
                            code=error_code,
                            message=str(e),
                            severity=severity,
                            category=category,
                            _original_type=type(e).__name__,
                            _original_module=type(e).__module__,
                            **context
                        ) from e
                    raise error from e

            return async_wrapper

        @functools.wraps(f)
        def sync_wrapper(*args: Any, **kwargs: Any) -> Any:  # noqa: ANN401
            try:
                return f(*args, **kwargs)
            except MznError:
                # Already an MznError, re-raise
                raise
            except Exception as e:
                # Transform to MznError
                # Extract function arguments for context
                sig = inspect.signature(f)
                bound = sig.bind(*args, **kwargs)
                bound.apply_defaults()

                # Build context from arguments (exclude self/cls)
                context = {
                    k: v for k, v in bound.arguments.items()
                    if k not in {"self", "cls"} and not k.startswith("_")
                }

                # Generate appropriate error code
                error_code = ErrorCode(f"{error_code_base}_failed")

                # Create error with optional severity/category
                error = from_exception(e, code=error_code, **context)

                # If severity/category were specified, we need to modify the error
                if severity is not None or category is not None:
                    # Create a new error with the specified values
                    raise create_error(
                        code=error_code,
                        message=str(e),
                        severity=severity,
                        category=category,
                        _original_type=type(e).__name__,
                        _original_module=type(e).__module__,
                        **context
                    ) from e
                raise error from e

        return sync_wrapper

    # Handle both @error_aware and @error_aware() usage
    if func is None:
        return decorator
    return decorator(func)


# --- Exports ------------------------------------------------------------------

__all__ = ["error_aware"]

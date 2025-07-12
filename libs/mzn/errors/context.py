"""
Title         : context.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/errors/context.py.

Description ----------- Minimal context managers for error handling patterns. Supports both sync and async usage with
zero feature creep.

"""

from __future__ import annotations

from contextlib import contextmanager
from typing import TYPE_CHECKING, Annotated, Any, Literal, Self

from mzn.errors.exceptions import MznError
from mzn.errors.factory import from_exception
from mzn.errors.types import ErrorCode


if TYPE_CHECKING:
    from collections.abc import Generator
    from types import TracebackType

# --- Error Boundary -----------------------------------------------------------


class ErrorBoundary:
    """
    Context manager that catches and transforms exceptions into MznErrors.

    Supports both sync and async usage:     # Sync     with ErrorBoundary("domain.operation_failed") as boundary:
    risky_operation()

    # Async async with ErrorBoundary("domain.operation_failed") as boundary:     await async_risky_operation()

    """

    def __init__(
        self,
        code: Annotated[str | ErrorCode, "Default error code"],
        *,
        suppress: Annotated[bool, "Suppress the exception"] = False,
    ) -> None:
        """Initialize boundary with error code and suppress flag."""
        super().__init__()
        self.code = ErrorCode(code) if isinstance(code, str) else code
        self.suppress = suppress
        self.error: MznError | None = None

    def __enter__(self) -> Self:
        """Enter sync context."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> bool:
        """Exit sync context, transforming exceptions."""
        if exc_val is None:
            return False

        # Transform to MznError
        if isinstance(exc_val, MznError):
            self.error = exc_val
        elif isinstance(exc_val, Exception):
            self.error = from_exception(exc_val, code=self.code)
        else:
            # Don't transform KeyboardInterrupt, SystemExit, etc.
            return False

        # Suppress if requested
        if self.suppress:
            return True
        raise self.error from exc_val

    async def __aenter__(self) -> Self:
        """Enter async context."""
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> bool:
        """Exit async context - delegates to sync method."""
        return self.__exit__(exc_type, exc_val, exc_tb)


# --- Error Collector ----------------------------------------------------------


class ErrorCollector:
    """
    Collects multiple errors without stopping execution.

    Example:     with ErrorCollector() as collector:         collector.try_("op1", lambda: risky_op1())
    collector.try_("op2", lambda: risky_op2())

    if collector.errors:     print(f"Failed: {[e.code for e in collector.errors]}")

    """

    def __init__(self) -> None:
        """Initialize empty collector."""
        super().__init__()
        self.errors: list[MznError] = []
        self.results: dict[str, Any] = {}

    def __enter__(self) -> Self:
        """Enter context."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> Literal[False]:
        """Exit context - don't suppress exceptions."""
        return False

    def try_(
        self,
        operation: Annotated[str, "Operation name"],
        func: Annotated[Any, "Function to execute"],  # noqa: ANN401
        *,
        code: Annotated[str | ErrorCode | None, "Error code"] = None,
    ) -> Any:  # noqa: ANN401
        """
        Try to execute a function, collecting any errors.

        Args:     operation: Name of the operation     func: Function to execute (callable or coroutine)     code:
        Optional error code override

        Returns:     Result of the function or None if it failed

        """
        try:
            # For sync functions
            result = func()
            self.results[operation] = result
            return result  # noqa: TRY300
        except MznError as e:
            self.errors.append(e)
            return None
        except Exception as e:  # noqa: BLE001
            # Generate error code if not provided
            if code is None:
                code = ErrorCode(f"collector.{operation}_failed")
            else:
                code = ErrorCode(code) if isinstance(code, str) else code

            error = from_exception(e, code=code, operation=operation)
            self.errors.append(error)
            return None

    async def atry_(
        self,
        operation: Annotated[str, "Operation name"],
        coro: Annotated[Any, "Coroutine to execute"],  # noqa: ANN401
        *,
        code: Annotated[str | ErrorCode | None, "Error code"] = None,
    ) -> Any:  # noqa: ANN401
        """Async version of try_."""
        try:
            result = await coro
            self.results[operation] = result
            return result  # noqa: TRY300
        except MznError as e:
            self.errors.append(e)
            return None
        except Exception as e:  # noqa: BLE001
            # Generate error code if not provided
            if code is None:
                code = ErrorCode(f"collector.{operation}_failed")
            else:
                code = ErrorCode(code) if isinstance(code, str) else code

            error = from_exception(e, code=code, operation=operation)
            self.errors.append(error)
            return None

    @property
    def has_errors(self) -> bool:
        """Check if any errors were collected."""
        return len(self.errors) > 0


# --- Suppress Errors ----------------------------------------------------------


@contextmanager
def suppress_errors(
    *codes: Annotated[str | ErrorCode, "Error codes to suppress"],
) -> Generator[list[MznError]]:
    """
    Suppress specific error codes.

    Example:     with suppress_errors("fs.not_found", "fs.permission_denied") as suppressed: read_optional_file()

    if suppressed:     print("File read failed, using defaults")

    """
    suppressed: list[MznError] = []
    error_codes = [ErrorCode(c) if isinstance(c, str) else c for c in codes]

    try:
        yield suppressed
    except MznError as e:
        if any(str(e.context.code) == str(code) for code in error_codes):
            suppressed.append(e)
        else:
            raise
    except Exception as e:
        # Transform to check if it matches our codes
        mzn_error = from_exception(e, code=ErrorCode("suppressed.check"))
        # We can't check the code since we don't know what it would be
        # So we don't suppress non-MznErrors
        raise mzn_error from e


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ErrorBoundary",
    "ErrorCollector",
    "suppress_errors",
]

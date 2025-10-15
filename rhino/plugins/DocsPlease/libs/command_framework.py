"""
Title         : command_framework.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/command_framework.py

Description
----------------------------------------------------------------------------
Centralized command framework with decorators for error handling and common operations. 
The @rhino_command decorator that handles all cross-cutting concerns for Rhino commands.

Exception Handling:
    The decorator handles all exception types from the exceptions hierarchy:
    - UserCancelledError: Silent exit (not treated as error)
    - ValidationError subclasses: Alert user with error message
    - CameraError, TransformError: Print error message
    - Unexpected exceptions: Print error with stack trace
"""

from __future__ import annotations

import traceback
from functools import wraps
from typing import Any, Callable, Optional

import scriptcontext as sc

from .common_utils import CommonUtils
from .constants import Strings
from .exceptions import (
    CameraError,
    DetailError,
    DocsPluginError,
    EnvironmentError,
    LayoutError,
    ScaleError,
    TransformError,
    UserCancelledError,
    ValidationError,
)


def rhino_command(
    requires_layout: bool = True,
    undo_description: Optional[str] = None,
    auto_redraw: bool = True,
    print_start: bool = True,
    print_end: bool = True,
) -> Callable[[Callable[..., None]], Callable[..., int]]:
    """Decorator for Rhino commands with centralized error handling.

    This decorator handles all cross-cutting concerns for Rhino commands:
    - Pre-validates layout view requirement
    - Manages undo records automatically
    - Catches and handles all exception types appropriately
    - Redraws views after successful completion
    - Prints start/end messages

    Args:
        requires_layout: Whether command requires layout view to be active.
                        If True, raises LayoutError if not in layout view.
        undo_description: Description for undo record. If provided, wraps
                         command execution in an undo block.
        auto_redraw: Whether to automatically redraw all views after
                    successful command completion.
        print_start: Whether to print "=== Command Started ===" message.
        print_end: Whether to print "=== Script Completed ===" message.

    Returns:
        Decorated function that returns int (0 for success, 1 for error).

    Exception Handling:
        - UserCancelledError: Prints "Operation cancelled" and returns 0
        - ValidationError subclasses: Shows alert dialog and returns 1
        - CameraError, TransformError: Prints error message and returns 1
        - Other DocsPluginError: Shows alert with "Plugin error:" prefix
        - Unexpected exceptions: Prints error with full stack trace

    Example:
        >>> @rhino_command(requires_layout=True, undo_description="Quick Detail")
        ... def quick_detail():
        ...     # No try/except needed - decorator handles all errors
        ...     rect = rs.GetRectangle()
        ...     if not rect:
        ...         raise UserCancelledError("No rectangle selected")
        ...     # ... business logic ...
    """

    def decorator(func: Callable[..., None]) -> Callable[..., int]:
        @wraps(func)
        def wrapper(*args: Any, **kwargs: Any) -> int:
            # Print start message
            if print_start:
                command_name = func.__name__.replace("_", " ").title()
                print(f"\n=== {command_name} Script Started ===")

            # Pre-validation
            try:
                if requires_layout and not CommonUtils.is_layout_view_active():
                    raise LayoutError(Strings.MSG_LAYOUT_VIEW_REQUIRED)

            except DocsPluginError as e:
                CommonUtils.alert_user(str(e))
                return 1

            # Undo record management
            undo_record = None
            if undo_description:
                undo_record = sc.doc.BeginUndoRecord(undo_description)

            try:
                # Execute the command
                func(*args, **kwargs)

                # Print completion message
                if print_end:
                    print("=== Script Completed ===\n")

                return 0  # Success

            except UserCancelledError:
                print("Operation cancelled by user.")
                return 0  # User cancellation is not an error

            except (ValidationError, DetailError, LayoutError, EnvironmentError, ScaleError) as e:
                CommonUtils.alert_user(str(e))
                return 1

            except (CameraError, TransformError) as e:
                print(f"Operation failed: {e}")
                return 1

            except DocsPluginError as e:
                CommonUtils.alert_user(f"Plugin error: {e}")
                return 1

            except Exception as e:
                print(f"Unexpected error: {e}")
                print("Stack trace:")
                traceback.print_exc()
                return 1

            finally:
                # Cleanup
                if undo_record is not None:
                    sc.doc.EndUndoRecord(undo_record)

                if auto_redraw:
                    sc.doc.Views.Redraw()

        return wrapper

    return decorator


def require_layout_view() -> None:
    """Validate that a layout view is active or raise LayoutError.

    This helper function can be used within commands that need to check
    for layout view at specific points in execution, rather than at the
    start via the decorator's requires_layout parameter.

    Raises:
        LayoutError: If the active view is not a layout (page) view.

    Example:
        >>> def my_command():
        ...     # Do some work in any view
        ...     prepare_data()
        ...     # Now require layout view for next operation
        ...     require_layout_view()
        ...     create_detail_view()
    """
    if not CommonUtils.is_layout_view_active():
        raise LayoutError(Strings.MSG_LAYOUT_VIEW_REQUIRED)


def safe_undo_block(description: str) -> Callable[[Callable[..., Any]], Callable[..., Any]]:
    """Decorator that wraps function execution in an undo block.

    This decorator is useful for library functions that need undo management
    but aren't full commands. For commands, use @rhino_command with
    undo_description parameter instead.

    Args:
        description: Description for the undo record shown in Rhino's undo list.

    Returns:
        Decorated function with automatic undo block management.

    Example:
        >>> @safe_undo_block("Batch Update Details")
        ... def update_all_details(detail_ids):
        ...     for detail_id in detail_ids:
        ...         update_detail(detail_id)
        ...     # All changes grouped in single undo operation
    """

    def decorator(func: Callable[..., Any]) -> Callable[..., Any]:
        @wraps(func)
        def wrapper(*args: Any, **kwargs: Any) -> Any:
            undo_record = sc.doc.BeginUndoRecord(description)
            try:
                return func(*args, **kwargs)
            finally:
                sc.doc.EndUndoRecord(undo_record)

        return wrapper

    return decorator

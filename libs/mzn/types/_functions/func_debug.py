"""
Title         : func_debug.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/types/_functions/func_debug.py.

Description ----------- Central debugging and introspection utilities for the type system.

Provides tools for tracing execution, inspecting object states, and generating rich diagnostic reports, keeping these
concerns separate from logging.

"""

from __future__ import annotations

import asyncio
import inspect
from typing import TYPE_CHECKING, Annotated, Any, ClassVar

from beartype import beartype

from mzn.types._contracts.prot_assets import CoreAsset, ValidatedAsset


# Debug configuration constants
_AUTO_TRACE_ENABLED = True  # Enable debug tracing by default


if TYPE_CHECKING:
    from collections.abc import Callable


# --- Debug Namespace Class ----------------------------------------------------


class Debug:
    """A namespace for all debugging, introspection, and diagnostic utilities."""

    _setup_done: bool = False
    _setup_lock: ClassVar[asyncio.Lock] = asyncio.Lock()
    _setup_task: ClassVar[asyncio.Task[Any] | None] = None

    @classmethod
    async def setup(
        cls: Annotated[type[Debug], "Debug class type"]
    ) -> Annotated[None, "Set up debug system from config"]:
        """Set up the debug system from the global config."""
        async with cls._setup_lock:
            if cls._setup_done:
                return
            # In the future, this could configure more complex debug settings
            cls._setup_done = True

    @classmethod
    def _ensure_setup(
        cls: Annotated[type[Debug], "Debug class type"]
    ) -> Annotated[None, "Ensure debug system is set up"]:
        if not cls._setup_done:

            if asyncio.get_event_loop().is_running():
                cls._setup_task = asyncio.create_task(cls.setup())
            else:
                asyncio.run(cls.setup())

    @classmethod
    def is_tracing_enabled(
        cls: Annotated[type[Debug], "Debug class type"]
    ) -> Annotated[bool, "Is debug tracing enabled"]:
        """Return True if debug tracing is enabled."""
        return _AUTO_TRACE_ENABLED

    @classmethod
    @beartype
    async def atrace(
        cls: Annotated[type[Debug], "Debug class type"],
        func: Annotated[Callable[..., Any], "Function to trace"],
        *args: Annotated[Any, "Function args"],
        **kwargs: Annotated[Any, "Function kwargs"]
    ) -> Annotated[dict[str, Any], "Trace information"]:
        """
        Trace the execution of a function, capturing inputs, output, and errors.

        This only executes if auto tracing is enabled.

        Args:     func: The function to trace.     *args: Positional arguments for the function.     **kwargs: Keyword
        arguments for the function.

        Returns:     A dictionary containing detailed trace information.

        """
        cls._ensure_setup()
        if not cls.is_tracing_enabled():
            return {}

        func_name = getattr(func, "__qualname__", str(func))
        trace_data: dict[str, Any] = {
            "function_name": func_name,
            "inputs": {"args": args, "kwargs": kwargs},
            "output": None,
            "error": None,
        }

        # Timer removed to fix import-time resource warnings
        try:
            result = (
                await func(*args, **kwargs)
                if inspect.iscoroutinefunction(func)
                else func(*args, **kwargs)
            )
            trace_data["output"] = result
        except (RuntimeError, ValueError, TypeError) as e:
            trace_data["error"] = {"type": type(e).__name__, "message": str(e)}
            # Debug logging removed to fix import-time resource warnings

        return trace_data

    @classmethod
    @beartype
    async def ainspect_object(
        cls: Annotated[type[Debug], "Debug class type"],
        obj: Annotated[object, "Object to inspect"]
    ) -> Annotated[dict[str, Any], "Inspection data"]:
        """
        Perform a deep inspection of an object's state.

        Args:     obj: The object to inspect.

        Returns:     A dictionary with detailed inspection data.

        """
        obj_type: type = type(obj)
        info: dict[str, Any] = {
            "type": f"{obj_type.__module__}.{obj_type.__qualname__}",
            "id": id(obj),
            "repr": repr(obj),
            "attributes": {},
            "methods": [],
            "protocols": [],
        }

        # Inspect attributes
        if hasattr(obj, "__dict__"):
            for key, value in obj.__dict__.items():
                info["attributes"][key] = repr(value)
        elif hasattr(obj, "__slots__"):
            slots = getattr(obj, "__slots__", ())
            if isinstance(slots, str):
                slots = (slots,)
            for slot in slots:
                info["attributes"][slot] = repr(getattr(obj, slot, "<not set>"))

        # Inspect methods
        for name, _ in inspect.getmembers(obj, inspect.ismethod):
            if not name.startswith("_"):
                info["methods"].append(name)

        # Check for protocol conformance (example)
        # This can be expanded with more protocols from the system

        if isinstance(obj, ValidatedAsset):
            info["protocols"].append("ValidatedAsset")
        elif isinstance(obj, CoreAsset):
            info["protocols"].append("CoreAsset")

        return info


# --- Public re-exports --------------------------------------------------------

__all__ = ["Debug"]

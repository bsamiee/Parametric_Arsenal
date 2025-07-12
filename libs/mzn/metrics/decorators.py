"""
Title         : decorators.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/metrics/decorators.py

Description
-----------
Single, focused metrics decorator for automatic function instrumentation.
Provides timing, counting, and error tracking with minimal configuration.
"""

from __future__ import annotations

import asyncio
import inspect
import time
import weakref
from functools import wraps
from typing import TYPE_CHECKING, Annotated, Any, TypeVar, overload

from anyio import from_thread
from beartype import beartype

from mzn.metrics.core import create_counter, create_histogram
from mzn.metrics.operations import increment, observe, time_histogram
from mzn.metrics.registry import metric_exists
from mzn.metrics.types import (
    MetricConfig,
    MetricLabelKey,
    MetricLabelValue,
    MetricName,
    MetricSpec,
    MetricType,
    MetricValue,
)


if TYPE_CHECKING:
    from collections.abc import Callable

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Module State -------------------------------------------------------------

# Track background tasks to prevent premature garbage collection
_background_tasks: weakref.WeakSet[asyncio.Task[Any]] = weakref.WeakSet()

# --- Internal Helpers ---------------------------------------------------------


def _extract_label_value(obj: Any, path: str) -> str:  # noqa: ANN401
    """
    Extract a value from an object using dot notation.

    Args:
        obj: Object to extract from
        path: Dot-separated path (e.g., "request.method")

    Returns:
        String representation of the value or "unknown" if not found
    """
    try:
        parts = path.split(".")
        current: Any = obj

        for part in parts:
            if hasattr(current, part):  # pyright: ignore[reportUnknownArgumentType]
                current = getattr(current, part)  # pyright: ignore[reportUnknownArgumentType]
            elif isinstance(current, dict) and part in current:
                current = current[part]  # pyright: ignore[reportUnknownVariableType]
            elif isinstance(current, (list, tuple)) and part.isdigit():
                idx = int(part)
                if 0 <= idx < len(current):  # pyright: ignore[reportUnknownArgumentType]
                    current = current[idx]  # pyright: ignore[reportUnknownVariableType]
                else:
                    return "unknown"
            else:
                return "unknown"

        return str(current) if current is not None else "unknown"  # pyright: ignore[reportUnknownArgumentType]
    except Exception:  # noqa: BLE001
        return "unknown"


def _generate_metric_names(func: Callable[..., Any], custom_name: str | None = None) -> dict[str, str]:
    """
    Generate metric names from a function.

    Args:
        func: Function to generate names from
        custom_name: Optional custom base name override

    Returns:
        Dictionary with histogram, counter, and error metric names
    """
    if custom_name:
        base = custom_name
    else:
        # Build components
        components: list[str] = []

        # Module (replace dots with underscores)
        if func.__module__ and func.__module__ != "__main__":
            components.append(func.__module__.replace(".", "_"))

        # Class name if it's a method
        if hasattr(func, "__qualname__") and "." in func.__qualname__:
            # Extract class name from qualified name
            parts = func.__qualname__.split(".")
            if len(parts) > 1:
                components.append(parts[-2])

        # Function name (no suffix)
        components.append(func.__name__)
        base = "_".join(components)

    return {
        "histogram": f"{base}.duration",
        "counter": f"{base}.calls",
        "errors": f"{base}.errors",
    }


# --- Decorator Overloads ------------------------------------------------------


@overload
def metered(
    func: Callable[..., Any],
) -> Callable[..., Any]: ...


@overload
def metered(
    *,
    metric: Annotated[str | None, "Custom metric name"] = None,
    labels: Annotated[dict[str, str] | None, "Static labels"] = None,
    label_from: Annotated[list[str] | None, "Dynamic label paths"] = None,
    track_calls: Annotated[bool, "Track call counts separately"] = False,
    track_errors: Annotated[bool, "Track errors separately"] = False,
    buckets: Annotated[list[float] | None, "Histogram buckets"] = None,
) -> Callable[[Callable[..., Any]], Callable[..., Any]]: ...


# --- Main Decorator -----------------------------------------------------------


@beartype
def metered(  # noqa: PLR0913, PLR0915
    func: Callable[..., Any] | None = None,
    *,
    metric: Annotated[str | None, "Custom metric name"] = None,
    labels: Annotated[dict[str, str] | None, "Static labels"] = None,
    label_from: Annotated[list[str] | None, "Dynamic label paths"] = None,
    track_calls: Annotated[bool, "Track call counts separately"] = False,
    track_errors: Annotated[bool, "Track errors separately"] = False,
    buckets: Annotated[list[float] | None, "Histogram buckets"] = None,
) -> Any:
    """
    Decorator for automatic function timing and optional call/error counting.

    ALWAYS tracks execution time as histogram.
    OPTIONALLY tracks call counts and errors.

    Args:
        func: Function to decorate (when used without parentheses)
        metric: Custom metric base name (default: auto-generated)
        labels: Static labels to apply to all metrics
        label_from: Paths to extract dynamic labels from args/result
        track_calls: Create separate counter for function calls
        track_errors: Create separate counter for errors
        buckets: Custom histogram buckets (default: prometheus defaults)

    Returns:
        Decorated function with metric instrumentation

    Examples:
        # Simple usage - tracks execution time only
        @metered
        async def process_request():
            ...

        # Track calls and timing
        @metered(track_calls=True)
        async def api_endpoint():
            ...

        # Track calls, timing, and errors
        @metered(track_calls=True, track_errors=True)
        async def risky_operation():
            ...

        # Custom metric name and labels
        @metered(metric="api.request", labels={"service": "users"})
        async def get_user(user_id: str):
            ...

        # Dynamic labels from function arguments
        @metered(label_from=["request.method", "response.status_code"])
        async def handle_request(request, response):
            ...
    """
    def decorator(fn: Callable[..., Any]) -> Callable[..., Any]:  # noqa: PLR0915
        # Check if function is async
        is_async = inspect.iscoroutinefunction(fn)

        # Generate metric names
        metric_names = _generate_metric_names(fn, metric)
        histogram_name = MetricName(metric_names["histogram"])
        counter_name = MetricName(metric_names["counter"]) if track_calls else None
        error_name = MetricName(metric_names["errors"]) if track_errors else None

        # Prepare label keys
        static_label_keys = list(labels.keys()) if labels else []
        dynamic_label_keys = label_from or []
        all_label_keys = static_label_keys + dynamic_label_keys

        # Store initialization state
        metrics_initialized = False

        async def ensure_metrics() -> None:
            """Ensure metrics are created (lazy initialization)."""
            nonlocal metrics_initialized
            if metrics_initialized:
                return

            # Create histogram if not exists
            if not await metric_exists(histogram_name):
                spec = MetricSpec(
                    name=histogram_name,
                    type=MetricType.HISTOGRAM,
                    description=f"Execution time for {fn.__name__}",
                    label_names=[MetricLabelKey(k) for k in all_label_keys],
                )
                config = None
                if buckets:

                    config = MetricConfig(histogram_buckets=buckets)
                _ = await create_histogram(spec, config)

            # Create call counter if needed
            if track_calls and counter_name and not await metric_exists(counter_name):
                spec = MetricSpec(
                    name=counter_name,
                    type=MetricType.COUNTER,
                    description=f"Total calls to {fn.__name__}",
                    label_names=[MetricLabelKey(k) for k in all_label_keys],
                )
                _ = await create_counter(spec)

            # Create error counter if needed
            if track_errors and error_name and not await metric_exists(error_name):
                spec = MetricSpec(
                    name=error_name,
                    type=MetricType.COUNTER,
                    description=f"Errors in {fn.__name__}",
                    label_names=[MetricLabelKey(k) for k in [*all_label_keys, "error_type"]],
                )
                _ = await create_counter(spec)

            metrics_initialized = True

        def build_labels(
            args: tuple[Any, ...],
            kwargs: dict[str, Any],
            result: Any = None,  # noqa: ANN401
        ) -> dict[MetricLabelKey, MetricLabelValue]:
            """Build labels from static and dynamic sources."""
            metric_labels: dict[MetricLabelKey, MetricLabelValue] = {}

            # Add static labels
            if labels:
                for k, v in labels.items():
                    metric_labels[MetricLabelKey(k)] = MetricLabelValue(v)

            # Extract dynamic labels
            if label_from:
                # Create a context object with args, kwargs, and result
                sig = inspect.signature(fn)
                try:
                    bound = sig.bind(*args, **kwargs)
                    bound.apply_defaults()
                    context = dict(bound.arguments)
                    if result is not None:
                        context["result"] = result
                except Exception:  # noqa: BLE001
                    context = {"args": args, "kwargs": kwargs, "result": result}

                # Extract each label
                for label_path in label_from:
                    # The label key is the last part of the path
                    label_key = label_path.split(".")[-1]
                    value = _extract_label_value(context, label_path)
                    metric_labels[MetricLabelKey(label_key)] = MetricLabelValue(value)

            return metric_labels

        if is_async:
            @wraps(fn)
            async def async_wrapper(*args: Any, **kwargs: Any) -> Any:  # noqa: ANN401
                await ensure_metrics()

                # Track call count
                if track_calls and counter_name:
                    call_labels = build_labels(args, kwargs)
                    await increment(counter_name, labels=call_labels)

                # Time execution
                try:
                    # Use context manager for timing
                    start_labels = build_labels(args, kwargs)
                    async with time_histogram(histogram_name, labels=start_labels):
                        return await fn(*args, **kwargs)
                except Exception as e:
                    # Track error if requested
                    if track_errors and error_name:
                        error_labels = build_labels(args, kwargs)
                        error_labels[MetricLabelKey("error_type")] = MetricLabelValue(type(e).__name__)
                        await increment(error_name, labels=error_labels)
                    raise

            return async_wrapper

        @wraps(fn)
        def sync_wrapper(*args: Any, **kwargs: Any) -> Any:  # noqa: ANN401
            # For sync functions, we need to run async operations

            # Ensure metrics are created
            try:
                # Try to get existing event loop
                _ = asyncio.get_running_loop()
                in_async_context = True
            except RuntimeError:
                # No event loop running
                in_async_context = False

            # Helper to run async code
            def run_async(coro: Any) -> Any:  # noqa: ANN401
                if in_async_context:
                    # We're in an async context, schedule the coroutine
                    # Keep task reference to prevent garbage collection
                    task = asyncio.create_task(coro)
                    _background_tasks.add(task)
                    # Clean up completed tasks automatically
                    task.add_done_callback(_background_tasks.discard)
                    return None
                # Run in new event loop using anyio
                return from_thread.run_sync(coro)

            if not metrics_initialized:
                run_async(ensure_metrics())

            # Track call count
            if track_calls and counter_name:
                call_labels = build_labels(args, kwargs)
                run_async(increment(counter_name, labels=call_labels))

            # Time execution
            start_time = time.perf_counter()
            try:
                result = fn(*args, **kwargs)
                duration = time.perf_counter() - start_time

                # Record timing
                timing_labels = build_labels(args, kwargs, result)
                run_async(observe(histogram_name, MetricValue(duration), labels=timing_labels))

                return result  # noqa: TRY300

            except Exception as e:
                duration = time.perf_counter() - start_time

                # Still record timing for errors
                error_timing_labels = build_labels(args, kwargs)
                run_async(observe(histogram_name, MetricValue(duration), labels=error_timing_labels))

                # Track error if requested
                if track_errors and error_name:
                    error_labels = build_labels(args, kwargs)
                    error_labels[MetricLabelKey("error_type")] = MetricLabelValue(type(e).__name__)
                    run_async(increment(error_name, labels=error_labels))
                raise

        return sync_wrapper

    # Handle usage with/without parentheses
    if func is None:
        return decorator
    return decorator(func)


# --- Exports ------------------------------------------------------------------

__all__ = ["metered"]

"""
Title         : registry.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/metrics/registry.py.

Description ----------- Prometheus registry management with async-first design. Handles metric registration, tracking,
and text format generation.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, cast

import anyio
from anyio import Lock
from beartype import beartype
from prometheus_client import (
    REGISTRY,
    CollectorRegistry,
    generate_latest,
)

from mzn.errors.namespace import Error
from mzn.metrics.exceptions import RegistrationError
from mzn.metrics.types import MetricName, MetricType  # noqa: TC001


if TYPE_CHECKING:

    from mzn.log.core import Logger
    from mzn.metrics.types import MetricInstance


# --- Module State -------------------------------------------------------------

_registry: CollectorRegistry = REGISTRY  # Use prometheus default
_metrics: dict[str, MetricInstance] = {}  # Our tracking
_logger: Logger | None = None  # Lazy-initialized logger
_lock: Lock | None = None  # Thread-safe registry access

# --- Internal Helpers ---------------------------------------------------------


async def _get_lock() -> anyio.Lock:
    """Get or create the async lock for thread-safe operations."""
    if globals().get("_lock") is None:
        globals()["_lock"] = anyio.Lock()
    return cast("Lock", globals()["_lock"])


async def _get_logger() -> Logger:
    """Get or create the metrics logger."""
    if _logger is None:
        # Import here to avoid circular dependencies
        from mzn.log.namespace import Log  # noqa: PLC0415

        # Use a local variable to avoid global statement
        logger = await Log.console("metrics.registry").level(Log.Level.INFO).build()
        # Store in module state
        globals()["_logger"] = logger
        return logger
    return _logger


# --- Registry Management ------------------------------------------------------


@beartype
async def set_registry(
    registry: Annotated[CollectorRegistry, "Custom prometheus registry"],
) -> None:
    """
    Set a custom prometheus registry.

    Useful for testing or multi-tenant applications.

    Args:     registry: CollectorRegistry instance to use

    Raises:     RegistrationError: If metrics already exist in current registry

    """
    lock = await _get_lock()
    async with lock:
        if _metrics:
            error = Error.create(
                "metrics.registry_not_empty",
                message="Cannot change registry with existing metrics",
                metric_count=len(_metrics),
            )
            raise RegistrationError(error.context)

        globals()["_registry"] = registry
        logger = await _get_logger()
        await logger.info("Registry updated", registry_id=id(registry))


@beartype
async def get_registry() -> CollectorRegistry:
    """
    Get the current prometheus registry.

    Returns:     Current CollectorRegistry instance

    """
    return _registry


# --- Metric Registration ------------------------------------------------------


@beartype
async def register_metric(
    instance: Annotated[MetricInstance, "Metric instance to register"],
) -> None:
    """
    Register a metric instance in our tracking.

    Args:     instance: MetricInstance to register

    Raises:     RegistrationError: If metric name already exists

    """
    name = str(instance.spec.name)

    lock = await _get_lock()
    async with lock:
        if name in _metrics:
            error = Error.create(
                "metrics.already_registered",
                message=f"Metric '{name}' is already registered",
                metric_name=name,
                existing_type=_metrics[name].spec.type.value,
            )
            raise RegistrationError(error.context)

        _metrics[name] = instance

    logger = await _get_logger()
    await logger.debug(
        "Metric registered",
        name=name,
        type=instance.spec.type.value,
        labels=[str(label) for label in instance.spec.label_names],
    )


@beartype
async def unregister_metric(
    name: Annotated[MetricName | str, "Metric name"],
) -> bool:
    """
    Unregister a metric from tracking.

    Note: This only removes from our tracking. Prometheus-client doesn't support true unregistration from collectors.

    Args:     name: Name of metric to unregister

    Returns:     True if metric was removed, False if not found

    """
    str_name = str(name)

    lock = await _get_lock()
    async with lock:
        if str_name in _metrics:
            del _metrics[str_name]
            logger = await _get_logger()
            await logger.info("Metric unregistered", name=str_name)
            return True

    return False


# --- Metric Queries -----------------------------------------------------------


@beartype
async def get_metric(
    name: Annotated[MetricName | str, "Metric name"],
) -> MetricInstance | None:
    """
    Get an existing metric instance by name.

    Args:     name: Metric name to retrieve

    Returns:     MetricInstance if found, None otherwise

    """
    lock = await _get_lock()
    async with lock:
        return _metrics.get(str(name))


@beartype
async def list_metrics(
    metric_type: Annotated[MetricType | None, "Filter by type"] = None,
) -> list[MetricInstance]:
    """
    List all registered metrics.

    Args:     metric_type: Optional filter by metric type

    Returns:     List of metric instances matching the filter

    """
    lock = await _get_lock()
    async with lock:
        metrics = list(_metrics.values())

    if metric_type:
        metrics = [m for m in metrics if m.spec.type == metric_type]

    return metrics


@beartype
async def metric_exists(
    name: Annotated[MetricName | str, "Metric name"],
) -> bool:
    """
    Check if a metric exists.

    Args:     name: Metric name to check

    Returns:     True if metric exists, False otherwise

    """
    lock = await _get_lock()
    async with lock:
        return str(name) in _metrics


# --- Registry Operations ------------------------------------------------------


@beartype
async def clear_metrics() -> None:
    """
    Clear all metric instances from tracking.

    WARNING: This is for testing only. It clears our tracking but doesn't unregister from prometheus collectors.

    """
    lock = await _get_lock()
    async with lock:
        _metrics.clear()

    logger = await _get_logger()
    await logger.warning("All metrics cleared (testing mode)")


@beartype
async def generate_metrics_text() -> str:
    """
    Generate prometheus text format for all metrics.

    This is the format expected by prometheus scrapers.

    Returns:     Prometheus text format string with all metrics

    Example:     # HELP http_requests_total Total HTTP requests     # TYPE http_requests_total counter
    http_requests_total{method="GET",status="200"} 1027.0

    """
    # generate_latest returns bytes, decode to string
    return generate_latest(_registry).decode("utf-8")


# --- Exports ------------------------------------------------------------------

__all__ = [
    "clear_metrics",
    "generate_metrics_text",
    "get_metric",
    "get_registry",
    "list_metrics",
    "metric_exists",
    "register_metric",
    "set_registry",
    "unregister_metric",
]

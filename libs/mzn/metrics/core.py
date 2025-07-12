"""
Title         : core.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/metrics/core.py.

Description ----------- Core metrics functionality providing typed factory methods for prometheus-client. Minimal
abstraction layer that adds type safety while exposing prometheus directly.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any

from beartype import beartype
from prometheus_client import (
    Counter,
    Gauge,
    Histogram,
    Info,
    Summary,
)

from mzn.errors.namespace import Error
from mzn.log.namespace import Log
from mzn.metrics.exceptions import MetricTypeError, RegistrationError, ValidationError
from mzn.metrics.registry import get_registry, metric_exists, register_metric
from mzn.metrics.types import (
    MetricConfig,
    MetricInstance,
    MetricSpec,
    MetricType,
    TimestampUTC,
)


if TYPE_CHECKING:
    from mzn.log.core import Logger


# --- Module State -------------------------------------------------------------

_logger: Logger | None = None


# --- Internal Helpers ---------------------------------------------------------


async def _get_logger() -> Logger:
    """Get or create the metrics logger (cached)."""
    if _logger is None:
        logger = await Log.console("metrics.core").level(Log.Level.INFO).build()
        globals()["_logger"] = logger
        return logger
    return _logger


def _validate_spec(spec: Annotated[MetricSpec, "Metric spec to validate"]) -> None:
    """Validate metric specification before creation."""
    # Check for duplicate label names
    label_names = [str(label) for label in spec.label_names]
    if len(label_names) != len(set(label_names)):
        error = Error.create(
            "metrics.invalid_labels",
            message=f"Duplicate label names in metric '{spec.name}'",
            metric_name=str(spec.name),
            labels=label_names,
        )
        raise ValidationError(error.context)


# --- Factory Functions --------------------------------------------------------


@beartype
async def create_counter(
    spec: Annotated[MetricSpec, "Metric specification"],
    config: Annotated[MetricConfig | None, "Optional configuration"] = None,
) -> MetricInstance:
    """
    Create a Counter metric.

    Counters only go up and are reset on restart. Use for: request counts, error counts, bytes processed.

    Args:     spec: Metric specification with name, description, and labels     config: Optional configuration (unused
    for counters)

    Returns:     MetricInstance wrapping the prometheus Counter

    Raises:     RegistrationError: If metric already exists     ValidationError: If spec is invalid     MetricTypeError:
    If spec type is not COUNTER

    """
    if spec.type != MetricType.COUNTER:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected COUNTER type, got {spec.type.value}",
            expected="counter",
            actual=spec.type.value,
        )
        raise MetricTypeError(error.context)

    _validate_spec(spec)

    # Check if already exists
    if await metric_exists(spec.name):
        error = Error.create(
            "metrics.already_registered",
            message=f"Metric '{spec.name}' is already registered",
            metric_name=str(spec.name),
        )
        raise RegistrationError(error.context)

    # Create prometheus counter
    try:
        registry = await get_registry()
        counter = Counter(
            str(spec.name),
            spec.description,
            labelnames=[str(label) for label in spec.label_names],
            registry=registry,
        )
    except Exception as e:
        error = Error.create(
            "metrics.creation_failed",
            message=f"Failed to create counter '{spec.name}'",
            metric_name=str(spec.name),
            metric_type="counter",
            error=str(e),
        )
        raise RegistrationError(error.context) from e

    # Create instance
    instance = MetricInstance(
        spec=spec,
        prometheus_metric=counter,
        created_at=TimestampUTC.now(),
    )

    # Register and log
    await register_metric(instance)
    logger = await _get_logger()
    await logger.info(
        "Counter registered",
        name=str(spec.name),
        labels=[str(label) for label in spec.label_names],
    )

    return instance


@beartype
async def create_gauge(
    spec: Annotated[MetricSpec, "Metric specification"],
    config: Annotated[MetricConfig | None, "Optional configuration"] = None,
) -> MetricInstance:
    """
    Create a Gauge metric.

    Gauges can go up or down and represent current state. Use for: temperature, queue size, memory usage.

    Args:     spec: Metric specification with name, description, and labels     config: Optional configuration with
    initial_gauge_value

    Returns:     MetricInstance wrapping the prometheus Gauge

    Raises:     RegistrationError: If metric already exists     ValidationError: If spec is invalid     MetricTypeError:
    If spec type is not GAUGE

    """
    if spec.type != MetricType.GAUGE:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected GAUGE type, got {spec.type.value}",
            expected="gauge",
            actual=spec.type.value,
        )
        raise MetricTypeError(error.context)

    _validate_spec(spec)

    # Check if already exists
    if await metric_exists(spec.name):
        error = Error.create(
            "metrics.already_registered",
            message=f"Metric '{spec.name}' is already registered",
            metric_name=str(spec.name),
        )
        raise RegistrationError(error.context)

    # Create prometheus gauge
    try:
        registry = await get_registry()
        gauge = Gauge(
            str(spec.name),
            spec.description,
            labelnames=[str(label) for label in spec.label_names],
            registry=registry,
        )

        # Set initial value if provided
        if config and config.initial_gauge_value is not None:
            gauge.set(float(config.initial_gauge_value))

    except Exception as e:
        error = Error.create(
            "metrics.creation_failed",
            message=f"Failed to create gauge '{spec.name}'",
            metric_name=str(spec.name),
            metric_type="gauge",
            error=str(e),
        )
        raise RegistrationError(error.context) from e

    # Create instance
    instance = MetricInstance(
        spec=spec,
        prometheus_metric=gauge,
        created_at=TimestampUTC.now(),
    )

    # Register and log
    await register_metric(instance)
    logger = await _get_logger()
    await logger.info(
        "Gauge registered",
        name=str(spec.name),
        labels=[str(label) for label in spec.label_names],
        initial_value=float(config.initial_gauge_value) if config and config.initial_gauge_value else None,
    )

    return instance


@beartype
async def create_histogram(
    spec: Annotated[MetricSpec, "Metric specification"],
    config: Annotated[MetricConfig | None, "Optional configuration"] = None,
) -> MetricInstance:
    """
    Create a Histogram metric.

    Histograms track distributions with configurable buckets. Use for: request durations, response sizes.

    Args:     spec: Metric specification with name, description, and labels     config: Optional configuration with
    histogram_buckets

    Returns:     MetricInstance wrapping the prometheus Histogram

    Raises:     RegistrationError: If metric already exists     ValidationError: If spec is invalid     MetricTypeError:
    If spec type is not HISTOGRAM

    """
    if spec.type != MetricType.HISTOGRAM:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected HISTOGRAM type, got {spec.type.value}",
            expected="histogram",
            actual=spec.type.value,
        )
        raise MetricTypeError(error.context)

    _validate_spec(spec)

    # Check if already exists
    if await metric_exists(spec.name):
        error = Error.create(
            "metrics.already_registered",
            message=f"Metric '{spec.name}' is already registered",
            metric_name=str(spec.name),
        )
        raise RegistrationError(error.context)

    # Create prometheus histogram
    try:
        registry = await get_registry()
        # Build kwargs conditionally
        kwargs: dict[str, Any] = {
            "name": str(spec.name),
            "documentation": spec.description,
            "labelnames": [str(label) for label in spec.label_names],
            "registry": registry,
        }

        if config and config.histogram_buckets:
            kwargs["buckets"] = config.histogram_buckets

        histogram = Histogram(**kwargs)

    except Exception as e:
        error = Error.create(
            "metrics.creation_failed",
            message=f"Failed to create histogram '{spec.name}'",
            metric_name=str(spec.name),
            metric_type="histogram",
            error=str(e),
        )
        raise RegistrationError(error.context) from e

    # Create instance
    instance = MetricInstance(
        spec=spec,
        prometheus_metric=histogram,
        created_at=TimestampUTC.now(),
    )

    # Register and log
    await register_metric(instance)
    logger = await _get_logger()
    await logger.info(
        "Histogram registered",
        name=str(spec.name),
        labels=[str(label) for label in spec.label_names],
        buckets=config.histogram_buckets if config else None,
    )

    return instance


@beartype
async def create_summary(
    spec: Annotated[MetricSpec, "Metric specification"],
    config: Annotated[MetricConfig | None, "Optional configuration"] = None,
) -> MetricInstance:
    """
    Create a Summary metric.

    Summaries calculate quantiles over sliding windows. Use for: percentiles, medians.

    Args:     spec: Metric specification with name, description, and labels     config: Optional configuration (unused
    for summaries)

    Returns:     MetricInstance wrapping the prometheus Summary

    Raises:     RegistrationError: If metric already exists     ValidationError: If spec is invalid     MetricTypeError:
    If spec type is not SUMMARY

    """
    if spec.type != MetricType.SUMMARY:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected SUMMARY type, got {spec.type.value}",
            expected="summary",
            actual=spec.type.value,
        )
        raise MetricTypeError(error.context)

    _validate_spec(spec)

    # Check if already exists
    if await metric_exists(spec.name):
        error = Error.create(
            "metrics.already_registered",
            message=f"Metric '{spec.name}' is already registered",
            metric_name=str(spec.name),
        )
        raise RegistrationError(error.context)

    # Create prometheus summary
    try:
        registry = await get_registry()
        summary = Summary(
            str(spec.name),
            spec.description,
            labelnames=[str(label) for label in spec.label_names],
            registry=registry,
        )
    except Exception as e:
        error = Error.create(
            "metrics.creation_failed",
            message=f"Failed to create summary '{spec.name}'",
            metric_name=str(spec.name),
            metric_type="summary",
            error=str(e),
        )
        raise RegistrationError(error.context) from e

    # Create instance
    instance = MetricInstance(
        spec=spec,
        prometheus_metric=summary,
        created_at=TimestampUTC.now(),
    )

    # Register and log
    await register_metric(instance)
    logger = await _get_logger()
    await logger.info(
        "Summary registered",
        name=str(spec.name),
        labels=[str(label) for label in spec.label_names],
    )

    return instance


@beartype
async def create_info(
    spec: Annotated[MetricSpec, "Metric specification"],
    config: Annotated[MetricConfig | None, "Optional configuration"] = None,
) -> MetricInstance:
    """
    Create an Info metric.

    Info metrics expose static labels for metadata. Use for: version info, build info, configuration.

    Args:     spec: Metric specification with name, description, and labels     config: Optional configuration (unused
    for info)

    Returns:     MetricInstance wrapping the prometheus Info

    Raises:     RegistrationError: If metric already exists     ValidationError: If spec is invalid     MetricTypeError:
    If spec type is not INFO

    Note:     Info metrics in prometheus are special - they always have     a value of 1 and are used solely for their
    labels.

    """
    if spec.type != MetricType.INFO:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected INFO type, got {spec.type.value}",
            expected="info",
            actual=spec.type.value,
        )
        raise MetricTypeError(error.context)

    _validate_spec(spec)

    # Check if already exists
    if await metric_exists(spec.name):
        error = Error.create(
            "metrics.already_registered",
            message=f"Metric '{spec.name}' is already registered",
            metric_name=str(spec.name),
        )
        raise RegistrationError(error.context)

    # Create prometheus info
    try:
        registry = await get_registry()
        info = Info(
            str(spec.name),
            spec.description,
            labelnames=[str(label) for label in spec.label_names],
            registry=registry,
        )
    except Exception as e:
        error = Error.create(
            "metrics.creation_failed",
            message=f"Failed to create info '{spec.name}'",
            metric_name=str(spec.name),
            metric_type="info",
            error=str(e),
        )
        raise RegistrationError(error.context) from e

    # Create instance
    instance = MetricInstance(
        spec=spec,
        prometheus_metric=info,
        created_at=TimestampUTC.now(),
    )

    # Register and log
    await register_metric(instance)
    logger = await _get_logger()
    await logger.info(
        "Info metric registered",
        name=str(spec.name),
        labels=[str(label) for label in spec.label_names],
    )

    return instance


# --- Exports ------------------------------------------------------------------

__all__ = [
    "create_counter",
    "create_gauge",
    "create_histogram",
    "create_info",
    "create_summary",
]

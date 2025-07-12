"""
Title         : operations.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/metrics/operations.py

Description
-----------
Async operations for prometheus metrics manipulation.
Provides clean wrappers for incrementing, setting, and observing metric values.
"""

# pyright: reportPrivateUsage=false

from __future__ import annotations

import time
from contextlib import asynccontextmanager
from typing import TYPE_CHECKING, Annotated, Any, cast

from beartype import beartype

from mzn.errors.namespace import Error
from mzn.log.namespace import Log
from mzn.metrics.exceptions import MetricError, MetricTypeError, ValidationError
from mzn.metrics.registry import get_metric
from mzn.metrics.types import (
    MetricLabelKey,
    MetricLabelValue,
    MetricName,
    MetricType,
    MetricValue,
)


if TYPE_CHECKING:
    from collections.abc import AsyncIterator

    from prometheus_client import Counter, Gauge, Histogram, Info, Summary

    from mzn.log.core import Logger
    from mzn.metrics.types import MetricInstance


# --- Module State -------------------------------------------------------------

_DEFAULT_INCREMENT = MetricValue(1.0)
_logger: Logger | None = None


# --- Internal Helpers ---------------------------------------------------------


async def _get_logger() -> Logger:
    """Get or create the metrics logger (cached)."""
    if _logger is None:

        logger = await Log.console("metrics.operations").level(Log.Level.INFO).build()
        globals()["_logger"] = logger
        return logger
    return _logger


def _validate_labels(
    instance: Annotated[MetricInstance, "Metric instance to validate against"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Labels to validate"],
) -> dict[str, str]:
    """
    Validate and convert labels to prometheus format.

    Args:
        instance: The metric instance
        labels: Label key-value pairs to validate

    Returns:
        Dictionary with string keys/values for prometheus

    Raises:
        ValidationError: If labels don't match spec
    """
    expected_labels = {str(label) for label in instance.spec.label_names}

    if not labels and expected_labels:
        error = Error.create(
            "metrics.missing_labels",
            message=f"Metric '{instance.spec.name}' requires labels: {expected_labels}",
            metric_name=str(instance.spec.name),
            required_labels=list(expected_labels),
        )
        raise ValidationError(error.context)

    if labels:
        provided_labels = {str(k) for k in labels}

        # Check for missing labels
        missing = expected_labels - provided_labels
        if missing:
            error = Error.create(
                "metrics.missing_labels",
                message=f"Missing required labels for metric '{instance.spec.name}': {missing}",
                metric_name=str(instance.spec.name),
                missing_labels=list(missing),
                provided_labels=list(provided_labels),
            )
            raise ValidationError(error.context)

        # Check for extra labels
        extra = provided_labels - expected_labels
        if extra:
            error = Error.create(
                "metrics.extra_labels",
                message=f"Extra labels not allowed for metric '{instance.spec.name}': {extra}",
                metric_name=str(instance.spec.name),
                extra_labels=list(extra),
                expected_labels=list(expected_labels),
            )
            raise ValidationError(error.context)

        # Convert to string dict for prometheus
        return {str(k): str(v) for k, v in labels.items()}

    return {}


async def _get_metric_with_labels(
    metric: Annotated[MetricName | str, "Metric name to retrieve"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Labels to apply"],
) -> tuple[MetricInstance, Any]:
    """
    Get metric instance and apply labels.

    Args:
        metric: Metric name
        labels: Optional labels to apply

    Returns:
        Tuple of (metric instance, prometheus metric with labels)

    Raises:
        MetricError: If metric not found
        ValidationError: If labels invalid
    """
    instance = await get_metric(metric)
    if not instance:
        error = Error.create(
            "metrics.not_found",
            message=f"Metric '{metric}' not found in registry",
            metric_name=str(metric),
        )
        raise MetricError(error.context)

    # Validate and convert labels
    label_dict = _validate_labels(instance, labels)

    # Apply labels if any
    if label_dict:
        return instance, instance.prometheus_metric.labels(**label_dict)
    return instance, instance.prometheus_metric


# --- Counter Operations -------------------------------------------------------


@beartype
async def increment(
    metric: Annotated[MetricName | str, "Counter metric name"],
    value: Annotated[MetricValue, "Amount to increment"] = _DEFAULT_INCREMENT,
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> None:
    """
    Increment a counter metric.

    Args:
        metric: Name of the counter metric
        value: Amount to increment (must be positive)
        labels: Labels to apply to the metric

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a counter
        ValidationError: If labels don't match specification or value is negative
    """
    if float(value) < 0:
        error = Error.create(
            "metrics.invalid_value",
            message="Counter increment value must be non-negative",
            value=float(value),
        )
        raise ValidationError(error.context)

    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type != MetricType.COUNTER:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected COUNTER type, got {instance.spec.type.value}",
            expected="counter",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Type assertion for mypy
    counter = cast("Counter", labeled_metric)
    counter.inc(float(value))

    logger = await _get_logger()
    await logger.debug(
        "Counter incremented",
        metric=str(metric),
        value=float(value),
        labels=labels,
    )


# --- Gauge Operations ---------------------------------------------------------


@beartype
async def gauge_set(
    metric: Annotated[MetricName | str, "Gauge metric name"],
    value: Annotated[MetricValue, "Value to set"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> None:
    """
    Set a gauge metric value.

    Args:
        metric: Name of the gauge metric
        value: Value to set
        labels: Labels to apply to the metric

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a gauge
        ValidationError: If labels don't match specification
    """
    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type != MetricType.GAUGE:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected GAUGE type, got {instance.spec.type.value}",
            expected="gauge",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Type assertion for mypy
    gauge = cast("Gauge", labeled_metric)
    gauge.set(float(value))

    logger = await _get_logger()
    await logger.debug(
        "Gauge updated",
        metric=str(metric),
        value=float(value),
        labels=labels,
    )


@beartype
async def gauge_inc(
    metric: Annotated[MetricName | str, "Gauge metric name"],
    value: Annotated[MetricValue, "Amount to increment"] = _DEFAULT_INCREMENT,
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> None:
    """
    Increment a gauge metric by value.

    Args:
        metric: Name of the gauge metric
        value: Amount to increment (can be negative)
        labels: Labels to apply to the metric

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a gauge
        ValidationError: If labels don't match specification
    """
    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type != MetricType.GAUGE:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected GAUGE type, got {instance.spec.type.value}",
            expected="gauge",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Type assertion for mypy
    gauge = cast("Gauge", labeled_metric)
    gauge.inc(float(value))

    logger = await _get_logger()
    await logger.debug(
        "Gauge incremented",
        metric=str(metric),
        value=float(value),
        labels=labels,
    )


@beartype
async def gauge_dec(
    metric: Annotated[MetricName | str, "Gauge metric name"],
    value: Annotated[MetricValue, "Amount to decrement"] = _DEFAULT_INCREMENT,
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> None:
    """
    Decrement a gauge metric by value.

    Args:
        metric: Name of the gauge metric
        value: Amount to decrement (positive value)
        labels: Labels to apply to the metric

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a gauge
        ValidationError: If labels don't match specification
    """
    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type != MetricType.GAUGE:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected GAUGE type, got {instance.spec.type.value}",
            expected="gauge",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Type assertion for mypy
    gauge = cast("Gauge", labeled_metric)
    gauge.dec(float(value))

    logger = await _get_logger()
    await logger.debug(
        "Gauge decremented",
        metric=str(metric),
        value=float(value),
        labels=labels,
    )


# --- Histogram/Summary Operations ---------------------------------------------


@beartype
async def observe(
    metric: Annotated[MetricName | str, "Histogram/Summary metric name"],
    value: Annotated[MetricValue, "Value to observe"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> None:
    """
    Observe a value for histogram or summary metrics.

    Args:
        metric: Name of the histogram/summary metric
        value: Value to observe
        labels: Labels to apply to the metric

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a histogram or summary
        ValidationError: If labels don't match specification
    """
    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type not in {MetricType.HISTOGRAM, MetricType.SUMMARY}:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected HISTOGRAM or SUMMARY type, got {instance.spec.type.value}",
            expected="histogram/summary",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Both Histogram and Summary have observe method
    labeled_metric.observe(float(value))

    logger = await _get_logger()
    await logger.debug(
        "Metric observed",
        metric_type=instance.spec.type.value,
        metric=str(metric),
        value=float(value),
        labels=labels,
    )


# --- Info Operations ----------------------------------------------------------


@beartype
async def set_info(
    metric: Annotated[MetricName | str, "Info metric name"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue], "Info labels"],
) -> None:
    """
    Set labels for an info metric.

    Info metrics are special - they always have a value of 1
    and are used solely for their labels to expose metadata.

    Args:
        metric: Name of the info metric
        labels: Labels to set (required)

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not an info metric
        ValidationError: If labels don't match specification
    """
    if not labels:
        error = Error.create(
            "metrics.missing_labels",
            message="Info metrics require labels",
            metric_name=str(metric),
        )
        raise ValidationError(error.context)

    instance = await get_metric(metric)
    if not instance:
        error = Error.create(
            "metrics.not_found",
            message=f"Metric '{metric}' not found in registry",
            metric_name=str(metric),
        )
        raise MetricError(error.context)

    # Check if this is an info metric
    if instance.spec.type != MetricType.INFO:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected INFO type, got {instance.spec.type.value}",
            expected="info",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    # Validate and convert labels
    label_dict = _validate_labels(instance, labels)

    # Set info labels
    info = cast("Info", instance.prometheus_metric)
    info.info(label_dict)

    logger = await _get_logger()
    await logger.debug(
        "Info metric updated",
        metric=str(metric),
        labels=labels,
    )


# --- Timing Context Manager ---------------------------------------------------


@asynccontextmanager
@beartype
async def time_histogram(
    metric: Annotated[MetricName | str, "Histogram metric name"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
) -> AsyncIterator[None]:
    """
    Time a block of code and record to histogram.

    Measures execution time in seconds and records it to the specified
    histogram metric.

    Args:
        metric: Name of the histogram metric
        labels: Labels to apply to the metric

    Yields:
        None

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a histogram
        ValidationError: If labels don't match specification

    Example:
        labels = {MetricLabelKey("endpoint"): MetricLabelValue("/api/users")}
        async with time_histogram("request.duration", labels):
            await process_request()
    """
    instance, labeled_metric = await _get_metric_with_labels(metric, labels)

    if instance.spec.type != MetricType.HISTOGRAM:
        error = Error.create(
            "metrics.type_mismatch",
            message=f"Expected HISTOGRAM type for timing, got {instance.spec.type.value}",
            expected="histogram",
            actual=instance.spec.type.value,
            metric_name=str(metric),
        )
        raise MetricTypeError(error.context)

    histogram = cast("Histogram", labeled_metric)
    start_time = time.perf_counter()

    try:
        yield
    finally:
        duration = time.perf_counter() - start_time
        histogram.observe(duration)

        logger = await _get_logger()
        await logger.debug(
            "Histogram timing recorded",
            metric=str(metric),
            duration_seconds=duration,
            labels=labels,
        )


@asynccontextmanager
@beartype
async def track_active(
    metric: Annotated[MetricName | str, "Gauge metric name"],
    labels: Annotated[dict[MetricLabelKey, MetricLabelValue] | None, "Metric labels"] = None,
    amount: Annotated[MetricValue, "Amount to inc/dec"] = _DEFAULT_INCREMENT,
) -> AsyncIterator[None]:
    """
    Context manager to track active operations.

    Increments gauge on enter, decrements on exit.
    Perfect for tracking active connections, sessions, running tasks, etc.

    Args:
        metric: Name of the gauge metric
        labels: Labels to apply to the metric
        amount: Amount to increment/decrement (default: 1.0)

    Yields:
        None

    Raises:
        MetricError: If metric not found
        MetricTypeError: If metric is not a gauge
        ValidationError: If labels don't match specification

    Example:
        labels = {MetricLabelKey("endpoint"): MetricLabelValue("/api/users")}
        async with track_active("websocket.connections", labels):
            await handle_websocket_connection()
    """
    await gauge_inc(metric, amount, labels)
    try:
        yield
    finally:
        await gauge_dec(metric, amount, labels)


# --- Label Helper -------------------------------------------------------------


@beartype
async def with_labels(
    metric: Annotated[MetricName | str, "Metric name"],
    **label_values: Annotated[str, "Label values"],
) -> Counter | Gauge | Histogram | Summary:
    """
    Get a metric with labels pre-applied.

    This is useful when you need to perform multiple operations
    on the same labeled metric.

    Args:
        metric: Name of the metric
        **label_values: Label key-value pairs

    Returns:
        Prometheus metric object with labels applied

    Raises:
        MetricError: If metric not found
        ValidationError: If labels don't match specification

    Example:
        user_requests = await with_labels("http.requests", method="GET", status="200")
        user_requests.inc()  # Direct prometheus API
    """
    # Convert kwargs to our typed labels
    labels = {
        MetricLabelKey(k): MetricLabelValue(v)
        for k, v in label_values.items()
    }

    _instance, labeled_metric = await _get_metric_with_labels(metric, labels)
    return cast("Counter | Gauge | Histogram | Summary", labeled_metric)


# --- Exports ------------------------------------------------------------------

__all__ = [
    "gauge_dec",
    "gauge_inc",
    "gauge_set",
    "increment",
    "observe",
    "set_info",
    "time_histogram",
    "track_active",
    "with_labels",
]

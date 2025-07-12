"""
Title         : namespace.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/metrics/namespace.py

Description
-----------
Unified metrics namespace with fluent builder pattern and static utilities.
Provides a clean API for prometheus metrics with minimal abstraction.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any, Self

from beartype import beartype

from mzn.errors.namespace import Error
from mzn.metrics.core import (
    create_counter,
    create_gauge,
    create_histogram,
    create_info,
    create_summary,
)
from mzn.metrics.decorators import metered
from mzn.metrics.exceptions import (
    ExportError,
    MetricError,
    MetricTypeError,
    RegistrationError,
    ValidationError,
)
from mzn.metrics.exporters import create_asgi_app, mount_metrics, serve
from mzn.metrics.operations import (
    gauge_dec,
    gauge_inc,
    gauge_set,
    increment,
    observe,
    set_info,
    time_histogram,
    track_active,
    with_labels,
)
from mzn.metrics.registry import (
    clear_metrics,
    generate_metrics_text,
    get_metric,
    get_registry,
    list_metrics,
    metric_exists,
    set_registry,
    unregister_metric,
)
from mzn.metrics.types import (
    MetricConfig,
    MetricInstance,
    MetricLabelKey,
    MetricLabelValue,
    MetricName,
    MetricSpec,
    MetricType,
    MetricValue,
)


if TYPE_CHECKING:
    from prometheus_client import CollectorRegistry


# --- Metric Builder -----------------------------------------------------------


class MetricBuilder:
    """
    Fluent builder for metric configuration.

    Provides a chainable API for configuring metrics
    with validation at each step.
    """

    def __init__(
        self,
        metric_type: Annotated[MetricType, "Type of metric to create"],
        name: Annotated[str, "Metric name"],
    ) -> None:
        """Initialize builder with metric type and name.

        Args:
            metric_type: Type of metric (counter, gauge, histogram, summary, info)
            name: Metric name following prometheus conventions
        """
        super().__init__()
        self._type = metric_type
        self._name = MetricName(name)
        self._description = ""
        self._label_names: list[MetricLabelKey] = []
        self._config = MetricConfig()

    @beartype
    def description(self, text: Annotated[str, "Metric description"]) -> Self:
        """Set metric description.

        Args:
            text: Human-readable description

        Returns:
            Builder instance for method chaining
        """
        self._description = text
        return self

    @beartype
    def labels(self, *names: Annotated[str, "Label names"]) -> Self:
        """Set metric label names.

        Args:
            *names: Variable number of label names

        Returns:
            Builder instance for method chaining
        """
        self._label_names = [MetricLabelKey(name) for name in names]
        return self

    @beartype
    def buckets(self, *values: Annotated[float, "Bucket boundaries"]) -> Self:
        """Set histogram bucket boundaries.

        Only valid for histogram metrics.

        Args:
            *values: Bucket boundary values in ascending order

        Returns:
            Builder instance for method chaining
        """
        if self._type != MetricType.HISTOGRAM:
            error = Error.create(
                "metrics.invalid_config",
                message="Buckets can only be set for histogram metrics",
                metric_type=self._type.value,
            )
            raise ValidationError(error.context)
        self._config.histogram_buckets = list(values)
        return self

    @beartype
    def initial_value(self, value: Annotated[float, "Initial gauge value"]) -> Self:
        """Set initial value for gauge metric.

        Only valid for gauge metrics.

        Args:
            value: Initial gauge value

        Returns:
            Builder instance for method chaining
        """
        if self._type != MetricType.GAUGE:
            error = Error.create(
                "metrics.invalid_config",
                message="Initial value can only be set for gauge metrics",
                metric_type=self._type.value,
            )
            raise ValidationError(error.context)
        self._config.initial_gauge_value = MetricValue(value)
        return self

    @beartype
    async def build(self) -> MetricInstance:
        """Build the configured metric.

        Returns:
            Created metric instance

        Raises:
            RegistrationError: If metric already exists
            ValidationError: If configuration is invalid
        """
        # Create metric spec
        spec = MetricSpec(
            name=self._name,
            type=self._type,
            description=self._description or f"{self._type.value} metric",
            label_names=self._label_names,
        )

        # Create appropriate metric type
        if self._type == MetricType.COUNTER:
            return await create_counter(spec, self._config)
        if self._type == MetricType.GAUGE:
            return await create_gauge(spec, self._config)
        if self._type == MetricType.HISTOGRAM:
            return await create_histogram(spec, self._config)
        if self._type == MetricType.SUMMARY:
            return await create_summary(spec, self._config)
        if self._type == MetricType.INFO:
            return await create_info(spec, self._config)
        error = Error.create(
            "metrics.unsupported_type",
            message=f"Unsupported metric type: {self._type}",
            metric_type=self._type.value,
        )
        raise MetricTypeError(error.context)


# --- Metrics Class ------------------------------------------------------------


class Metrics:
    """
    Unified namespace for all metrics functionality.

    Provides fluent builder pattern, static utilities, and
    direct access to prometheus metrics with minimal abstraction.

    Example:
        # Fluent API
        counter = await Metrics.counter("api.requests")
            .description("Total API requests")
            .labels("method", "status")
            .build()

        # Quick operations
        await Metrics.inc("api.requests", {"method": "GET", "status": "200"})
        await Metrics.observe("request.duration", 0.123)

        # Context managers
        async with Metrics.time("db.query.duration"):
            async with Metrics.track_active("active.connections"):
                await process_request()

        # Export metrics
        app = await Metrics.asgi()  # ASGI app for /metrics
        await Metrics.serve(port=9090)  # Standalone server

        # Use decorator
        @Metrics.metered(metric="operation", track_calls=True, track_errors=True)
        async def process_data(data: dict) -> dict:
            return transformed_data
    """

    # --- Core State -----------------------------------------------------------
    # Note: We use the registry module for state management, not local storage

    # --- Types ----------------------------------------------------------------
    Type = MetricType
    Spec = MetricSpec
    Config = MetricConfig
    Instance = MetricInstance
    Name = MetricName
    LabelKey = MetricLabelKey
    LabelValue = MetricLabelValue
    Value = MetricValue

    # --- Exceptions -----------------------------------------------------------
    Error = MetricError
    RegistrationError = RegistrationError
    ValidationError = ValidationError
    ExportError = ExportError
    TypeMismatchError = MetricTypeError

    # --- Decorator ------------------------------------------------------------
    metered = staticmethod(metered)

    # --- Fluent Factory Methods -----------------------------------------------

    @classmethod
    @beartype
    def counter(cls, name: Annotated[str, "Counter metric name"]) -> MetricBuilder:
        """Create fluent builder for counter metric.

        Counters only go up and are reset on restart.

        Args:
            name: Metric name following prometheus conventions

        Returns:
            MetricBuilder configured for counter
        """
        return MetricBuilder(MetricType.COUNTER, name)

    @classmethod
    @beartype
    def gauge(cls, name: Annotated[str, "Gauge metric name"]) -> MetricBuilder:
        """Create fluent builder for gauge metric.

        Gauges can go up or down and represent current state.

        Args:
            name: Metric name following prometheus conventions

        Returns:
            MetricBuilder configured for gauge
        """
        return MetricBuilder(MetricType.GAUGE, name)

    @classmethod
    @beartype
    def histogram(cls, name: Annotated[str, "Histogram metric name"]) -> MetricBuilder:
        """Create fluent builder for histogram metric.

        Histograms track distributions with configurable buckets.

        Args:
            name: Metric name following prometheus conventions

        Returns:
            MetricBuilder configured for histogram
        """
        return MetricBuilder(MetricType.HISTOGRAM, name)

    @classmethod
    @beartype
    def summary(cls, name: Annotated[str, "Summary metric name"]) -> MetricBuilder:
        """Create fluent builder for summary metric.

        Summaries calculate quantiles over sliding windows.

        Args:
            name: Metric name following prometheus conventions

        Returns:
            MetricBuilder configured for summary
        """
        return MetricBuilder(MetricType.SUMMARY, name)

    @classmethod
    @beartype
    def info(cls, name: Annotated[str, "Info metric name"]) -> MetricBuilder:
        """Create fluent builder for info metric.

        Info metrics expose static labels for metadata.

        Args:
            name: Metric name following prometheus conventions

        Returns:
            MetricBuilder configured for info
        """
        return MetricBuilder(MetricType.INFO, name)

    # --- Direct Operations ----------------------------------------------------

    @classmethod
    @beartype
    async def inc(
        cls,
        metric: Annotated[str, "Counter metric name"],
        labels: Annotated[dict[str, str] | None, "Metric labels"] = None,
        value: Annotated[float, "Amount to increment"] = 1.0,
    ) -> None:
        """Increment a counter metric.

        Args:
            metric: Counter metric name
            labels: Optional labels as string dict
            value: Amount to increment (must be positive)

        Raises:
            MetricError: If metric not found
            MetricTypeError: If metric is not a counter
            ValidationError: If value is negative
        """
        metric_labels = None
        if labels:
            metric_labels = {
                MetricLabelKey(k): MetricLabelValue(v)
                for k, v in labels.items()
            }
        await increment(MetricName(metric), MetricValue(value), metric_labels)

    @classmethod
    @beartype
    async def set(
        cls,
        metric: Annotated[str, "Gauge metric name"],
        value: Annotated[float, "Value to set"],
        labels: Annotated[dict[str, str] | None, "Metric labels"] = None,
    ) -> None:
        """Set a gauge metric value.

        Args:
            metric: Gauge metric name
            value: Value to set
            labels: Optional labels as string dict

        Raises:
            MetricError: If metric not found
            MetricTypeError: If metric is not a gauge
        """
        metric_labels = None
        if labels:
            metric_labels = {
                MetricLabelKey(k): MetricLabelValue(v)
                for k, v in labels.items()
            }
        await gauge_set(MetricName(metric), MetricValue(value), metric_labels)

    @classmethod
    @beartype
    async def observe(
        cls,
        metric: Annotated[str, "Histogram/Summary metric name"],
        value: Annotated[float, "Value to observe"],
        labels: Annotated[dict[str, str] | None, "Metric labels"] = None,
    ) -> None:
        """Observe a value for histogram or summary.

        Args:
            metric: Histogram or summary metric name
            value: Value to observe
            labels: Optional labels as string dict

        Raises:
            MetricError: If metric not found
            MetricTypeError: If metric is not histogram/summary
        """
        metric_labels = None
        if labels:
            metric_labels = {
                MetricLabelKey(k): MetricLabelValue(v)
                for k, v in labels.items()
            }
        await observe(MetricName(metric), MetricValue(value), metric_labels)

    # --- Context Managers -----------------------------------------------------

    @classmethod
    def time(
        cls,
        metric: Annotated[str, "Histogram metric name"],
        labels: Annotated[dict[str, str] | None, "Metric labels"] = None,
    ) -> Any:  # noqa: ANN401
        """Context manager to time a block of code.

        Args:
            metric: Histogram metric name
            labels: Optional labels as string dict

        Returns:
            Async context manager

        Example:
            async with Metrics.time("db.query.duration", {"query": "select"}):
                result = await db.execute(query)
        """
        metric_labels = None
        if labels:
            metric_labels = {
                MetricLabelKey(k): MetricLabelValue(v)
                for k, v in labels.items()
            }
        return time_histogram(MetricName(metric), metric_labels)

    @classmethod
    def track_active(
        cls,
        metric: Annotated[str, "Gauge metric name"],
        labels: Annotated[dict[str, str] | None, "Metric labels"] = None,
        amount: Annotated[float, "Amount to inc/dec"] = 1.0,
    ) -> Any:  # noqa: ANN401
        """Context manager to track active operations.

        Increments gauge on enter, decrements on exit.
        Perfect for tracking active connections, sessions, running tasks, etc.

        Args:
            metric: Gauge metric name
            labels: Optional labels as string dict
            amount: Amount to increment/decrement

        Returns:
            Async context manager

        Example:
            async with Metrics.track_active("websocket.connections"):
                await handle_websocket_connection()
        """
        metric_labels = None
        if labels:
            metric_labels = {
                MetricLabelKey(k): MetricLabelValue(v)
                for k, v in labels.items()
            }
        return track_active(MetricName(metric), metric_labels, MetricValue(amount))

    # --- Export Utilities -----------------------------------------------------

    @classmethod
    @beartype
    async def asgi(
        cls,
        registry: Annotated[CollectorRegistry | None, "Prometheus registry"] = None,
    ) -> Any:  # noqa: ANN401
        """Create ASGI app for metrics endpoint.

        Args:
            registry: Optional custom registry (uses default if None)

        Returns:
            ASGI application serving metrics at /metrics
        """
        return await create_asgi_app(registry)

    @classmethod
    @beartype
    async def serve(
        cls,
        host: Annotated[str, "Host to bind to"] = "0.0.0.0",  # noqa: S104
        port: Annotated[int, "Port to serve on"] = 9090,
    ) -> None:
        """Start standalone metrics server.

        Args:
            host: Host address to bind to
            port: Port number to serve on

        Note:
            This function blocks until interrupted
        """
        await serve(host, port)

    @classmethod
    @beartype
    async def fastapi(
        cls,
        app: Annotated[Any, "FastAPI application"],  # noqa: ANN401
        path: Annotated[str, "Metrics endpoint path"] = "/metrics",
    ) -> None:
        """Add metrics endpoint to FastAPI app.

        Args:
            app: FastAPI application instance
            path: Path for metrics endpoint
        """
        await mount_metrics(app, path)

    # --- Registry Management --------------------------------------------------

    @classmethod
    @beartype
    async def get_metric(
        cls,
        name: Annotated[str, "Metric name"],
    ) -> MetricInstance | None:
        """Get a metric instance by name.

        Args:
            name: Metric name to retrieve

        Returns:
            Metric instance if found, None otherwise
        """
        return await get_metric(MetricName(name))

    @classmethod
    @beartype
    async def list_metrics(cls) -> list[str]:
        """List all registered metric names.

        Returns:
            List of metric names
        """
        metrics = await list_metrics()
        return [str(m.spec.name) for m in metrics]

    @classmethod
    @beartype
    async def clear(cls) -> None:
        """Clear all metrics from registry.

        Warning: This removes all metrics and their data.
        """
        await clear_metrics()

    @classmethod
    @beartype
    async def export_text(cls) -> str:
        """Export metrics in prometheus text format.

        Returns:
            Metrics in prometheus exposition format
        """
        return await generate_metrics_text()

    # --- Advanced Operations --------------------------------------------------

    # Re-export operations without clean alternatives
    gauge_inc = staticmethod(gauge_inc)
    gauge_dec = staticmethod(gauge_dec)
    set_info = staticmethod(set_info)
    with_labels = staticmethod(with_labels)

    # Re-export registry functions
    get_registry = staticmethod(get_registry)
    set_registry = staticmethod(set_registry)
    metric_exists = staticmethod(metric_exists)
    unregister_metric = staticmethod(unregister_metric)


# --- Exports ------------------------------------------------------------------

__all__ = ["Metrics"]

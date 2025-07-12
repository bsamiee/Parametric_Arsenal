"""
Title         : models.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/metrics/models.py.

Description ----------- Pydantic models for the metrics package.

Provides structured models for metric definitions, data points, and configurations. All models use validated types from
the aliases module.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any

from pydantic import BaseModel, Field

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


if TYPE_CHECKING:
    from mzn.types.packages.general.aliases import TimestampUTC
    from mzn.types.packages.metrics.aliases import (
        MetricLabelKey,
        MetricName,
        MetricValue,
    )
    from mzn.types.packages.metrics.enums import MetricType


# --- Metric Specification Model -----------------------------------------------


@Build.model(
    description="Specification for creating a prometheus-client metric",
    tags=(SYSTEM.METRICS, SYSTEM.COMMON.metadata),
)
class MetricSpec(BaseModel):
    """
    Specification for creating prometheus-client metrics.

    This model defines the metadata needed to create a Counter, Gauge, Histogram, or Summary using prometheus-client. It
    enforces our naming conventions and adds metadata that prometheus doesn't track.

    Use with prometheus-client:     spec = MetricSpec(name="api_requests_total", ...)     counter = Counter(spec.name,
    spec.description, spec.label_names)

    """

    # Core prometheus requirements
    name: Annotated[MetricName, Field(description="Metric name following our conventions")]
    type: Annotated[MetricType, Field(description="Prometheus metric type (Counter, Gauge, etc.)")]
    description: Annotated[str, Field(description="Help text for prometheus metric (required)")]

    # Label schema definition
    label_names: Annotated[
        list[MetricLabelKey],
        Field(default_factory=list, description="Allowed label keys for this metric"),
    ]


# --- Metric Configuration Model -----------------------------------------------


@Build.model(
    description="Configuration for prometheus metric initialization",
    tags=(SYSTEM.METRICS, SYSTEM.CONFIG),
)
class MetricConfig(BaseModel):
    """
    Configuration for creating prometheus-client metrics.

    This model holds type-specific configuration that prometheus-client requires when creating certain metric types.

    Use with prometheus-client:     config = MetricConfig(histogram_buckets=[0.1, 0.5, 1.0])     histogram =
    Histogram(..., buckets=config.histogram_buckets)

    """

    # Histogram-specific configuration
    histogram_buckets: Annotated[
        list[float] | None,
        Field(
            default=None,
            description="Bucket boundaries for Histogram metrics (e.g., [0.1, 0.5, 1.0, 5.0])",
        ),
    ]

    # Initial value for gauges
    initial_gauge_value: Annotated[
        MetricValue | None,
        Field(default=None, description="Initial value for Gauge metrics"),
    ]


# --- Metric Instance Model ----------------------------------------------------


@Build.model(
    description="Runtime wrapper for an initialized prometheus metric",
    tags=(SYSTEM.METRICS, SYSTEM.INFRA),
    model_config={"arbitrary_types_allowed": True},  # Allow prometheus objects
)
class MetricInstance(BaseModel):
    """
    Runtime wrapper for initialized prometheus-client metrics.

    This model wraps the actual prometheus Counter/Gauge/Histogram/Summary object with our metadata and tracking
    information. It provides a type-safe way to manage metric lifecycle.

    Use pattern:     spec = MetricSpec(...)     prom_metric = Counter(spec.name, spec.description)     instance =
    MetricInstance(spec=spec, prometheus_metric=prom_metric)

    """

    # Metric specification
    spec: Annotated[MetricSpec, Field(description="The specification used to create this metric")]

    # The actual prometheus metric object
    prometheus_metric: Annotated[
        Any,
        Field(
            description="The actual prometheus-client Counter/Gauge/Histogram/Summary instance"
        ),
    ]

    # Runtime tracking
    created_at: Annotated[TimestampUTC, Field(description="When this metric was initialized")]


# --- Exports ------------------------------------------------------------------

__all__ = [
    "MetricConfig",
    "MetricInstance",
    "MetricSpec",
]

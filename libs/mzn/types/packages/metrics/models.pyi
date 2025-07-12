from typing import Any

from pydantic import BaseModel

from mzn.types.packages.general.aliases import TimestampUTC
from mzn.types.packages.metrics.aliases import (
    MetricLabelKey,
    MetricName,
    MetricValue,
)
from mzn.types.packages.metrics.enums import MetricType

class MetricSpec(BaseModel):
    name: MetricName
    type: MetricType
    description: str
    label_names: list[MetricLabelKey] = []

class MetricConfig(BaseModel):
    histogram_buckets: list[float] | None = None
    initial_gauge_value: MetricValue | None = None

class MetricInstance(BaseModel):
    spec: MetricSpec
    prometheus_metric: Any
    created_at: TimestampUTC

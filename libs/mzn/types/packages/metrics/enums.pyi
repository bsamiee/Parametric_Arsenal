from aenum import StrEnum

class MetricType(StrEnum):
    COUNTER: MetricType
    GAUGE: MetricType
    HISTOGRAM: MetricType
    SUMMARY: MetricType
    INFO: MetricType
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

__all__ = [
    "MetricType",
]

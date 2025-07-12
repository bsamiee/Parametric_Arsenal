"""
Title         : enums.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/packages/metrics/enums.py

Description
-----------
Enumerations for the metrics package.

Provides rich enums for metric types and units of measurement.
All enums include metadata for enhanced functionality.
"""

from __future__ import annotations

import aenum

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


@Build.enum(
    base_type=aenum.StrEnum,
    description="Types of metrics with their behavior characteristics.",
    tags=(SYSTEM.METRICS,),
    enable_caching=True,
)
class MetricType(aenum.StrEnum):
    """Types of metrics with their behavior characteristics."""

    COUNTER = "counter"     # Monotonically increasing value
    GAUGE = "gauge"         # Value that can go up or down (default)
    HISTOGRAM = "histogram"  # Distribution of values
    SUMMARY = "summary"     # Statistical summary of observations
    INFO = "info"           # Static metadata exposed as labels


# --- Exports ------------------------------------------------------------------

__all__ = [
    "MetricType",
]

"""
Title         : aliases.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/packages/metrics/aliases.py

Description
-----------
Type aliases for the metrics package, providing validated domain-specific types
for metric names, values, timestamps, and configuration parameters.
"""

from mzn.types._core.core_builders import Build
from mzn.types._core.core_operations import MethodConfig
from mzn.types._core.core_tags import SYSTEM
from mzn.types.primitives.prim_standard import PrimFloat, PrimStr
from mzn.types.rules.rule_composites import And
from mzn.types.rules.rule_registry import NORM, VALID


# --- Metric Identifiers -------------------------------------------------------


@Build.alias(
    base=PrimStr,
    operations=None,
    rules=[
        # Normalization - convert common patterns to prometheus format
        NORM.STRING.strip_whitespace(),
        NORM.STRING.replace(old=".", new="_"),  # Convert dots to underscores
        NORM.STRING.replace(old="-", new="_"),  # Convert hyphens to underscores
        NORM.STRING.replace(old=" ", new="_"),  # Convert spaces to underscores
        NORM.STRING.deduplicate_separators(),  # Remove __, ::
        # Validation - prometheus naming rules
        VALID.STRING.matches_pattern(pattern=r"^[a-zA-Z_:][a-zA-Z0-9_:]*$"),
        VALID.STRING.has_length(min_length=1, max_length=128),
        VALID.CORE.is_not_one_of(forbidden_values=["__name__"]),  # Reserved by prometheus
    ],
    tags=(SYSTEM.METRICS, SYSTEM.COMMON.identity),
    description="A prometheus-compliant metric name",
    enable_caching=True,
)
class MetricName:
    """Prometheus metric name following [a-zA-Z_:][a-zA-Z0-9_:]* pattern."""


@Build.alias(
    base=PrimStr,
    operations=None,
    rules=[
        # Normalization
        NORM.STRING.strip_whitespace(),
        NORM.STRING.to_snake_case(),  # Convert to snake_case for consistency
        # Validation
        VALID.STRING.matches_pattern(pattern=r"^[a-zA-Z][a-zA-Z0-9_]*$"),
        VALID.STRING.has_length(min_length=1, max_length=32),
        VALID.STRING.no_consecutive_chars(char="_"),  # Prevent "__" in keys
        VALID.CORE.is_not_one_of(
            forbidden_values=[
                # Reserved keywords
                "class",
                "type",
                "id",
                "name",
                "value",
                "timestamp",
                # Common metric dimensions that should be explicit
                "metric",
                "label",
                "tag",
                "key",
            ]
        ),
    ],
    tags=(SYSTEM.METRICS, SYSTEM.COMMON.metadata),
    description="A metric label key for dimensional data",
)
class MetricLabelKey:
    """Key for metric labels/tags (e.g., 'endpoint', 'status_code')."""


@Build.alias(
    base=PrimStr,
    operations=None,
    rules=[
        # Normalization
        NORM.STRING.strip_whitespace(),
        NORM.STRING.normalize_whitespace(),  # Collapse multiple spaces
        NORM.STRING.remove_control_characters(),  # Remove non-printable chars
        # Validation
        VALID.STRING.has_length(max_length=256),
        VALID.STRING.is_ascii_printable(),
        # Prevent injection-like patterns
        And(
            VALID.CORE.is_not_one_of(forbidden_values=["null", "undefined", "none", "nil"]),
            VALID.STRING.has_no_leading_trailing_whitespace(),  # After normalization
        ),
    ],
    tags=(SYSTEM.METRICS, SYSTEM.COMMON.metadata),
    description="A metric label value with sanitization",
)
class MetricLabelValue:
    """Value for metric labels/tags with minimal restrictions."""


# --- Metric Values ------------------------------------------------------------


@Build.alias(
    base=PrimFloat,
    rules=[
        VALID.NUMERIC.is_finite(),
    ],
    operations=MethodConfig(
        arithmetic=True,
        casting=True,
    ),
    tags=(SYSTEM.METRICS, SYSTEM.COMMON.operations),
    description="A prometheus metric value (float64)",
)
class MetricValue:
    """Prometheus metric value supporting all metric types (Counter, Gauge, Histogram, Summary)."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "MetricLabelKey",
    "MetricLabelValue",
    "MetricName",
    "MetricValue",
]

"""
Title         : exceptions.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/metrics/exceptions.py.

Description ----------- Metrics-specific exceptions for the mzn.metrics package.

"""

from __future__ import annotations

from mzn.errors.exceptions import MznError


# --- Base Metrics Exception ---------------------------------------------------

class MetricError(MznError):
    """Base exception for all metrics-related errors."""

# --- Specific Exceptions ------------------------------------------------------


class RegistrationError(MetricError):
    """Raised when metric registration fails (e.g., duplicate metric name)."""


class ValidationError(MetricError):
    """Raised when metric validation fails (e.g., invalid labels, values)."""


class ExportError(MetricError):
    """Raised when metric export fails (e.g., prometheus endpoint issues)."""


class MetricTypeError(MetricError):
    """Raised when operations are incompatible with the metric type."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ExportError",
    "MetricError",
    "MetricTypeError",
    "RegistrationError",
    "ValidationError",
]

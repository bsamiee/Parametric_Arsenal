"""
Title         : exceptions.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/log/exceptions.py

Description
-----------
Minimal log-specific exceptions using the new error system.
"""

from __future__ import annotations

from mzn.errors.exceptions import MznError


# --- Base Log Error -----------------------------------------------------------


class LogError(MznError):
    """Base exception for all logging-related errors."""


# --- Specific Log Errors ------------------------------------------------------


class ConfigError(LogError):
    """Raised when log configuration is invalid or fails."""


class HandlerError(LogError):
    """Raised when handler operations fail."""


class FormatError(LogError):
    """Raised when log formatting or serialization fails."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ConfigError",
    "FormatError",
    "HandlerError",
    "LogError",
]

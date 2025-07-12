"""
Title         : exceptions.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/cache/exceptions.py.

Description ----------- Minimal cache-specific exceptions using the new error system.

"""

from __future__ import annotations

from mzn.errors.exceptions import MznError


# --- Base Cache Error ---------------------------------------------------------


class CacheError(MznError):
    """Base exception for all cache-related errors."""


# --- Specific Cache Errors ----------------------------------------------------


class CacheKeyError(CacheError):
    """Raised when there's an issue with a cache key."""


class CacheBackendError(CacheError):
    """Raised when the cache backend encounters an error."""


class CacheSerializationError(CacheError):
    """Raised when serialization/deserialization fails."""


class CacheConnectionError(CacheBackendError):
    """Raised when unable to connect to cache backend."""


class CacheConfigError(CacheError):
    """Raised when cache configuration is invalid."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "CacheBackendError",
    "CacheConfigError",
    "CacheConnectionError",
    "CacheError",
    "CacheKeyError",
    "CacheSerializationError",
]

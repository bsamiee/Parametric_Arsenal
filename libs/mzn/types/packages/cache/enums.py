"""
Title         : enums.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/cache/enums.py.

Description ----------- Simplified cache enumerations for the new cache implementation. Only includes what's actually
used, with clear, modern design.

"""

from __future__ import annotations

import aenum

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


# --- Backend Types ------------------------------------------------------------

@Build.enum(
    base_type=aenum.StrEnum,
    description="Cache backend implementations.",
    tags=(SYSTEM.CACHE, SYSTEM.INFRA.backend),
)
class CacheBackend(aenum.StrEnum):
    """Cache backend implementations."""

    MEMORY = "memory"       # In-memory cache using aiocache (default)
    REDIS = "redis"         # Redis-based distributed cache
    CACHEBOX = "cachebox"   # High-performance Rust-based cache
    DISK = "disk"           # Disk-based persistent cache


# --- Serialization Formats ----------------------------------------------------

@Build.enum(
    base_type=aenum.StrEnum,
    description="Serialization formats for cache values.",
    tags=(SYSTEM.CACHE, SYSTEM.INFRA.serialization),
)
class SerializationFormat(aenum.StrEnum):
    """Serialization formats for cache values."""

    PICKLE = "pickle"   # Python pickle format (default for Redis)
    JSON = "json"       # JSON format using orjson


# --- Eviction Policies --------------------------------------------------------

@Build.enum(
    base_type=aenum.StrEnum,
    description="Cache eviction policies.",
    tags=(SYSTEM.CACHE, SYSTEM.INFRA.memory),
)
class EvictionPolicy(aenum.StrEnum):
    """Cache eviction policies."""

    LRU = "lru"     # Least Recently Used (default)
    LFU = "lfu"     # Least Frequently Used
    FIFO = "fifo"   # First In, First Out


# --- Exports ------------------------------------------------------------------

__all__ = [
    "CacheBackend",
    "EvictionPolicy",
    "SerializationFormat",
]

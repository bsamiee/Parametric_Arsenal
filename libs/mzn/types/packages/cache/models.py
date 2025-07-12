"""
Title         : models.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/cache/models.py.

Description ----------- Simplified cache models for the new cache implementation. Only includes what's actually needed,
with clear, modern design.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated

from pydantic import BaseModel, ConfigDict, Field

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.packages.cache.enums import CacheBackend, EvictionPolicy, SerializationFormat


if TYPE_CHECKING:
    from datetime import timedelta

    # Type aliases for clarity
    type CacheKey = str
    type CacheNamespace = str
    type CacheTTL = timedelta
    type EntryCount = int
    type HitRatePercentage = float
    type MaxEntries = int
    type OperationCount = int
    type RedisConnectionURL = str
    type FilePath = str


# --- Cache Statistics Model ---------------------------------------------------

@Build.model(
    description="Cache performance statistics.",
    tags=(SYSTEM.CACHE, SYSTEM.METRICS.performance),
)
class CacheStats(BaseModel):
    """Cache performance statistics."""

    model_config = ConfigDict(
        validate_assignment=True,
    )

    # Operation counts
    hits: Annotated[OperationCount, Field(description="Cache hits")]
    misses: Annotated[OperationCount, Field(description="Cache misses")]
    sets: Annotated[OperationCount, Field(description="Set operations")]
    deletes: Annotated[OperationCount, Field(description="Delete operations")]

    # Performance metrics
    hit_rate: Annotated[HitRatePercentage, Field(description="Hit rate percentage")]
    total_entries: Annotated[EntryCount, Field(description="Current number of entries")]


# --- Cache Configuration Model ------------------------------------------------

@Build.model(
    description="Streamlined cache configuration.",
    tags=(SYSTEM.CACHE, SYSTEM.CONFIG),
)
class CacheConfig(BaseModel):
    """Streamlined cache configuration."""

    model_config = ConfigDict(
        validate_assignment=True,
        arbitrary_types_allowed=True,
    )

    # Core configuration
    backend: Annotated[CacheBackend, Field(description="Backend type")]
    namespace: Annotated[CacheNamespace, Field(default="default", description="Key namespace for segregation")]

    # Common settings
    default_ttl: Annotated[CacheTTL | None, Field(default=None, description="Default TTL")]
    max_entries: Annotated[MaxEntries | None, Field(default=None, description="Maximum entries")]

    # Backend-specific settings
    eviction_policy: Annotated[EvictionPolicy, Field(default=EvictionPolicy.LRU, description="Eviction policy")]
    serialization: Annotated[
        SerializationFormat,
        Field(
            default=SerializationFormat.PICKLE,
            description="Serialization format",
        ),
    ]

    # Redis-specific
    redis_url: Annotated[RedisConnectionURL | None, Field(default=None, description="Redis connection URL")]

    # Disk-specific
    disk_path: Annotated[FilePath | None, Field(default=None, description="Disk cache directory")]


# --- Exports ------------------------------------------------------------------

__all__ = [
    "CacheConfig",
    "CacheStats",
]

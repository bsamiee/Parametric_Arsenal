from typing import Annotated

from pydantic import BaseModel, Field

from mzn.types.packages.cache.aliases import (
    CacheNamespace,
    CacheTTL,
    EntryCount,
    HitRatePercentage,
    MaxEntries,
    OperationCount,
    RedisConnectionURL,
)
from mzn.types.packages.cache.enums import CacheBackend, EvictionPolicy, SerializationFormat
from mzn.types.packages.general.aliases import FilePath

class CacheStats(BaseModel):
    hits: Annotated[OperationCount, Field(description="Cache hits")]
    misses: Annotated[OperationCount, Field(description="Cache misses")]
    sets: Annotated[OperationCount, Field(description="Set operations")]
    deletes: Annotated[OperationCount, Field(description="Delete operations")]
    hit_rate: Annotated[HitRatePercentage, Field(description="Hit rate percentage")]
    total_entries: Annotated[EntryCount, Field(description="Current number of entries")]

class CacheConfig(BaseModel):
    backend: Annotated[CacheBackend, Field(description="Backend type")]
    namespace: Annotated[CacheNamespace, Field(default="default", description="Key namespace for segregation")]
    default_ttl: Annotated[CacheTTL | None, Field(default=None, description="Default TTL")]
    max_entries: Annotated[MaxEntries | None, Field(default=None, description="Maximum entries")]
    eviction_policy: Annotated[EvictionPolicy, Field(default=EvictionPolicy.LRU, description="Eviction policy")]
    serialization: Annotated[
        SerializationFormat,
        Field(
            default=SerializationFormat.PICKLE,
            description="Serialization format"
        )
    ]
    redis_url: Annotated[RedisConnectionURL | None, Field(default=None, description="Redis connection URL")]
    disk_path: Annotated[FilePath | None, Field(default=None, description="Disk cache directory")]

__all__ = [
    "CacheConfig",
    "CacheStats",
]

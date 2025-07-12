"""
Title         : types.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/cache/types.py.

Description ----------- Re-exports of cache-specific type assets from the types package.

"""

# --- Cache-specific aliases --------------------------------------------------
from mzn.types.packages.cache.aliases import (
    CacheKey,
    CacheNamespace,
    CacheTTL,
    EntryCount,
    HitRatePercentage,
    KeyPattern,
    MaxEntries,
    OperationCount,
    RedisConnectionURL,
)

# --- Cache-specific enums ----------------------------------------------------
from mzn.types.packages.cache.enums import (
    CacheBackend,
    EvictionPolicy,
    SerializationFormat,
)

# --- Cache-specific models ---------------------------------------------------
from mzn.types.packages.cache.models import (
    CacheConfig,
    CacheStats,
)

# --- General aliases ---------------------------------------------------------
from mzn.types.packages.general.aliases import (
    FilePath,
)


# --- Exports ------------------------------------------------------------------

__all__ = [  # noqa: RUF022
    # Aliases
    "CacheKey",
    "CacheNamespace",
    "CacheTTL",
    "EntryCount",
    "FilePath",
    "HitRatePercentage",
    "KeyPattern",
    "MaxEntries",
    "OperationCount",
    "RedisConnectionURL",
    # Enums
    "CacheBackend",
    "EvictionPolicy",
    "SerializationFormat",
    # Models
    "CacheConfig",
    "CacheStats",
]

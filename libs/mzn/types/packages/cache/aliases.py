"""
Title         : aliases.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/cache/aliases.py.

Description ----------- Cache-related type aliases - Redesigned with proper primitive bases.

This module provides semantic types for the cache package with: - Correct primitive bases - Enhanced
validation/normalization rules - Consolidated redundant types - Clear semantic boundaries

"""

from __future__ import annotations

import sys

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.primitives.prim_numeric import PrimDecimal
from mzn.types.primitives.prim_standard import PrimInt, PrimStr
from mzn.types.rules.rule_registry import NORM, VALID


# --- Core Cache Identifiers ---------------------------------------------------

@Build.alias(
    base=PrimStr,
    rules=[
        NORM.DOMAINS.CACHE.normalize_cache_key(),
        NORM.DOMAINS.CACHE.hash_if_too_long(max_length=200),
        VALID.DOMAINS.CACHE.is_valid_cache_key(),
        VALID.DOMAINS.CACHE.key_within_length_limit(),
    ],
    operations=None,
    description="A normalized cache key for safe storage across all backends.",
    tags=(SYSTEM.CACHE, SYSTEM.COMMON.identity),
    enable_caching=True,
)
class CacheKey:
    """A normalized cache key for safe storage across all backends."""


@Build.alias(
    base=PrimStr,
    rules=[
        NORM.STRING.strip_whitespace(),
        NORM.STRING.to_lowercase(),
        VALID.DOMAINS.CACHE.has_valid_namespace(),
    ],
    operations=None,
    description="A validated cache namespace identifier.",
    tags=(SYSTEM.CACHE, SYSTEM.COMMON.identity),
)
class CacheNamespace:
    """A validated cache namespace identifier."""


# --- Size and Count Types -----------------------------------------------------

@Build.alias(
    base=PrimInt,
    rules=[
        VALID.NUMERIC.is_non_negative(),
        VALID.NUMERIC.is_in_range(min_value=0, max_value=10_000_000),  # 10 million max
    ],
    operations=None,
    description="Count of cache entries.",
    tags=(SYSTEM.CACHE, SYSTEM.METRICS.performance),
)
class EntryCount:
    """Count of cache entries."""


@Build.alias(
    base=PrimInt,
    rules=[
        VALID.NUMERIC.is_positive(),
        VALID.NUMERIC.is_in_range(min_value=1, max_value=10_000_000),  # At least 1, 10M max
    ],
    operations=None,
    description="Maximum number of cache entries allowed.",
    tags=(SYSTEM.CACHE,),
)
class MaxEntries:
    """Maximum number of cache entries allowed."""


# --- Duration and Time Types --------------------------------------------------

@Build.alias(
    base=PrimInt,
    rules=[
        VALID.NUMERIC.is_positive(),
        VALID.DOMAINS.CACHE.is_valid_ttl_range(min_ttl=1, max_ttl=86400 * 365),
        NORM.DOMAINS.CACHE.add_ttl_jitter(jitter_percent=10.0),
    ],
    operations=None,
    description="Time-to-live for cache entries in seconds.",
    tags=(SYSTEM.CACHE, SYSTEM.COMMON.time),
)
class CacheTTL:
    """Time-to-live for cache entries in seconds."""


# --- Performance Metrics ------------------------------------------------------

@Build.alias(
    base=PrimDecimal,
    rules=[
        VALID.NUMERIC.is_in_range(min_value=0.0, max_value=100.0),
        NORM.NUMERIC.round_to(places=2),
    ],
    operations=None,
    description="Cache hit rate percentage (0.0 to 100.0).",
    tags=(SYSTEM.CACHE, SYSTEM.METRICS.performance),
)
class HitRatePercentage:
    """Cache hit rate percentage (0.0 to 100.0)."""


@Build.alias(
    base=PrimInt,
    rules=[
        VALID.NUMERIC.is_non_negative(),
        VALID.NUMERIC.is_in_range(min_value=0, max_value=sys.maxsize),
    ],
    operations=None,
    description="Number of cache operations (hits, misses, evictions).",
    tags=(SYSTEM.CACHE, SYSTEM.METRICS.performance),
)
class OperationCount:
    """Number of cache operations (hits, misses, evictions)."""


# --- Backend-Specific Types ---------------------------------------------------

@Build.alias(
    base=PrimStr,
    rules=[
        # PROTOCOLS rules don't exist yet - use basic validation
        VALID.STRING.matches_pattern(pattern=r"^(redis|rediss|unix)://"),
    ],
    operations=None,
    description="Redis connection URL with validation.",
    tags=(SYSTEM.CACHE, SYSTEM.INFRA.database),
)
class RedisConnectionURL:
    """Redis connection URL with validation."""


# --- Pattern Matching ---------------------------------------------------------

@Build.alias(
    base=PrimStr,
    rules=[
        NORM.STRING.strip_whitespace(),
        VALID.STRING.has_length(min_length=1, max_length=500),
    ],
    operations=None,
    description="Pattern for cache key matching (glob or regex).",
    tags=(SYSTEM.CACHE, SYSTEM.COMMON.pattern),
)
class KeyPattern:
    """Pattern for cache key matching (glob or regex)."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "CacheKey",
    "CacheNamespace",
    "CacheTTL",
    "EntryCount",
    "HitRatePercentage",
    "KeyPattern",
    "MaxEntries",
    "OperationCount",
    "RedisConnectionURL",
]

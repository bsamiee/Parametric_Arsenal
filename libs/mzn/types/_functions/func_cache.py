"""
Title         : func_cache.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_functions/func_cache.py

Description
-----------
Central cache management for Parametric_Arsenal systems.
Provides configured caches with decorators and complete caching functionality.
"""

from __future__ import annotations

from collections.abc import Callable, MutableMapping
from typing import Annotated, Any, TypeVar

from cachetools import LRUCache, TTLCache, cached
from cachetools.keys import hashkey


# --- Cache Configuration ------------------------------------------------------

_DEFAULT_CACHE_SIZE = 256
_DEFAULT_LOG_CACHE_SIZE = 128
_DEFAULT_LOG_CACHE_TTL = 5.0

# --- Type Variables -----------------------------------------------------------

F = TypeVar("F", bound=Callable[..., Any])

# --- Cache Namespace Class ----------------------------------------------------


class Cache:
    """Namespace for all cache-related utilities, instances, and decorators."""

    # Preconfigured cache instances, typed as LRUCache[Any, Any] and TTLCache[Any, Any] for explicit types.
    main: Annotated[LRUCache[Any, Any], "Main LRU cache instance"] = LRUCache(maxsize=_DEFAULT_CACHE_SIZE)
    log: Annotated[TTLCache[Any, Any], "Log TTL cache instance"] = TTLCache(
        maxsize=_DEFAULT_LOG_CACHE_SIZE,
        ttl=_DEFAULT_LOG_CACHE_TTL,
    )

    @staticmethod
    def get_lru_cache(maxsize: int | None = None) -> LRUCache[Any, Any]:
        """Get a new LRU cache with the specified size or default."""
        return LRUCache(maxsize=maxsize or _DEFAULT_CACHE_SIZE)

    @staticmethod
    def get_ttl_cache(maxsize: int | None = None, ttl: float | None = None) -> TTLCache[Any, Any]:
        """Get a new TTL cache with the specified size and TTL (default values if not provided)."""
        return TTLCache(
            maxsize=maxsize if maxsize is not None else _DEFAULT_LOG_CACHE_SIZE,
            ttl=ttl if ttl is not None else _DEFAULT_LOG_CACHE_TTL,
        )

    # --- Caching Decorators ---------------------------------------------------

    @staticmethod
    def cached(
        cache_instance: MutableMapping[Any, Any] | None = None,
        key: Callable[..., Any] = hashkey,
    ) -> Callable[[F], F]:
        """
        Caching decorator using our cache instances.

        Args:
            cache_instance: Cache to use (defaults to Cache.main)
            key: Key function for cache keys (defaults to hashkey)

        Returns:
            Decorated function with caching
        """
        cache = cache_instance if cache_instance is not None else Cache.main
        return cached(cache=cache, key=key)

    @staticmethod
    def lru_cached(maxsize: int | None = None) -> Callable[[F], F]:
        """
        LRU caching decorator with config-aware sizing.

        Args:
            maxsize: Maximum cache size (defaults to config cache_size)

        Returns:
            Decorated function with LRU caching
        """
        cache: LRUCache[Any, Any] = Cache.get_lru_cache(maxsize)
        return cached(cache=cache, key=hashkey)

    @staticmethod
    def ttl_cached(maxsize: int | None = None, ttl: float | None = None) -> Callable[[F], F]:
        """
        TTL caching decorator with config-aware sizing and timing.

        Args:
            maxsize: Maximum cache size (defaults to config log_cache_size)
            ttl: Time to live in seconds (defaults to config log_cache_ttl)

        Returns:
            Decorated function with TTL caching
        """
        cache: TTLCache[Any, Any] = Cache.get_ttl_cache(maxsize, ttl)
        return cached(cache=cache, key=hashkey)

    @staticmethod
    def clear_all() -> None:
        """Clear all preconfigured caches."""
        Cache.main.clear()
        Cache.log.clear()

    @staticmethod
    def get_stats() -> dict[str, Any]:
        """Get statistics for all preconfigured caches."""
        return {
            "main": {
                "size": Cache.main.currsize,
                "maxsize": Cache.main.maxsize,
                "hits": getattr(Cache.main, "hits", 0),
                "misses": getattr(Cache.main, "misses", 0),
            },
            "log": {
                "size": Cache.log.currsize,
                "maxsize": Cache.log.maxsize,
                "ttl": Cache.log.ttl,
                "hits": getattr(Cache.log, "hits", 0),
                "misses": getattr(Cache.log, "misses", 0),
            },
        }


# --- Public re-exports --------------------------------------------------------

__all__ = ["Cache"]

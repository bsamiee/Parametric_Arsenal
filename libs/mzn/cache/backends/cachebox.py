"""
Title         : cachebox.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/cache/backends/cachebox.py.

Description ----------- Cachebox backend implementation - high-performance Rust-based caching.

"""

from __future__ import annotations

import asyncio
from typing import TYPE_CHECKING, Any

import cachebox
from beartype import beartype

from mzn.cache.types import CacheKey, EvictionPolicy, MaxEntries


if TYPE_CHECKING:
    from collections.abc import Sequence


@beartype
class CacheboxBackend:
    """Cachebox backend using Rust-based high-performance cache."""

    def __init__(  # pyright: ignore[reportMissingSuperCall]
        self,
        *,
        max_entries: MaxEntries | int = 10000,
        eviction_policy: EvictionPolicy = EvictionPolicy.LRU,
    ) -> None:
        """Initialize Cachebox backend."""
        max_size = int(max_entries) if isinstance(max_entries, MaxEntries) else max_entries

        # Select appropriate cachebox implementation based on eviction policy
        match eviction_policy:
            case EvictionPolicy.LRU:
                self._cache: cachebox.BaseCacheImpl[str, Any] = cachebox.LRUCache(maxsize=max_size)
            case EvictionPolicy.LFU:
                self._cache = cachebox.LFUCache(maxsize=max_size)
            case EvictionPolicy.FIFO:
                self._cache = cachebox.FIFOCache(maxsize=max_size)
            case _:
                # Default to LRU
                self._cache = cachebox.LRUCache(maxsize=max_size)

    async def get(self, key: CacheKey) -> Any:
        """Get value by key."""
        # Cachebox is sync, so we run in executor
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._cache.get, str(key), None)

    async def set(self, key: CacheKey, value: Any, ttl: int | None = None) -> bool:
        """Set value with optional TTL in seconds."""
        loop = asyncio.get_event_loop()

        if ttl is not None and hasattr(self._cache, "insert_with_ttl"):
            # Use TTL cache if available
            # pyright doesn't understand hasattr, so we need to help it
            await loop.run_in_executor(None, getattr(self._cache, "insert_with_ttl"), str(key), value, ttl)  # noqa: B009
        else:
            # Regular insert
            await loop.run_in_executor(None, self._cache.insert, str(key), value)

        return True

    async def delete(self, key: CacheKey) -> bool:
        """Delete key from cache."""
        loop = asyncio.get_event_loop()
        key_str = str(key)

        # Check if key exists before deletion
        exists = await loop.run_in_executor(None, lambda: key_str in self._cache)
        if exists:
            _ = await loop.run_in_executor(None, self._cache.pop, key_str, None)
            return True
        return False

    async def exists(self, key: CacheKey) -> bool:
        """Check if key exists."""
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, lambda: str(key) in self._cache)

    async def clear(self) -> None:
        """Clear all entries."""
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, self._cache.clear)

    async def get_many(self, keys: Sequence[CacheKey]) -> list[Any]:
        """Get multiple values at once."""
        loop = asyncio.get_event_loop()

        def _get_many() -> list[Any]:
            return [self._cache.get(str(k), None) for k in keys]

        return await loop.run_in_executor(None, _get_many)

    async def set_many(self, items: list[tuple[CacheKey, Any]], ttl: int | None = None) -> bool:
        """Set multiple values at once."""
        loop = asyncio.get_event_loop()

        def _set_many() -> None:
            for key, value in items:
                if ttl is not None and hasattr(self._cache, "insert_with_ttl"):
                    getattr(self._cache, "insert_with_ttl")(str(key), value, ttl)  # noqa: B009
                else:
                    self._cache.insert(str(key), value)

        await loop.run_in_executor(None, _set_many)
        return True

    async def close(self) -> None:
        """Close backend connections."""
        # Cachebox doesn't need explicit cleanup
        await self.clear()

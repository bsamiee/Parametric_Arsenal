"""
Title         : memory.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/cache/backends/memory.py.

Description ----------- Memory backend implementation using aiocache.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

from aiocache.backends.memory import SimpleMemoryBackend
from beartype import beartype


if TYPE_CHECKING:
    from collections.abc import Sequence

    from mzn.cache.types import CacheKey


@beartype
class MemoryBackend:
    """Memory backend wrapper around aiocache's SimpleMemoryBackend."""

    def __init__(self, *, max_entries: int = 1000) -> None:  # pyright: ignore[reportMissingSuperCall]
        """Initialize memory backend."""
        self._backend = SimpleMemoryBackend()
        self._max_entries = max_entries
        # aiocache doesn't expose max_entries directly, we'll track it
        self._entries: dict[str, Any] = {}

    async def get(self, key: CacheKey) -> Any:
        """Get value by key."""
        key_str = str(key)
        return await self._backend.get(key_str)

    async def set(self, key: CacheKey, value: Any, ttl: int | None = None) -> bool:
        """Set value with optional TTL in seconds."""
        key_str = str(key)

        # Simple LRU eviction if at capacity
        if len(self._entries) >= self._max_entries and key_str not in self._entries:
            # Remove oldest entry
            oldest_key = next(iter(self._entries))
            _ = await self._backend.delete(oldest_key)
            del self._entries[oldest_key]

        result = await self._backend.set(key_str, value, ttl=ttl)
        if result:
            self._entries[key_str] = True
        return bool(result)

    async def delete(self, key: CacheKey) -> bool:
        """Delete key from cache."""
        key_str = str(key)
        result = await self._backend.delete(key_str)
        self._entries.pop(key_str, None)
        return bool(result)

    async def exists(self, key: CacheKey) -> bool:
        """Check if key exists."""
        key_str = str(key)
        result = await self._backend.exists(key_str)
        return bool(result)

    async def clear(self) -> None:
        """Clear all entries."""
        await self._backend.clear()
        self._entries.clear()

    async def get_many(self, keys: Sequence[CacheKey]) -> list[Any]:
        """Get multiple values at once."""
        key_strs = [str(k) for k in keys]
        results = await self._backend.multi_get(key_strs)
        return list(results)

    async def set_many(self, items: list[tuple[CacheKey, Any]], ttl: int | None = None) -> bool:
        """Set multiple values at once."""
        # Check capacity
        new_keys = [str(k) for k, _ in items if str(k) not in self._entries]
        overflow = len(self._entries) + len(new_keys) - self._max_entries

        if overflow > 0:
            # Remove oldest entries to make room
            keys_to_remove = list(self._entries.keys())[:overflow]
            for key in keys_to_remove:
                _ = await self._backend.delete(key)
                del self._entries[key]

        # Convert to aiocache format
        pairs = [(str(k), v) for k, v in items]
        result = await self._backend.multi_set(pairs, ttl=ttl)

        if result:
            for k, _ in items:
                self._entries[str(k)] = True

        return bool(result)

    async def close(self) -> None:
        """Close backend connections."""
        # Memory backend doesn't need explicit cleanup
        self._entries.clear()

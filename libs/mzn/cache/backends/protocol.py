"""
Title         : protocol.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/cache/backends/protocol.py.

Description ----------- Cache backend protocol definition for type-safe backend implementations.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any, Protocol, runtime_checkable


if TYPE_CHECKING:
    from collections.abc import Sequence

    from mzn.cache.types import CacheKey


@runtime_checkable
class CacheBackendProtocol(Protocol):
    """Protocol defining the interface all cache backends must implement."""

    async def get(self, key: CacheKey) -> Any:
        """Get value by key."""
        ...

    async def set(self, key: CacheKey, value: Any, ttl: int | None = None) -> bool:
        """Set value with optional TTL in seconds."""
        ...

    async def delete(self, key: CacheKey) -> bool:
        """Delete key from cache."""
        ...

    async def exists(self, key: CacheKey) -> bool:
        """Check if key exists."""
        ...

    async def clear(self) -> None:
        """Clear all entries."""
        ...

    async def get_many(self, keys: Sequence[CacheKey]) -> list[Any]:
        """Get multiple values at once."""
        ...

    async def set_many(self, items: list[tuple[CacheKey, Any]], ttl: int | None = None) -> bool:
        """Set multiple values at once."""
        ...

    async def close(self) -> None:
        """Close backend connections."""
        ...

"""
Title         : core.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/cache/core.py

Description
-----------
Streamlined cache implementation with pluggable backends.
"""

from __future__ import annotations

import fnmatch
from datetime import timedelta
from typing import TYPE_CHECKING, Annotated, Any, Self

import anyio
from beartype import beartype

from mzn.cache.backends.cachebox import CacheboxBackend
from mzn.cache.backends.disk import DiskBackend
from mzn.cache.backends.memory import MemoryBackend
from mzn.cache.backends.redis import RedisBackend
from mzn.cache.exceptions import CacheBackendError, CacheConfigError
from mzn.cache.types import (
    CacheBackend,
    CacheConfig,
    CacheKey,
    CacheStats,
    CacheTTL,
    EntryCount,
    HitRatePercentage,
    KeyPattern,
    OperationCount,
)
from mzn.errors.namespace import Error


if TYPE_CHECKING:
    from collections.abc import Sequence
    from types import TracebackType

    from mzn.cache.backends.protocol import CacheBackendProtocol


@beartype
class Cache:
    """
    Streamlined async cache implementation with pluggable backends.

    Features:
    - Multiple backends (memory, Redis, disk, cachebox)
    - Flexible serialization
    - TTL support
    - Statistics tracking
    - Tag-based operations
    """

    def __init__(self, config: Annotated[CacheConfig, "Cache configuration"]) -> None:  # pyright: ignore[reportMissingSuperCall]
        """Initialize cache with configuration."""
        self.config = config
        self._backend: CacheBackendProtocol | None = None
        self._stats = CacheStats(
            hits=OperationCount(0),
            misses=OperationCount(0),
            sets=OperationCount(0),
            deletes=OperationCount(0),
            hit_rate=HitRatePercentage(0.0),
            total_entries=EntryCount(0),
        )
        self._tags: dict[str, set[CacheKey]] = {}  # Tag to keys mapping

    def _require_backend(self) -> CacheBackendProtocol:
        """Ensure backend is initialized and return it."""
        if not self._backend:
            msg = "Cache not initialized"
            raise RuntimeError(msg)
        return self._backend

    @staticmethod
    def _ensure_cache_key(key: CacheKey | str) -> CacheKey:
        """Convert string to CacheKey if needed."""
        return CacheKey(key) if isinstance(key, str) else key

    @staticmethod
    def _convert_ttl(ttl: CacheTTL | timedelta | int | None) -> int | None:
        """Convert TTL to seconds."""
        if ttl is None:
            return None
        if isinstance(ttl, CacheTTL):
            return int(ttl)  # CacheTTL is already in seconds
        if isinstance(ttl, timedelta):
            return int(ttl.total_seconds())
        return ttl  # Already int

    async def initialize(self) -> None:
        """Initialize the cache backend."""
        try:
            self._backend = self._create_backend()
        except Exception as e:
            error = Error.create(
                "cache.initialization_failed",
                message=f"Failed to initialize {self.config.backend} backend",
                backend=self.config.backend.value,
                config=self.config.model_dump(),
            )
            raise CacheConfigError(error.context) from e

    def _create_backend(self) -> CacheBackendProtocol:
        """Create the appropriate backend based on configuration."""
        namespace = str(self.config.namespace)

        match self.config.backend:
            case CacheBackend.MEMORY:
                max_entries = int(self.config.max_entries) if self.config.max_entries else 1000
                return MemoryBackend(max_entries=max_entries)

            case CacheBackend.REDIS:
                if not self.config.redis_url:
                    msg = "Redis URL required for Redis backend"
                    raise ValueError(msg)
                return RedisBackend(
                    self.config.redis_url,
                    namespace=namespace,
                    serialization=self.config.serialization,
                )

            case CacheBackend.CACHEBOX:
                max_entries = int(self.config.max_entries) if self.config.max_entries else 10000
                return CacheboxBackend(
                    max_entries=max_entries,
                    eviction_policy=self.config.eviction_policy,
                )

            case CacheBackend.DISK:
                if not self.config.disk_path:
                    msg = "Disk path required for disk backend"
                    raise ValueError(msg)
                return DiskBackend(
                    self.config.disk_path,
                    serialization=self.config.serialization,
                )

            case _:
                msg = f"Unsupported backend: {self.config.backend}"
                raise ValueError(msg)

    async def get(
        self,
        key: Annotated[CacheKey | str, "Cache key"],
        default: Annotated[Any, "Default value if key not found"] = None,
    ) -> Any:
        """Get value from cache."""
        backend = self._require_backend()
        cache_key = self._ensure_cache_key(key)

        try:
            value = await backend.get(cache_key)

            if value is None:
                self._stats.misses = OperationCount(int(self._stats.misses) + 1)
                return default

            self._stats.hits = OperationCount(int(self._stats.hits) + 1)
            return value  # noqa: TRY300  # Valid pattern - stats tracking
        except Exception as e:
            error = Error.create(
                "cache.get_failed",
                message=f"Get operation failed for key '{cache_key}'",
                backend=self.config.backend.value,
                key=str(cache_key),
            )
            raise CacheBackendError(error.context) from e
        finally:
            self._update_hit_rate()

    async def set(
        self,
        key: Annotated[CacheKey | str, "Cache key"],
        value: Annotated[Any, "Value to cache"],
        ttl: Annotated[CacheTTL | timedelta | int | None, "Time to live"] = None,
        tags: Annotated[Sequence[str] | None, "Tags for grouping"] = None,
    ) -> bool:
        """Set value in cache with optional TTL."""
        backend = self._require_backend()
        cache_key = self._ensure_cache_key(key)
        ttl_seconds = self._convert_ttl(ttl)

        try:
            result = await backend.set(cache_key, value, ttl=ttl_seconds)

            if result:
                self._stats.sets = OperationCount(int(self._stats.sets) + 1)

                # Update tags mapping
                if tags:
                    for tag in tags:
                        if tag not in self._tags:
                            self._tags[tag] = set()
                        self._tags[tag].add(cache_key)

            return bool(result)
        except Exception as e:
            error = Error.create(
                "cache.set_failed",
                message=f"Set operation failed for key '{cache_key}'",
                backend=self.config.backend.value,
                key=str(cache_key),
                ttl=float(ttl_seconds) if ttl_seconds else None,
            )
            raise CacheBackendError(error.context) from e

    async def delete(self, key: Annotated[CacheKey | str, "Cache key"]) -> bool:
        """Delete key from cache."""
        backend = self._require_backend()
        cache_key = self._ensure_cache_key(key)

        try:
            result = await backend.delete(cache_key)

            if result:
                self._stats.deletes = OperationCount(int(self._stats.deletes) + 1)

                # Remove from tags
                for tag_keys in self._tags.values():
                    tag_keys.discard(cache_key)

            return bool(result)
        except Exception as e:
            error = Error.create(
                "cache.delete_failed",
                message=f"Delete operation failed for key '{cache_key}'",
                backend=self.config.backend.value,
                key=str(cache_key),
            )
            raise CacheBackendError(error.context) from e

    async def exists(self, key: Annotated[CacheKey | str, "Cache key"]) -> bool:
        """Check if key exists in cache."""
        backend = self._require_backend()
        cache_key = self._ensure_cache_key(key)

        try:
            return await backend.exists(cache_key)
        except Exception as e:
            error = Error.create(
                "cache.exists_failed",
                message=f"Exists check failed for key '{cache_key}'",
                backend=self.config.backend.value,
                key=str(cache_key),
            )
            raise CacheBackendError(error.context) from e

    async def clear(self) -> None:
        """Clear all entries from cache."""
        backend = self._require_backend()

        try:
            await backend.clear()
            self._tags.clear()
            self._stats.total_entries = EntryCount(0)
        except Exception as e:
            error = Error.create(
                "cache.clear_failed",
                message="Clear operation failed",
                backend=self.config.backend.value,
            )
            raise CacheBackendError(error.context) from e

    async def get_many(self, keys: Annotated[Sequence[CacheKey | str], "Cache keys"]) -> dict[str, Any]:
        """Get multiple values at once using concurrent execution."""
        backend = self._require_backend()

        if not keys:
            return {}

        try:
            cache_keys = [self._ensure_cache_key(k) for k in keys]

            # Use concurrent execution for better performance
            async with anyio.create_task_group() as tg:
                tasks: list[Any] = []
                for key in cache_keys:
                    task = tg.start_soon(backend.get, key)
                    tasks.append(task)

            # Collect results
            values: list[Any] = [task.result() for task in tasks]

            # Update stats
            for value in values:
                if value is None:
                    self._stats.misses = OperationCount(int(self._stats.misses) + 1)
                else:
                    self._stats.hits = OperationCount(int(self._stats.hits) + 1)

            self._update_hit_rate()
            # Return dict with string keys for consistency
            return dict(zip([str(k) for k in cache_keys], values, strict=True))
        except Exception as e:
            error = Error.create(
                "cache.get_many_failed",
                message="Get many operation failed",
                backend=self.config.backend.value,
                key_count=len(keys),
            )
            raise CacheBackendError(error.context) from e

    async def set_many(
        self,
        mapping: Annotated[dict[CacheKey | str, Any], "Key-value pairs"],
        ttl: Annotated[CacheTTL | timedelta | int | None, "Time to live"] = None,
    ) -> bool:
        """Set multiple values at once using concurrent execution."""
        backend = self._require_backend()

        if not mapping:
            return True

        try:
            ttl_seconds = self._convert_ttl(ttl)
            cache_items = [(self._ensure_cache_key(k), v) for k, v in mapping.items()]

            async with anyio.create_task_group() as tg:
                tasks: list[Any] = []
                for key, value in cache_items:
                    task = tg.start_soon(backend.set, key, value, ttl_seconds)
                    tasks.append(task)

            # Check if all operations succeeded
            results: list[bool] = [task.result() for task in tasks]
            success = all(results)

            if success:
                self._stats.sets = OperationCount(int(self._stats.sets) + len(mapping))

            return success  # noqa: TRY300
        except Exception as e:
            error = Error.create(
                "cache.set_many_failed",
                message="Set many operation failed",
                backend=self.config.backend.value,
                key_count=len(mapping),
            )
            raise CacheBackendError(error.context) from e

    async def delete_by_tag(self, tag: Annotated[str, "Tag to delete by"]) -> int:
        """Delete all entries with a specific tag."""
        _ = self._require_backend()

        if tag not in self._tags:
            return 0

        keys = list(self._tags[tag])
        deleted = 0

        for key in keys:
            if await self.delete(key):
                deleted += 1

        # Clean up the tag
        del self._tags[tag]
        return deleted

    async def delete_pattern(self, pattern: Annotated[KeyPattern | str, "Pattern to match keys"]) -> int:
        """Delete all keys matching a glob pattern."""
        _ = self._require_backend()

        pattern_str = str(pattern)
        deleted = 0

        try:
            # Get all keys from tags (this is our key tracking mechanism)
            all_keys: set[CacheKey] = set()
            for tag_keys in self._tags.values():
                all_keys.update(tag_keys)

            # Match keys against pattern
            matching_keys = [key for key in all_keys if fnmatch.fnmatch(str(key), pattern_str)]

            # Delete matching keys concurrently
            if matching_keys:
                async with anyio.create_task_group() as tg:
                    tasks: list[Any] = []
                    for key in matching_keys:
                        task = tg.start_soon(self.delete, key)
                        tasks.append(task)

                # Count successful deletions
                results: list[bool] = [task.result() for task in tasks]
                deleted = sum(1 for result in results if result)

            return deleted  # noqa: TRY300
        except Exception as e:
            error = Error.create(
                "cache.delete_pattern_failed",
                message="Delete pattern operation failed",
                backend=self.config.backend.value,
                pattern=pattern_str,
            )
            raise CacheBackendError(error.context) from e

    async def get_keys(self, pattern: Annotated[KeyPattern | str, "Pattern to match keys"]) -> list[CacheKey]:
        """Get all keys matching a glob pattern."""
        _ = self._require_backend()

        pattern_str = str(pattern)

        try:
            # Get all keys from tags (this is our key tracking mechanism)
            all_keys: set[CacheKey] = set()
            for tag_keys in self._tags.values():
                all_keys.update(tag_keys)

            # Match keys against pattern
            matching_keys = [key for key in all_keys if fnmatch.fnmatch(str(key), pattern_str)]

            # Verify keys still exist concurrently
            if matching_keys:
                async with anyio.create_task_group() as tg:
                    tasks: list[Any] = []
                    for key in matching_keys:
                        task = tg.start_soon(self.exists, key)
                        tasks.append(task)

                # Filter to only existing keys
                results: list[bool] = [task.result() for task in tasks]
                return [key for key, exists in zip(matching_keys, results, strict=True) if exists]

            return []  # noqa: TRY300
        except Exception as e:
            error = Error.create(
                "cache.get_keys_failed",
                message="Get keys operation failed",
                backend=self.config.backend.value,
                pattern=pattern_str,
            )
            raise CacheBackendError(error.context) from e

    async def get_stats(self) -> CacheStats:
        """Get cache statistics."""
        # Update total entries if possible
        # Note: Most backends don't support len(), so we keep the manually tracked count

        return self._stats

    def _update_hit_rate(self) -> None:
        """Update the hit rate percentage."""
        total = int(self._stats.hits) + int(self._stats.misses)
        if total > 0:
            rate = (int(self._stats.hits) / total) * 100
            self._stats.hit_rate = HitRatePercentage(round(rate, 2))

    async def close(self) -> None:
        """Close cache connections."""
        if self._backend:
            await self._backend.close()
            self._backend = None

    # Context manager support
    async def __aenter__(self) -> Self:
        """Enter async context."""
        await self.initialize()
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Exit async context."""
        await self.close()

"""
Title         : namespace.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/cache/namespace.py.

Description ----------- Streamlined Cache namespace with integrated functionality. Single export pattern with modern
Python 3.13+ async design.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, ClassVar, Self

from beartype import beartype

from mzn.cache.core import Cache as CacheImpl
from mzn.cache.decorator import cached
from mzn.cache.exceptions import (
    CacheBackendError,
    CacheConfigError,
    CacheConnectionError,
    CacheError,
    CacheKeyError,
    CacheSerializationError,
)
from mzn.cache.types import (
    CacheBackend,
    CacheConfig,
    CacheKey,
    CacheNamespace,
    CacheStats,
    CacheTTL,
    EvictionPolicy,
    FilePath,
    MaxEntries,
    RedisConnectionURL,
    SerializationFormat,
)


if TYPE_CHECKING:
    from types import TracebackType


# --- Cache Builder ------------------------------------------------------------


class CacheBuilder:
    """
    Fluent builder for cache configuration.

    Provides a chainable API for configuring cache instances with validation at each step.

    """

    def __init__(
        self,
        backend_type: Annotated[CacheBackend, "Backend type for cache"],
        name: Annotated[str, "Cache instance name"]
    ) -> None:
        """
        Initialize builder with backend type and name.

        Args:     backend_type: Cache backend type (memory, redis, disk, cachebox)     name: Unique identifier for the
        cache instance

        """
        super().__init__()
        self._backend = backend_type
        self._name = CacheNamespace(name)
        self._ttl: CacheTTL | None = None
        self._max_entries: MaxEntries | None = None
        self._redis_url: RedisConnectionURL | None = None
        self._disk_path: FilePath | None = None
        self._serialization: SerializationFormat = SerializationFormat.JSON
        self._eviction_policy: EvictionPolicy = EvictionPolicy.LRU

    @beartype
    def url(self, redis_url: Annotated[str, "Redis connection URL"]) -> Self:
        """
        Set Redis connection URL.

        Args:     redis_url: Redis connection URL (e.g., "redis://localhost:6379")

        Returns:     Builder instance for method chaining

        """
        self._redis_url = RedisConnectionURL(redis_url)
        return self

    @beartype
    def path(self, disk_path: Annotated[str, "Disk cache directory path"]) -> Self:
        """
        Set disk cache path.

        Args:     disk_path: Absolute path to cache directory

        Returns:     Builder instance for method chaining

        """
        self._disk_path = FilePath(disk_path)
        return self

    @beartype
    def ttl(self, seconds: Annotated[int, "Default TTL in seconds"]) -> Self:
        """
        Set default TTL.

        Args:     seconds: Time-to-live in seconds (must be positive)

        Returns:     Builder instance for method chaining

        """
        self._ttl = CacheTTL(seconds)
        return self

    @beartype
    def max_entries(self, count: Annotated[int, "Maximum cache entries"]) -> Self:
        """
        Set maximum entries.

        Args:     count: Maximum number of cache entries (must be positive)

        Returns:     Builder instance for method chaining

        """
        self._max_entries = MaxEntries(count)
        return self

    # Convenience methods for common serialization formats
    def json(self) -> Self:
        """
        Use JSON serialization.

        Returns:     Builder instance for method chaining

        """
        self._serialization = SerializationFormat.JSON
        return self

    def pickle(self) -> Self:
        """
        Use Pickle serialization.

        Returns:     Builder instance for method chaining

        """
        self._serialization = SerializationFormat.PICKLE
        return self

    # Convenience methods for common eviction policies
    def lru(self) -> Self:
        """
        Use LRU (Least Recently Used) eviction policy.

        Returns:     Builder instance for method chaining

        """
        self._eviction_policy = EvictionPolicy.LRU
        return self

    def lfu(self) -> Self:
        """
        Use LFU (Least Frequently Used) eviction policy.

        Returns:     Builder instance for method chaining

        """
        self._eviction_policy = EvictionPolicy.LFU
        return self

    def fifo(self) -> Self:
        """
        Use FIFO (First In, First Out) eviction policy.

        Returns:     Builder instance for method chaining

        """
        self._eviction_policy = EvictionPolicy.FIFO
        return self

    @beartype
    async def build(self) -> CacheImpl:
        """
        Build the configured cache instance.

        Returns:     Fully configured and initialized cache instance

        """
        config = CacheConfig(
            backend=self._backend,
            namespace=self._name,
            serialization=self._serialization,
            eviction_policy=self._eviction_policy,
            default_ttl=self._ttl,
            max_entries=self._max_entries,
            redis_url=self._redis_url,
            disk_path=self._disk_path,
        )
        return await Cache.create_from_config(str(self._name), config)

# --- Cache Class --------------------------------------------------------------


class Cache:
    """
    Unified namespace for all cache functionality.

    Provides intelligent, async-first caching with multiple backends and a clean, simple API.

    Example:     # Fluent API     cache = await Cache.redis("api-
    cache").url("redis://localhost").ttl(300).lru().json().build()     cache = await
    Cache.memory("session").max_entries(1000).lfu().build()     cache = await
    Cache.disk("persistent").path("/cache").fifo().pickle().build()

    # Context manager async with Cache.memory("temp-cache").max_entries(100).build() as cache:     await
    cache.set("key", "value")

    # Use decorator @Cache.cached(cache, ttl=300) async def expensive_operation(x: int) -> str:     return f"Result:
    {x}"

    """

    # --- Core Implementation --------------------------------------------------
    _caches: ClassVar[dict[str, CacheImpl]] = {}

    # --- Types ----------------------------------------------------------------
    Config = CacheConfig
    Stats = CacheStats
    Key = CacheKey
    Namespace = CacheNamespace

    # --- Enumerations ---------------------------------------------------------
    Backend = CacheBackend
    Eviction = EvictionPolicy
    Format = SerializationFormat

    # --- Exceptions -----------------------------------------------------------
    Error = CacheError
    KeyError = CacheKeyError
    BackendError = CacheBackendError
    SerializationError = CacheSerializationError
    ConnectionError = CacheConnectionError
    ConfigError = CacheConfigError

    # --- Decorators -----------------------------------------------------------
    cached = staticmethod(cached)

    # --- Context Manager Support ---------------------------------------------

    @classmethod
    @beartype
    async def __aenter__(cls) -> Self:
        """
        Enter async context for factory-style usage.

        Returns:     Cache class instance for creating multiple caches

        """
        return cls()

    @classmethod
    @beartype
    async def __aexit__(
        cls,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """
        Exit async context and cleanup all caches.

        Args:     exc_type: Exception type if any occurred     exc_val: Exception value if any occurred     exc_tb:
        Exception traceback if any occurred

        """
        await cls.close_all()

    # --- Fluent Factory Methods ----------------------------------------------

    @classmethod
    @beartype
    def memory(cls, name: Annotated[str, "Cache instance identifier"]) -> CacheBuilder:
        """
        Create fluent builder for memory cache.

        Args:     name: Unique cache instance identifier

        Returns:     CacheBuilder configured for memory backend

        """
        return CacheBuilder(CacheBackend.MEMORY, name)

    @classmethod
    @beartype
    def redis(cls, name: Annotated[str, "Cache instance identifier"]) -> CacheBuilder:
        """
        Create fluent builder for Redis cache.

        Args:     name: Unique cache instance identifier

        Returns:     CacheBuilder configured for Redis backend

        """
        return CacheBuilder(CacheBackend.REDIS, name)

    @classmethod
    @beartype
    def disk(cls, name: Annotated[str, "Cache instance identifier"]) -> CacheBuilder:
        """
        Create fluent builder for disk cache.

        Args:     name: Unique cache instance identifier

        Returns:     CacheBuilder configured for disk backend

        """
        return CacheBuilder(CacheBackend.DISK, name)

    @classmethod
    @beartype
    def cachebox(cls, name: Annotated[str, "Cache instance identifier"]) -> CacheBuilder:
        """
        Create fluent builder for Cachebox cache.

        Args:     name: Unique cache instance identifier

        Returns:     CacheBuilder configured for Cachebox backend

        """
        return CacheBuilder(CacheBackend.CACHEBOX, name)

    # --- Internal Factory Method ------------------------------------------------

    @classmethod
    async def create_from_config(
        cls,
        name: Annotated[str, "Cache instance name"],
        config: Annotated[CacheConfig, "Validated cache configuration"]
    ) -> CacheImpl:
        """
        Create cache from validated config.

        Args:     name: Unique cache instance identifier     config: Validated cache configuration with type assets

        Returns:     Configured and initialized cache instance

        """
        # Check if already cached
        if name in cls._caches:
            return cls._caches[name]

        cache = CacheImpl(config)
        await cache.initialize()

        # Store in registry
        cls._caches[name] = cache
        return cache

    @classmethod
    @beartype
    def get(cls, name: Annotated[str, "Cache instance identifier"]) -> CacheImpl | None:
        """
        Get an existing cache by name.

        Args:     name: Cache instance identifier

        Returns:     Cache instance if found, None otherwise

        """
        return cls._caches.get(name)

    @classmethod
    @beartype
    async def close(cls, name: Annotated[str, "Cache instance identifier"]) -> bool:
        """
        Close and remove a cache instance.

        Args:     name: Cache instance identifier

        Returns:     True if cache was closed, False if not found

        """
        if name in cls._caches:
            cache = cls._caches[name]
            await cache.close()
            del cls._caches[name]
            return True
        return False

    @classmethod
    @beartype
    async def close_all(cls) -> None:
        """
        Close all cache instances.

        Closes all cached instances and clears the registry. Useful for cleanup in tests or application shutdown.

        """
        for cache in cls._caches.values():
            await cache.close()
        cls._caches.clear()


# --- Exports ------------------------------------------------------------------

__all__ = ["Cache"]

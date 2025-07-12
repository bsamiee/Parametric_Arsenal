"""
Title         : redis.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/cache/backends/redis.py

Description
-----------
Redis backend implementation using redis-py with async support.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

import redis.asyncio as redis
from beartype import beartype

from mzn.cache.exceptions import CacheBackendError, CacheConnectionError
from mzn.cache.serializers import Serializer
from mzn.cache.types import CacheKey, RedisConnectionURL, SerializationFormat
from mzn.errors.namespace import Error


if TYPE_CHECKING:
    from collections.abc import Sequence


@beartype
class RedisBackend:
    """Redis backend using redis-py async client."""

    def __init__(  # pyright: ignore[reportMissingSuperCall]
        self,
        url: RedisConnectionURL | str,
        *,
        namespace: str = "cache",
        serialization: SerializationFormat = SerializationFormat.PICKLE,
    ) -> None:
        """Initialize Redis backend with connection URL."""
        self._url = str(url)
        self._namespace = namespace
        self._serializer = Serializer(serialization)
        self._client: redis.Redis | None = None

    async def _ensure_connected(self) -> redis.Redis:
        """Ensure Redis client is connected."""
        if self._client is None:
            try:
                self._client = await redis.from_url(self._url, decode_responses=False)
                # Test connection
                _ = await self._client.ping()
            except Exception as e:
                error = Error.create(
                    "cache.redis_connection_failed",
                    message="Failed to connect to Redis",
                    backend="redis",
                    url=self._url,
                )
                raise CacheConnectionError(error.context) from e
        return self._client

    def _make_key(self, key: CacheKey) -> str:
        """Create namespaced key."""
        return f"{self._namespace}:{key}"

    async def get(self, key: CacheKey) -> Any:
        """Get value by key."""
        try:
            client = await self._ensure_connected()
            redis_key = self._make_key(key)
            value = await client.get(redis_key)
            return self._serializer.deserialize(value) if value is not None else None
        except (CacheConnectionError, CacheBackendError):
            raise  # Re-raise our errors
        except Exception as e:
            error = Error.create(
                "cache.redis_get_failed",
                message="Redis get operation failed",
                backend="redis",
                key=str(key),
            )
            raise CacheBackendError(error.context) from e

    async def set(self, key: CacheKey, value: Any, ttl: int | None = None) -> bool:
        """Set value with optional TTL in seconds."""
        try:
            client = await self._ensure_connected()
            redis_key = self._make_key(key)
            serialized = self._serializer.serialize(value)

            if ttl is not None:
                return await client.setex(redis_key, ttl, serialized)

            set_result = await client.set(redis_key, serialized)
            return set_result is not None  # noqa: TRY300
        except (CacheConnectionError, CacheBackendError):
            raise
        except Exception as e:
            error = Error.create(
                "cache.redis_set_failed",
                message="Redis set operation failed",
                backend="redis",
                key=str(key),
                ttl=float(ttl) if ttl else None,
            )
            raise CacheBackendError(error.context) from e

    async def delete(self, key: CacheKey) -> bool:
        """Delete key from cache."""
        client = await self._ensure_connected()
        redis_key = self._make_key(key)
        result = await client.delete(redis_key)
        return bool(result)

    async def exists(self, key: CacheKey) -> bool:
        """Check if key exists."""
        client = await self._ensure_connected()
        redis_key = self._make_key(key)
        result = await client.exists(redis_key)
        return bool(result)

    async def clear(self) -> None:
        """Clear all entries in namespace."""
        client = await self._ensure_connected()
        # Use SCAN to find all keys in our namespace
        pattern = f"{self._namespace}:*"
        cursor = 0

        while True:
            cursor, keys = await client.scan(cursor, match=pattern, count=100)
            if keys:
                _ = await client.delete(*keys)
            if cursor == 0:
                break

    async def get_many(self, keys: Sequence[CacheKey]) -> list[Any]:
        """Get multiple values at once."""
        if not keys:
            return []

        client = await self._ensure_connected()
        redis_keys = [self._make_key(k) for k in keys]
        values = await client.mget(redis_keys)

        return [self._serializer.deserialize(v) if v is not None else None for v in values]

    async def set_many(self, items: list[tuple[CacheKey, Any]], ttl: int | None = None) -> bool:
        """Set multiple values at once."""
        if not items:
            return True

        client = await self._ensure_connected()

        # Redis doesn't support bulk set with TTL, so we use pipeline
        async with client.pipeline() as pipe:
            for key, value in items:
                redis_key = self._make_key(key)
                serialized = self._serializer.serialize(value)

                _ = pipe.setex(redis_key, ttl, serialized) if ttl is not None else pipe.set(redis_key, serialized)

            results = await pipe.execute()

        return all(results)

    async def close(self) -> None:
        """Close Redis connection."""
        if self._client:
            try:
                await self._client.close()
            except Exception:  # noqa: BLE001,S110  # Best effort close - ignore all errors
                pass
            finally:
                self._client = None


from typing import Any, ClassVar, TypeVar

from redis.asyncio.client import Redis
from redis.asyncio.connection import ConnectionPool

_D = TypeVar("_D")

class BaseCache[T]:
    def __init__(
        self,
        ttl: float = 0,
        timeout: float = 1,
        namespace: str | None = None,
        **kwargs: Any,
    ) -> None: ...
    async def get(self, key: str, default: _D | None = None) -> object | _D | None: ...
    async def set(self, key: str, value: Any, ttl: float | None = None) -> None: ...  # noqa: ANN401
    async def delete(self, key: str) -> bool: ...
    async def exists(self, key: str) -> bool: ...
    async def clear(self) -> None: ...
    async def close(self) -> None: ...
    async def increment(self, key: str, delta: int = 1) -> int: ...
    async def get_keys(self, pattern: str = "*") -> list[str]: ...

class Cache:
    MEMORY: ClassVar[type[BaseCache[Any]]]
    REDIS: ClassVar[type[BaseCache[Any]]]
    DISK: ClassVar[type[BaseCache[Any]]]

class LRUCache(BaseCache[Any]):
    def __init__(self, maxsize: int = 1024, **kwargs: Any) -> None: ...

class RedisCache(BaseCache[Any]):
    def __init__(
        self,
        endpoint: str = "127.0.0.1",
        port: int = 6379,
        db: int = 0,
        password: str | None = None,
        pool_min_size: int = 1,
        pool_max_size: int = 10,
        client_class: type[Redis] | None = None,
        connection_pool_class: type[ConnectionPool] | None = None,
        **kwargs: Any,
    ) -> None: ...

class PickleSerializer:
    def __init__(self, *args: Any, **kwargs: Any) -> None: ...
    def dumps(self, value: Any) -> bytes: ...  # noqa: ANN401
    def loads(self, value: bytes | None) -> Any: ...  # noqa: ANN401

class serializers:  # noqa: N801
    PickleSerializer: ClassVar[type[PickleSerializer]]

class TimingPlugin:
    def __init__(self, *args: Any, **kwargs: Any) -> None: ...

class HitMissRatioPlugin:
    def __init__(self, *args: Any, **kwargs: Any) -> None: ...

class plugins:  # noqa: N801
    TimingPlugin: ClassVar[type[TimingPlugin]]
    HitMissRatioPlugin: ClassVar[type[HitMissRatioPlugin]]

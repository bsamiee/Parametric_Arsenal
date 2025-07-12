from types import TracebackType
from typing import TYPE_CHECKING, Any, Self

if TYPE_CHECKING:  # noqa: PYI002
    from collections.abc import Sequence

# Import connection module
from redis.asyncio import connection as connection

__all__ = ["Pipeline", "Redis", "connection", "from_url"]

async def from_url(
    url: str,
    *,
    decode_responses: bool = False,
    **kwargs: object,
) -> Redis: ...

class Redis:
    def __init__(self, *, connection_pool: Any | None = None, **kwargs: Any) -> None: ...  # noqa: ANN401
    async def get(self, name: str | bytes) -> bytes | None: ...
    async def set(
        self,
        name: str | bytes,
        value: str | bytes | float,
        ex: int | None = None,
        px: int | None = None,
        nx: bool = False,
        xx: bool = False,
    ) -> bool | None: ...
    async def setex(
        self,
        name: str | bytes,
        time: int,
        value: str | bytes | float,
    ) -> bool: ...
    async def delete(self, *names: str | bytes) -> int: ...
    async def exists(self, *names: str | bytes) -> int: ...
    async def scan(
        self,
        cursor: int = 0,
        match: str | bytes | None = None,
        count: int | None = None,
        _type: str | None = None,
    ) -> tuple[int, list[bytes]]: ...
    async def info(self, section: str | None = None) -> dict[str, object]: ...
    async def ping(self) -> bool: ...
    async def incrby(self, name: str | bytes, amount: int = 1) -> int: ...
    async def decrby(self, name: str | bytes, amount: int = 1) -> int: ...
    async def mget(self, keys: Sequence[str | bytes], *names: str | bytes) -> list[bytes | None]: ...
    def pipeline(self, transaction: bool = True) -> Pipeline: ...
    async def close(self) -> None: ...
    async def __aenter__(self) -> Self: ...
    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None: ...

class Pipeline:
    def set(
        self,
        name: str | bytes,
        value: str | bytes | float,
        ex: int | None = None,
        px: int | None = None,
        nx: bool = False,
        xx: bool = False,
    ) -> Self: ...
    def setex(
        self,
        name: str | bytes,
        time: int,
        value: str | bytes | float,
    ) -> Self: ...
    async def execute(self) -> list[Any]: ...
    async def __aenter__(self) -> Self: ...
    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None: ...

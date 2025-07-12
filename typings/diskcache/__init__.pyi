from collections.abc import Callable, Iterator
from pathlib import Path
from typing import Any, Final, Literal, TypeVar, overload

_T = TypeVar("_T")

__all__ = ["EVICTION_POLICY", "Cache", "Disk"]

EVICTION_POLICY: Final[dict[str, str]]

class Cache:
    def __init__(
        self,
        directory: str | Path | None = None,
        *,
        timeout: float = 60.0,
        disk: type[Disk] | None = None,
        size_limit: int | None = None,
        cull_limit: int = 10,
        statistics: int = 0,
        tag_index: int = 0,
        eviction_policy: str = "least-recently-stored",
        disk_min_file_size: int = 32768,
        disk_pickle_protocol: int = -1,
        sqlite_auto_vacuum: int = 0,
        sqlite_cache_size: int = 8192,
        sqlite_journal_mode: str = "wal",
        sqlite_mmap_size: int = 67108864,
        sqlite_synchronous: int = 1,
    ) -> None: ...
    @property
    def directory(self) -> str: ...
    @property
    def timeout(self) -> float: ...
    @property
    def size_limit(self) -> int | None: ...
    @property
    def cull_limit(self) -> int: ...
    @property
    def hits(self) -> int: ...
    @property
    def misses(self) -> int: ...
    def __len__(self) -> int: ...
    def __contains__(self, key: object) -> bool: ...
    def __getitem__(self, key: object) -> Any: ...
    def __setitem__(self, key: object, value: object) -> None: ...
    def __delitem__(self, key: object) -> None: ...
    @overload
    def get(self, key: object) -> Any | None: ...
    @overload
    def get(self, key: object, default: _T) -> Any | _T: ...
    @overload
    def get(
        self, key: object, default: _T, read: bool, expire_time: bool, tag: bool, retry: bool
    ) -> Any | _T: ...
    def set(
        self,
        key: object,
        value: object,
        expire: float | None = None,
        read: bool = False,
        tag: str | None = None,
        retry: bool = False,
    ) -> bool: ...
    def delete(self, key: object, retry: bool = False) -> bool: ...
    def clear(self, retry: bool = False) -> int: ...
    def close(self) -> None: ...
    def iterkeys(self, reverse: bool = False) -> Iterator[Any]: ...
    def touch(self, key: object, expire: float | None = None, retry: bool = False) -> bool: ...
    def add(
        self,
        key: object,
        value: object,
        expire: float | None = None,
        read: bool = False,
        tag: str | None = None,
        retry: bool = False,
    ) -> bool: ...
    def incr(self, key: object, delta: int = 1, default: int = 0, retry: bool = False) -> int: ...
    def decr(self, key: object, delta: int = 1, default: int = 0, retry: bool = False) -> int: ...
    @overload
    def pop(
        self,
        key: object,
        default: None = None,
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | None: ...
    @overload
    def pop(
        self,
        key: object,
        default: _T,
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | _T: ...
    def expire(self, now: float | None = None, retry: bool = False) -> int: ...
    def evict(self, tag: str, retry: bool = False) -> int: ...
    def stats(self, enable: bool = True, reset: bool = False) -> tuple[int, int]: ...
    def volume(self) -> int: ...
    def check(self, fix: bool = False, retry: bool = False) -> list[str]: ...
    def read(self, key: object, retry: bool = False) -> Any: ...
    def reset(self, key: str, value: Any | None = None, update: bool = True) -> Any | None: ...
    def push(
        self,
        value: object,
        prefix: str | None = None,
        side: Literal["back", "front"] = "back",
        expire: float | None = None,
        read: bool = False,
        tag: str | None = None,
        retry: bool = False,
    ) -> Any: ...
    @overload
    def pull(
        self,
        prefix: str | None = None,
        default: None = None,
        side: Literal["back", "front"] = "front",
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | None: ...
    @overload
    def pull(
        self,
        prefix: str | None = None,
        default: _T = ...,
        side: Literal["back", "front"] = "front",
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | _T: ...
    @overload
    def peek(
        self,
        prefix: str | None = None,
        default: None = None,
        side: Literal["back", "front"] = "front",
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | None: ...
    @overload
    def peek(
        self,
        prefix: str | None = None,
        default: _T = ...,
        side: Literal["back", "front"] = "front",
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> Any | _T: ...
    def peekitem(
        self,
        last: bool = True,
        expire_time: bool = False,
        tag: bool = False,
        retry: bool = False,
    ) -> tuple[Any, Any]: ...
    def memoize(
        self,
        name: str | None = None,
        typed: bool = False,
        expire: float | None = None,
        tag: str | None = None,
        ignore: list[str] | None = None,
    ) -> Callable[[Callable[..., _T]], Callable[..., _T]]: ...
    def create_tag_index(self) -> None: ...
    def drop_tag_index(self) -> None: ...
    def cull(self, retry: bool = False) -> int: ...
    def count(self, tag: str | None = None, evict: bool = True, retry: bool = False) -> int: ...
    def size(self, tag: str | None = None, evict: bool = True, retry: bool = False) -> int: ...

class Disk: ...

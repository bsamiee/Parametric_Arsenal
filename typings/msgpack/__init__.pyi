from collections.abc import Callable
from typing import Any

# Exception types
class PackException(Exception): ...  # noqa: N818
class UnpackException(Exception): ...  # noqa: N818
class PackValueError(PackException, ValueError): ...
class UnpackValueError(UnpackException, ValueError): ...

# Main functions
def packb(
    o: object,
    *,
    use_bin_type: bool = True,
    strict_types: bool = False,
    datetime: bool = False,
    unicode_errors: str | None = None,
) -> bytes: ...

def unpackb(
    packed: bytes,
    *,
    raw: bool = False,
    strict_map_key: bool = True,
    unicode_errors: str | None = None,
    object_hook: Callable[[dict[Any, Any]], Any] | None = None,
    object_pairs_hook: Callable[[list[tuple[Any, Any]]], Any] | None = None,
    list_hook: Callable[[list[Any]], Any] | None = None,
    timestamp: int = 0,
    max_str_len: int = -1,
    max_bin_len: int = -1,
    max_array_len: int = -1,
    max_map_len: int = -1,
    max_ext_len: int = -1,
) -> object: ...

# Version info
version: tuple[int, int, int]

__all__ = [
    "PackException",
    "PackValueError",
    "UnpackException",
    "UnpackValueError",
    "packb",
    "unpackb",
    "version",
]

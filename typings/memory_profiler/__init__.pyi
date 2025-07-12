from collections.abc import Callable
from typing import Any, TypeVar, overload

T = TypeVar("T")

@overload
def profile[T](func: Callable[..., T]) -> Callable[..., T]: ...
@overload
def profile[T](
    func: None = None,
    *,
    precision: int = 1,
    backend: str = "psutil",
    stream: Any | None = None,  # noqa: ANN401
) -> Callable[[Callable[..., T]], Callable[..., T]]: ...

class LineProfiler:
    def __init__(self, **kwargs: Any) -> None: ...
    def add_function(self, func: Callable[..., Any]) -> None: ...
    def enable(self) -> None: ...
    def disable(self) -> None: ...
    def print_stats(self, stream: Any | None = None) -> None: ...  # noqa: ANN401

def memory_usage(
    proc: Callable[..., Any] | int = -1,
    interval: float = 0.1,
    timeout: float | None = None,
    timestamps: bool = False,
    include_children: bool = False,
    multiprocess: bool = False,
    max_usage: bool = False,
    retval: bool = False,
    stream: Any | None = None,  # noqa: ANN401
    backend: str = "psutil",
) -> list[float] | tuple[list[float], Any]: ...

__all__ = ["LineProfiler", "memory_usage", "profile"]

# Type stubs for line_profiler package
from collections.abc import Callable
from typing import IO, Any

class LineProfiler:

    def __init__(self, *functions: Callable[..., Any]) -> None: ...

    def add_function(self, func: Callable[..., Any]) -> None:
        ...

    def enable(self) -> None:
        ...

    def disable(self) -> None:
        ...

    def enable_by_count(self) -> None:
        ...

    def disable_by_count(self) -> None:
        ...

    def get_stats(self) -> dict[str, Any]:
        ...

    def print_stats(
        self,
        stream: IO[str] | None = None,
        output_unit: float = 1e-6,
        stripzeros: bool = True
    ) -> None:
        ...

    def dump_stats(self, filename: str) -> None:
        ...

def profile[F: Callable[..., Any]](func: F) -> F:
    ...

__all__ = ["LineProfiler", "profile"]

# Type stubs for pyinstrument package
from collections.abc import Callable
from typing import IO, Any

class Session:
    ...

class Profiler:

    def __init__(
        self,
        interval: float = 0.001,
        async_mode: str = "enabled"
    ) -> None: ...

    def start(self) -> None:
        ...

    def stop(self) -> Session:
        ...

    def reset(self) -> None:
        ...

    def output_text(
        self,
        unicode: bool = True,
        color: bool = True,
        show_all: bool = False,
        timeline: bool = False
    ) -> str:
        ...

    def output_html(
        self,
        file: str | IO[str] | None = None,
        unicode: bool = True,
        show_all: bool = False,
        timeline: bool = False
    ) -> str | None:
        ...

    def print(
        self,
        file: IO[str] | None = None,
        unicode: bool = True,
        color: bool = True,
        show_all: bool = False,
        timeline: bool = False
    ) -> None:
        ...

def profile(
    func: Callable[..., Any] | None = None,
    interval: float = 0.001
) -> Callable[..., Any]:
    ...

__all__ = ["Profiler", "Session", "profile"]

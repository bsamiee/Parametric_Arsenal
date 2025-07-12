from collections.abc import Callable
from typing import Any, TypeVar, overload

_T = TypeVar("_T")

class IceCreamDebugger:

    enabled: bool
    prefix: str | Callable[[], str]
    outputFunction: Callable[[str], None]
    argToStringFunction: Callable[[Any], str]
    includeContext: bool
    contextAbsPath: bool

    @overload
    def __call__(self, arg: _T) -> _T: ...

    @overload
    def __call__(self, arg1: Any, arg2: Any, /, *args: Any) -> tuple[Any, ...]: ...

    @overload
    def __call__(self) -> None: ...

    def configureOutput(
        self,
        *,
        prefix: str | Callable[[], str] | None = None,
        outputFunction: Callable[[str], None] | None = None,
        argToStringFunction: Callable[[Any], str] | None = None,
        includeContext: bool | None = None,
        contextAbsPath: bool | None = None,
    ) -> None: ...

    def disable(self) -> None: ...

    def enable(self) -> None: ...

ic: IceCreamDebugger

def install() -> None: ...
def uninstall() -> None: ...

__all__ = ["ic", "install", "uninstall"]

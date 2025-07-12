from collections.abc import Callable
from typing import Any, TypeVar

_F = TypeVar("_F", bound=Callable[..., Any])

def decorator(
    wrapper: Callable[..., Any],
) -> Callable[[_F], _F]: ...

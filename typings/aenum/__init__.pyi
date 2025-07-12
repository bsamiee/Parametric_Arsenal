from collections.abc import Iterator
from typing import ClassVar, override

class EnumMeta(type):
    def __new__(cls, name: str, bases: tuple[type, ...], namespace: dict[str, object], **kwds: object) -> type: ...
    def __iter__(cls) -> Iterator[object]: ...
    def __getitem__(cls, name: str) -> object: ...
    @override
    def __call__(cls, value: object, *args: object, **kwargs: object) -> object: ...

class Enum(metaclass=EnumMeta):
    __members__: ClassVar[dict[str, object]]
    name: str
    value: object
    def __init__(self, *args: object, **kwargs: object) -> None: ...

class IntEnum(Enum):
    value: int  # pyright: ignore[reportIncompatibleVariableOverride]

class StrEnum(Enum):
    value: str  # pyright: ignore[reportIncompatibleVariableOverride]

# Additional typed enum base classes for better type safety
class TypedEnum[T](Enum):
    value: T  # pyright: ignore[reportIncompatibleVariableOverride]

class TypedStrEnum(TypedEnum[str]):
    value: str

class TypedIntEnum(TypedEnum[int]):
    value: int

class MultiValue:
    value: object
    def __init__(self, value: object, *args: object, **kwargs: object) -> None: ...

class EnumMember[M = object](MultiValue):
    name: str
    value: object
    metadata: M | None
    def __init__(
        self,
        value: object,
        *,
        description: str | None = ...,
        tags: tuple[object, ...] | None = ...,
        aliases: set[str] | frozenset[str] | None = ...,
        is_default: bool = ...,
        deprecated: bool = ...,
        deprecation_message: str | None = ...,
        metadata: M | None = None,
    ) -> None: ...
    def to_dict(self) -> dict[str, object]: ...

def auto() -> object: ...
def enum(*args: object, **kwargs: object) -> object: ...

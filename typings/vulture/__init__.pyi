# Type stubs for vulture package

class Item:

    typ: str
    name: str
    file: str  # This is the actual attribute name used
    filename: str  # Alias for file
    lineno: int  # This is the actual attribute name used
    first_lineno: int  # Alias for lineno
    last_lineno: int
    message: str
    confidence: int

class Vulture:

    def __init__(
        self,
        verbose: bool = False,
        ignore_names: list[str] | None = None,
        ignore_decorators: list[str] | None = None,
        min_confidence: int = 0
    ) -> None: ...

    def scan(self, paths: list[str], exclude: str | list[str] | None = None) -> None: ...

    def scavenge(self, paths: list[str], exclude: str | list[str] | None = None) -> None:
        ...

    def get_unused_code(
        self,
        min_confidence: int = 0,
        sort_by_size: bool = False
    ) -> list[Item]: ...

    def report(self) -> None: ...

__all__ = ["Item", "Vulture"]

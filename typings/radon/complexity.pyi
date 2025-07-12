class ComplexityVisitor:
    name: str
    complexity: int
    lineno: int
    col_offset: int
    is_method: bool
    classname: str | None

    def __init__(
        self,
        name: str,
        complexity: int,
        lineno: int = 0,
        col_offset: int = 0,
        is_method: bool = False,
        classname: str | None = None,
    ) -> None: ...

def cc_visit(code: str, filename: str = "<string>") -> list[ComplexityVisitor]: ...
def cc_rank(cc: int) -> str: ...

__all__ = ["ComplexityVisitor", "cc_rank", "cc_visit"]

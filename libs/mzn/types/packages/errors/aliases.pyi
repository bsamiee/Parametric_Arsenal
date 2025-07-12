from pydantic import RootModel

class ErrorCode(RootModel[str]):
    root: str

class ErrorMessage(RootModel[str]):
    root: str

class RecoveryHint(RootModel[str]):
    root: str

__all__ = [
    "ErrorCode",
    "ErrorMessage",
    "RecoveryHint",
]

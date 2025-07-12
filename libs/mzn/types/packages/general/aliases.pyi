from datetime import date, datetime, time, tzinfo
from uuid import UUID

from pydantic import RootModel

# These are all Pydantic RootModel classes created by @Build.alias
# They have a .root attribute to access the underlying value

# Path-based aliases (from PrimPath)
class FilePath(RootModel[str]):
    root: str

# Timestamp-based aliases (from PrimTimestamp)
class TimestampUTC(RootModel[datetime]):
    root: datetime

    # Datetime methods exposed from PrimTimestamp and DateTimeLikeMixin
    @classmethod
    def now(cls) -> TimestampUTC: ...
    def strftime(self, fmt: str) -> str: ...
    def isoformat(self) -> str: ...
    def astimezone(self, tz: tzinfo | None = None) -> datetime: ...
    def timestamp(self) -> float: ...
    def date(self) -> date: ...
    def time(self) -> time: ...

# UUID-based aliases (from PrimUUID)
class RequestID(RootModel[UUID]):
    root: UUID

__all__ = [
    "FilePath",
    "RequestID",
    "TimestampUTC",
]

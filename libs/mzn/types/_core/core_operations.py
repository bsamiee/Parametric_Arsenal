"""
Title         : core_operations.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_core/core_operations.py.

Description ----------- Configuration and mixins for enabling primitive-like operations on type assets.

"""
# pyright: reportUninitializedInstanceVariable=false

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Any, Self, TypeVar, override


if TYPE_CHECKING:
    from datetime import date, datetime, time, timedelta


# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Configuration Class ------------------------------------------------------


@dataclass(frozen=True, kw_only=True)
class MethodConfig:
    """A configuration object to explicitly enable primitive-like method sets on a type alias."""

    arithmetic: bool = False
    container: bool = False
    casting: bool = False
    path_like: bool = False
    datetime_like: bool = False
    timedelta_like: bool = False


# --- CastingMixin -------------------------------------------------------------

class CastingMixin:
    """Provides explicit type casting methods (__int__, __float__, etc.)."""

    root: Any

    @override
    def __str__(self) -> str:
        """Return the string representation of the root value."""
        return str(self.root)

    def __int__(self) -> int:
        """Return the integer representation of the root value."""
        return int(self.root)

    def __float__(self) -> float:
        """Return the float representation of the root value."""
        return float(self.root)

    def __bytes__(self) -> bytes:
        """Return the bytes representation of the root value."""
        return bytes(self.root)

# --- ArithmeticMixin ----------------------------------------------------------


class ArithmeticMixin:
    """Provides arithmetic dunder methods, returning a new instance of self."""

    root: Any

    def __add__(self, other: object) -> Self:
        """Add the root value to another value, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root + other_val)  # type: ignore[call-arg]

    def __sub__(self, other: object) -> Self:
        """Subtract another value from the root value, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root - other_val)  # type: ignore[call-arg]

    def __mul__(self, other: object) -> Self:
        """Multiply the root value by another value, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root * other_val)  # type: ignore[call-arg]

    def __truediv__(self, other: object) -> object:
        """
        Perform true division, returning a new instance or a float.

        If the result is an integer, a new instance is returned. Otherwise, the float result is returned directly.

        """
        other_val = getattr(other, "root", other)
        result = self.root / other_val
        return self.__class__(result) if isinstance(result, int) else result  # type: ignore[call-arg]

    def __floordiv__(self, other: object) -> Self:
        """Perform floor division, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root // other_val)  # type: ignore[call-arg]

    def __mod__(self, other: object) -> Self:
        """Perform modulo, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root % other_val)  # type: ignore[call-arg]

    def __pow__(self, other: object) -> Self:
        """Perform power, returning a new instance."""
        other_val = getattr(other, "root", other)
        return self.__class__(self.root**other_val)  # type: ignore[call-arg]

    def __neg__(self) -> Self:
        """Return a new instance with the negated root value."""
        return self.__class__(-self.root)  # type: ignore[call-arg]

    def __pos__(self) -> Self:
        """Return a new instance with the positive root value."""
        return self.__class__(+self.root)  # type: ignore[call-arg]

    def __abs__(self) -> Self:
        """Return a new instance with the absolute root value."""
        return self.__class__(abs(self.root))  # type: ignore[call-arg]

# --- ContainerMixin -----------------------------------------------------------


class ContainerMixin:
    """Provides container-like dunder methods (__len__, __getitem__, etc.)."""

    root: Any

    def __len__(self) -> int:
        """Return the length of the root value."""
        return len(self.root)

    def __getitem__(self, key: object) -> object:
        """Get an item from the root value."""
        return self.root[key]

    def __contains__(self, item: object) -> bool:
        """Check if an item is in the root value."""
        return item in self.root

    def __iter__(self) -> object:
        """Return an iterator for the root value."""
        return iter(self.root)

# --- PathLikeMixin ------------------------------------------------------------


class PathLikeMixin:
    """Provides pathlib.Path-like methods."""

    root: str

    def as_path(self) -> Path:
        """Return the root value as a pathlib.Path object."""
        return Path(self.root)

    def exists(self) -> bool:
        """Check if the path exists."""
        return self.as_path().exists()

    def is_dir(self) -> bool:
        """Check if the path is a directory."""
        return self.as_path().is_dir()

    def is_file(self) -> bool:
        """Check if the path is a file."""
        return self.as_path().is_file()

    @property
    def parent(self) -> Path:
        """Return the parent directory of the path."""
        return self.as_path().parent

    @property
    def name(self) -> str:
        """Return the final path component."""
        return self.as_path().name

    @property
    def stem(self) -> str:
        """Return the final path component, without its suffix."""
        return self.as_path().stem

    @property
    def suffix(self) -> str:
        """Return the file extension of the final component."""
        return self.as_path().suffix

# --- DateTimeLikeMixin --------------------------------------------------------


class DateTimeLikeMixin:
    """Provides datetime-like methods."""

    root: datetime

    def timestamp(self) -> float:
        """Return the POSIX timestamp corresponding to the datetime instance."""
        return self.root.timestamp()

    def isoformat(self) -> str:
        """Return the ISO 8601 representation of the datetime."""
        return self.root.isoformat()

    def date(self) -> date:
        """Return the date part of the datetime instance."""
        return self.root.date()

    def time(self) -> time:
        """Return the time part of the datetime instance."""
        return self.root.time()

# --- TimeDeltaLikeMixin -------------------------------------------------------


class TimeDeltaLikeMixin:
    """Provides timedelta-like methods."""

    root: timedelta

    def total_seconds(self) -> float:
        """Return the total number of seconds contained in the duration."""
        return self.root.total_seconds()

"""
Title         : prim_collections.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/primitives/prim_collections.py

Description
-----------
Collection-based primitive types for various data structures.
"""

from __future__ import annotations

from typing import Any

from mzn.types._core.core_builders import Build


# --- Simple List Primitives ---


@Build.primitive(list[str])
class PrimStrList:
    """A primitive type for a list of strings."""


@Build.primitive(list[int])
class PrimIntList:
    """A primitive type for a list of integers."""


@Build.primitive(list[float])
class PrimFloatList:
    """A primitive type for a list of floats."""


# --- Simple Set Primitives ---


@Build.primitive(set[str])
class PrimStrSet:
    """A primitive type for a set of strings."""


@Build.primitive(set[int])
class PrimIntSet:
    """A primitive type for a set of integers."""


@Build.primitive(set[float])
class PrimFloatSet:
    """A primitive type for a set of floats."""


# --- Simple Tuple Primitives ---


@Build.primitive(tuple[str, ...])
class PrimStrTuple:
    """A primitive type for a tuple of strings."""


@Build.primitive(tuple[int, ...])
class PrimIntTuple:
    """A primitive type for a tuple of integers."""


@Build.primitive(tuple[float, ...])
class PrimFloatTuple:
    """A primitive type for a tuple of floats."""


# --- Nested Collection Primitives ---


@Build.primitive(list[list[str]])
class PrimListOfStrList:
    """A primitive type for a list of string lists."""


@Build.primitive(list[list[int]])
class PrimListOfIntList:
    """A primitive type for a list of integer lists."""


@Build.primitive(set[frozenset[str]])
class PrimSetOfStrSet:
    """A primitive type for a set of string frozensets."""


# --- Mapping Primitives (Dictionaries) ---


@Build.primitive(dict[str, str])
class PrimMapStrToStr:
    """A primitive type for a dictionary mapping strings to strings."""


@Build.primitive(dict[str, Any])
class PrimMapStrToAny:
    """A primitive type for a dictionary mapping strings to any values."""


@Build.primitive(dict[str, int])
class PrimMapStrToInt:
    """A primitive type for a dictionary mapping strings to integers."""


@Build.primitive(dict[str, float])
class PrimMapStrToFloat:
    """A primitive type for a dictionary mapping strings to floats."""


@Build.primitive(dict[int, str])
class PrimMapIntToStr:
    """A primitive type for a dictionary mapping integers to strings."""


@Build.primitive(dict[str, list[int]])
class PrimMapStrToIntList:
    """A primitive for a dict mapping strings to lists of integers."""

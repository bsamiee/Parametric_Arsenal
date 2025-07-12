"""
Title         : prim_standard.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/primitives/prim_standard.py

Description
-----------
Standard primitive types for the most common Python built-ins.
"""

from __future__ import annotations

from mzn.types._core.core_builders import Build


@Build.primitive(str)
class PrimStr:
    """
    Immutable string primitive.

    A wrapper around the built-in str type that provides enhanced type safety and validation capabilities. This
    primitive is suitable for representing textual data, names, descriptions, or any string value that should be treated
    as an immutable value object.

    """


@Build.primitive(int)
class PrimInt:
    """
    Immutable integer primitive.

    A wrapper around the built-in int type that provides enhanced type safety and validation capabilities. This
    primitive is suitable for representing whole numbers, counts, indices, or any integer value that should be treated
    as an immutable value object.

    """


@Build.primitive(float)
class PrimFloat:
    """
    Immutable floating-point number primitive.

    A wrapper around the built-in float type that provides enhanced type safety and validation capabilities. This
    primitive is suitable for general-purpose numerical computations where floating-point precision is acceptable.

    """


@Build.primitive(bool)
class PrimFlag:
    """
    Immutable boolean flag primitive.

    A wrapper around the built-in bool type that provides enhanced type safety and validation capabilities. This
    primitive is useful for representing boolean flags, switches, or binary states with clear semantic meaning.

    """


@Build.primitive(bytes)
class PrimBinary:
    """
    Immutable binary data primitive.

    A wrapper around the built-in bytes type that provides enhanced type safety and validation capabilities. This
    primitive is suitable for handling binary data such as file contents, cryptographic material, or any sequence of
    bytes that should be treated as an immutable value object.

    """

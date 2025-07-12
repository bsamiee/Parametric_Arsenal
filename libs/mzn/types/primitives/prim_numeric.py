"""
Title         : prim_numeric.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/primitives/prim_numeric.py

Description
-----------
Specialized numeric primitive types.
"""

from __future__ import annotations

from decimal import Decimal as _Decimal
from fractions import Fraction as _Fraction

from mzn.types._core.core_builders import Build


@Build.primitive(_Decimal)
class PrimDecimal:
    """
    Immutable decimal number primitive.

    A wrapper around decimal.Decimal that provides enhanced type safety and validation capabilities. This primitive is
    ideal for financial calculations, monetary values, or any computation requiring exact decimal arithmetic without
    floating-point precision errors.

    """


@Build.primitive(_Fraction)
class PrimFractional:
    """
    Immutable exact rational number primitive.

    A wrapper around fractions.Fraction that provides enhanced type safety and validation capabilities. This primitive
    represents exact rational numbers as fractions, avoiding floating-point precision errors.

    """

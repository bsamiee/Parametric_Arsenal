"""
Title         : norm_numeric.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_numeric.py.

Description ----------- Numeric (int, float) normalization rules.

"""

from __future__ import annotations

import math
from typing import TYPE_CHECKING, TypeVar

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T", bound=str | float | int)
T_Numeric = TypeVar("T_Numeric", bound=int | float)

# --- Normalizers --------------------------------------------------------------


def round_to(*, places: int) -> Normalizer[float, float]:
    """Factory for a normalizer that rounds a float to a number of decimal places."""
    @Build.normalizer(
        description="Rounds a float to a number of decimal places.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: float, info: ValidationInfo) -> float:
        return round(value, places)

    return _normalizer


def clamp(
    *, min_value: float | None = None, max_value: float | None = None
) -> Normalizer[float, float]:
    """Factory for a normalizer that constrains a number to be within a specific range."""
    @Build.normalizer(
        description="Constrains a number to be within a specific range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: float, info: ValidationInfo) -> float:
        if min_value is not None and value < min_value:
            return min_value
        if max_value is not None and value > max_value:
            return max_value
        return value

    return _normalizer


def snap_to_grid(*, multiple: float) -> Normalizer[int | float, float]:
    """Factory for a normalizer that rounds a number to the nearest multiple of a given value."""
    @Build.normalizer(
        description="Rounds a number to the nearest multiple of a given value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: float, info: ValidationInfo) -> float:
        return multiple * round(value / multiple)

    return _normalizer


@Build.normalizer(
    register_as=NORM.NUMERIC.to_int,
    description="Attempts to convert a value to an integer.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_int(value: float | str, info: ValidationInfo) -> int:
    """Attempts to convert a value to an integer."""
    try:
        return int(value)
    except (ValueError, TypeError) as e:
        msg = f"Could not convert {value!r} to an integer."
        raise ValueError(msg) from e


@Build.normalizer(
    register_as=NORM.NUMERIC.to_float,
    description="Attempts to convert a value to a float.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_float(value: int | str, info: ValidationInfo) -> float:
    """Attempts to convert a value to a float."""
    try:
        return float(value)
    except (ValueError, TypeError) as e:
        msg = f"Could not convert {value!r} to a float."
        raise ValueError(msg) from e


@Build.normalizer(
    register_as=NORM.NUMERIC.absolute_value,
    description="Returns the absolute value of a number.",
    tags=(SYSTEM.INFRA.io,),
)
async def absolute_value(value: float, info: ValidationInfo) -> float:
    """Returns the absolute value of a number as a float."""
    return float(abs(value))


@Build.normalizer(
    register_as=NORM.NUMERIC.floor,
    description="Returns the floor of a number.",
    tags=(SYSTEM.INFRA.io,),
)
async def floor(value: float, info: ValidationInfo) -> int:
    """Returns the floor of a number."""
    return math.floor(value)


@Build.normalizer(
    register_as=NORM.NUMERIC.ceiling,
    description="Returns the ceiling of a number.",
    tags=(SYSTEM.INFRA.io,),
)
async def ceiling(value: float, info: ValidationInfo) -> int:
    """Returns the ceiling of a number."""
    return math.ceil(value)


def round_to_precision(*, precision: int) -> Normalizer[float, float]:
    """Factory for a normalizer that rounds a float to a given precision."""
    @Build.normalizer(
        description="Rounds a float to a given precision.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: float, info: ValidationInfo) -> float:
        return round(value, precision)

    return _normalizer

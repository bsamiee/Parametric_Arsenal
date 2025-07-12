"""
Title         : valid_numeric.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/validators/valid_numeric.py.

Description ----------- Numeric (int, float) validation rules.

"""

from __future__ import annotations

import math
from typing import TYPE_CHECKING, TypeVar

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import AEC, SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator

# --- Type Variables -----------------------------------------------------------

T_Numeric = TypeVar("T_Numeric", bound=int | float)

# --- Validators ---------------------------------------------------------------


@Build.validator(
    register_as=VALID.NUMERIC.is_non_negative,
    error_template="Value must be non-negative (>= 0).",
    description="Check if a number is zero or greater.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_non_negative(value: float, info: ValidationInfo) -> bool:
    """Check if a number is zero or greater."""
    return value >= 0


def is_in_range(*, min_value: float, max_value: float) -> Validator[int | float]:
    """Factory for a validator that checks if a number is within a specified range."""
    @Build.validator(
        error_template=f"Value must be between {min_value} and {max_value}.",
        description="Check if a number is within a specified range (inclusive).",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: float, info: ValidationInfo) -> bool:
        return min_value <= value <= max_value

    return _validator


def is_between_exclusive(*, min_value: float, max_value: float) -> Validator[int | float]:
    """Factory for a validator that checks if a number is within a specified range (exclusive)."""
    @Build.validator(
        error_template=f"Value must be between {min_value} and {max_value} (exclusive).",
        description="Check if a number is within a specified range (exclusive).",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: float, info: ValidationInfo) -> bool:
        return min_value < value < max_value

    return _validator


@Build.validator(
    register_as=VALID.NUMERIC.is_positive,
    error_template="Value must be positive.",
    description="Check if a number is greater than zero.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_positive(value: float, info: ValidationInfo) -> bool:
    """Check if a number is greater than zero."""
    return value > 0


@Build.validator(
    register_as=VALID.NUMERIC.is_negative,
    error_template="Value must be negative.",
    description="Check if a number is less than zero.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_negative(value: float, info: ValidationInfo) -> bool:
    """Check if a number is less than zero."""
    return value < 0


def is_multiple_of(*, factor: int) -> Validator[int]:
    """Factory for a validator that checks if an integer is a multiple of a given factor."""
    @Build.validator(
        error_template=f"Value must be a multiple of {factor}.",
        description="Check if an integer is a multiple of a given factor.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: int, info: ValidationInfo) -> bool:
        return value % factor == 0

    return _validator


@Build.validator(
    register_as=VALID.NUMERIC.is_latitude,
    error_template="Value must be a valid latitude (between -90 and 90).",
    description="Checks if a float is a valid latitude.",
    tags=(AEC.GEOMETRY,),
)
async def is_latitude(value: float, info: ValidationInfo) -> bool:
    """Checks if a float is a valid latitude."""
    return -90 <= value <= 90


@Build.validator(
    register_as=VALID.NUMERIC.is_longitude,
    error_template="Value must be a valid longitude (between -180 and 180).",
    description="Checks if a float is a valid longitude.",
    tags=(AEC.GEOMETRY,),
)
async def is_longitude(value: float, info: ValidationInfo) -> bool:
    """Checks if a float is a valid longitude."""
    return -180 <= value <= 180


def is_greater_than(*, threshold: float) -> Validator[int | float]:
    """Factory for a validator that checks if a number is strictly greater than a value."""
    @Build.validator(
        error_template=f"Value must be greater than {threshold}.",
        description="Checks if a number is strictly greater than a value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: float, info: ValidationInfo) -> bool:
        return value > threshold

    return _validator


def is_less_than(*, threshold: float) -> Validator[int | float]:
    """Factory for a validator that checks if a number is strictly less than a value."""
    @Build.validator(
        error_template=f"Value must be less than {threshold}.",
        description="Checks if a number is strictly less than a value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: float, info: ValidationInfo) -> bool:
        return value < threshold

    return _validator


@Build.validator(
    register_as=VALID.NUMERIC.is_even,
    error_template="Value must be an even number.",
    description="Checks if an integer is even.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_even(value: int, info: ValidationInfo) -> bool:
    """Checks if an integer is even."""
    return value % 2 == 0


@Build.validator(
    register_as=VALID.NUMERIC.is_odd,
    error_template="Value must be an odd number.",
    description="Checks if an integer is odd.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_odd(value: int, info: ValidationInfo) -> bool:
    """Checks if an integer is odd."""
    return value % 2 != 0


@Build.validator(
    register_as=VALID.NUMERIC.is_prime,
    error_template="Value must be a prime number.",
    description="Checks if a number is prime.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_prime(value: int, info: ValidationInfo) -> bool:
    """Checks if a number is prime."""
    if value <= 1:
        return False
    if value <= 3:
        return True
    if value % 2 == 0 or value % 3 == 0:
        return False
    i = 5
    while i * i <= value:
        if value % i == 0 or value % (i + 2) == 0:
            return False
        i += 6
    return True


def is_power_of(*, base: int) -> Validator[int]:
    """Factory for a validator that checks if a number is a power of a given base."""
    @Build.validator(
        error_template=f"Value must be a power of {base}.",
        description="Checks if a number is a power of a given base.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: int, info: ValidationInfo) -> bool:
        if value <= 0:
            return False
        while value % base == 0:
            value //= base
        return value == 1

    return _validator


@Build.validator(
    register_as=VALID.NUMERIC.is_finite,
    error_template="Value must be a finite number.",
    description="Checks if a number is finite (not infinity or NaN).",
    tags=(SYSTEM.INFRA.io,),
)
async def is_finite(value: float, info: ValidationInfo) -> bool:
    """Checks if a number is finite (not infinity or NaN)."""
    # `math.isfinite()` already returns False for both NaN and infinities.
    return math.isfinite(value)


@Build.validator(
    register_as=VALID.NUMERIC.is_nan,
    error_template="Value must be NaN.",
    description="Checks if a number is NaN.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_nan(value: float, info: ValidationInfo) -> bool:
    """Checks if a number is NaN."""
    return math.isnan(value)


@Build.validator(
    register_as=VALID.NUMERIC.is_integer_value,
    error_template="Value must be an integer.",
    description="Checks if a float has an integer value.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_integer_value(value: float, info: ValidationInfo) -> bool:
    """Checks if a float has an integer value."""
    return value.is_integer()

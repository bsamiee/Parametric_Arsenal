"""
Title         : valid_decimal.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_decimal.py

Description
-----------
Decimal validation rules for precise financial and numerical checks.
"""

from __future__ import annotations

from decimal import Decimal
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


def has_max_digits(*, max_digits: int) -> Validator[Decimal]:
    """Factory for a validator that checks that the total number of digits is within a limit."""
    @Build.validator(
        error_template=f"Value must not have more than {max_digits} digits in total.",
        description="Checks that the total number of digits is within a limit.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Decimal, info: ValidationInfo) -> bool:
        return len(value.as_tuple().digits) <= max_digits

    return _validator


def has_max_decimal_places(*, max_decimal_places: int) -> Validator[Decimal]:
    """Factory for a validator that checks that the number of decimal places is within a limit."""
    @Build.validator(
        error_template=f"Value must not have more than {max_decimal_places} decimal places.",
        description="Checks that the number of decimal places is within a limit.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Decimal, info: ValidationInfo) -> bool:
        exponent = value.as_tuple().exponent
        if isinstance(exponent, int):
            return exponent >= -max_decimal_places
        return False

    return _validator


def is_quantized(*, exp: str) -> Validator[Decimal]:
    """Factory for a validator that checks if the Decimal has a specific exponent."""
    @Build.validator(
        error_template=f"Value must be quantized to {exp}.",
        description="Checks if the Decimal has a specific exponent, useful for currency.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Decimal, info: ValidationInfo) -> bool:
        quantizer = Decimal(exp)
        return value == value.quantize(quantizer)

    return _validator


def has_precision(*, precision: int) -> Validator[Decimal]:
    """Factory for a validator that checks that the total number of digits is equal to a specific value."""
    @Build.validator(
        error_template=f"Value must have exactly {precision} digits in total.",
        description="Checks that the total number of digits is equal to a specific value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Decimal, info: ValidationInfo) -> bool:
        return len(value.as_tuple().digits) == precision

    return _validator


@Build.validator(
    register_as=VALID.NUMERIC.is_positive,
    error_template="Value must be positive.",
    description="Check if a decimal number is greater than zero.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_positive(value: Decimal, info: ValidationInfo) -> bool:
    """Check if a decimal number is greater than zero."""
    return value > Decimal(0)


@Build.validator(
    register_as=VALID.NUMERIC.is_negative,
    error_template="Value must be negative.",
    description="Check if a decimal number is less than zero.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_negative(value: Decimal, info: ValidationInfo) -> bool:
    """Check if a decimal number is less than zero."""
    return value < Decimal(0)

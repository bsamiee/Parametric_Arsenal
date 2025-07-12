"""
Title         : norm_decimal.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_decimal.py.

Description ----------- Decimal normalization rules for rounding and quantization.

"""

from __future__ import annotations

from decimal import ROUND_HALF_UP, Decimal
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


def quantize(
    *,
    exp: str,
    rounding: str = ROUND_HALF_UP) -> Normalizer[Decimal, Decimal]:
    """Factory for a normalizer that rounds the Decimal to a fixed exponent."""
    @Build.normalizer(
        description="Rounds the Decimal to a fixed exponent, essential for financial calculations.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: Decimal, info: ValidationInfo) -> Decimal:
        quantizer = Decimal(exp)
        return value.quantize(quantizer, rounding=rounding)

    return _normalizer

"""
Title         : valid_language.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/validators/valid_language.py.

Description ----------- Language-specific validation rules.

"""

from __future__ import annotations

from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


def matches_language(*, lang: str) -> Validator[str]:
    """Factory for a validator that checks if a string matches a given language."""
    @Build.validator(
        error_template=f"Value must be in the {lang} language.",
        description="Checks if a string matches a given language.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        # This is a placeholder implementation.
        # A real implementation would use a language detection library.
        return True

    return _validator

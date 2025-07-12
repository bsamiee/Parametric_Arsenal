"""
Title         : valid_uuid.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_uuid.py

Description
-----------
UUID validation rules for checking versions and formats.
"""

from __future__ import annotations

import uuid
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


@Build.validator(
    register_as=VALID.PROTOCOLS.is_uuid,
    error_template="Value must be a valid UUID.",
    description="Checks if the string is a valid UUID.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_uuid(value: str, info: ValidationInfo) -> bool:
    """Checks if the string is a valid UUID."""
    try:
        _ = uuid.UUID(value)
    except ValueError:
        return False
    return True


def is_uuid_version(*, version: int) -> Validator[str]:
    """Factory for a validator that checks if a UUID string is of a specific version."""
    @Build.validator(
        error_template=f"Value must be a valid UUID version {version}.",
        description="Checks if a UUID string is of a specific version.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        try:
            uuid_obj = uuid.UUID(value)
        except (ValueError, KeyError):
            return False
        return uuid_obj.version == version

    return _validator

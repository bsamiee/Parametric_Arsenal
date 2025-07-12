"""
Title         : norm_uuid.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_uuid.py

Description
-----------
UUID normalization rules for generating and formatting UUIDs.
"""

from __future__ import annotations

import uuid
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo


@Build.normalizer(
    register_as=NORM.PROTOCOLS.generate_if_none,
    description="Generates a new UUID v4 if the input value is None.",
    tags=(SYSTEM.INFRA.io,),
)
async def generate_if_none(value: str | None, info: ValidationInfo) -> str:
    """Generates a new UUID v4 if the input value is None."""
    if value is None:
        return str(uuid.uuid4())
    return value


@Build.normalizer(
    register_as=NORM.PROTOCOLS.to_urn_format,
    description="Converts a UUID string to its URN representation.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_urn_format(value: str, info: ValidationInfo) -> str:
    """Converts a UUID string to its URN representation."""
    try:
        return uuid.UUID(value).urn
    except ValueError:
        return value

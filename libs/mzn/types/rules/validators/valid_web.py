"""
Title         : valid_web.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_web.py

Description
-----------
Web-related validation rules for CSS selectors, SQL identifiers, and other web formats.
"""

from __future__ import annotations

import re
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo


@Build.validator(
    register_as=VALID.PROTOCOLS.is_css_selector,
    error_template="Value must be a valid CSS selector.",
    description="Validates CSS selector format.",
    tags=(SYSTEM.INFRA.web,),
)
async def is_css_selector(value: str, info: ValidationInfo) -> bool:
    """Validates CSS selector format."""
    # This is a basic check. A more robust implementation would use a dedicated library.
    return bool(re.match(r"^[a-zA-Z0-9\s\.\-\_#\[\]>+~*:]+$", value))


@Build.validator(
    register_as=VALID.PROTOCOLS.is_sql_identifier,
    error_template="Value must be a valid SQL identifier.",
    description="Validates SQL identifier format.",
    tags=(SYSTEM.INFRA.database,),
)
async def is_sql_identifier(value: str, info: ValidationInfo) -> bool:
    """Validates SQL identifier format."""
    return bool(re.match(r"^[a-zA-Z_][a-zA-Z0-9_]*$", value))

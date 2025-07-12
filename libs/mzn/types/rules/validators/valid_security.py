"""
Title         : valid_security.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/validators/valid_security.py.

Description ----------- Security-related validation rules for passwords, tokens, and other security checks.

"""

from __future__ import annotations

import base64
import binascii
import re
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator

# --- Private Helper Functions -------------------------------------------------


def _is_base64url(value: str) -> bool:
    """Check if a string is base64url encoded."""
    try:
        standard = value.replace("-", "+").replace("_", "/")
        padding = 4 - (len(standard) % 4) if len(standard) % 4 else 0
        standard += "=" * padding
        _ = base64.b64decode(standard)
    except (ValueError, TypeError, binascii.Error):
        return False
    else:
        return True

# --- Validators ---------------------------------------------------------------


def is_strong_password(
    *,
    min_length: int = 8,
    require_uppercase: bool = True,
    require_lowercase: bool = True,
    require_digits: bool = True,
    require_special: bool = True) -> Validator[str]:
    """Factory for a validator that checks if a password meets security requirements."""
    error_template = (
        f"Value must be a strong password with at least {min_length} characters, "
        "including uppercase, lowercase, digits, and special characters."
    )

    @Build.validator(
        error_template=error_template,
        description="Checks if a password meets security requirements.",
        tags=(SYSTEM.INFRA.io, SYSTEM.DEBUG),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if len(value) < min_length:
            return False
        if require_uppercase and not any(c.isupper() for c in value):
            return False
        if require_lowercase and not any(c.islower() for c in value):
            return False
        if require_digits and not any(c.isdigit() for c in value):
            return False
        return not (require_special and not any(c in "!@#$%^&*()_+-=[]{}|;:,.<>?/" for c in value))

    return _validator


@Build.validator(
    register_as=VALID.SECURITY.is_jwt,
    error_template="Value must be a valid JWT token.",
    description="Validates JWT format (not cryptographic validation).",
    tags=(SYSTEM.INFRA.network,),
)
async def is_jwt(value: str, info: ValidationInfo) -> bool:
    """
    Validates JWT format (not cryptographic validation).

    Checks for three base64url-encoded parts separated by dots.

    """
    parts = value.split(".")
    if len(parts) != 3:
        return False
    return all(_is_base64url(part) for part in parts)


def has_no_sensitive_info(
    *,
    check_credit_cards: bool = True,
    check_ssns: bool = True,
    check_api_keys: bool = True) -> Validator[str]:
    """Factory for a validator that checks that a string doesn't contain sensitive info."""
    @Build.validator(
        error_template="Value must not contain sensitive information patterns.",
        description="Checks that a string doesn't contain sensitive information patterns.",
        tags=(SYSTEM.INFRA.io, SYSTEM.DEBUG),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if check_credit_cards and re.search(r"(?:\d{4}[- ]?){3}\d{4}", value):
            return False
        if check_ssns and re.search(r"\b\d{3}[-]?\d{2}[-]?\d{4}\b", value):
            return False
        return not (check_api_keys and re.search(r"\b[A-Za-z0-9_-]{20,}\b", value))

    return _validator


def is_hmac(*, length: int = 64) -> Validator[str]:
    """Factory for a validator that checks if a string looks like an HMAC."""
    @Build.validator(
        error_template="Value must be a valid HMAC.",
        description="Validates that a string looks like an HMAC (hexadecimal format).",
        tags=(SYSTEM.INFRA.io, SYSTEM.DEBUG),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        return len(value) == length and bool(re.match(r"^[0-9a-f]+$", value.lower()))

    return _validator

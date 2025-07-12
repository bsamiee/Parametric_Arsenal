"""
Title         : valid_string.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_string.py

Description
-----------
String-specific validation rules, refactored for the new registry system.
"""

from __future__ import annotations

import re
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


def has_length(
    *, min_length: int = 0, max_length: int | None = None
) -> Validator[str]:
    """Factory for a validator to check if a string's length is within a specified range."""
    error_template = f"Value must be between {min_length} and {max_length or 'infinity'} characters long."

    @Build.validator(
        error_template=error_template,
        description="Checks if a string's length is within a specified range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if len(value) < min_length:
            return False
        return not (max_length is not None and len(value) > max_length)

    return _validator


@Build.validator(
    register_as=VALID.STRING.is_alpha,
    error_template="Value must contain only alphabetic characters.",
    description="Checks if a string contains only alphabetic characters.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_alpha(value: str, info: ValidationInfo) -> bool:
    """Check if a string contains only alphabetic characters."""
    return value.isalpha()


@Build.validator(
    register_as=VALID.STRING.is_alnum,
    error_template="Value must contain only alphanumeric characters.",
    description="Checks if a string contains only alphanumeric characters.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_alnum(value: str, info: ValidationInfo) -> bool:
    """Check if a string contains only alphanumeric characters."""
    return value.isalnum()


@Build.validator(
    register_as=VALID.STRING.is_email,
    error_template="Value must be a valid email address.",
    description="Checks if a string is a valid email address (simple regex).",
    tags=(SYSTEM.INFRA.io,),
)
async def is_email(value: str, info: ValidationInfo) -> bool:
    """Check if a string is a valid email address (simple regex)."""
    pattern = r"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$"
    return re.match(pattern, value) is not None


@Build.validator(
    register_as=VALID.STRING.is_credit_card,
    error_template="Value must be a valid credit card number.",
    description="Validates common credit card number formats using the Luhn algorithm.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_credit_card(value: str, info: ValidationInfo) -> bool:
    """Validates common credit card number formats using the Luhn algorithm."""
    s = "".join(filter(str.isdigit, value))
    if not 13 <= len(s) <= 19:
        return False
    digits = [int(d) for d in s]
    checksum = sum(digits[-1::-2]) + sum(sum(divmod(d * 2, 10)) for d in digits[-2::-2])
    return checksum % 10 == 0


def is_isbn(*, version: str = "13") -> Validator[str]:
    """Factory for a validator that checks for valid ISBN-10 or ISBN-13 numbers."""
    error_template = f"Value must be a valid ISBN-{version}."

    @Build.validator(
        error_template=error_template,
        description=f"Checks for valid ISBN-{version} numbers.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:  # noqa: PLR0911
        # Remove hyphens and spaces
        cleaned = value.replace("-", "").replace(" ", "")

        if version == "10":
            if len(cleaned) != 10:
                return False
            # ISBN-10 validation logic
            try:
                checksum = sum((10 - i) * (int(x) if x.isdigit() else 10) for i, x in enumerate(cleaned))
                return checksum % 11 == 0
            except ValueError:
                return False
        elif version == "13":
            if len(cleaned) != 13 or not cleaned.isdigit():
                return False
            # ISBN-13 validation logic
            try:
                checksum = sum(int(cleaned[i]) * (1 if i % 2 == 0 else 3) for i in range(12))
                return (10 - (checksum % 10)) % 10 == int(cleaned[12])
            except ValueError:
                return False
        return False

    return _validator


@Build.validator(
    register_as=VALID.STRING.has_no_leading_trailing_whitespace,
    error_template="Value must not have leading or trailing whitespace.",
    description="Checks that a string has no whitespace at the start or end.",
    tags=(SYSTEM.INFRA.io,),
)
async def has_no_leading_trailing_whitespace(value: str, info: ValidationInfo) -> bool:
    """Checks that a string has no whitespace at the start or end."""
    return value == value.strip()


@Build.validator(
    register_as=VALID.STRING.is_lowercase,
    error_template="Value must be in all lowercase.",
    description="Checks if a string is entirely in lowercase.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_lowercase(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is entirely in lowercase."""
    return value.islower()


@Build.validator(
    register_as=VALID.STRING.is_uppercase,
    error_template="Value must be in all uppercase.",
    description="Checks if a string is entirely in uppercase.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_uppercase(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is entirely in uppercase."""
    return value.isupper()


@Build.validator(
    register_as=VALID.STRING.is_titlecase,
    error_template="Value must be in title case.",
    description="Checks if a string is in title case.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_titlecase(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is in title case."""
    return value.istitle()


@Build.validator(
    register_as=VALID.STRING.is_ascii,
    error_template="Value must contain only ASCII characters.",
    description="Checks if a string contains only ASCII characters.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_ascii(value: str, info: ValidationInfo) -> bool:
    """Checks if a string contains only ASCII characters."""
    return value.isascii()


@Build.validator(
    register_as=VALID.STRING.is_ascii_printable,
    error_template="Value must contain only printable ASCII characters.",
    description="Checks if a string contains only printable ASCII characters.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_ascii_printable(value: str, info: ValidationInfo) -> bool:
    """Checks if a string contains only printable ASCII characters."""
    return value.isprintable() and value.isascii()


@Build.validator(
    register_as=VALID.STRING.is_palindrome,
    error_template="Value must be a palindrome.",
    description="Checks if a string is a palindrome.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_palindrome(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is a palindrome."""
    return value == value[::-1]


@Build.validator(
    register_as=VALID.STRING.has_balanced_brackets,
    error_template="Value must have balanced brackets.",
    description="Checks if a string has balanced brackets.",
    tags=(SYSTEM.INFRA.io,),
)
async def has_balanced_brackets(value: str, info: ValidationInfo) -> bool:
    """Checks if a string has balanced brackets."""
    stack: list[str] = []
    bracket_map = {")": "(", "}": "{", "]": "["}
    for char in value:
        if char in bracket_map.values():
            stack.append(char)
        elif char in bracket_map and (not stack or bracket_map[char] != stack.pop()):
            return False
    return not stack


def contains_only(*, chars: str) -> Validator[str]:
    """Factory for a validator that checks if a string contains only characters from a given set."""
    error_template = f"Value must contain only characters from: {chars}"

    @Build.validator(
        error_template=error_template,
        description="Checks if a string contains only characters from a given set.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        return all(c in chars for c in value)

    return _validator


@Build.validator(
    register_as=VALID.STRING.is_semver,
    error_template="Value must be a valid semantic version string.",
    description="Checks for semantic versioning strings (e.g., '1.2.3').",
    tags=(SYSTEM.INFRA.io,),
)
async def is_semver(value: str, info: ValidationInfo) -> bool:
    """Checks for semantic versioning strings (e.g., '1.2.3')."""
    return (
        re.match(
            r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
            value,
        )
        is not None
    )


@Build.validator(
    register_as=VALID.STRING.is_slug,
    error_template="Value must be a valid URL slug.",
    description="Checks if a string is a valid URL slug.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_slug(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is a valid URL slug."""
    return re.match(r"^[a-z0-9]+(?:-[a-z0-9]+)*$", value) is not None


# Additional string validators
def does_not_end_with(*, suffix: str) -> Validator[str]:
    """Factory for creating a suffix exclusion validator."""
    error_template = f"Value must not end with '{suffix}'"

    @Build.validator(
        error_template=error_template,
        description="Checks that a string does not end with a specific suffix.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        return not value.endswith(suffix)

    return _validator


def matches_pattern(*, pattern: str) -> Validator[str]:
    """Factory for creating a regex pattern validator."""
    error_template = f"Value must match pattern: {pattern}"

    @Build.validator(
        error_template=error_template,
        description="Checks if a string matches a regular expression pattern.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        return re.match(pattern, value) is not None

    return _validator


def no_consecutive_chars(*, char: str) -> Validator[str]:
    """Factory for creating a validator that checks for no consecutive characters."""
    error_template = f"Value must not contain consecutive '{char}' characters"

    @Build.validator(
        error_template=error_template,
        description="Checks that a string does not contain consecutive occurrences of a character.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        double_char = char + char
        return double_char not in value

    return _validator


@Build.validator(
    register_as=VALID.STRING.is_internal_prefix,
    error_template="Value must be a valid internal prefix (e.g., '__name:')",
    description="Checks if string is a valid internal prefix pattern",
    tags=(SYSTEM.INFRA.io,),
)
async def is_internal_prefix(value: str, info: ValidationInfo) -> bool:
    """
    Check if string matches internal prefix pattern.

    Pattern: starts with __, followed by lowercase letters, ends with :
    """
    return re.match(r"^__[a-z]+:$", value) is not None

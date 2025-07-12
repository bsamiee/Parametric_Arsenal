"""
Title         : norm_string.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/types/rules/normalizers/norm_string.py.

Description ----------- String-specific normalization rules.

"""

from __future__ import annotations

import hashlib
import re
import unicodedata
from typing import TYPE_CHECKING, Literal

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


@Build.normalizer(
    register_as=NORM.STRING.to_lowercase,
    description="Converts the string to lowercase.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_lowercase(value: str, info: ValidationInfo) -> str:
    """Converts the string to lowercase."""
    return value.lower()


@Build.normalizer(
    register_as=NORM.STRING.to_uppercase,
    description="Converts the string to uppercase.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_uppercase(value: str, info: ValidationInfo) -> str:
    """Converts the string to uppercase."""
    return value.upper()


@Build.normalizer(
    register_as=NORM.STRING.strip_whitespace,
    description="Removes leading/trailing whitespace.",
    tags=(SYSTEM.INFRA.io,),
)
async def strip_whitespace(value: str, info: ValidationInfo) -> str:
    """Removes leading/trailing whitespace."""
    return value.strip()


@Build.normalizer(
    register_as=NORM.STRING.capitalize_words,
    description="Capitalizes the first letter of each word.",
    tags=(SYSTEM.INFRA.io,),
)
async def capitalize_words(value: str, info: ValidationInfo) -> str:
    """Capitalizes the first letter of each word."""
    return value.title()


@Build.normalizer(
    register_as=NORM.STRING.to_snake_case,
    description="Converts a string to snake_case.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_snake_case(value: str, info: ValidationInfo) -> str:
    """Converts a string to snake_case."""
    s1 = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", value)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", s1).lower()


@Build.normalizer(
    register_as=NORM.STRING.to_camel_case,
    description="Converts a string to camelCase.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_camel_case(value: str, info: ValidationInfo) -> str:
    """Converts a string to camelCase."""
    parts = value.replace("-", "_").split("_")
    return parts[0] + "".join(p.title() for p in parts[1:])


@Build.normalizer(
    register_as=NORM.STRING.to_pascal_case,
    description="Converts a string to PascalCase.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_pascal_case(value: str, info: ValidationInfo) -> str:
    """Converts a string to PascalCase from snake_case or kebab-case."""
    return "".join(word.capitalize() for word in re.split(r"[-_ ]", value))


@Build.normalizer(
    register_as=NORM.STRING.to_kebab_case,
    description="Converts a string to kebab-case.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_kebab_case(value: str, info: ValidationInfo) -> str:
    """Converts a string to kebab-case."""
    s1 = re.sub(r"(.)([A-Z][a-z]+)", r"\1-\2", value)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", s1).lower()


@Build.normalizer(
    register_as=NORM.STRING.to_title_case,
    description="Converts a string to Title Case.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_title_case(value: str, info: ValidationInfo) -> str:
    """Converts a string to Title Case."""
    return value.title()


@Build.normalizer(
    register_as=NORM.STRING.remove_punctuation,
    description="Removes all punctuation from a string.",
    tags=(SYSTEM.INFRA.io,),
)
async def remove_punctuation(value: str, info: ValidationInfo) -> str:
    """Removes all punctuation from a string."""
    return re.sub(r"[^\w\s]", "", value)


def truncate(*, max_length: int, suffix: str = "...") -> Normalizer[str, str]:
    """Factory for a normalizer that truncates a string to a max length."""
    @Build.normalizer(
        description="Truncates a string to a max length, adding a suffix.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        if len(value) <= max_length:
            return value
        return value[: max_length - len(suffix)] + suffix

    return _normalizer


def replace(*, old: str, new: str) -> Normalizer[str, str]:
    """Factory for a normalizer that replaces all occurrences of a substring."""
    @Build.normalizer(
        description="Replaces all occurrences of a substring.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        return value.replace(old, new)

    return _normalizer


def pad_left(*, width: int, fill: str = " ") -> Normalizer[str, str]:
    """Factory for a normalizer that pads the string on the left."""
    @Build.normalizer(
        description="Pads the string on the left.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        return str(value).rjust(width, fill)

    return _normalizer


@Build.normalizer(
    register_as=NORM.STRING.reverse,
    description="Reverses a string",
    tags=(SYSTEM.INFRA.io,),
)
async def reverse(value: str, info: ValidationInfo) -> str:
    """Reverse a string."""
    return value[::-1]


@Build.normalizer(
    register_as=NORM.STRING.empty_string_to_none,
    description="Converts empty strings to None",
    tags=(SYSTEM.INFRA.io,),
)
async def empty_string_to_none(value: str, info: ValidationInfo) -> str | None:
    """Convert empty strings to None."""
    if not value:
        return None
    return value


@Build.normalizer(
    register_as=NORM.STRING.normalize_whitespace,
    description="Normalizes whitespace by collapsing multiple spaces to single spaces",
    tags=(SYSTEM.INFRA.io,),
)
async def normalize_whitespace(value: str, info: ValidationInfo) -> str:
    """Normalize whitespace by collapsing multiple spaces to single spaces."""
    # Replace multiple whitespace characters with single space
    return re.sub(r"\s+", " ", value.strip())


@Build.normalizer(
    register_as=NORM.STRING.normalize_unicode,
    description="Normalizes unicode data.",
    tags=(SYSTEM.INFRA.io,),
)
async def normalize_unicode(
    value: str, info: ValidationInfo, *, form: Literal["NFC", "NFKC", "NFD", "NFKD"] = "NFKC"
) -> str:
    """Normalizes unicode data."""
    return unicodedata.normalize(form, value)


@Build.normalizer(
    register_as=NORM.STRING.ensure_ascii,
    description="Ensures a string is ASCII, replacing non-ASCII characters.",
    tags=(SYSTEM.INFRA.io,),
)
async def ensure_ascii(value: str, info: ValidationInfo, *, replacement: str = "?") -> str:
    """Ensures a string is ASCII, replacing non-ASCII characters."""
    return value.encode("ascii", errors="replace").decode("ascii").replace("?", replacement)


def limit_words(*, max_words: int) -> Normalizer[str, str]:
    """Factory for a normalizer that limits the number of words in a string."""
    @Build.normalizer(
        description="Limits the number of words in a string.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        return " ".join(value.split()[:max_words])

    return _normalizer


@Build.normalizer(
    register_as=NORM.STRING.slugify,
    description="Converts a string to a URL-friendly slug.",
    tags=(SYSTEM.INFRA.io,),
)
async def slugify(value: str, info: ValidationInfo) -> str:
    """Converts a string to a URL-friendly slug."""
    value = value.lower().strip()
    value = re.sub(r"[^\w\s-]", "", value)
    value = re.sub(r"[\s_-]+", "-", value)
    return re.sub(r"^-+|-+$", "", value)


@Build.normalizer(
    register_as=NORM.STRING.strip_html,
    description="Removes HTML tags from a string.",
    tags=(SYSTEM.INFRA.io,),
)
async def strip_html(value: str, info: ValidationInfo) -> str:
    """Removes HTML tags from a string."""
    return re.sub(r"<[^>]*>", "", value)


# Additional string normalizers
@Build.normalizer(
    register_as=NORM.STRING.deduplicate_separators,
    description="Remove duplicate separator characters",
    tags=(SYSTEM.INFRA.io,),
)
async def deduplicate_separators(value: str, info: ValidationInfo) -> str:
    """
    Remove duplicate separator characters.

    Default separators: ::, __, --, // Can be customized via info.context['separators']

    """
    separators = (info.context.get("separators", ["::", "__", "--", "//"])
                  if info.context is not None else ["::", "__", "--", "//"])

    result = value
    for sep in separators:
        if len(sep) >= 2:
            single = sep[0]
            result = re.sub(f"{re.escape(single)}{{2,}}", single, result)

    return result


@Build.normalizer(
    register_as=NORM.STRING.remove_control_characters,
    description="Remove control characters from string",
    tags=(SYSTEM.INFRA.io,),
)
async def remove_control_characters(value: str, info: ValidationInfo) -> str:
    """
    Remove control characters (non-printable) from string.

    Keeps tabs, newlines, and carriage returns by default.

    """
    keep_chars = (info.context.get("keep_chars", "\t\n\r")
                  if info.context is not None else "\t\n\r")

    def is_allowed(char: str) -> bool:
        return char in keep_chars or (ord(char) >= 32 and ord(char) != 127)

    return "".join(char for char in value if is_allowed(char))


def hash_if_exceeds_length(
    *, max_length: int = 200, hash_prefix: str = "hash:", hash_algorithm: str = "sha256"
) -> Normalizer[str, str]:
    """Factory for creating a hash-if-exceeds-length normalizer."""

    @Build.normalizer(
        description=f"Hash string if longer than {max_length} characters",
        tags=(SYSTEM.INFRA.io, SYSTEM.PERFORMANCE),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        """Hash the string if it exceeds max length."""
        if len(value) <= max_length:
            return value

        # Get hash function
        hash_func = getattr(hashlib, hash_algorithm, hashlib.sha256)
        hash_val = hash_func(value.encode()).hexdigest()[:16]

        # Include a preview of the original string
        preview_length = max_length - len(hash_prefix) - 16 - 1
        if preview_length > 0:
            preview = value[:preview_length]
            return f"{hash_prefix}{preview}:{hash_val}"

        return f"{hash_prefix}{hash_val}"

    return _normalizer

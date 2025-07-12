"""
Cache domain-specific normalizers.

This module provides normalization rules specifically designed for cache operations, including key normalization, TTL
adjustments, and tag processing.

"""

from __future__ import annotations

import hashlib
import random
import re
from typing import TYPE_CHECKING, Annotated

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from collections.abc import Sequence

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


# Key normalization
@Build.normalizer(
    register_as=NORM.DOMAINS.CACHE.normalize_cache_key,
    description="Comprehensive cache key normalization",
    tags=(SYSTEM.CACHE,),
)
async def normalize_cache_key(
    value: Annotated[str, "The cache key to normalize"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> str:
    """
    Normalize cache key with full pipeline.

    Steps: 1. Strip whitespace 2. Convert to lowercase 3. Replace spaces with underscores 4. Remove any remaining
    invalid characters 5. Deduplicate separators

    """
    # Strip whitespace
    normalized = value.strip()

    # Convert to lowercase
    normalized = normalized.lower()

    # Replace spaces with underscores
    normalized = normalized.replace(" ", "_")

    # Remove invalid characters (keep only alphanumeric, :, _, -)
    normalized = re.sub(r"[^a-z0-9:_\-]", "", normalized)

    # Deduplicate separators
    normalized = re.sub(r":{2,}", ":", normalized)
    normalized = re.sub(r"_{2,}", "_", normalized)
    normalized = re.sub(r"-{2,}", "-", normalized)

    # Remove leading/trailing separators
    normalized = normalized.strip(":_-")

    return normalized or "default"


def hash_if_too_long(
    *, max_length: int = 200, hash_prefix: str = "hash:", hash_length: int = 16
) -> Normalizer[str, str]:
    """Factory for creating a hash-if-too-long normalizer."""

    @Build.normalizer(
        register_as=NORM.DOMAINS.CACHE.hash_if_too_long,
        description=f"Hash keys longer than {max_length} characters",
        tags=(SYSTEM.CACHE, SYSTEM.PERFORMANCE),
    )
    async def _normalizer(
        value: Annotated[str, "The key to potentially hash"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> str:
        """Hash the value if it exceeds max length."""
        if len(value) <= max_length:
            return value

        # Use SHA256 and take first N characters of hex
        hash_val = hashlib.sha256(value.encode()).hexdigest()[:hash_length]

        # Include a portion of the original key for debugging
        preview_length = max_length - len(hash_prefix) - hash_length - 1
        if preview_length > 0:
            preview = value[:preview_length]
            return f"{hash_prefix}{preview}:{hash_val}"

        return f"{hash_prefix}{hash_val}"

    return _normalizer


def add_namespace_prefix(*, namespace: str, separator: str = ":") -> Normalizer[str, str]:
    """Factory for creating a namespace prefix normalizer."""

    @Build.normalizer(
        register_as=NORM.DOMAINS.CACHE.add_namespace_prefix,
        description=f"Add namespace prefix: {namespace}",
        tags=(SYSTEM.CACHE,),
    )
    async def _normalizer(
        value: Annotated[str, "The key to prefix"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> str:
        """Add namespace prefix to key if not already present."""
        prefix = f"{namespace}{separator}"

        if value.startswith(prefix):
            return value

        return f"{prefix}{value}"

    return _normalizer


@Build.normalizer(
    register_as=NORM.DOMAINS.CACHE.deduplicate_separators,
    description="Remove duplicate separator characters",
    tags=(SYSTEM.CACHE,),
)
async def deduplicate_separators(
    value: Annotated[str, "The string to clean"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> str:
    """
    Remove duplicate separator characters.

    Handles: ::, __, --, // etc.

    """
    separators = (info.context.get("separators", ["::", "__", "--", "//"])
                  if info.context is not None else ["::", "__", "--", "//"])

    result = value
    for sep in separators:
        if len(sep) >= 2:
            single = sep[0]
            result = re.sub(f"{re.escape(single)}{{2,}}", single, result)

    return result


# TTL normalization
def add_ttl_jitter(
    *, jitter_percent: float = 10.0, min_ttl: int = 60
) -> Normalizer[int, int]:
    """
    Factory for creating a TTL jitter normalizer.

    Adds random jitter to prevent thundering herd problem.

    """

    @Build.normalizer(
        register_as=NORM.DOMAINS.CACHE.add_ttl_jitter,
        description=f"Add up to {jitter_percent}% jitter to TTL",
        tags=(SYSTEM.CACHE, SYSTEM.PERFORMANCE),
    )
    async def _normalizer(
        value: Annotated[int, "The base TTL in seconds"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> int:
        """Add random jitter to TTL value."""
        if value < min_ttl:
            return value  # Don't add jitter to very short TTLs

        max_jitter = int(value * (jitter_percent / 100))
        jitter = random.randint(-max_jitter, max_jitter)  # noqa: S311

        return max(min_ttl, value + jitter)

    return _normalizer


@Build.normalizer(
    register_as=NORM.DOMAINS.CACHE.normalize_ttl_value,
    description="Normalize TTL to standard seconds",
    tags=(SYSTEM.CACHE, SYSTEM.COMMON.time),
)
async def normalize_ttl_value(
    value: Annotated[int | float | str, "The TTL value to normalize"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> int:
    """
    Normalize various TTL formats to seconds.

    Handles: - Integer seconds - Float seconds (rounded) - String with units: "1h", "30m", "1d"

    """
    if isinstance(value, int):
        return value

    if isinstance(value, float):
        return round(value)

    # If value is a string, try to parse string with units
    if type(value) is str:
        value = value.strip().lower()

        # Simple unit parsing
        multipliers = {
            "s": 1,
            "sec": 1,
            "second": 1,
            "seconds": 1,
            "m": 60,
            "min": 60,
            "minute": 60,
            "minutes": 60,
            "h": 3600,
            "hr": 3600,
            "hour": 3600,
            "hours": 3600,
            "d": 86400,
            "day": 86400,
            "days": 86400,
        }

        # Extract number and unit
        match = re.match(r"^(\d+(?:\.\d+)?)\s*([a-z]+)?$", value)
        if match:
            number_str, unit = match.groups()
            number = float(number_str)

            if unit and unit in multipliers:
                return round(number * multipliers[unit])

            # No unit means seconds
            return round(number)

    # Fallback: try to convert directly
    try:
        return int(value)
    except (ValueError, TypeError):
        # Default to 1 hour if we can't parse
        return 3600


# Tag normalization
@Build.normalizer(
    register_as=NORM.DOMAINS.CACHE.normalize_tag_name,
    description="Normalize cache tag name",
    tags=(SYSTEM.CACHE,),
)
async def normalize_tag_name(
    value: Annotated[str, "The tag to normalize"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> str:
    """
    Normalize tag name for consistency.

    Steps: 1. Strip whitespace 2. Convert to lowercase 3. Replace spaces and underscores with hyphens 4. Remove invalid
    characters 5. Ensure valid format

    """
    # Strip and lowercase
    normalized = value.strip().lower()

    # Replace spaces and underscores with hyphens
    normalized = re.sub(r"[\s_]+", "-", normalized)

    # Keep only valid characters
    normalized = re.sub(r"[^a-z0-9\-\.]", "", normalized)

    # Remove multiple consecutive hyphens
    normalized = re.sub(r"-{2,}", "-", normalized)

    # Remove leading/trailing hyphens and dots
    normalized = normalized.strip("-.")

    return normalized or "default-tag"


@Build.normalizer(
    register_as=NORM.DOMAINS.CACHE.deduplicate_tags,
    description="Remove duplicate tags from collection",
    tags=(SYSTEM.CACHE,),
)
async def deduplicate_tags(
    value: Annotated[Sequence[str], "The list of tags"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> list[str]:
    """
    Remove duplicate tags while preserving order.

    Also normalizes each tag in the process.

    """
    seen: set[str] = set()
    unique_tags: list[str] = []

    for tag in value:
        # Normalize the tag first
        normalized = await normalize_tag_name(tag, info)

        if normalized not in seen:
            seen.add(normalized)
            unique_tags.append(normalized)

    return unique_tags

"""
Title         : cache.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/core/cache.py

Description
-----------
Cache domain-specific validators.

This module provides validation rules specifically designed for cache operations,
including key validation, TTL checks, tag validation, and configuration validation.
"""

from __future__ import annotations

import re
from typing import TYPE_CHECKING, Annotated, Any

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from collections.abc import Sequence

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


# Key validation
@Build.validator(
    register_as=VALID.DOMAINS.CACHE.is_valid_cache_key,
    description="Validates cache key format and constraints",
    error_template="Invalid cache key: {error_detail}",
    tags=(SYSTEM.CACHE,),
)
async def is_valid_cache_key(
    value: Annotated[str, "The cache key to validate"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> bool:
    """
    Validate cache key format and constraints.

    Checks:
    - Only contains alphanumeric characters, colons, underscores, and hyphens
    - No consecutive separators (::, __, --)
    - Length within limits (1-250 characters)
    """
    # Check length
    if not value or len(value) > 250:
        if info.context is not None:
            info.context["error_detail"] = "must be between 1 and 250 characters"
        return False

    # Check for invalid characters
    if not re.match(r"^[a-zA-Z0-9:_\-]+$", value):
        if info.context is not None:
            info.context["error_detail"] = "contains invalid characters"
        return False

    # Check for consecutive separators
    if any(sep in value for sep in ["::", "__", "--"]):
        if info.context is not None:
            info.context["error_detail"] = "contains consecutive separators"
        return False

    return True


@Build.validator(
    register_as=VALID.DOMAINS.CACHE.has_valid_namespace,
    description="Validates cache namespace format",
    error_template="Invalid namespace: {error_detail}",
    tags=(SYSTEM.CACHE,),
)
async def has_valid_namespace(
    value: Annotated[str, "The namespace to validate"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> bool:
    """
    Validate cache namespace format.

    Namespaces should:
    - Start with a letter
    - Contain only letters, numbers, and underscores
    - Be between 1 and 50 characters
    """
    if not value:
        if info.context is not None:
            info.context["error_detail"] = "cannot be empty"
        return False

    if len(value) > 50:
        if info.context is not None:
            info.context["error_detail"] = "exceeds maximum length of 50 characters"
        return False

    if not re.match(r"^[a-zA-Z][a-zA-Z0-9_]*$", value):
        if info.context is not None:
            info.context["error_detail"] = "must start with letter and contain only letters, numbers, underscores"
        return False

    return True


def has_key_prefix(*, prefix: str) -> Validator[str]:
    """Factory for creating a key prefix validator."""

    @Build.validator(
        register_as=VALID.DOMAINS.CACHE.has_key_prefix,
        description=f"Check if key has prefix: {prefix!r}",
        error_template=f"Cache key missing required prefix: {prefix}",
        tags=(SYSTEM.CACHE,),
    )
    async def _validator(
        value: Annotated[str, "The cache key to check"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> bool:
        """Check if cache key starts with required prefix."""
        if info.context is not None:
            info.context["prefix"] = prefix
        return value.startswith(prefix)

    return _validator


@Build.validator(
    register_as=VALID.DOMAINS.CACHE.key_within_length_limit,
    description="Validates cache key length is within acceptable limits",
    error_template="Cache key length {length} exceeds limit of {max_length}",
    tags=(SYSTEM.CACHE,),
)
async def key_within_length_limit(
    value: Annotated[str, "The cache key to validate"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> bool:
    """Check if cache key length is within acceptable limits."""
    max_length = info.context.get("max_length", 250) if info.context is not None else 250
    length = len(value)

    if length > max_length:
        if info.context is not None:
            info.context["length"] = length
            info.context["max_length"] = max_length
        return False

    return True


# TTL validation
def is_valid_ttl_range(*, min_ttl: int = 1, max_ttl: int = 86400 * 365) -> Validator[int]:
    """
    Factory for creating a TTL range validator.

    Default range: 1 second to 1 year.
    """

    @Build.validator(
        register_as=VALID.DOMAINS.CACHE.is_valid_ttl_range,
        description=f"Validate TTL is between {min_ttl} and {max_ttl} seconds",
        error_template="TTL {value} is outside valid range [{min_ttl}, {max_ttl}]",
        tags=(SYSTEM.CACHE, SYSTEM.COMMON.time),
    )
    async def _validator(
        value: Annotated[int, "The TTL value in seconds"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> bool:
        """Validate TTL is within acceptable range."""
        if info.context is not None:
            info.context["value"] = value
            info.context["min_ttl"] = min_ttl
            info.context["max_ttl"] = max_ttl
        return min_ttl <= value <= max_ttl

    return _validator


def has_ttl_jitter_range(*, max_jitter_percent: float = 10.0) -> Validator[float]:
    """Factory for creating a TTL jitter range validator."""

    @Build.validator(
        register_as=VALID.DOMAINS.CACHE.has_ttl_jitter_range,
        description=f"Validate TTL jitter is within {max_jitter_percent}%",
        error_template="TTL jitter {value}% exceeds maximum of {max_jitter_percent}%",
        tags=(SYSTEM.CACHE, SYSTEM.COMMON.time),
    )
    async def _validator(
        value: Annotated[float, "The jitter percentage"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> bool:
        """Validate TTL jitter percentage is reasonable."""
        if info.context is not None:
            info.context["value"] = value
            info.context["max_jitter_percent"] = max_jitter_percent
        return 0 <= value <= max_jitter_percent

    return _validator


# Tag validation
@Build.validator(
    register_as=VALID.DOMAINS.CACHE.is_valid_tag_pattern,
    description="Validates cache tag format",
    error_template="Invalid tag format: {error_detail}",
    tags=(SYSTEM.CACHE,),
)
async def is_valid_tag_pattern(
    value: Annotated[str, "The tag to validate"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> bool:
    """
    Validate cache tag format.

    Tags should:
    - Contain only lowercase letters, numbers, hyphens, and dots
    - Not start or end with separators
    - Be between 1 and 100 characters
    """
    if not value or len(value) > 100:
        if info.context is not None:
            info.context["error_detail"] = "must be between 1 and 100 characters"
        return False

    if not re.match(r"^[a-z0-9][a-z0-9\-\.]*[a-z0-9]$", value):
        if info.context is not None:
            info.context["error_detail"] = "must contain only lowercase letters, numbers, hyphens, dots"
        return False

    return True


@Build.validator(
    register_as=VALID.DOMAINS.CACHE.has_valid_tag_hierarchy,
    description="Validates hierarchical tag structure",
    error_template="Invalid tag hierarchy: {error_detail}",
    tags=(SYSTEM.CACHE,),
)
async def has_valid_tag_hierarchy(
    value: Annotated[str, "The hierarchical tag to validate"],
    info: Annotated[ValidationInfo, "Validation context"],
) -> bool:
    """
    Validate hierarchical tag structure (e.g., 'category.subcategory.item').

    Each segment should be a valid tag pattern.
    """
    segments = value.split(".")

    if len(segments) > 5:
        if info.context is not None:
            info.context["error_detail"] = "hierarchy too deep (max 5 levels)"
        return False

    for segment in segments:
        if not segment:
            if info.context is not None:
                info.context["error_detail"] = "empty segment in hierarchy"
            return False

        # Each segment should match basic tag pattern
        if not re.match(r"^[a-z0-9][a-z0-9\-]*[a-z0-9]$|^[a-z0-9]$", segment):
            if info.context is not None:
                info.context["error_detail"] = f"invalid segment: {segment}"
            return False

    return True


# Configuration validation
def has_compatible_features(
    *, incompatible_pairs: Sequence[tuple[str, str]] | None = None
) -> Validator[dict[str, Any]]:
    """Factory for creating a feature compatibility validator."""
    default_incompatible = [
        ("sync_mode", "async_mode"),
        ("no_compression", "force_compression"),
    ]

    incompatible_pairs = incompatible_pairs or default_incompatible

    @Build.validator(
        register_as=VALID.DOMAINS.CACHE.has_compatible_features,
        description="Validate feature flag compatibility",
        error_template="Incompatible features enabled: {feature1} and {feature2}",
        tags=(SYSTEM.CACHE, SYSTEM.CONFIG),
    )
    async def _validator(
        value: Annotated[dict[str, Any], "Feature configuration"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> bool:
        """Check that incompatible features are not enabled together."""
        for feature1, feature2 in incompatible_pairs:
            if value.get(feature1) and value.get(feature2):
                if info.context is not None:
                    info.context["feature1"] = feature1
                    info.context["feature2"] = feature2
                return False

        return True

    return _validator


def eviction_policy_supported(
    *, supported_policies: Sequence[str] | None = None
) -> Validator[str]:
    """Factory for creating an eviction policy validator."""
    default_policies = ["LRU", "LFU", "FIFO", "MRU", "RANDOM"]
    supported_policies = supported_policies or default_policies

    @Build.validator(
        register_as=VALID.DOMAINS.CACHE.eviction_policy_supported,
        description=f"Validate eviction policy is one of: {', '.join(supported_policies)}",
        error_template="Unsupported eviction policy: {value}. Supported: {supported}",
        tags=(SYSTEM.CACHE, SYSTEM.CONFIG),
    )
    async def _validator(
        value: Annotated[str, "The eviction policy"],
        info: Annotated[ValidationInfo, "Validation context"],
    ) -> bool:
        """Check if eviction policy is supported."""
        if info.context is not None:
            info.context["value"] = value
            info.context["supported"] = ", ".join(supported_policies)
        return value in supported_policies

    return _validator

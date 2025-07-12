"""
Title         : norm_collections.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_collections.py

Description
-----------
Collection (list, set, dict) normalization rules.
"""

from __future__ import annotations

import random
from collections.abc import Hashable, Mapping
from operator import itemgetter
from typing import TYPE_CHECKING, Any, Protocol, TypeVar, overload

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from collections.abc import Iterable

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


# --- Type Variables and Protocols ---------------------------------------------

T = TypeVar("T")
K = TypeVar("K")
V = TypeVar("V")
T_Hashable = TypeVar("T_Hashable", bound=Hashable)
T_Mapping = TypeVar("T_Mapping", bound=Mapping[str, Any])


class SupportsRichComparison(Protocol):
    """Protocol for objects that support rich comparison."""
    def __lt__(self, other: SupportsRichComparison) -> bool:
        """Return True if self is less than other."""
        ...


T_Comparable = TypeVar("T_Comparable", bound=SupportsRichComparison)


# --- Normalizers --------------------------------------------------------------

@overload
def sort_list(
    *,
    key: None = None,
    reverse: bool = False) -> Normalizer[list[T_Comparable], list[T_Comparable]]: ...


@overload
def sort_list(
    *,
    key: str,
    reverse: bool = False) -> Normalizer[list[T_Mapping], list[T_Mapping]]: ...


def sort_list(
    *,
    key: str | None = None,
    reverse: bool = False,
) -> (
    Normalizer[list[T_Comparable], list[T_Comparable]]
    | Normalizer[list[T_Mapping], list[T_Mapping]]
):
    """Factory for a normalizer that sorts a list, optionally by a key and in reverse order."""
    # --- Logic for sorting a list of comparable items ---
    if key is None:
        @Build.normalizer(
            description="Sorts a list of comparable items.",
            tags=(SYSTEM.INFRA.io,),
        )
        async def _normalizer(value: list[T_Comparable], info: ValidationInfo) -> list[T_Comparable]:
            return sorted(value, reverse=reverse)
        return _normalizer

    @Build.normalizer(
        description=f"Sorts a list by key '{key}'.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer_by_key(value: list[T_Mapping], info: ValidationInfo) -> list[T_Mapping]:
        return sorted(value, key=itemgetter(key), reverse=reverse)
    return _normalizer_by_key


@Build.normalizer(
    register_as=NORM.COLLECTIONS.remove_duplicates,
    description="Removes duplicate items from a list while preserving order.",
    tags=(SYSTEM.INFRA.io,),
)
async def remove_duplicates[T](value: list[T], info: ValidationInfo) -> list[T]:
    """Removes duplicate items from a list while preserving order."""
    return list(dict.fromkeys(value))


@Build.normalizer(
    register_as=NORM.COLLECTIONS.flatten,
    description="Flattens a list of lists into a single list.",
    tags=(SYSTEM.INFRA.io,),
)
async def flatten[T](value: list[list[T]], info: ValidationInfo) -> list[T]:
    """Flattens a list of lists into a single list."""
    return [item for sublist in value for item in sublist]


def get_value[K, V](*, key: K, default: V | None = None) -> Normalizer[Mapping[K, V], V | None]:
    """Factory for a normalizer that safely gets a value from a dictionary key."""
    @Build.normalizer(
        register_as=NORM.COLLECTIONS.get_value,
        description="Safely gets a value from a dictionary key, returning a default if not found.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: Mapping[K, V], info: ValidationInfo) -> V | None:
        return value.get(key, default)

    return _normalizer


def filter_by_key[T](
    *, key: str, value_to_match: T | None = None
) -> Normalizer[list[dict[str, T]], list[dict[str, T]]]:
    """Factory for a normalizer that filters a list of dictionaries."""
    @Build.normalizer(
        register_as=NORM.COLLECTIONS.filter_by_key,
        description="Filters a list of dictionaries.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(
        value: list[dict[str, T]], info: ValidationInfo
    ) -> list[dict[str, T]]:
        if value_to_match is not None:
            return [d for d in value if d.get(key) == value_to_match]
        return [d for d in value if key in d]

    return _normalizer


def join_list(*, separator: str = ", ") -> Normalizer[Iterable[str], str]:
    """Factory for a normalizer that joins a list of strings."""
    @Build.normalizer(
        description="Joins a list of strings into a single string with a separator.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: Iterable[str], info: ValidationInfo) -> str:
        return separator.join(value)

    return _normalizer


@Build.normalizer(
    register_as=NORM.CORE.remove_none_values,
    description="Removes None values from a list.",
    tags=(SYSTEM.INFRA.io,),
)
async def remove_none_values[T](
    value: list[T | None],
    info: ValidationInfo,
) -> list[T]:
    """Removes None values from a list."""
    return [item for item in value if item is not None]


@Build.normalizer(
    register_as=NORM.CORE.remove_empty_strings,
    description="Removes empty strings from a list of strings.",
    tags=(SYSTEM.INFRA.io,),
)
async def remove_empty_strings(
    value: list[str | None],
    info: ValidationInfo,
) -> list[str]:
    """Removes empty and None strings from a list of strings."""
    return [item for item in value if item]


@Build.normalizer(
    register_as=NORM.COLLECTIONS.reverse,
    description="Reverses a list.",
    tags=(SYSTEM.INFRA.io,),
)
async def reverse[T](value: list[T], info: ValidationInfo) -> list[T]:
    """Reverses a list."""
    return value[::-1]


@Build.normalizer(
    register_as=NORM.COLLECTIONS.shuffle,
    description="Shuffles a list.",
    tags=(SYSTEM.INFRA.io,),
)
async def shuffle[T](value: list[T], info: ValidationInfo) -> list[T]:
    """Shuffles a list."""
    random.shuffle(value)
    return value


def chunk(*, size: int) -> Normalizer[list[T], list[list[T]]]:
    """Factory for a normalizer that chunks a list into smaller lists."""
    @Build.normalizer(
        description="Chunks a list into smaller lists.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: list[T], info: ValidationInfo) -> list[list[T]]:
        return [value[i : i + size] for i in range(0, len(value), size)]

    return _normalizer


def interleave[T](*, lists: list[list[T]]) -> Normalizer[list[T], list[T]]:
    """Factory for a normalizer that interleaves multiple lists."""
    @Build.normalizer(
        register_as=NORM.COLLECTIONS.interleave,
        description="Interleaves multiple lists.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: list[T], info: ValidationInfo) -> list[T]:
        result: list[T] = []
        max_len = max(len(lst) for lst in lists)
        for i in range(max_len):
            for lst in lists:
                if i < len(lst):
                    result.extend([lst[i]])
        return result

    return _normalizer


def group_by(*, key: str) -> Normalizer[list[dict[str, Any]], dict[str, list[dict[str, Any]]]]:
    """Factory for a normalizer that groups a list of dictionaries by a common key."""
    @Build.normalizer(
        description="Groups a list of dictionaries by a common key.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(
        value: list[dict[str, Any]], info: ValidationInfo
    ) -> dict[str, list[dict[str, Any]]]:
        result: dict[str, list[dict[str, Any]]] = {}
        for item in value:
            if key in item:
                result.setdefault(str(item[key]), []).append(item)
        return result

    return _normalizer


def deep_merge(*, other: dict[str, Any]) -> Normalizer[dict[str, Any], dict[str, Any]]:
    """Factory for a normalizer that deep merges two dictionaries."""
    @Build.normalizer(
        description="Deep merges two dictionaries.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(
        value: dict[str, Any], info: ValidationInfo
    ) -> dict[str, Any]:
        result = value.copy()
        for key, val in other.items():
            if key in result and isinstance(result[key], dict) and isinstance(val, dict):
                result[key] = await _normalizer(result[key], info)
            else:
                result[key] = val
        return result

    return _normalizer


# Additional collection normalizers
@Build.normalizer(
    register_as=NORM.COLLECTIONS.lowercase_keys,
    description="Convert all dictionary keys to lowercase",
    tags=(SYSTEM.INFRA.io,),
)
async def lowercase_keys(
    value: dict[str, Any], info: ValidationInfo
) -> dict[str, Any]:
    """
    Convert all dictionary keys to lowercase.

    Note: This may cause key collisions if keys differ only by case.
    The last value wins in case of collision.
    """
    result: dict[str, Any] = {}

    for key, val in value.items():
        lowercase_key = key.lower()
        if lowercase_key in result and info.context is not None:
            # Log warning about collision
            info.context.setdefault("key_collisions", []).append({
                "original_keys": [key, lowercase_key],
                "lowercase_key": lowercase_key
            })
        result[lowercase_key] = val

    return result

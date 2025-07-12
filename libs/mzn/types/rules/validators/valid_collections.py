"""
Title         : valid_collections.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       :
MIT Path          : libs/mzn/types/rules/validators/valid_collections.py.

Description ----------- Collection (list, set, dict) validation rules.

"""

from __future__ import annotations

import json
import re
from collections.abc import Callable, Collection, Iterable, Mapping, Sized
from typing import TYPE_CHECKING, Any, TypeIs, TypeVar, cast

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from collections.abc import Hashable

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")
K = TypeVar("K")
T_Sized = TypeVar("T_Sized", bound=Sized)
T_Mapping = TypeVar("T_Mapping", bound=Mapping[Any, Any])
T_Collection = TypeVar("T_Collection", bound=Collection[Any])


# --- Validators ---------------------------------------------------------------


def has_size(*, min_size: int = 0, max_size: int | None = None) -> Validator[Sized]:
    """Factory for a validator to check if a collection's size is within a specified range."""
    error_template = f"Collection must have between {min_size} and {max_size or 'infinity'} items."

    @Build.validator(
        error_template=error_template,
        description="Checks if a collection's size is within a specified range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Sized, info: ValidationInfo) -> bool:
        size = len(value)
        if max_size is None:
            return size >= min_size
        return min_size <= size <= max_size

    return _validator


@Build.validator(
    register_as=VALID.COLLECTIONS.is_not_empty,
    error_template="Collection cannot be empty.",
    description="Checks that a collection has at least one item.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_not_empty(value: Sized, info: ValidationInfo) -> bool:
    """Check that a collection has at least one item."""
    return len(value) > 0


@Build.validator(
    register_as=VALID.COLLECTIONS.items_are_unique,
    error_template="All items in the list must be unique.",
    description="Checks if all items in a list are unique.",
    tags=(SYSTEM.INFRA.io,),
)
async def items_are_unique(value: list[Hashable], info: ValidationInfo) -> bool:
    """Check if all items in a list are unique."""
    return len(value) == len(set(value))


def has_required_keys(*, required_keys: list[str]) -> Validator[Mapping[str, Any]]:
    """Factory for a validator to check if a dictionary contains all the required keys."""
    error_template = f"Dictionary must contain the required keys: {required_keys}"

    @Build.validator(
        error_template=error_template,
        description="Checks if a dictionary contains all the required keys.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Mapping[str, Any], info: ValidationInfo) -> bool:
        return all(key in value for key in required_keys)

    return _validator


def contains_item[T](*, item: T) -> Validator[Collection[T]]:
    """Factory for a validator that checks if a specific item exists in a collection."""
    error_template = f"Collection must contain the item: {item}"

    @Build.validator(
        register_as=VALID.COLLECTIONS.contains_item,
        error_template=error_template,
        description="Checks if a specific item exists in a list or set.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Collection[T], info: ValidationInfo) -> bool:
        return item in value

    return _validator


def all_items_are_of_type[T](*, item_type: type[T]) -> Validator[Iterable[T]]:
    """Factory for a validator that checks if all items in a collection are of a specific type."""
    error_template = f"All items in the collection must be of type: {item_type.__name__}"

    @Build.validator(
        register_as=VALID.COLLECTIONS.all_items_are_of_type,
        error_template=error_template,
        description="Checks if all items in a collection are of a specific type.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Iterable[T], info: ValidationInfo) -> bool:
        return all(isinstance(item, item_type) for item in value)

    return _validator


def has_key[K](*, key: K) -> Validator[Mapping[K, Any]]:
    """Factory for a validator that checks for a single key in a dictionary."""
    error_template = f"Dictionary must contain the key: {key}"

    @Build.validator(
        register_as=VALID.COLLECTIONS.has_key,
        error_template=error_template,
        description="A more specific version of has_required_keys for a single key in a dictionary.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Mapping[K, Any], info: ValidationInfo) -> bool:
        return key in value

    return _validator


@Build.validator(
    register_as=VALID.COLLECTIONS.has_no_duplicates,
    error_template="Collection must not contain duplicate items.",
    description="Checks if all items in a list are unique. Alias for items_are_unique.",
    tags=(SYSTEM.INFRA.io,),
)
async def has_no_duplicates(value: list[Hashable], info: ValidationInfo) -> bool:
    """
    Checks if all items in a list are unique.

    Alias for items_are_unique.

    """
    return len(value) == len(set(value))


def is_subset_of[T](*, other: set[T]) -> Validator[set[T]]:
    """Factory for a validator that checks if a set is a subset of another set."""
    @Build.validator(
        register_as=VALID.COLLECTIONS.is_subset_of,
        error_template=f"Collection must be a subset of {other}.",
        description="Checks if a set is a subset of another set.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: set[T], info: ValidationInfo) -> bool:
        return value.issubset(other)

    return _validator


def is_superset_of[T](*, other: set[T]) -> Validator[set[T]]:
    """Factory for a validator that checks if a set is a superset of another set."""
    @Build.validator(
        register_as=VALID.COLLECTIONS.is_superset_of,
        error_template=f"Collection must be a superset of {other}.",
        description="Checks if a set is a superset of another set.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: set[T], info: ValidationInfo) -> bool:
        return value.issuperset(other)

    return _validator


def all_satisfy[T](*, predicate: Callable[[T], bool]) -> Validator[Iterable[T]]:
    """Factory for a validator that checks if all items in a collection satisfy a predicate."""
    error_template = "Not all items satisfy the required condition."

    @Build.validator(
        register_as=VALID.COLLECTIONS.all_satisfy,
        error_template=error_template,
        description="Checks if all items in a collection satisfy a predicate.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Iterable[T], info: ValidationInfo) -> bool:
        return all(predicate(item) for item in value)

    return _validator


def any_satisfy[T](*, predicate: Callable[[T], bool]) -> Validator[Iterable[T]]:
    """Factory for a validator that checks if any item in a collection satisfy a predicate."""
    error_template = "No item satisfies the required condition."

    @Build.validator(
        register_as=VALID.COLLECTIONS.any_satisfy,
        error_template=error_template,
        description="Checks if any item in a collection satisfies a predicate.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Iterable[T], info: ValidationInfo) -> bool:
        return any(predicate(item) for item in value)

    return _validator


def has_length_between(
    *, min_len: int, max_len: int
) -> Validator[Sized]:
    """Factory for a validator that checks if a collection's length is within a specified range."""
    error_template = f"Collection length must be between {min_len} and {max_len}."

    @Build.validator(
        error_template=error_template,
        description="Checks if a collection's length is within a specified range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Sized, info: ValidationInfo) -> bool:
        return min_len <= len(value) <= max_len

    return _validator


@Build.validator(
    register_as=VALID.COLLECTIONS.is_nested_dict,
    error_template="Value must be a nested dictionary.",
    description="Checks if a value is a dictionary containing other dictionaries.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_nested_dict(value: Mapping[Any, Any], info: ValidationInfo) -> bool:
    """Checks if a value is a dictionary containing other dictionaries."""
    # The type annotation already guarantees that 'value' is a Mapping, so the extra
    # runtime check is unnecessary and causes static-analysis warnings.
    return any(isinstance(v, Mapping) for v in value.values())


def has_depth(*, depth: int) -> Validator[Any]:
    """Factory for a validator that checks the nesting depth of a collection."""
    error_template = f"Collection must have a nesting depth of {depth}."

    def _get_depth(item: object, current_depth: int) -> int:
        if not isinstance(item, (Collection, Mapping)):
            return current_depth
        if not item:
            return current_depth + 1

        iterable_item: Iterable[Any]
        if isinstance(item, Mapping):
            # Cast ensures static type checkers recognise the iterable's element type.
            iterable_item = cast("Iterable[Any]", item.values())
        else:
            # 'item' is already known to be a Collection here, so a direct cast is sufficient.
            iterable_item = cast("Iterable[Any]", item)

        return max(_get_depth(sub_item, current_depth + 1) for sub_item in iterable_item)

    @Build.validator(
        error_template=error_template,
        description="Checks the nesting depth of a collection.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: object, info: ValidationInfo) -> bool:
        return _get_depth(value, 0) == depth

    return _validator


@Build.validator(
    register_as=VALID.COLLECTIONS.is_homogeneous,
    error_template="All items in the collection must be of the same type.",
    description="Checks if all items in a collection are of the same type.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_homogeneous(value: Iterable[Any], info: ValidationInfo) -> bool:
    """Checks if all items in a collection are of the same type."""
    iterator = iter(value)
    try:
        first_item: Any = next(iterator)
    except StopIteration:
        return True  # Empty collection is considered homogeneous

    # Explicitly annotate to avoid partially-unknown type warnings from static analyzers.
    # Cast to `object` so the resulting `type` is fully known and not reported as partially unknown.
    first_type: type[object] = type(cast("object", first_item))
    return all(isinstance(item, first_type) for item in iterator)


@Build.validator(
    register_as=VALID.COLLECTIONS.has_no_cycles,
    error_template="Collection must not contain cyclical references.",
    description="Checks for cyclical references in collections.",
    tags=(SYSTEM.INFRA.io,),
)
async def has_no_cycles(value: object, info: ValidationInfo) -> bool:
    """Checks for cyclical references in collections."""
    seen: set[int] = set()

    def _detect_cycles(obj: object) -> bool:
        obj_id = id(obj)
        if obj_id in seen:
            return False  # Cycle detected

        if isinstance(obj, (Mapping, Collection)) and not isinstance(obj, str):
            seen.add(obj_id)

            # Explicitly cast so static analyzers know the element type inside the iterable.
            iterable: Iterable[object]
            if isinstance(obj, Mapping):
                iterable = cast("Iterable[object]", obj.values())
            else:
                # At this point, `obj` is guaranteed to be a non-string `Collection`
                iterable = cast("Iterable[object]", obj)

            for item in iterable:
                if not _detect_cycles(item):
                    return False

            seen.remove(obj_id)

        return True

    return _detect_cycles(value)


def is_disjoint_from[T](*, other: set[T]) -> Validator[set[T]]:
    """Factory for a validator that checks if two sets have no common elements."""
    @Build.validator(
        register_as=VALID.COLLECTIONS.is_disjoint_from,
        error_template=f"Collection must be disjoint from {other}.",
        description="Checks if two sets have no common elements.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: set[T], info: ValidationInfo) -> bool:
        return value.isdisjoint(other)

    return _validator


# Additional collection validators
def keys_match_pattern(*, pattern: str) -> Validator[Mapping[str, Any]]:
    """Factory for creating a dictionary key pattern validator."""

    @Build.validator(
        error_template=f"Dictionary keys must match pattern: {pattern}",
        description=f"Check all keys match pattern: {pattern}",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: Mapping[str, Any], info: ValidationInfo) -> bool:
        """Check if all dictionary keys match the specified pattern."""
        compiled_pattern = re.compile(pattern)

        for key in value:
            if not compiled_pattern.match(key):
                if info.context is not None:
                    info.context["invalid_key"] = key
                return False

        return True

    return _validator


def _is_mapping(value: Collection[Any] | Mapping[Any, Any]) -> TypeIs[Mapping[Any, Any]]:
    """Type guard to check if a value is a Mapping."""
    return isinstance(value, Mapping)


@Build.validator(
    register_as=VALID.COLLECTIONS.values_are_serializable,
    error_template="All values must be JSON serializable",
    description="Check if all values in collection are serializable",
    tags=(SYSTEM.INFRA.io,),
)
async def values_are_serializable(value: Collection[Any] | Mapping[Any, Any], info: ValidationInfo) -> bool:
    """
    Check if all values in a collection are JSON serializable.

    Works with lists, sets, tuples, and dictionaries.

    """

    def is_serializable(obj: object) -> bool:
        try:
            _ = json.dumps(obj)
        except (TypeError, ValueError):
            return False
        else:
            return True

    if _is_mapping(value):
        # TypeIs narrowed the type, no cast needed
        for k, v in value.items():
            if not is_serializable(v):
                if info.context is not None:
                    info.context["non_serializable_key"] = k
                return False
    else:
        for idx, item in enumerate(value):
            if not is_serializable(item):
                if info.context is not None:
                    info.context["non_serializable_index"] = idx
                return False

    return True

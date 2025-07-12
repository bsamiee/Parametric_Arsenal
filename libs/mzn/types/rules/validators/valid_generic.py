"""
Title         : valid_generic.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/rules/validators/valid_generic.py.

Description ----------- Generic validation rules that can apply to any type.

"""

from __future__ import annotations

import json
from typing import TYPE_CHECKING, TypeVar

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from collections.abc import Callable

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Validators ---------------------------------------------------------------


def is_equal_to[T](*, target_value: T) -> Validator[T]:
    """Factory for a validator that checks for equality with a specific value."""
    @Build.validator(
        register_as=VALID.CORE.is_equal_to,
        error_template=f"Value must be equal to {target_value}.",
        description="Check for equality with a specific value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: T, info: ValidationInfo) -> bool:
        return value == target_value

    return _validator


def is_not_one_of[T](*, forbidden_values: list[T]) -> Validator[T]:
    """Factory for a validator that checks that the value is not in a list of forbidden values."""
    @Build.validator(
        register_as=VALID.CORE.is_not_one_of,
        error_template="Value cannot be one of the forbidden values.",
        description="Check that the value is not in a list of forbidden values.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: T, info: ValidationInfo) -> bool:
        return value not in forbidden_values

    return _validator


def is_one_of[T](*, allowed_values: list[T]) -> Validator[T]:
    """Factory for a validator that checks that the value is in a list of allowed values."""
    @Build.validator(
        register_as=VALID.CORE.is_one_of,
        error_template=f"Value must be one of the allowed values: {allowed_values}",
        description="Check that the value is in a list of allowed values.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: T, info: ValidationInfo) -> bool:
        return value in allowed_values

    return _validator


def is_not_equal_to[T](*, target_value: T) -> Validator[T]:
    """Factory for a validator that checks for inequality with a specific value."""
    @Build.validator(
        register_as=VALID.CORE.is_not_equal_to,
        error_template=f"Value must not be equal to {target_value}.",
        description="Check for inequality with a specific value.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: T, info: ValidationInfo) -> bool:
        return value != target_value

    return _validator


def is_instance_of(*, target_type: type) -> Validator[object]:
    """Factory for a validator that checks for instance type."""
    @Build.validator(
        error_template=f"Value must be an instance of {target_type.__name__}.",
        description="Check for instance type.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: object, info: ValidationInfo) -> bool:
        return isinstance(value, target_type)

    return _validator


@Build.validator(
    register_as=VALID.CORE.is_callable,
    error_template="Value must be callable.",
    description="Check if value is callable.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_callable(value: object, info: ValidationInfo) -> bool:
    """Check if value is callable."""
    return callable(value)


@Build.validator(
    register_as=VALID.CORE.is_hashable,
    error_template="Value must be hashable.",
    description="Check if value is hashable.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_hashable(value: object, info: ValidationInfo) -> bool:
    """Check if value is hashable."""
    try:
        _ = hash(value)
    except TypeError:
        return False
    else:
        return True


def is_one_of_types(*, types: list[type]) -> Validator[object]:
    """Factory for a validator that checks if a value's type is in a given list."""
    type_names = ", ".join(t.__name__ for t in types)
    error_template = f"Value must be one of the following types: {type_names}."

    @Build.validator(
        error_template=error_template,
        description="Checks if a value's type is in a given list.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: object, info: ValidationInfo) -> bool:
        return isinstance(value, tuple(types))

    return _validator


def has_attribute(*, name: str) -> Validator[object]:
    """Factory for a validator that checks if an object has a specific attribute."""
    error_template = f"Object must have the attribute: '{name}'."

    @Build.validator(
        error_template=error_template,
        description="Checks if an object has a specific attribute.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: object, info: ValidationInfo) -> bool:
        return hasattr(value, name)

    return _validator


def satisfies_predicate[T](*, predicate: Callable[[T], bool]) -> Validator[T]:
    """Factory for a validator that checks if a value satisfies a given predicate function."""
    error_template = "Value does not satisfy the required condition."

    @Build.validator(
        register_as=VALID.CORE.satisfies_predicate,
        error_template=error_template,
        description="Checks if a value satisfies a given predicate function.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: T, info: ValidationInfo) -> bool:
        return predicate(value)

    return _validator


@Build.validator(
    register_as=VALID.CORE.is_json_serializable,
    error_template="Value must be JSON serializable.",
    description="Checks if a value can be serialized to JSON.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_json_serializable(value: object, info: ValidationInfo) -> bool:
    """Checks if a value can be serialized to JSON."""
    try:
        _ = json.dumps(value)
    except (TypeError, OverflowError):
        return False
    else:
        return True

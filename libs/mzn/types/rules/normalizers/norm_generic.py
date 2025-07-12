"""
Title         : norm_generic.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_generic.py

Description
-----------
Generic normalization rules that can apply to any type.
"""

from __future__ import annotations

from collections.abc import Callable, Iterable
from decimal import Decimal
from typing import TYPE_CHECKING, Any, Protocol, TypeVar, cast

from mzn.types._core.core_builders import Build
from mzn.types._core.core_processors import function_normalizer_factory
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer

# --- Type Variables -----------------------------------------------------------

T_co = TypeVar("T_co", covariant=True)
T = TypeVar("T")
R = TypeVar("R")

# --- Normalizers --------------------------------------------------------------


class TypeCallable(Protocol[T_co]):
    """Protocol for a callable that takes any value and returns a value of type T_co."""

    __name__: str

    def __call__(self, value: object) -> T_co:
        """Call the object with a value and return a value of type T_co."""
        ...


def ensure_type[T](*, target_type: TypeCallable[T]) -> Normalizer[Any, T]:
    """Factory for a normalizer that ensures a value is of the target type by conversion."""
    type_name = target_type.__name__

    async def _normalizer(value: object, info: ValidationInfo) -> T:
        # We can't use isinstance with a Protocol that has __call__
        # so we rely on the conversion logic.
        try:
            # Attempt to convert the value by calling the target type's constructor
            return target_type(value)
        except (TypeError, ValueError) as e:
            msg = f"Cannot convert {type(value).__name__} to {type_name}: {e}"
            raise ValueError(msg) from e

    return function_normalizer_factory(
        _normalizer,
        error_template=f"Cannot convert to {type_name}",
    )


@Build.normalizer(
    register_as=NORM.CORE.to_bool,
    description="Converts various values to boolean using truthiness",
    tags=(SYSTEM.INFRA.io,),
)
async def to_bool(value: object, info: ValidationInfo) -> bool:
    """Convert value to boolean using Python truthiness rules."""
    if isinstance(value, bool):
        return value

    # Handle string representations of boolean values
    if isinstance(value, str):
        value_lower = value.lower().strip()
        if value_lower in {"true", "yes", "1", "on", "enabled"}:
            return True
        if value_lower in {"false", "no", "0", "off", "disabled", ""}:
            return False

    # Use Python's truthiness for everything else
    return bool(value)


@Build.normalizer(
    register_as=NORM.CORE.to_decimal,
    description="Converts numeric values to Decimal for precise arithmetic",
    tags=(SYSTEM.INFRA.io,),
)
async def to_decimal(value: object, info: ValidationInfo) -> Decimal:
    """Convert value to Decimal for precise arithmetic."""
    if isinstance(value, Decimal):
        return value

    try:
        return Decimal(str(value))
    except (TypeError, ValueError) as e:
        msg = f"Cannot convert {type(value).__name__} to Decimal: {e}"
        raise ValueError(msg) from e


@Build.normalizer(
    register_as=NORM.CORE.ensure_list,
    description="Ensures value is a list, wrapping single values if needed",
    tags=(SYSTEM.INFRA.io,),
)
async def ensure_list[T](
    value: T | Iterable[T] | None,
    info: ValidationInfo,
) -> list[T]:
    """Ensure value is a list, wrapping single values if needed."""
    if isinstance(value, list):
        # Help the type checker understand the element type
        return cast("list[T]", value)

    if isinstance(value, str):
        return [value]  # type: ignore[list-item]

    if value is None:
        # Return an empty list of the expected generic type
        return cast("list[T]", [])

    # Only treat as iterable if not a string or bytes
    if isinstance(value, Iterable) and not isinstance(value, (str, bytes)):
        # Explicitly cast to help pyright understand the type
        return list(cast("Iterable[T]", value))

    # Wrap single non-iterable value in a list
    return cast("list[T]", [value])


def apply_if_not_none[T, R](*, normalizer: Normalizer[T, R]) -> Normalizer[T | None, R | None]:
    """Factory for a normalizer that applies another normalizer only if the value is not None."""
    @Build.normalizer(
        register_as=NORM.CORE.apply_if_not_none,
        description="Applies a normalizer only if the value is not None.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: T | None, info: ValidationInfo) -> R | None:
        if value is None:
            return None
        return await normalizer(value, info)

    return _normalizer


def fallback_to[T](*, default: T) -> Normalizer[T | None, T]:
    """Factory for a normalizer that returns a default value if the input is None."""
    @Build.normalizer(
        description="Returns a default value if the input is None.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: T | None, info: ValidationInfo) -> T:
        return value if value is not None else default

    return _normalizer


def apply_if[T, R](
    *,
    predicate: Callable[[T], bool],
    normalizer: Normalizer[T, R],
) -> Normalizer[T, R | T]:
    """Factory for a normalizer that applies another normalizer only if a predicate is met."""
    @Build.normalizer(
        register_as=NORM.CORE.apply_if,
        description="Applies a normalizer only if a predicate is met.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: T, info: ValidationInfo) -> R | T:
        if predicate(value):
            return await normalizer(value, info)
        return value

    return _normalizer


def chain(*, normalizers: list[Normalizer[Any, Any]]) -> Normalizer[Any, Any]:
    """Factory for a normalizer that chains multiple normalizers together."""
    @Build.normalizer(
        description="Chains multiple normalizers together.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: object, info: ValidationInfo) -> object:
        result: object = value
        for normalizer in normalizers:
            # The runtime ensures type-compatibility between `result` and `normalizer`
            result = await normalizer(result, info)
        return result

    return _normalizer


def select_by_type(*, type_map: dict[type, Normalizer[Any, Any]]) -> Normalizer[Any, Any]:
    """Factory for a normalizer that selects a normalizer based on the input type."""
    @Build.normalizer(
        description="Selects a normalizer based on the input type.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: object, info: ValidationInfo) -> object:
        for t, normalizer in type_map.items():
            if isinstance(value, t):
                # Safe by the preceding isinstance check
                return await normalizer(value, info)
        return value

    return _normalizer

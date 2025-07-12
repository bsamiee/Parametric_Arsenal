"""
Title         : core_processors.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_core/core_processors.py.

Description ----------- Central utilities for processing validator and normalizer rules. This is part of the "Rules"
feature and is self-contained and fully async.

"""

from __future__ import annotations

import inspect
from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    TypeGuard,
    TypeVar,
    cast,
    override,
)

from mzn.types._contracts.prot_base import (
    Normalizer,
    Rule,
    Validator,
    is_normalizer,
    is_validator,
    validation_context,
)
from mzn.types._core.core_constants import Sentinel


if TYPE_CHECKING:
    from collections.abc import Awaitable, Callable

    from pydantic import ValidationInfo


# --- Type Variables & Aliases -------------------------------------------------

T_Ret = TypeVar("T_Ret", bound=object)
T_in_contra = TypeVar("T_in_contra", contravariant=True)
T_out_co = TypeVar("T_out_co", covariant=True)
T_invariant = TypeVar("T_invariant")

# --- TypeGuards ---------------------------------------------------------------


def is_rule(obj: Annotated[object, "Object to check"]) -> Annotated[TypeGuard[Rule], "True if Rule"]:
    """Check if the object is a Rule (validator or normalizer)."""
    if obj is None:
        return False
    # Check if it's a validator or normalizer first (includes composite rules)
    # But only if it's async or has proper rule structure
    if (is_validator(obj) or is_normalizer(obj)) and (
        hasattr(obj, "is_async") or inspect.iscoroutinefunction(obj) or callable(obj)
    ):
        # If it's a simple lambda without async, reject it
        return not (inspect.isfunction(obj) and not inspect.iscoroutinefunction(obj))
    # Only accept async callables, not sync lambdas
    return callable(obj) and inspect.iscoroutinefunction(obj)


def is_async_rule(obj: Annotated[object, "Rule to check"]) -> Annotated[bool, "True if async rule"]:
    """Check if the rule is asynchronous (has is_async True or is a coroutine function)."""
    if obj is None:
        return False
    if not is_rule(obj):
        return False
    # Check for is_async attribute first
    if hasattr(obj, "is_async"):
        return bool(getattr(obj, "is_async", False))
    # Check if the object itself is a coroutine function
    if inspect.iscoroutinefunction(obj):
        return True
    # Check if the object has an async __call__ method (for composite rules)
    # This is important for And, Or, Not which have async __call__ methods
    if callable(obj):
        # Check if __call__ method is async
        return inspect.iscoroutinefunction(obj.__call__)
    return False


def accepts_validation_info(func: Annotated[object, "Callable to check"]) -> Annotated[bool, "True if accepts info"]:
    """Check if the callable accepts a 'info' parameter."""
    if not callable(func):
        return False
    try:
        sig = inspect.signature(func)
        params = list(sig.parameters.keys())
        return len(params) >= 2 and params[1] == "info"
    except (ValueError, TypeError):
        return False


def is_structural_normalizer(
    obj: Annotated[object, "Object to check"]
) -> Annotated[TypeGuard[Normalizer[Any, Any]], "True if structural normalizer"]:
    """Check if the object is a structural normalizer (callable with specific attributes)."""
    return isinstance(obj, FunctionNormalizer)


def is_structural_validator(
    obj: Annotated[object, "Object to check"]
) -> Annotated[TypeGuard[Validator[Any]], "True if structural validator"]:
    """Check if the object is a structural validator (callable with specific attributes)."""
    return isinstance(obj, FunctionValidator)


# --- Main Rule Processing Function --------------------------------------------

async def process_rules(  # noqa: PLR0912
    rules: Annotated[
        tuple[
            Annotated[Rule, "Rule"] | Annotated[Sentinel, "Sentinel"],
            ...
        ],
        "Tuple of rules or sentinels to process"
    ],
) -> tuple[
    list[Normalizer[Any, Any]], list[Validator[Any]]
]:
    """Asynchronously process and categorize a tuple of rules."""
    normalizers: list[Normalizer[Any, Any]] = []
    validators: list[Validator[Any]] = []
    skip_sentinels = {Sentinel.SKIP, Sentinel.DISABLED, Sentinel.DEFER, Sentinel.NOOP}
    # Log map removed to fix import-time resource warnings

    def _handle_validator(rule: Validator[Any]) -> None:
        if not is_async_rule(rule):
            return
        validators.append(rule)

    def _handle_normalizer(rule: Normalizer[Any, Any]) -> None:
        if not is_async_rule(rule):
            return
        normalizers.append(rule)

    for rule in rules:
        # Debug logging removed to fix import-time resource warnings
        if rule in skip_sentinels:
            # Sentinel logging removed
            continue
        # Check concrete types first to avoid ambiguity
        if isinstance(rule, FunctionNormalizer):
            _handle_normalizer(cast("Normalizer[Any, Any]", rule))
        elif isinstance(rule, FunctionValidator):
            _handle_validator(cast("Validator[Any]", rule))
        elif is_validator(rule):
            # TypeGuard ensures rule is Validator[Any] here (Python 3.13+)
            _handle_validator(rule)
        elif is_normalizer(rule):
            # TypeGuard ensures rule is Normalizer[Any, Any] here (Python 3.13+)
            _handle_normalizer(rule)
        elif inspect.isfunction(rule):
            if inspect.iscoroutinefunction(rule):
                sig = inspect.signature(rule)
                ret = sig.return_annotation
                if ret is bool or ret is inspect.Signature.empty:
                    validators.append(function_validator_factory(rule))
                else:
                    normalizers.append(function_normalizer_factory(rule))
            else:
                # Warning logging removed to fix import-time resource warnings
                pass
        elif isinstance(rule, Sentinel):
            # Warning logging removed to fix import-time resource warnings
            pass
        else:
            # Warning logging removed to fix import-time resource warnings
            pass
    return normalizers, validators


# --- Function-to-Class Factories ----------------------------------------------

def function_validator_factory[T_invariant](
    func: Callable[[T_invariant, ValidationInfo], Awaitable[bool]],
    *,
    error_template: str | Sentinel | None = None,
    **kwargs: Any,
) -> Validator[T_invariant]:
    """Create a FunctionValidator instance from a callable."""
    error_msg = error_template or Sentinel.DEFAULT
    return FunctionValidator(func, error_msg, **kwargs)


def function_normalizer_factory[T_in_contra, T_out_co](
    func: Callable[[T_in_contra, ValidationInfo], Awaitable[T_out_co]],
    *,
    error_template: str | Sentinel | None = None,
    **kwargs: Any,
) -> Normalizer[T_in_contra, T_out_co]:
    """Create a FunctionNormalizer instance from a callable."""
    error_msg = error_template or Sentinel.DEFAULT
    return FunctionNormalizer(func, error_msg, **kwargs)


# --- Function Validator Class -------------------------------------------------


class FunctionValidator(Validator[T_invariant]):
    """Async validator class generated from a function."""
    __slots__ = ("accepts_context", "error_message", "func", "is_async")

    func: Callable[[T_invariant, ValidationInfo], Awaitable[bool]]
    is_async: bool
    accepts_context: bool
    error_message: str | Sentinel

    def __init__(  # pyright: ignore[reportMissingSuperCall]
        self,
        func: Callable[[T_invariant, ValidationInfo], Awaitable[bool]],
        error_message: str | Sentinel,
        **kwargs: Any,
    ) -> None:
        """Initialize the FunctionValidator with a function and error message."""
        self.func = func
        self.is_async = True
        self.accepts_context = accepts_validation_info(func)
        self.error_message = error_message
        for k, v in kwargs.items():
            setattr(self, k, v)

    @override
    async def __call__(
        self,
        value: T_invariant,
        info: ValidationInfo,
    ) -> T_invariant:
        """Call the validator on the given value and info."""
        token = validation_context.set(info)
        try:
            result = await self.func(value, info)
            if not result:
                err = str(self.error_message).format(value=value)
                raise ValueError(err)
            return value
        finally:
            validation_context.reset(token)

    @classmethod
    async def add_rule(cls, rule: Rule) -> None:
        """Add a validation or normalization rule to the class (dummy for interface compatibility)."""
        # No-op for interface compatibility

# --- Function Normalizer Class ------------------------------------------------


class FunctionNormalizer(Normalizer[T_in_contra, T_out_co]):
    """Async normalizer class generated from a function."""
    __slots__ = ("accepts_context", "error_message", "func", "is_async")

    func: Callable[[T_in_contra, ValidationInfo], Awaitable[T_out_co]]
    is_async: bool
    accepts_context: bool
    error_message: str | Sentinel

    def __init__(  # pyright: ignore[reportMissingSuperCall]
        self,
        func: Callable[[T_in_contra, ValidationInfo], Awaitable[T_out_co]],
        error_message: str | Sentinel,
        **kwargs: Any,
    ) -> None:
        """Initialize the FunctionNormalizer with a function and error message."""
        self.func = func
        self.is_async = True
        self.accepts_context = accepts_validation_info(func)
        self.error_message = error_message
        for k, v in kwargs.items():
            setattr(self, k, v)

    @override
    async def __call__(
        self,
        value: T_in_contra,
        info: ValidationInfo,
    ) -> T_out_co:
        """Call the normalizer on the given value and info."""
        token = validation_context.set(info)
        try:
            return await self.func(value, info)
        finally:
            validation_context.reset(token)

    @classmethod
    async def add_rule(cls, rule: Rule) -> None:
        """Add a validation or normalization rule to the class (dummy for interface compatibility)."""
        # No-op for interface compatibility


# --- Public re-exports --------------------------------------------------------


__all__ = [
    "FunctionNormalizer",
    "FunctionValidator",
    "accepts_validation_info",
    "is_async_rule",
    "is_rule",
    "is_structural_normalizer",
    "is_structural_validator",
    "process_rules",
]

"""
Title         : prot_base.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/_contracts/prot_base.py.

Description ----------- Base protocols and type aliases for the contract system. This module should have no local
dependencies on other protocol files.

"""

from __future__ import annotations

from abc import abstractmethod
from collections.abc import Awaitable, Callable
from contextvars import ContextVar
from inspect import iscoroutinefunction, signature
from typing import TYPE_CHECKING, Annotated, Any, ParamSpec, Protocol, TypeGuard, TypeVar, runtime_checkable

from beartype import beartype

from mzn.types._core.core_constants import Sentinel


if TYPE_CHECKING:
    from pydantic import ValidationInfo


# --- Type Variables -----------------------------------------------------------

T_in_contra = TypeVar("T_in_contra", contravariant=True)
T_out_co = TypeVar("T_out_co", covariant=True)
T_invariant = TypeVar("T_invariant")
P = ParamSpec("P")

# Type variables for Normalizer and Validator factories
T_Validator = TypeVar("T_Validator", bound="Validator[Any]")
T_Normalizer = TypeVar("T_Normalizer", bound="Normalizer[Any, Any]")


# --- Context Variables --------------------------------------------------------

validation_context: ContextVar[ValidationInfo | None] = ContextVar(
    "validation_context",
    default=None,
)


# --- Core Rule Protocols ------------------------------------------------------

@runtime_checkable
class Normalizer(Protocol[T_in_contra, T_out_co]):
    """Protocol for a normalizer rule that transforms an input value (async)."""

    @abstractmethod
    async def __call__(
        self,
        value: Annotated[T_in_contra, "The input value to be transformed."],
        info: Annotated[
            ValidationInfo, "Pydantic's validation context information."
        ],
    ) -> Annotated[T_out_co, "The transformed output value."]:
        """Transform the input value asynchronously, using validation context info."""
        ...


@runtime_checkable
class Validator(Protocol[T_invariant]):
    """Protocol for a validator rule that checks a value but does not change it (async)."""

    @abstractmethod
    async def __call__(
        self,
        value: Annotated[T_invariant, "The input value to be validated."],
        info: Annotated[
            ValidationInfo, "Pydantic's validation context information."
        ],
    ) -> Annotated[T_invariant, "The validated value (unchanged on success)."]:
        """Validate the input value asynchronously, using validation context info."""
        ...


# --- Type Aliases ------------------------------------------------------------

type RuleProcessingResult = Annotated[
    tuple[
        list[Annotated[Normalizer[Any, Any], "A normalizer rule."]],
        list[Annotated[Validator[Any], "A validator rule."]],
    ],
    "A tuple of (normalizers, validators).",
]

Rule = (
    Annotated[Normalizer[Any, Any], "A normalizer rule."]
    | Annotated[Validator[Any], "A validator rule."]
    | Annotated[Callable[..., Awaitable[Any]], "A generic async callable rule."]
    | Sentinel  # Allow sentinels as rules
)

# A factory is a callable that returns a rule instance
RuleFactory = Annotated[
    Callable[P, T_Validator], "A factory for Validator rules."
] | Annotated[
    Callable[P, T_Normalizer], "A factory for Normalizer rules."]

# --- TypeGuard Functions -----------------------------------------------------


@beartype
def is_normalizer(obj: object) -> TypeGuard[Normalizer[Any, Any]]:
    """TypeGuard to check if an object conforms to the Normalizer protocol (structural check)."""
    # Accepts both Protocol inheritance and structural typing
    if isinstance(obj, Normalizer):
        return True
    # Structural: must be callable, async, and accept (value, info)
    if callable(obj) and iscoroutinefunction(obj):
        try:
            sig = signature(obj)
            params = list(sig.parameters.keys())
            if len(params) == 2:
                # Handle standalone functions: (value, info)
                return params[0] == "value" and params[1] == "info"
            if len(params) >= 3:
                # Handle methods: (self, value, info)
                return params[1] == "value" and params[2] == "info"
        except (ValueError, TypeError):
            return False
    return False


@beartype
def is_validator(obj: object) -> TypeGuard[Validator[Any]]:
    """TypeGuard to check if an object conforms to the Validator protocol (structural check)."""
    if isinstance(obj, Validator):
        return True
    if callable(obj) and iscoroutinefunction(obj):
        try:
            sig = signature(obj)
            params = list(sig.parameters.keys())
            if len(params) == 2:
                # Handle standalone functions: (value, info)
                return params[0] == "value" and params[1] == "info"
            if len(params) >= 3:
                # Handle methods: (self, value, info)
                return params[1] == "value" and params[2] == "info"
        except (ValueError, TypeError):
            return False
    return False

from collections.abc import Awaitable, Callable
from typing import Any, TypeVar

from pydantic import BaseModel, ValidationInfo

from mzn.types._contracts.prot_base import Normalizer, Rule as BaseRule, Validator
from mzn.types._core.core_operations import MethodConfig
from mzn.types._core.core_tags import Tag

_E = TypeVar("_E", bound=type)
_T_Model = TypeVar("_T_Model", bound=BaseModel)
_T_invariant = TypeVar("_T_invariant")
_T_in_contra = TypeVar("_T_in_contra", contravariant=True)
_T_out_co = TypeVar("_T_out_co", covariant=True)

class Build:
    @staticmethod
    def primitive(
        inner_type: type[object],
        *,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        enable_caching: bool = ...,
        **kwargs: Any,
    ) -> Callable[[type[object]], type[object]]: ...

    @staticmethod
    def alias(
        *,
        base: type[object],
        rules: list[BaseRule] | None = ...,
        operations: MethodConfig | None,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        enable_caching: bool = ...,
        **kwargs: Any,
    ) -> Callable[[type[object]], type[object]]: ...

    @staticmethod
    def model(
        *,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        rules: list[BaseRule] | None = ...,
        model_config: dict[str, Any] | None = ...,
        enable_caching: bool = ...,
        **kwargs: Any,
    ) -> Callable[[type[_T_Model]], type[_T_Model]]: ...

    @staticmethod
    def enum(
        *,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        base_type: type = ...,
        enable_caching: bool = ...,
        auto_placeholder: object = ...,
        **kwargs: Any,
    ) -> Callable[[_E], _E]: ...

    @staticmethod
    def validator(
        *,
        error_template: str | None = ...,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        register_as: object | None = None,
        **kwargs: Any,
    ) -> Callable[[Callable[[_T_invariant, ValidationInfo], Awaitable[bool]]], Validator[_T_invariant]]: ...

    @staticmethod
    def normalizer(
        *,
        description: str | None = ...,
        tags: tuple[Tag | object, ...] | None = ...,
        register_as: object | None = None,
        **kwargs: Any,
    ) -> Callable[
        [Callable[[_T_in_contra, ValidationInfo], Awaitable[_T_out_co]]],
        Normalizer[_T_in_contra, _T_out_co],
    ]: ...

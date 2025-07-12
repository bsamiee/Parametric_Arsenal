"""
Title         : core_builders.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_core/core_builders.py

Description
-----------
High-level, declarative decorators for creating type assets.

This module provides a modern, clean API for creating domain assets with direct parameters
instead of nested metadata objects, allowing for clearer and more consistent configuration.
All asset types now default to advanced capabilities with performance features disabled by default.
Core features like documentation, comparison, auditing, and lifecycle are always enabled.
"""

from __future__ import annotations

import asyncio
import atexit
import contextlib
from typing import TYPE_CHECKING, Annotated, Any, TypeVar, cast

from aenum import Enum as PyEnum
from pydantic import BaseModel

from mzn.types._contracts.prot_assets import AdvancedAsset
from mzn.types._core.core_factory import (
    acreate_alias,
    acreate_enum,
    acreate_model,
    acreate_primitive,
)
from mzn.types._core.core_processors import (
    function_normalizer_factory,
    function_validator_factory,
)


if TYPE_CHECKING:
    from collections.abc import Awaitable, Callable, Coroutine

    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import (
        Normalizer,
        Rule as BaseRule,
        Validator,
    )
    from mzn.types._core.core_operations import MethodConfig
    from mzn.types._core.core_tags import Tag


# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")


# --- Event Loop Management for Import-Time Async Operations -------------------

# Global event loop for import-time async operations
_import_loop: asyncio.AbstractEventLoop | None = None


def _get_import_loop() -> asyncio.AbstractEventLoop:
    """Get or create a dedicated event loop for import-time operations."""
    global _import_loop  # noqa: PLW0603

    if _import_loop is None or _import_loop.is_closed():
        _import_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(_import_loop)

        # Register cleanup at exit
        def cleanup() -> None:
            if _import_loop and not _import_loop.is_closed():
                # Cancel all pending tasks
                pending = asyncio.all_tasks(_import_loop)
                for task in pending:
                    _ = task.cancel()
                # Give the loop one more spin to cancel tasks
                _ = _import_loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
                _import_loop.close()

        _ = atexit.register(cleanup)

    return _import_loop


def _run_import_async[T](coro: Coroutine[Any, Any, T]) -> T:
    """Run async code at import time using a managed event loop."""
    loop = _get_import_loop()

    # Save the current event loop
    old_loop = None
    with contextlib.suppress(RuntimeError):
        old_loop = asyncio.get_event_loop()

    try:
        # Set our loop as current
        asyncio.set_event_loop(loop)
        # Run the coroutine
        return loop.run_until_complete(coro)
    finally:
        # Restore the old loop
        if old_loop:
            asyncio.set_event_loop(old_loop)
        else:
            asyncio.set_event_loop(None)


E = TypeVar("E", bound=PyEnum)
T_Model = TypeVar("T_Model", bound=BaseModel)
T_invariant = TypeVar("T_invariant")
T_in_contra = TypeVar("T_in_contra", contravariant=True)
T_out_co = TypeVar("T_out_co", covariant=True)
M = TypeVar("M")

# --- Builder Namespace --------------------------------------------------------


class Build:
    """A namespace for all asset and rule builders."""

    # --- Asset Decorators -----------------------------------------------------

    @staticmethod
    def primitive(
        inner_type: type[object],
        *,
        description: str | None = None,
        tags: Annotated[tuple[Tag | object, ...] | None, "Hierarchical tags for categorization."] = None,
        enable_caching: bool = False,
        **kwargs: Any,
    ) -> Callable[[type[object]], type[object]]:
        """
        Create a pure wrapper around a Python built-in or collection type.

        Args:
            inner_type: The base Python type this asset wraps
            description: Optional description of the primitive
            tags: Optional tags for categorizing or annotating the asset
            enable_caching: Whether to enable caching for this asset (default: False)
            **kwargs: Additional metadata to attach

        Returns:
            A decorator that creates a primitive type asset
        """
        def decorator(cls: type[object]) -> type[object]:
            # Pass the inner_type through kwargs so it can be used in abuild_asset_model
            kwargs["inner_type"] = inner_type
            return _run_import_async(
                acreate_primitive(
                    cls,
                    description=description,
                    tags=tags,
                    enable_caching=enable_caching,
                    **kwargs,
                )
            )
        return decorator

    @staticmethod
    def alias(  # noqa: PLR0913
        *,
        base: type[object],
        rules: Annotated[list[BaseRule] | None, "Validation rules for the alias"] = None,
        operations: Annotated[MethodConfig | None, "Configuration for primitive-like operations"],
        description: str | None = None,
        tags: Annotated[tuple[Tag | object, ...] | None, "Hierarchical tags for categorization."] = None,
        enable_caching: bool = False,
        **kwargs: Any,
    ) -> Callable[[type[object]], type[object]]:
        """
        Create a domain-specific alias from a base primitive or composite type.

        Args:
            base: The base primitive or composite type to alias
            rules: Optional list of validation rules for the alias
            operations: Optional configuration for primitive-like operations (arithmetic, casting, etc.)
            description: Optional description of the alias
            tags: Optional tags for categorizing or annotating the asset
            enable_caching: Whether to enable caching for this asset (default: False)
            **kwargs: Additional metadata to attach

        Returns:
            A decorator that creates an alias type asset
        """
        def decorator(cls: type[object]) -> type[object]:
            return _run_import_async(
                acreate_alias(
                    cls,
                    target_protocol=AdvancedAsset,  # Always use AdvancedAsset by default
                    base=base,
                    rules=rules,
                    operations=operations,
                    description=description,
                    tags=tags,
                    enable_caching=enable_caching,
                    **kwargs,
                )
            )
        return decorator

    @staticmethod
    def model(
        *,
        description: str | None = None,
        tags: Annotated[tuple[Tag | object, ...] | None, "Hierarchical tags for categorization."] = None,
        rules: list[BaseRule] | None = None,
        model_config: dict[str, Any] | None = None,
        enable_caching: bool = False,
        **kwargs: Any,
    ) -> Callable[[type[T_Model]], type[T_Model]]:
        """
        Enhance a Pydantic BaseModel with domain-specific features.

        Args:
            description: Optional description of the model.
            tags: Optional tags for categorizing or annotating the asset.
            rules: Optional list of validation rules for the model.
            model_config: Pydantic V2 ConfigDict for model configuration.
            enable_caching: Whether to enable caching for this asset (default: False)
            **kwargs: Additional metadata to attach.

        Returns:
            A decorator that creates an enhanced model asset.
        """
        def decorator(cls: type[T_Model]) -> type[T_Model]:
            if model_config:
                kwargs["model_config"] = model_config
            return _run_import_async(
                acreate_model(
                    cls,
                    target_protocol=AdvancedAsset,  # Always use AdvancedAsset by default
                    description=description,
                    tags=tags,
                    rules=rules,
                    enable_caching=enable_caching,
                    **kwargs,
                )
            )
        return decorator

    @staticmethod
    def enum(
        *,
        description: str | None = None,
        tags: tuple[Tag | object, ...] | None = None,
        base_type: type[PyEnum] = PyEnum,
        enable_caching: bool = False,
        **kwargs: Any,
    ) -> Callable[[type[E]], type[E]]:
        """
        Enhance a standard Python Enum with domain-specific features.

        Args:
            description: Optional description of the enum.
            tags: Optional tags for categorizing or annotating the asset.
            base_type: The base enum type to use (e.g., aenum.Enum, aenum.StrEnum).
            enable_caching: Whether to enable caching for this asset (default: False)
            **kwargs: Additional metadata to attach.

        Returns:
            A decorator that creates an enhanced enum asset.

        Example:
            @Build.enum(base_type=aenum.StrEnum, description="Status types")
            class Status(aenum.StrEnum):
                ACTIVE = "active"
                PENDING = aenum.auto()  # Use aenum.auto() directly
                DONE = "done"
        """
        def decorator(cls: type[E]) -> type[E]:
            # Cast to the correct enum type for type safety
            enum_cls = cast("type[PyEnum]", cls)
            kwargs["base_type"] = base_type

            # Create the enhanced enum and cast the result back to type[E]
            enhanced_enum: type[PyEnum] = _run_import_async(
                acreate_enum(
                    enum_cls,
                    target_protocol=AdvancedAsset,
                    description=description,
                    tags=tags,
                    enable_caching=enable_caching,
                    **kwargs,
                )
            )
            return cast("type[E]", enhanced_enum)
        return decorator

    # --- Rule Decorators ------------------------------------------------------

    @staticmethod
    def validator(
        *,
        error_template: Annotated[str | None, "Template for error messages"] = None,
        description: Annotated[str | None, "Detailed description of the rule's purpose."] = None,
        tags: Annotated[tuple[Tag | Any, ...] | None, "Hierarchical tags for categorization."] = None,
        register_as: Annotated[Any | None, "Key to register the rule with."] = None,  # noqa: ANN401
        **kwargs: Any,
    ) -> Callable[[Callable[[T_invariant, ValidationInfo], Awaitable[bool]]], Validator[T_invariant]]:
        """
        A decorator to create a self-describing, configurable validator.

        Args:
            error_template: Template for error messages.
            description: Description of what the validator does.
            tags: Optional tags for categorizing the validator.
            register_as: Key to register the rule in the global registry.
            **kwargs: Additional metadata to attach.

        Returns:
            A decorator that creates a validator instance.
        """

        def decorator(
            func: Callable[[T_invariant, ValidationInfo], Awaitable[bool]]
        ) -> Validator[T_invariant]:
            """The actual decorator that takes the function and returns a validator instance."""
            metadata = kwargs.copy()
            if description is not None:
                metadata["description"] = description
            if tags is not None:
                metadata["tags"] = tags

            instance = function_validator_factory(
                func, error_template=error_template, **metadata
            )

            if register_as:
                from mzn.types.rules.rule_registry import register_rule  # noqa: PLC0415
                register_rule(register_as, instance)

            return instance

        return decorator

    @staticmethod
    def normalizer(
        *,
        description: Annotated[str | None, "Detailed description of the rule's purpose."] = None,
        tags: Annotated[tuple[Tag | Any, ...] | None, "Hierarchical tags for categorization."] = None,
        register_as: Annotated[Any | None, "Key to register the rule with."] = None,  # noqa: ANN401
        **kwargs: Any,
    ) -> Callable[
        [Callable[[T_in_contra, ValidationInfo], Awaitable[T_out_co]]],
        Normalizer[T_in_contra, T_out_co],
    ]:
        """
        A decorator to create a self-describing, configurable normalizer.

        Args:
            description: Description of what the normalizer does.
            tags: Optional tags for categorizing the normalizer.
            register_as: Key to register the rule in the global registry.
            **kwargs: Additional metadata to attach.

        Returns:
            A decorator that creates a normalizer instance.
        """

        def decorator(
            func: Callable[[T_in_contra, ValidationInfo], Awaitable[T_out_co]]
        ) -> Normalizer[T_in_contra, T_out_co]:
            """The actual decorator that takes the function and returns a normalizer instance."""
            metadata = kwargs.copy()
            if description is not None:
                metadata["description"] = description
            if tags is not None:
                metadata["tags"] = tags

            instance = function_normalizer_factory(func, **metadata)

            if register_as:
                from mzn.types.rules.rule_registry import register_rule  # noqa: PLC0415
                register_rule(register_as, instance)

            return instance

        return decorator


# --- Public re-exports --------------------------------------------------------

__all__ = [
    "Build",
]

"""
Title         : core_factory.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_core/core_factory.py

Description
-----------
Async-first protocol-aware factory for creating type assets with automatic feature composition.

This module provides intelligent async factories that automatically apply the appropriate
feature mixins based on asset type and target protocol tier, enabling progressive
enhancement while maintaining clean APIs. Fully integrated with Error/Cache/Log infrastructure.
"""

from __future__ import annotations

import functools
from typing import Annotated, Any, TypeGuard, TypeVar, cast

from aenum import Enum
from beartype import beartype
from pydantic import RootModel, TypeAdapter

from mzn.types._contracts.diagnostics import aget_required_mixins, aimplements_protocol
from mzn.types._contracts.prot_assets import (
    AdvancedAsset,
    CoreAsset,
    ValidatedAsset,
)
from mzn.types._contracts.prot_features import TaggingProvider
from mzn.types._core import core_constants as constants
from mzn.types._core.core_factory_base import TypeAsset
from mzn.types._core.core_operations import (
    ArithmeticMixin,
    CastingMixin,
    ContainerMixin,
    DateTimeLikeMixin,
    MethodConfig,
    PathLikeMixin,
    TimeDeltaLikeMixin,
)
from mzn.types._core.core_registry import aregister_type
from mzn.types._features.feat_caching import CachingMixin
from mzn.types._features.feat_comparison import ComparisonMixin
from mzn.types._features.feat_documentation import DocumentationMixin
from mzn.types._features.feat_metadata import MetadataMixin
from mzn.types._features.feat_rules import RulesMixin
from mzn.types._features.feat_tagging import TaggingMixin


# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")
T_Build = TypeVar("T_Build")
M = TypeVar("M", bound=object, default=object)


# --- TypeGuards ---------------------------------------------------------------


def is_metadata_dict(value: object) -> Annotated[TypeGuard[dict[str, Any]], "Checks if value is a metadata dict"]:
    """Check if a value is a dictionary containing metadata."""
    return isinstance(value, dict)


# --- Asset Type Defaults ------------------------------------------------------

# Smart defaults: what protocol tier each asset type gets by default
ASSET_TYPE_DEFAULTS = {
    constants.PRIMITIVE: CoreAsset,  # Primitives only need metadata + registry
    constants.ALIAS: AdvancedAsset,  # Upgraded: full features for aliases
    constants.ENUM: AdvancedAsset,  # Upgraded: full features for enums
    constants.MODEL: AdvancedAsset,  # Upgraded: full features for models
    constants.PROTOCOL: AdvancedAsset,  # Protocols get full features too
}


# --- Mixin Application System -------------------------------------------------


@beartype
async def aget_mixin_classes() -> dict[str, type]:
    """
    Get available mixin classes for feature composition.

    Returns:
        Dictionary mapping mixin names to mixin classes
    """
    mixins: dict[str, type] = {
        "ComparisonMixin": ComparisonMixin,
        "MetadataMixin": MetadataMixin,
        "RulesMixin": RulesMixin,
        "CachingMixin": CachingMixin,
        "DocumentationMixin": DocumentationMixin,
        "TaggingMixin": TaggingMixin,
        # Operation mixins
        "ArithmeticMixin": ArithmeticMixin,
        "ContainerMixin": ContainerMixin,
        "CastingMixin": CastingMixin,
        "PathLikeMixin": PathLikeMixin,
        "DateTimeLikeMixin": DateTimeLikeMixin,
        "TimeDeltaLikeMixin": TimeDeltaLikeMixin,
    }

    # Debug: Loaded mixin classes
    return mixins


# --- Helper Functions for Mixin Application ----------------------------------


@beartype
def _copy_attribute(target_cls: type, attr_name: str, attr_value: Any) -> None:  # noqa: ANN401
    """Copy a single attribute to the target class."""
    if isinstance(attr_value, classmethod):
        # For classmethods, we need to extract the underlying function and re-wrap it
        # This ensures the classmethod is properly bound to the target class
        func = attr_value.__func__  # pyright: ignore[reportUnknownMemberType,reportUnknownVariableType]
        new_classmethod = classmethod(func)  # pyright: ignore[reportUnknownArgumentType,reportUnknownVariableType]
        setattr(target_cls, attr_name, new_classmethod)
    elif isinstance(attr_value, staticmethod):
        # Staticmethods can be used directly
        setattr(target_cls, attr_name, attr_value)
    elif callable(attr_value):
        # Regular method from mixin class
        setattr(target_cls, attr_name, attr_value)
    else:
        # Non-method attribute
        setattr(target_cls, attr_name, attr_value)


@beartype
def _apply_mixin_to_enum(cls: type, mixin: type) -> None:
    """Apply a single mixin to an enum class."""
    for attr_name in dir(mixin):
        if not attr_name.startswith("_") and not hasattr(cls, attr_name):
            attr_value = getattr(mixin, attr_name)
            _copy_attribute(cls, attr_name, attr_value)


@beartype
def _apply_type_asset_to_enum(cls: type) -> None:
    """Apply TypeAsset attributes to an enum class."""
    # First, ensure the essential class variables are initialized
    if not hasattr(cls, "mzn_metadata"):
        setattr(cls, "mzn_metadata", {})  # noqa: B010
    if not hasattr(cls, "mzn_tags"):
        setattr(cls, "mzn_tags", set())  # noqa: B010

    # Then copy other attributes from TypeAsset
    for attr_name in dir(TypeAsset):
        if not attr_name.startswith("_") and not hasattr(cls, attr_name):
            attr_value = getattr(TypeAsset, attr_name)
            _copy_attribute(cls, attr_name, attr_value)


@beartype
async def aapply_mixins[T](cls: type[T], mixin_names: list[str]) -> type[T]:
    """
    Apply feature mixins to a class.

    Args:
        cls: Base class to enhance
        mixin_names: List of mixin names to apply

    Returns:
        Enhanced class with mixins applied

    Raises:
        RuntimeError: If mixin application fails
    """
    # Apply mixins to the class
    try:
        available_mixins = await aget_mixin_classes()
        mixins_to_apply = [available_mixins[name] for name in mixin_names if name in available_mixins]

        if not mixins_to_apply:
            # Debug: No mixins to apply
            return cls

        # Check if this is an Enum type - they have special metaclass requirements
        if hasattr(cls, "__members__") and hasattr(cls, "_member_names_"):
            # For enums, we can't use multiple inheritance due to metaclass conflicts
            # Instead, we'll add methods directly to the enum class
            for mixin in mixins_to_apply:
                _apply_mixin_to_enum(cls, mixin)

            # Add TypeAsset attributes if not present
            _apply_type_asset_to_enum(cls)

            enhanced_cls = cls
        else:
            # Non-enum classes can use normal multiple inheritance
            # Create new class with mixins as first bases, user class next,
            # and our TypeAsset as the ultimate base
            class_name = cls.__name__
            # All bases are types by construction
            bases = (TypeAsset, *mixins_to_apply, cast("type", cls))

            # Preserve original class dict
            class_dict = dict(cls.__dict__)

            # Add a default model_config, which can be overridden by the user's class
            default_config = {"frozen": True}
            existing_config = class_dict.get("model_config", {})
            class_dict["model_config"] = {**default_config, **existing_config}

            enhanced_cls = type(class_name, bases, class_dict)

        # Info logging removed to fix import-time resource warnings

        return enhanced_cls  # pyright: ignore[reportReturnType]  # noqa: TRY300

    except Exception as e:
        # Wrap other exceptions with descriptive message
        msg = f"Failed to apply mixins {mixin_names} to {cls.__name__}: {e}"
        raise RuntimeError(msg) from e


# --- Universal Pydantic Model Builder -----------------------------------------


async def abuild_asset_model[T_Build](
    name: str,
    inner_type: type[T_Build] | object,  # Can be a type or a generic alias like list[str]
    *,
    shell_cls: type[Any],
) -> type[RootModel[Any]]:
    """
    Universal builder for creating the final Pydantic RootModel for an asset.

    This function takes a class that has been enhanced with feature mixins
    and constructs the final, instantiable Pydantic model using Pydantic's
    official create_model API.

    Args:
        name: The name of the new model class.
        inner_type: The core Python type this asset wraps (e.g., str, int).
        shell_cls: The class composed by the AssetFactory, containing mixins.

    Returns:
        A new Pydantic RootModel subclass.
    """
    # 1. Process rules if the class supports it
    all_rules: list[Any] = []
    if await aimplements_protocol(shell_cls, ValidatedAsset) and hasattr(shell_cls, "process_rules"):
        normalizers, validators = await shell_cls.process_rules()
        all_rules.extend(normalizers)
        all_rules.extend(validators)

    # 2. Construct the Annotated type
    annotated_type = Annotated[inner_type, *all_rules] if all_rules else inner_type

    # 3. Create a proper RootModel class using direct inheritance
    # This follows Pydantic's standard pattern and ensures proper metaclass handling

    # Get configuration from shell_cls (keep it simple)
    config = getattr(shell_cls, "model_config", {"frozen": True})

    # Create the class dictionary with proper annotations and configuration
    class_dict = {
        "__module__": shell_cls.__module__,
        "__doc__": shell_cls.__doc__,
        "__annotations__": {"root": annotated_type},
        "model_config": config,
    }

    # Create the RootModel subclass using type() with proper generic inheritance
    # This ensures beartype recognizes it as RootModel[T] not just RootModel
    asset_root_model = type(
        name,
        (RootModel[Any],),  # Use RootModel[Any] as the base class for dynamic creation
        class_dict,
    )

    # Add ClassVar attributes - these are dynamic, so ignore type checker warnings
    asset_root_model.mzn_metadata = shell_cls.mzn_metadata  # type: ignore[attr-defined]
    asset_root_model.mzn_tags = shell_cls.mzn_tags  # type: ignore[attr-defined]

    # Selectively copy operation methods that make sense for RootModel instances
    operation_methods = {
        # From CastingMixin
        "__int__",
        "__float__",
        "__bytes__",
        "__bool__",
        # From ContainerMixin
        "__len__",
        "__getitem__",
        "__contains__",
        "__iter__",
        "keys",
        "values",
        "items",
        "get",
        # From ArithmeticMixin
        "__add__",
        "__sub__",
        "__mul__",
        "__truediv__",
        "__floordiv__",
        "__mod__",
        "__pow__",
        "__neg__",
        "__pos__",
        "__abs__",
        # From PathLikeMixin
        "__fspath__",  # From DateTimeLikeMixin and TimeDeltaLikeMixin
        "total_seconds",
        "isoformat",
        # Special methods that are always useful
        "__repr__",
        "__str__",
        "__hash__",
        "__eq__",
        "__lt__",
        "__le__",
        "__gt__",
        "__ge__",
        # UUID specific
        "as_uuid",
    }

    # Copy operation methods from shell_cls to the new model
    for method_name in operation_methods:
        if hasattr(shell_cls, method_name):
            method = getattr(shell_cls, method_name)
            if callable(method) and not isinstance(method, type):
                setattr(asset_root_model, method_name, method)

    # 4. Apply total_ordering only if comparison methods exist
    final_cls: type[Any]
    if any(hasattr(asset_root_model, op) for op in ["__lt__", "__le__", "__gt__", "__ge__"]):
        final_cls = functools.total_ordering(asset_root_model)
    else:
        final_cls = asset_root_model

    # 5. Create and set the TypeAdapter for validation
    adapter = TypeAdapter(final_cls)
    final_cls.adapter = adapter

    return cast("type[RootModel[Any]]", final_cls)


# --- Core Asset Factory -------------------------------------------------------


@beartype
class AssetFactory:
    """
    Async protocol-aware factory for creating type assets.

    Automatically applies appropriate feature mixins based on the target protocol
    and asset type, enabling progressive enhancement with zero configuration.
    """

    def __init__(self, asset_type: constants.AssetType) -> None:
        """Initialize the factory for a specific asset type.

        Args:
            asset_type: Type of asset this factory creates
        """
        super().__init__()
        self.asset_type = asset_type
        self.default_protocol = ASSET_TYPE_DEFAULTS.get(asset_type, CoreAsset)

    @beartype
    async def acreate(  # noqa: PLR0913
        self,
        cls: type[T],
        *,
        target_protocol: type | None = None,
        metadata: dict[str, Any] | None = None,
        tags: tuple[Any, ...] | None = None,
        register: bool = True,
        operations: MethodConfig | None = None,
        # Performance features - OFF by default
        enable_caching: bool = False,
    ) -> type[T]:
        """
        Create an enhanced asset with automatic feature composition.

        Args:
            cls: Base class to enhance
            target_protocol: Target protocol tier (auto-selected if None)
            metadata: Additional metadata to attach
            tags: Set of tags to attach to the asset
            register: Whether to register in the type registry
            operations: Configuration for primitive-like operations.
            enable_caching: Whether to enable caching (performance feature, off by default)

        Returns:
            Enhanced class with appropriate mixins applied

        Raises:
            RuntimeError: If asset creation fails
        """
        # Create the asset with appropriate features
        try:
            # Determine target protocol
            protocol = target_protocol or self.default_protocol
            protocol_name = self._get_protocol_tier_name(protocol)

            # Debug: Starting asset creation

            # Get required mixins for the protocol
            mixin_names = await aget_required_mixins(protocol)
            mixin_names = await self.apply_asset_specific_mixins(
            mixin_names,
            operations=operations,
            enable_caching=enable_caching,
            )

            # Apply mixins to create enhanced class
            enhanced_cls = await aapply_mixins(cls, mixin_names)

            # Cast the enhanced class to satisfy the type checker, as it can't
            # infer the methods from the dynamically added mixins.
            typed_enhanced_cls: type[Any] = cast("type[Any]", enhanced_cls)

            # Set up metadata
            base_metadata: dict[str, Any] = {
                "asset_type": str(self.asset_type),
                "protocol_tier": protocol_name,
                "applied_mixins": mixin_names,
                "performance_features": {
                    "caching": enable_caching,
                },
                "strict_comparison": True,
            }

            # If metadata is provided, use it.
            if metadata:
                base_metadata.update(metadata)

            # Apply metadata if the class supports it
            if hasattr(typed_enhanced_cls, "set_metadata"):
                await typed_enhanced_cls.set_metadata(**base_metadata)
            # No need for fallback - TypeAsset already has mzn_metadata attribute

            # Initialize and apply tags if the class supports it
            if await aimplements_protocol(typed_enhanced_cls, TaggingProvider) and tags:
                # TypeAsset already has mzn_tags attribute initialized as set()
                await cast("Any", typed_enhanced_cls).add_tags(*tags)

            # Register if requested
            # Final step: build the actual Pydantic model
            if self.asset_type == constants.MODEL:
                final_model = typed_enhanced_cls
            else:
                # For primitives and aliases, we need the actual inner type
                # For primitives, this comes from the decorator's inner_type parameter
                # For aliases, we need to extract it from the base primitive's root type
                inner_type = metadata.get("inner_type") if metadata else None
                if inner_type is None and metadata and "base_primitive" in metadata:
                    # For aliases, the base_primitive is a RootModel class
                    # We need to get its root type annotation
                    base_prim = metadata["base_primitive"]
                    if hasattr(base_prim, "__annotations__") and "root" in base_prim.__annotations__:
                        inner_type = base_prim.__annotations__["root"]
                    else:
                        # Try to get from model_fields
                        root_field = getattr(base_prim, "model_fields", {}).get("root")
                        inner_type = root_field.annotation if root_field else object
                if inner_type is None:
                    # Fallback to object type
                    inner_type = object

                final_model = await abuild_asset_model(
                    name=cls.__name__,
                    inner_type=inner_type,
                    shell_cls=typed_enhanced_cls,
                )

            if register:
                _ = await aregister_type(final_model, asset_type=self.asset_type)

            # Info: Asset creation completed successfully

            return cast("type[T]", final_model)

        except Exception as e:
            # Wrap other exceptions with descriptive message
            msg = f"Failed to create {self.asset_type} asset from {cls.__name__}: {e}"
            raise RuntimeError(msg) from e

    @staticmethod
    def _get_protocol_tier_name(protocol: type[Any]) -> str:
        """Get human-readable name for protocol tier."""
        protocol_map = {
            AdvancedAsset: "advanced",
            ValidatedAsset: "validated",
            CoreAsset: "core",
        }
        return protocol_map.get(protocol, "unknown")

    async def apply_asset_specific_mixins(  # noqa: PLR6301
        self,
        mixin_names: list[str],
        *,
        operations: MethodConfig | None = None,
        enable_caching: bool = False,
    ) -> list[str]:
        """
        Add asset-specific mixins and core/performance features.

        Args:
            mixin_names: Base mixin names from protocol requirements
            operations: Configuration for primitive-like operations.
            enable_caching: Whether to enable caching (performance feature, off by default)

        Returns:
            List of mixin names to apply, with duplicates removed
        """
        # Always apply core mixins
        core_mixins = [
            "MetadataMixin",  # Always required
            "DocumentationMixin",  # Core feature
            "ComparisonMixin",  # Core feature
            "TaggingMixin",  # Always required for categorization
            "RulesMixin",  # Required for validation
        ]
        mixin_names.extend(core_mixins)

        # Asset-specific mixins
        # (No special mixins needed for simplified enums)

        # Apply operation mixins if configured
        if operations:
            if operations.arithmetic:
                mixin_names.append("ArithmeticMixin")
            if operations.container:
                mixin_names.append("ContainerMixin")
            if operations.casting:
                mixin_names.append("CastingMixin")
            if operations.path_like:
                mixin_names.append("PathLikeMixin")
            if operations.datetime_like:
                mixin_names.append("DateTimeLikeMixin")
            if operations.timedelta_like:
                mixin_names.append("TimeDeltaLikeMixin")

        # Add performance features only if explicitly enabled
        if enable_caching:
            mixin_names.append("CachingMixin")

        # Remove duplicates while preserving order
        return list(dict.fromkeys(mixin_names))


# --- Factory Instances --------------------------------------------------------

primitive_factory = AssetFactory(constants.PRIMITIVE)
alias_factory = AssetFactory(constants.ALIAS)
enum_factory = AssetFactory(constants.ENUM)
model_factory = AssetFactory(constants.MODEL)

# --- High-Level Async API Functions -------------------------------------------


@beartype
async def acreate_primitive[T](
    cls: type[T],
    *,
    register: bool = True,
    enable_caching: bool = False,
    **kwargs: Any,
) -> type[T]:
    """
    Create a primitive type asset.

    Primitives always get CoreAsset tier (metadata + registry only).

    Args:
        cls: Base class to enhance
        register: Whether to register in the type registry
        enable_caching: Whether to enable caching (performance feature, off by default)
        **kwargs: Additional metadata to attach

    Returns:
        Enhanced primitive type
    """
    return await primitive_factory.acreate(
        cls,
        metadata=kwargs,
        register=register,
        enable_caching=enable_caching,
    )


@beartype
async def acreate_alias[T](  # noqa: PLR0913
    cls: type[T],
    *,
    target_protocol: type | None = None,
    register: bool = True,
    base: type[Any] | None = None,
    rules: list[Any] | None = None,
    operations: MethodConfig | None = None,
    enable_caching: bool = False,
    **kwargs: Any,
) -> type[T]:
    """
    Create an alias type asset.

    Aliases default to ValidatedAsset tier (rules processing for business logic).

    Args:
        cls: Base class to enhance
        target_protocol: Target protocol tier (ValidatedAsset if None)
        register: Whether to register in the type registry
        base: The base primitive or composite type to alias
        rules: Optional list of validation rules for the alias
        operations: Configuration for primitive-like operations.
        enable_caching: Whether to enable caching (performance feature, off by default)
        **kwargs: Additional metadata to attach

    Returns:
        Enhanced alias type
    """
    # Build metadata from direct parameters
    metadata: dict[str, Any] = dict(kwargs)
    if base is not None:
        metadata["base_primitive"] = base
    if rules is not None:
        metadata["rules"] = rules

    return await alias_factory.acreate(
        cls,
        target_protocol=target_protocol,
        metadata=metadata,
        register=register,
        operations=operations,
        enable_caching=enable_caching,
    )


@beartype
async def acreate_enum(
    cls: type[Any],
    *,
    target_protocol: type | None = None,
    register: bool = True,
    enable_caching: bool = False,
    **kwargs: Any,
) -> type[Enum]:
    """
    Create a rich enum asset from a standard Python Enum.

    This factory enhances the user's enum class with composable features,
    working directly with aenum to support rich, self-describing members.

    Args:
        cls: The standard Enum class to enhance.
        target_protocol: Target protocol tier (AdvancedAsset if None).
        register: Whether to register in the type registry.
        enable_caching: Whether to enable caching (performance feature, off by default).
        **kwargs: Additional metadata, including `base_type`.

    Returns:
        A new, enhanced enum asset class.
    """
    enum_cls = cls
    base_type = kwargs.pop("base_type", Enum)

    # 1. Create the new enum using aenum's functional API
    # This preserves the rich EnumMember objects as the actual members
    members = {}

    # Check if this is already an enum (has __members__) or a regular class
    if hasattr(enum_cls, "__members__"):
        # Already an enum, extract members
        for name, member in enum_cls.__members__.items():
            member_value = getattr(member, "value", member)
            members[name] = member_value
    else:
        # Regular class with attributes, extract enum-like attributes
        for name in dir(enum_cls):
            if not name.startswith("_"):  # Skip private/dunder attributes
                value = getattr(enum_cls, name)
                if not callable(value):  # Skip methods
                    members[name] = value

    enhanced_enum = base_type(enum_cls.__name__, members)

    # 2. Get the full metadata payload for the factory
    # We no longer store member metadata here; it's on the members themselves.
    final_metadata: dict[str, Any] = kwargs

    # 3. Apply mixins directly to the enum class
    # Get required mixins for the target protocol
    mixin_names = await aget_required_mixins(target_protocol or AdvancedAsset)

    # Apply asset-specific mixins including the core ones
    factory = AssetFactory(constants.ENUM)
    mixin_names = await factory.apply_asset_specific_mixins(
        mixin_names,
        operations=None,
        enable_caching=enable_caching,
    )

    # Apply mixins to the enum
    final_enhanced_enum = await aapply_mixins(enhanced_enum, mixin_names)

    # For enums, we need to set metadata directly since the mixin methods might not work correctly
    # due to the special way enums handle class attributes
    metadata_dict = {
        "asset_type": str(constants.ENUM),
        "protocol_tier": "advanced",
        "applied_mixins": mixin_names,
        "performance_features": {
            "caching": enable_caching,
        },
        **final_metadata
    }

    # Set metadata directly on the enum class
    final_enhanced_enum.mzn_metadata = metadata_dict

    # Debug: Set metadata directly on enum

    # Initialize tags if not already present
    if not hasattr(final_enhanced_enum, "mzn_tags"):
        final_enhanced_enum.mzn_tags = set()

    # Apply tags if provided (direct assignment for enums to avoid mixin issues)
    if "tags" in kwargs:
        final_enhanced_enum.mzn_tags = set(kwargs["tags"])

    # 4. Generate docstring for the enhanced enum
    if hasattr(final_enhanced_enum, "generate_docstring"):
        try:
            docstring = await final_enhanced_enum.generate_docstring("enum", cls)
            final_enhanced_enum.__doc__ = docstring
        except RuntimeError:
            # If docstring generation fails, log the exception for debugging
            # Warning: Docstring generation failed
            pass

    # 5. Final registration with all metadata
    if register:
        await aregister_type(
            final_enhanced_enum,
            asset_type=constants.ENUM,
            metadata=final_metadata,
        )

    return cast("type[Enum]", final_enhanced_enum)


@beartype
async def acreate_model[T](
    cls: type[T],
    *,
    target_protocol: type | None = None,
    register: bool = True,
    rules: list[Any] | None = None,
    enable_caching: bool = False,
    **kwargs: Any,
) -> type[T]:
    """
    Create a model type asset.

    Models default to ValidatedAsset tier but can be enhanced to AdvancedAsset.

    Args:
        cls: Base class to enhance
        target_protocol: Target protocol tier (ValidatedAsset if None)
        register: Whether to register in the type registry
        rules: Optional list of validation rules for the model
        enable_caching: Whether to enable caching (performance feature, off by default)
        **kwargs: Additional metadata to attach

    Returns:
        Enhanced model type
    """
    # Build metadata from direct parameters
    metadata: dict[str, Any] = dict(kwargs)
    if rules is not None:
        metadata["rules"] = rules

    # For models, the class itself is the core of the asset
    return await model_factory.acreate(
        cls,
        target_protocol=target_protocol,
        metadata=metadata,
        register=register,
        enable_caching=enable_caching,
    )


@beartype
async def acreate_asset[T](  # noqa: PLR0913
    cls: type[T],
    *,
    asset_type: constants.AssetType,
    target_protocol: type | None = None,
    register: bool = True,
    operations: MethodConfig | None = None,
    enable_caching: bool = False,
    **kwargs: Any,
) -> type[T]:
    """
    Generic asset creation function.

    Args:
        cls: Base class to enhance
        asset_type: Type of asset to create
        target_protocol: Target protocol tier (auto-selected if None)
        register: Whether to register in the type registry
        operations: Configuration for primitive-like operations.
        enable_caching: Whether to enable caching (performance feature, off by default)
        **kwargs: Additional metadata to attach

    Returns:
        Enhanced asset type
    """
    factory_map = {
        constants.PRIMITIVE: primitive_factory,
        constants.ALIAS: alias_factory,
        constants.ENUM: enum_factory,
        constants.MODEL: model_factory,
    }

    factory = factory_map.get(asset_type)
    if not factory:
        msg = f"Unknown asset type: {asset_type}"
        raise ValueError(msg)

    return await factory.acreate(
        cls,
        target_protocol=target_protocol,
        metadata=kwargs,
        register=register,
        operations=operations,
        enable_caching=enable_caching,
    )


# --- Protocol Upgrade Functions -----------------------------------------------


@beartype
async def aupgrade_to_validated[T](cls: type[T]) -> type[T]:
    """
    Upgrade an asset to ValidatedAsset tier.

    Args:
        cls: Asset class to upgrade

    Returns:
        Upgraded asset class
    """
    # Timer: protocol_upgrade to validated
    mixin_names = await aget_required_mixins(ValidatedAsset)
    # Info: Upgraded asset to ValidatedAsset
    return await aapply_mixins(cls, mixin_names)


@beartype
async def aupgrade_to_advanced[T](cls: type[T]) -> type[T]:
    """
    Upgrade an asset to AdvancedAsset tier.

    Args:
        cls: Asset class to upgrade

    Returns:
        Upgraded asset class
    """
    # Timer: protocol_upgrade to advanced
    mixin_names = await aget_required_mixins(AdvancedAsset)
    # Info: Upgraded asset to AdvancedAsset
    return await aapply_mixins(cls, mixin_names)

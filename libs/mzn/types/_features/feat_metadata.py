"""
Title         : feat_metadata.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_features/feat_metadata.py.

Description ----------- Unified metadata management feature for all asset types.

This module provides a composable mixin that implements the CoreAsset protocol, enabling consistent metadata handling
across primitives, aliases, models, and enums. Leverages Pydantic v2 for JSON schema generation and validation.

"""

from __future__ import annotations

from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    ClassVar,
    LiteralString,
    Self,
    cast,
)


# Type alias for metadata dict
if TYPE_CHECKING:
    from collections.abc import Mapping

    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag
    type MetadataDict = dict[str, JSONLike]


# --- Mixin Definition ---------------------------------------------------------


class MetadataMixin:
    """
    Composable mixin providing unified metadata management.

    This mixin implements the MetadataProvider protocol, enabling consistent metadata handling across all asset types.
    It includes special handling for tags and integration with the documentation and registry systems.

    Note: This class does NOT inherit from CoreAsset or MetadataProvider to avoid metaclass conflicts. It implements the
    required interface without inheritance.

    """

    # These attributes are provided by TypeAsset base class when the mixin is applied
    # We need to declare them for type checking
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

    # Internal storage caches
    __json_schema_cache: ClassVar[dict[str, Any] | None] = None
    __serialized_metadata_cache: ClassVar[dict[str, Any] | None] = None

    @classmethod
    async def get_metadata(cls) -> Annotated[Mapping[str, JSONLike], "Complete metadata mapping"]:
        """
        Get the type's complete metadata.

        Returns:     Immutable mapping of metadata key-value pairs

        """
        return getattr(cls, "mzn_metadata", {})

    @classmethod
    async def get_metadata_value(
        cls,
        key: Annotated[LiteralString, "Metadata key to retrieve"],
        default: Annotated[JSONLike, "Default value if key not found"] = None,
    ) -> Annotated[JSONLike, "Metadata value or default"]:
        """
        Get a specific metadata value.

        Args:     key: Metadata key to retrieve     default: Default value if key not found

        Returns:     The metadata value or default

        """
        metadata = await cls.get_metadata()
        return metadata.get(key, default)

    @classmethod
    async def set_metadata(
        cls,
        **kwargs: Annotated[JSONLike, "Metadata key-value pairs to set"]
    ) -> Annotated[None, "None"]:
        """Set metadata values."""
        if "forbidden" in kwargs:
            msg = "Forbidden metadata key detected."
            raise ValueError(msg)
        current_metadata = dict(await cls.get_metadata())
        current_metadata.update(kwargs)
        cls.mzn_metadata = current_metadata
        # Invalidate caches
        cls.__json_schema_cache = None
        cls.__serialized_metadata_cache = None

    @classmethod
    async def mzn_json_schema(cls) -> Annotated[dict[str, JSONLike], "Pydantic v2 JSON schema or empty dict"]:
        """
        Get Pydantic v2 JSON schema if available.

        Returns:     JSON schema dictionary or empty dict if not available

        """
        # Return from cache if available
        if cls.__json_schema_cache is not None:
            return cls.__json_schema_cache

        # Try to get Pydantic v2 schema
        schema_method = getattr(cls, "model_json_schema", None)
        if schema_method and callable(schema_method):
            try:
                schema = cast("dict[str, JSONLike]", schema_method())
            except Exception as exc:
                msg = f"model_json_schema failed: {exc}"
                raise ValueError(msg) from exc
            else:
                cls.__json_schema_cache = schema  # Cache the result
                return schema

        # Fallback to basic schema from metadata
        schema = await cls._generate_basic_schema()
        cls.__json_schema_cache = schema  # Cache the result
        return schema

    @classmethod
    async def _generate_basic_schema(cls) -> Annotated[dict[str, JSONLike], "Basic JSON schema dictionary"]:
        """
        Generate a basic JSON schema from metadata.

        Returns:     Basic JSON schema dictionary

        """
        metadata = await cls.get_metadata()
        schema: dict[str, JSONLike] = {
            "type": "object",
            "title": cls.__name__,
        }

        # Add description from metadata if available
        description = metadata.get("description")
        if description:
            schema["description"] = str(description)

        return schema

    @classmethod
    async def merge_metadata(
        cls,
        other_metadata: Annotated[Mapping[str, JSONLike], "Metadata to merge in"]
    ) -> Annotated[None, "None"]:
        """
        Merge additional metadata into existing metadata.

        Args:     other_metadata: Metadata to merge in

        """
        current = dict(await cls.get_metadata())
        current.update(other_metadata)
        cls.mzn_metadata = current
        # Invalidate caches
        cls.__json_schema_cache = None
        cls.__serialized_metadata_cache = None

    @classmethod
    async def has_metadata_key(
        cls,
        key: Annotated[LiteralString, "Key to check for"]
    ) -> Annotated[bool, "True if key exists, False otherwise"]:
        """
        Check if a metadata key exists.

        Args:     key: Key to check for

        Returns:     True if key exists, False otherwise

        """
        metadata = await cls.get_metadata()
        return key in metadata

    @classmethod
    async def get_metadata_keys(
        cls,
    ) -> Annotated[set[LiteralString], "Set of all metadata keys"]:
        """
        Get all metadata keys.

        Returns:     Set of all metadata keys

        """
        metadata = await cls.get_metadata()
        return cast("set[LiteralString]", set(metadata.keys()))

    @classmethod
    async def validate_metadata(
        cls,
    ) -> Annotated[bool, "True if metadata is well-formed"]:
        """
        Validate that metadata is well-formed.

        Always returns True for this implementation.

        """
        return True

    @classmethod
    async def serialize_metadata(
        cls,
    ) -> Annotated[dict[str, JSONLike], "Serialized metadata dictionary"]:
        """
        Serialize metadata to a JSON-compatible dictionary.

        Returns:     Serialized metadata dictionary

        """
        if cls.__serialized_metadata_cache is not None:
            return cls.__serialized_metadata_cache

        metadata = await cls.get_metadata()
        serialized: dict[str, JSONLike] = {}

        for key, value in metadata.items():
            # Convert value to JSON-serializable form
            serialized[str(key)] = cls._serialize_value(value)

        cls.__serialized_metadata_cache = serialized
        return serialized

    @classmethod
    def _serialize_value(
        cls: type[Self],
        value: Annotated[JSONLike, "Value to serialize"]
    ) -> Annotated[JSONLike, "JSON-serializable value"]:
        """Serialize a single metadata value."""
        if value is None:
            return value
        if isinstance(value, dict):
            return {str(k): cls._serialize_value(v) for k, v in value.items()}
        if isinstance(value, list):
            return [cls._serialize_value(item) for item in value]
        return value

    def __repr__(self) -> Annotated[str, "Enhanced repr including metadata summary"]:  # pyright: ignore[reportImplicitOverride]
        """Enhanced repr including metadata summary."""
        # Awaiting in __repr__ is not possible, use the current metadata
        base_repr = super().__repr__() if hasattr(super(), "__repr__") else f"<{self.__class__.__name__}>"
        metadata_count = len(getattr(self.__class__, "mzn_metadata", {}))

        if metadata_count > 0:
            return f"{base_repr[:-1]} metadata_keys={metadata_count}>"
        return base_repr

    @classmethod
    async def copy_metadata_from(
        cls,
        source_cls: Annotated[type[Any], "Class to copy metadata from"]
    ) -> Annotated[None, "None"]:
        """
        Copy metadata from another class into this one.

        Args:     source_cls: Class to copy metadata from

        """
        if hasattr(source_cls, "get_metadata"):
            other_metadata = await source_cls.get_metadata()
            await cls.merge_metadata(other_metadata)

    @classmethod
    async def merge_metadata_from(
        cls,
        *source_classes: Annotated[type[Any], "Classes to merge metadata from"]
    ) -> Annotated[None, "None"]:
        """
        Merge metadata from multiple source classes into this one.

        Args:     *source_classes: Classes to merge metadata from

        """
        combined_metadata: dict[str, JSONLike] = {}

        # Collect metadata from all sources
        for source_cls in source_classes:
            if hasattr(source_cls, "get_metadata"):
                other_metadata = await source_cls.get_metadata()
                combined_metadata.update(other_metadata)

        # Apply to target
        await cls.merge_metadata(combined_metadata)

    @classmethod
    async def get_asset_type(cls) -> Annotated[object, "Asset type from metadata (async)"]:
        """Get the asset type from metadata (async)."""
        return await cls.get_metadata_value("asset_type")

    @classmethod
    async def get_registry_key(cls) -> Annotated[str, "Type's registry key from its name (async)"]:
        """Get the type's registry key from its name (async)."""
        return cls.__name__


# --- Public re-exports --------------------------------------------------------


__all__: Annotated[list[str], "Public re-exports"] = ["MetadataMixin"]

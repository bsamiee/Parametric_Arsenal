"""
Title         : core_registry.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_core/core_registry.py.

Description ----------- Async-first central registry for all types in the system.

This module provides a thread-safe, async-enabled registry for tracking, registering, and looking up all type assets.
Fully integrated with Error/Cache/Log infrastructure.

"""
from __future__ import annotations

import asyncio
from types import MappingProxyType
from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    ClassVar,
    Final,
    Literal,
    TypeVar,
    assert_type,
    cast,
    override,
)

from mzn.types._contracts.diagnostics import aimplements_protocol
from mzn.types._contracts.prot_assets import CoreAsset
from mzn.types._contracts.prot_features import RegistryProvider, TaggingProvider
from mzn.types._core.core_constants import (
    ASSET_TYPES,
    AssetType,
    Sentinel,
)


if TYPE_CHECKING:
    from mzn.types._core.core_tags import Tag


# --- Type Variables -----------------------------------------------------------

DEFAULT_REGISTRY_NAME: Final = "global"

T = TypeVar("T", bound=type[CoreAsset])
CT = TypeVar("CT", bound=type[CoreAsset])  # Class type
CT_co = TypeVar("CT_co", bound=type[CoreAsset], covariant=True)

# --- Type Registry class ------------------------------------------------------


class TypeRegistry(RegistryProvider[CT]):
    """Thread-safe, fully async registry for type assets."""
    __slots__ = ("_by_asset_type", "_lock", "name", "registry")

    def __init__(
        self,
        name: Annotated[Literal["global"], "Registry name"] = DEFAULT_REGISTRY_NAME
    ) -> None:
        """Initialize the registry with a name and empty asset mappings."""
        super().__init__()
        self.name: Annotated[Literal["global"], "Registry name"] = name
        self.registry: Annotated[dict[str, CT], "Key to asset mapping"] = {}
        self._by_asset_type: Annotated[dict[AssetType, dict[str, CT]], "Asset type to key mapping"] = {
            asset_type: {} for asset_type in ASSET_TYPES
        }
        self._lock: Annotated[asyncio.Lock, "Async lock for thread safety"] = asyncio.Lock()

    @staticmethod
    async def _determine_asset_type(
        asset_class: Annotated[CT, "Class to check"],
        explicit_type: Annotated[AssetType | Sentinel | None, "Explicit asset type or sentinel"] = Sentinel.AUTO_DETECT,
    ) -> Annotated[AssetType, "Determined asset type"]:
        """Determine the asset type for a class with fallback logic."""
        match explicit_type:
            case None | Sentinel.AUTO_DETECT:
                pass  # Fallback logic below
            case AssetType() as at:
                _ = assert_type(at, AssetType)
                return at
            case Sentinel():
                pass
        # Use isinstance for clarity and type safety
        if isinstance(explicit_type, Sentinel):
            pass  # Already handled above, but explicit for clarity
        if callable(getattr(asset_class, "aget_asset_type", None)):
            try:
                if (result := await asset_class.aget_asset_type()) in ASSET_TYPES:
                    return result
            except AttributeError:
                # Exception occurred during asset type determination
                pass
        return AssetType.PRIMITIVE

    @override
    async def aregister(
        self,
        cls: Annotated[CT, "Class to register"],
        *,
        key: Annotated[str | None, "Registry key"] = None,
        asset_type: Annotated[AssetType | Sentinel | None, "Asset type or sentinel"] = Sentinel.AUTO_DETECT,
        metadata: Annotated[dict[str, Any] | None, "Optional metadata"] = None,
    ) -> Annotated[CT, "Registered class"]:
        """Register a class in the registry with async logging."""
        # ── 1. work out the registry key ────────────────────────────────────
        if key is not None:
            registry_key: str = key
        else:
            aget_registry_key = getattr(cls, "aget_registry_key", None)
            if callable(aget_registry_key):
                result = aget_registry_key()
                if asyncio.iscoroutine(result):
                    registry_key = await result
                else:
                    msg = "aget_registry_key must be an async method returning a string."
                    raise TypeError(msg)
            else:
                registry_key = getattr(cls, "__name__", "") or str(id(cls))

        # ── 2. work out the asset type ───────────────────────────────────────
        final_asset_type = await self._determine_asset_type(cls, asset_type)
        _ = assert_type(final_asset_type, AssetType)

        # ── 3. register atomically ───────────────────────────────────────────
        async with self._lock:
            if (existing := self.registry.get(registry_key)) is not None:
                if existing is not cls:
                    msg = f"Key '{registry_key}' already registered to a different class"
                    raise TypeError(msg)
                return cls                       # already registered → nothing to do

            self.registry[registry_key] = cls
            self._by_asset_type[final_asset_type][registry_key] = cls

        # ── 4. optional post-registration hook ───────────────────────────────
        aregistered = getattr(cls, "aregistered", None)
        if callable(aregistered):
            try:
                result = aregistered(
                    registry_key=registry_key,
                    asset_type=final_asset_type,
                    metadata=metadata,
                )
                if asyncio.iscoroutine(result):
                    await result
            except AttributeError:
                # Asset post-registration hook raised
                pass

        return cls

    @override
    async def aget(
        self,
        key: Annotated[str, "Registry key"]
    ) -> Annotated[CT | None, "Registered class or None"]:
        """Get a type by registry key with async logging."""
        async with self._lock:
            return self.registry.get(key)

    @override
    async def aget_asset_type(
        self,
        asset_type: Annotated[AssetType, "Asset type"]
    ) -> Annotated[dict[str, CT], "Mapping of keys to classes"]:
        """Get all types of a specific asset type with async logging."""
        async with self._lock:
            return dict(self._by_asset_type[asset_type])

    @override
    async def aget_asset_types(
        self,
    ) -> Annotated[set[AssetType], "Set of asset types"]:
        """Get all asset types in the registry with data."""
        async with self._lock:
            return {asset_type for asset_type, types in self._by_asset_type.items() if types}

    @override
    async def aget_all(
        self,
    ) -> Annotated[MappingProxyType[str, CT], "Immutable mapping of all registered types"]:
        """Get an immutable view of all registered types."""
        async with self._lock:
            return MappingProxyType(dict(self.registry))

    @override
    async def aunregister(
        self,
        key: Annotated[str, "Registry key"]
    ) -> Annotated[CT | None, "Unregistered class or None"]:
        """Unregister a type by registry key with async logging."""
        async with self._lock:
            if (result := self.registry.pop(key, None)) is not None:
                for asset_dict in self._by_asset_type.values():
                    if key in asset_dict:
                        _ = asset_dict.pop(key)
                        break
            else:
                result = None
            return result

    @override
    async def aclear(
        self,
    ) -> Annotated[None, "None"]:
        """Clear the registry with async logging."""
        async with self._lock:
            self.registry.clear()
            for d in self._by_asset_type.values():
                d.clear()

    @override
    async def acontains(
        self,
        key: Annotated[str, "Registry key"]
    ) -> Annotated[bool, "True if key is in registry"]:
        """Check if a key is in the registry."""
        async with self._lock:
            return key in self.registry

    @override
    async def alen(
        self,
    ) -> Annotated[int, "Number of registered types"]:
        """Get the number of registered types."""
        async with self._lock:
            return len(self.registry)

    @override
    async def aget_stats(
        self,
    ) -> Annotated[dict[str, Any], "Registry statistics"]:
        """Get comprehensive registry statistics."""
        async with self._lock:
            asset_types_present = [str(at) for at in await self.aget_asset_types()]
            return {
                "registry_name": self.name,
                "total_types": len(self.registry),
                "asset_type_breakdown": {str(at): len(types) for at, types in self._by_asset_type.items()},
                "registered_keys": list(self.registry.keys()),
                "asset_types_present": asset_types_present
            }

    async def aget_by_tag(
        self,
        tag: Annotated[Tag, "Tag to filter by"]
    ) -> Annotated[dict[str, CT], "Assets with the specified tag"]:
        """
        Get all assets with a specific tag.

        Args:     tag: The tag to filter assets by

        Returns:     Dictionary of registry keys to asset classes with the tag

        """
        results: dict[str, CT] = {}
        async with self._lock:
            for key, asset_cls in self.registry.items():
                if await aimplements_protocol(cast("Any", asset_cls), TaggingProvider):
                    try:
                        if await cast("TaggingProvider", asset_cls).has_tag(tag):
                            results[key] = asset_cls
                    except AttributeError:
                        # Error checking tag
                        pass
        return results

    async def aget_by_tag_prefix(
        self,
        prefix: Annotated[str, "Tag path prefix"]
    ) -> Annotated[dict[str, CT], "Assets with tags matching the prefix"]:
        """
        Get all assets with tags matching a prefix.

        Args:     prefix: The tag path prefix to filter by

        Returns:     Dictionary of registry keys to asset classes with matching tags

        """
        results: dict[str, CT] = {}
        async with self._lock:
            for key, asset_cls in self.registry.items():
                if await aimplements_protocol(cast("Any", asset_cls), TaggingProvider):
                    try:
                        matching_tags = await cast("TaggingProvider", asset_cls).filter_tags_by_prefix(prefix)
                        if matching_tags:
                            results[key] = asset_cls
                    except AttributeError:
                        # Error filtering tags by prefix
                        pass
        return results

    @staticmethod
    async def _has_all_tags(
        asset: Annotated[TaggingProvider, "Asset to check"],
        tags: Annotated[set[Tag], "Tags to check"]
    ) -> Annotated[bool, "True if asset has all tags"]:
        """Check if an asset has all specified tags."""
        for tag in tags:
            if not await asset.has_tag(tag):
                return False
        return True

    async def aget_by_tags(
        self,
        tags: Annotated[set[Tag], "Tags to filter by"],
        *,
        match_all: Annotated[bool, "Require all tags to match"] = False
    ) -> Annotated[dict[str, CT], "Assets with the specified tags"]:
        """
        Get assets with specified tags.

        Args:     tags: The set of tags to filter by     match_all: If True, assets must have all specified tags to
        match                If False, assets with any of the specified tags will match

        Returns:     Dictionary of registry keys to asset classes with matching tags

        """
        results: dict[str, CT] = {}
        async with self._lock:
            for key, asset_cls in self.registry.items():
                # Skip assets without tag support
                if not await aimplements_protocol(cast("Any", asset_cls), TaggingProvider):
                    continue
                try:
                    asset_cls_tagged = cast("TaggingProvider", asset_cls)
                    if match_all:
                        # Check if asset has all specified tags
                        if await TypeRegistry._has_all_tags(asset_cls_tagged, tags):
                            results[key] = asset_cls
                    # Check if asset has any of the specified tags
                    elif await asset_cls_tagged.has_any_tag(*tags):
                        results[key] = asset_cls
                except AttributeError:
                    # Error checking tags
                    pass

        return results

    async def aget_by_ancestor(
        self,
        ancestor: Annotated[Tag, "Ancestor tag to filter by"]
    ) -> Annotated[dict[str, CT], "Assets with tags descending from the ancestor"]:
        """
        Get all assets that have a tag with the specified ancestor.

        Args:     ancestor: The ancestor tag to filter by.

        Returns:     A dictionary of registry keys to asset classes that have a matching tag.

        """
        results: dict[str, CT] = {}
        async with self._lock:
            for key, asset_cls in self.registry.items():
                if await aimplements_protocol(cast("Any", asset_cls), TaggingProvider):
                    try:
                        if await cast("TaggingProvider", asset_cls).has_tag_with_ancestor(ancestor):
                            results[key] = asset_cls
                    except AttributeError:
                        # Error checking ancestor tag
                        pass
        return results

    async def aget_by_asset_type(
        self,
        asset_type: Annotated[AssetType, "Asset type"]
    ) -> Annotated[dict[str, CT], "Mapping of keys to classes"]:
        """Get all types of a specific asset type (protocol alias)."""
        return await self.aget_asset_type(asset_type)

    async def get_all(
        self,
    ) -> Annotated[MappingProxyType[str, CT], "Immutable mapping of all registered types"]:
        """Get an immutable view of all registered types (required by RegistryProvider)."""
        return await self.aget_all()


# --- Global Registry Instance -------------------------------------------------

_REGISTRY: Annotated[TypeRegistry[type[CoreAsset]], "Global registry instance"] = TypeRegistry()

# --- Async API Functions ------------------------------------------------------


async def aregister_type[
    T: type[CoreAsset]
](
    cls: Annotated[T, "Type to register"],
    *,
    key: Annotated[str | None, "Registry key"] = None,
    asset_type: Annotated[AssetType | None, "Asset type"] = None,
    metadata: Annotated[dict[str, Any] | None, "Optional metadata"] = None,
) -> Annotated[T, "Registered type"]:
    """Register a type in the global registry."""
    return cast("T", await _REGISTRY.aregister(cls, key=key, asset_type=asset_type, metadata=metadata))


async def aget_type(
    key: Annotated[str, "Registry key"]
) -> Annotated[type[CoreAsset] | None, "Registered type or None"]:
    """Get a type from the global registry by key."""
    return await _REGISTRY.aget(key)


async def aget_types_by_asset(
    asset_type: Annotated[AssetType, "Asset type"]
) -> Annotated[dict[str, type[CoreAsset]], "Mapping of keys to types"]:
    """Get all types of a specific asset type from the global registry."""
    return await _REGISTRY.aget_asset_type(asset_type)


async def aget_registry_stats() -> Annotated[dict[str, Any], "Registry statistics"]:
    """Get comprehensive global registry statistics."""
    return await _REGISTRY.aget_stats()


# --- Tag-Based Query Functions ------------------------------------------------

async def aget_by_tag(
    tag: Annotated[Tag, "Tag to filter by"]
) -> Annotated[dict[str, type[CoreAsset]], "Assets with the specified tag"]:
    """Get all assets with a specific tag from the global registry."""
    return await _REGISTRY.aget_by_tag(tag)


async def aget_by_tag_prefix(
    prefix: Annotated[str, "Tag path prefix"]
) -> Annotated[dict[str, type[CoreAsset]], "Assets with tags matching the prefix"]:
    """Get all assets with tags matching a prefix from the global registry."""
    return await _REGISTRY.aget_by_tag_prefix(prefix)


async def aget_by_tags(
    tags: Annotated[set[Tag], "Tags to filter by"],
    *,
    match_all: Annotated[bool, "Require all tags to match"] = False
) -> Annotated[dict[str, type[CoreAsset]], "Assets with the specified tags"]:
    """Get assets with specified tags from the global registry."""
    return await _REGISTRY.aget_by_tags(tags, match_all=match_all)


async def aget_by_ancestor(
    ancestor: Annotated[Tag, "Ancestor tag to filter by"]
) -> Annotated[dict[str, type[CoreAsset]], "Assets with tags descending from the ancestor"]:
    """Get all assets with a tag descending from the specified ancestor."""
    return await _REGISTRY.aget_by_ancestor(ancestor)


# --- Main Registry Namespace Class --------------------------------------------

class Registry:
    """
    Unified namespace for all registry functionality in the types package.

    Provides async registration, lookup, and type guard helpers.

    """
    # Core types
    TypeRegistry: ClassVar[type] = TypeRegistry

    # Async API
    aregister_type: ClassVar[Any] = staticmethod(aregister_type)
    aget_type: ClassVar[Any] = staticmethod(aget_type)
    aget_types_by_asset: ClassVar[Any] = staticmethod(aget_types_by_asset)
    aget_registry_stats: ClassVar[Any] = staticmethod(aget_registry_stats)

    # Tag-based query API
    aget_by_tag: ClassVar[Any] = staticmethod(aget_by_tag)
    aget_by_tag_prefix: ClassVar[Any] = staticmethod(aget_by_tag_prefix)
    aget_by_tags: ClassVar[Any] = staticmethod(aget_by_tags)
    aget_by_ancestor: ClassVar[Any] = staticmethod(aget_by_ancestor)


# --- Public re-exports --------------------------------------------------------

__all__ = [
    "Registry",
    "aget_by_ancestor",
    "aget_by_tag",
    "aget_by_tag_prefix",
    "aget_by_tags",
]
